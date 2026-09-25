#!/bin/bash
# Provisions the AWS-side stores into LocalStack. Runs once LocalStack is ready.
#
# Everything here is owned by exactly one service — see CLAUDE.md's
# architecture table:
#
#   suggestx-search-events         (Firehose) -> CollectionService publishes to it
#   suggestx-raw-logs              (S3)       -> Firehose's delivery destination
#   suggestx-trie-snapshots        (S3)       -> TrieBuilder, exclusively
#   suggestx-phrase-frequencies    (DynamoDB) -> Aggregator, exclusively
#   suggestx-aggregator-checkpoints (DynamoDB) -> Aggregator, exclusively —
#     its own read-progress marker, kept separate from the frequency counts
#     above so operational state never needs special-casing in anything
#     that later scans that table
#
# No SNS/SQS here, unlike the sibling JameX project: nothing in this system
# is event-driven request/response. The pipeline is a linear offline chain —
# each stage reads the previous stage's store directly, on its own timer —
# see DESIGN.md §1 decision 2 for why the read path specifically must never
# depend on any of this.
set -euo pipefail

REGION="${AWS_DEFAULT_REGION:-us-east-1}"
RAW_LOGS_BUCKET="suggestx-raw-logs"
TRIE_SNAPSHOTS_BUCKET="suggestx-trie-snapshots"
FREQUENCIES_TABLE="suggestx-phrase-frequencies"
CHECKPOINT_TABLE="suggestx-aggregator-checkpoints"
DELIVERY_STREAM="suggestx-search-events"

echo "[suggestx] provisioning local AWS in ${REGION}"

# ---------------------------------------------------------------------------
# S3 — the HDFS stand-in (raw logs) and the trie-snapshot durability store.
# ---------------------------------------------------------------------------
awslocal s3api create-bucket --bucket "${RAW_LOGS_BUCKET}"       --region "${REGION}" >/dev/null
awslocal s3api create-bucket --bucket "${TRIE_SNAPSHOTS_BUCKET}" --region "${REGION}" >/dev/null

echo "[suggestx] s3 ready: ${RAW_LOGS_BUCKET}, ${TRIE_SNAPSHOTS_BUCKET}"

# ---------------------------------------------------------------------------
# DynamoDB — the Cassandra stand-in. One item per phrase; Aggregator writes
# it exclusively via an atomic ADD, never a plain PutItem, so a redelivered
# log batch increments rather than silently overwrites a real count.
# ---------------------------------------------------------------------------
# DynamoDB's own SQLite-backed persistence can survive a container restart
# even though this project isn't on a licensed plan (see PROGRESS.md's
# Environment notes) — so a plain create-table here fails with
# ResourceInUseException on the second boot against the same volume.
if ! awslocal dynamodb describe-table --table-name "${FREQUENCIES_TABLE}" --region "${REGION}" >/dev/null 2>&1; then
  awslocal dynamodb create-table \
    --table-name "${FREQUENCIES_TABLE}" \
    --attribute-definitions AttributeName=phrase,AttributeType=S \
    --key-schema AttributeName=phrase,KeyType=HASH \
    --billing-mode PAY_PER_REQUEST \
    --region "${REGION}" >/dev/null
fi

echo "[suggestx] dynamodb ready: ${FREQUENCIES_TABLE}"

# ---------------------------------------------------------------------------
# Aggregator's own checkpoint table — Module 3. One row, tracking the last
# S3 key it has fully processed, so a restart resumes instead of
# reprocessing the whole raw-logs bucket. See DynamoAggregatorCheckpoint.
# ---------------------------------------------------------------------------
if ! awslocal dynamodb describe-table --table-name "${CHECKPOINT_TABLE}" --region "${REGION}" >/dev/null 2>&1; then
  awslocal dynamodb create-table \
    --table-name "${CHECKPOINT_TABLE}" \
    --attribute-definitions AttributeName=checkpointId,AttributeType=S \
    --key-schema AttributeName=checkpointId,KeyType=HASH \
    --billing-mode PAY_PER_REQUEST \
    --region "${REGION}" >/dev/null
fi

echo "[suggestx] dynamodb ready: ${CHECKPOINT_TABLE}"

# ---------------------------------------------------------------------------
# Kinesis Data Firehose — the managed "buffer records, batch-write them to
# S3" pipeline that replaced CollectionService's own hand-rolled in-memory
# buffer + flush worker (DESIGN.md decision 11). CollectionService calls
# PutRecord and is done; Firehose owns the batching and the S3 write.
#
# BufferingHints below are demo-scale: AWS's real minimum IntervalInSeconds
# for an S3 destination is 60, but LocalStack accepts 10, which makes the
# pipeline observable in a local session instead of requiring a minute of
# waiting per test. Not a claim about production cadence — same convention
# as every other interval in this project (see CLAUDE.md).
#
# The RoleARN is required by the API and genuinely assumed (Firehose calls
# sts:AssumeRole even under emulation, which is why SERVICES in
# docker-compose.yml must include sts and iam alongside firehose), but
# LocalStack does not enforce the role's policy — in a real account this
# role would need s3:PutObject on the destination bucket.
# ---------------------------------------------------------------------------
if ! awslocal firehose describe-delivery-stream --delivery-stream-name "${DELIVERY_STREAM}" --region "${REGION}" >/dev/null 2>&1; then
  awslocal firehose create-delivery-stream \
    --delivery-stream-name "${DELIVERY_STREAM}" \
    --region "${REGION}" \
    --s3-destination-configuration "{
      \"RoleARN\": \"arn:aws:iam::000000000000:role/suggestx-firehose-role\",
      \"BucketARN\": \"arn:aws:s3:::${RAW_LOGS_BUCKET}\",
      \"Prefix\": \"search-events/\",
      \"ErrorOutputPrefix\": \"delivery-failures/\",
      \"BufferingHints\": { \"SizeInMBs\": 1, \"IntervalInSeconds\": 10 },
      \"CompressionFormat\": \"UNCOMPRESSED\"
    }" >/dev/null
fi

echo "[suggestx] firehose ready: ${DELIVERY_STREAM} -> s3://${RAW_LOGS_BUCKET}/search-events/"

echo "[suggestx] bootstrap complete"
