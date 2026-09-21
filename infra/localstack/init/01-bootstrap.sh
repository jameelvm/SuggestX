#!/bin/bash
# Provisions the AWS-side stores into LocalStack. Runs once LocalStack is ready.
#
# Everything here is owned by exactly one service — see CLAUDE.md's
# architecture table:
#
#   suggestx-raw-logs          (S3)       -> CollectionService, exclusively
#   suggestx-trie-snapshots    (S3)       -> TrieBuilder, exclusively
#   suggestx-phrase-frequencies (DynamoDB) -> Aggregator, exclusively
#
# No SNS/SQS here, unlike the sibling JameX project: nothing in this system
# is event-driven. Each pipeline stage reads the previous stage's store
# directly, on its own timer — see DESIGN.md §1 decision 2 for why the read
# path specifically must never depend on any of this.
set -euo pipefail

REGION="${AWS_DEFAULT_REGION:-us-east-1}"
RAW_LOGS_BUCKET="suggestx-raw-logs"
TRIE_SNAPSHOTS_BUCKET="suggestx-trie-snapshots"
FREQUENCIES_TABLE="suggestx-phrase-frequencies"

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
awslocal dynamodb create-table \
  --table-name "${FREQUENCIES_TABLE}" \
  --attribute-definitions AttributeName=phrase,AttributeType=S \
  --key-schema AttributeName=phrase,KeyType=HASH \
  --billing-mode PAY_PER_REQUEST \
  --region "${REGION}" >/dev/null

echo "[suggestx] dynamodb ready: ${FREQUENCIES_TABLE}"

echo "[suggestx] bootstrap complete"
