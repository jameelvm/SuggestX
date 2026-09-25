# SuggestX

A working typeahead/autocomplete system, built end to end to internalise the
"Typeahead Suggestion System" design chapters in `../*.pdf`. This document is
revision material — it explains what was built,
why, and how to see it working — updated at the end of every phase, not a
running change log. For the decision-by-decision reasoning and the
failure-mode/Q&A material, see `DESIGN.md`. For live build state, see
`PROGRESS.md`.

## What is a typeahead suggestion system?

As a user types into a search box, the system suggests the most likely
completions of what they're typing — one request per keystroke, answered in
under 200ms, ranked by how often that completion has actually been searched
by everyone. Google Search, e-commerce product search, and code editor
autocomplete are all instances of the same problem: **fast prefix lookup
against a ranked, constantly-shifting set of strings, at a request volume
that makes "just query a database" impossible.**

The one functional requirement is deceptively small — *return the top N
completions for a prefix* — but it forces almost every other decision in the
system, because "under 200ms, at Google scale" rules out doing any real work
in that 200ms. Everything interesting about this design is about *where the
real work happens instead*.

## The shape of the system, in one paragraph

A **Suggestion Service** answers every keystroke with a single cache lookup —
no computation, no database query, no trie traversal, at request time. Behind
it, entirely offline, an **assembler pipeline** (Collection → Aggregator →
Trie Builder) logs what people search, periodically aggregates those logs into
frequency counts, periodically rebuilds a compressed trie from those counts,
and periodically publishes a flattened `prefix → top-N` projection of that
trie into the cache the Suggestion Service reads. The read path and the write
pipeline never touch each other directly — the only thing that crosses
between them is a new cache version, swapped in atomically once it's fully
built. That's the whole system; every store choice below exists to make that
one sentence true under real load.

## Why this project has no code yet

This is Phase 0. The six source chapters were read in full first — this
system, unlike a from-scratch build, comes from a design document with
specific named components (a compressed trie, an assembler with three named
sub-services, ZooKeeper-coordinated cache swaps), and the point of this
project is to give each of those a real, running counterpart rather than
inventing an architecture that happens to also do autocomplete. `CLAUDE.md`
and `DESIGN.md` were written from that reading before any scaffolding, so the
architecture below is design-doc-first, not code-first.

## Architecture

Five services, each owning exactly one store (or, for the read-only
`SuggestionService`, no durable store at all):

| Service | Role | Owns | Doc component it implements |
|---|---|---|---|
| `SuggestionService` | Hot read path | — (stateless) | "Suggestion service" |
| `CollectionService` | Ingests raw search events | `suggestx-raw-logs` (S3) | "Collection service" |
| `Aggregator` | Batch-aggregates frequencies | `suggestx-phrase-frequencies` (DynamoDB) | "Aggregator" (MapReduce job) |
| `TrieBuilder` | Rebuilds the trie, publishes it | `suggestx-trie-snapshots` (S3), the `trie:*` Redis namespace, ZooKeeper znodes | "Trie builder" |
| `Gateway` | Routing + a small admin/insights view | — | "web servers" |

Full reasoning for every store choice — including why there's deliberately no
relational database anywhere in this system — is in `DESIGN.md` §1.

### Why the read path is only ever a cache `GET`

The source doc's own high-level design diagram shows the suggestion service
reading "the top ten popular queries" straight from a Redis cache, not
walking a trie over the network per keystroke. This build takes that
literally: the compressed trie, as an actual in-memory data structure, exists
**only inside `TrieBuilder`**. Each build cycle, `TrieBuilder` walks it once
to precompute the top-N completions for every prefix up to a bounded length,
and writes that flattened `prefix → top-N` map into Redis. `SuggestionService`
never reconstructs or traverses anything — every request is one `GET`. The
expensive part of "trie" — traversal — gets paid for once per aggregation
cycle by one service, not once per keystroke by every user on earth. See
`DESIGN.md` §1 decision 2 and the "trie traversal time" Q&A entry.

## Resource estimates the design targets

Carried straight from the source doc's Requirements chapter, since they're
the numbers every later decision (partitioning, offline updates, caching) is
actually answering:

| Metric | Value |
|---|---|
| Total queries/day | 3.5 billion |
| Unique queries/day (stored) | 2 billion |
| Avg query length | 15 characters |
| Storage/day | 60 GB |
| Storage/year | 21.9 TB |
| Incoming bandwidth | 9.7 Mb/sec |
| Outgoing bandwidth (top-10 per keystroke) | 97 Mb/sec |
| Servers needed (realistic peak: ~3 chars/sec/user, 3.5B users) | ~164,000 |

This local build obviously runs at a tiny fraction of that scale — a handful
of containers, not 164K servers — but every architectural lever named in the
design doc (partitioning, caching, offline updates, replication) is the same
lever a real deployment would pull to close that gap, just turned down. Where
a local default stands in for a production value (batch cadence, partition
count, prefix-length bound), it's called out explicitly rather than left to
look like a real capacity number.

## Phase log

### Phase 0 — design reading and scaffolding (2026-09-20)

Read all six source chapters in full. Wrote `CLAUDE.md` (architecture,
stack/stand-in table, layout, conventions), `DESIGN.md` (8-entry decision
register, 7-row failure-mode table, a Q&A bank answering every embedded
question the chapters posed, open questions, and empty doc-to-code/coverage
maps ready for Phase 1), `PROGRESS.md` (live state tracker), and this file.
No code, no containers, nothing runnable yet — that starts in Phase 1.

**Design talking points from this phase:**

- The single biggest lever in this design is *moving the trie traversal off
  the read path entirely*, not any particular data-structure trick. A
  compressed trie is a nice constant-factor win; not traversing one per
  request at all is the actual latency win.
- A typeahead system is a genuinely relationship-free data problem — no
  entities, no foreign keys, just a log, a counter table, a snapshot blob, a
  flattened cache, and a coordination service. That absence of a relational
  store is a real architectural signal, not a gap.
- HDFS/Cassandra/MongoDB/ZooKeeper in the source doc map onto S3, DynamoDB,
  S3 (again — a blob store fits a trie snapshot better than a document store
  does), and a real ZooKeeper container respectively. Only one of those four
  swaps (MongoDB → S3) actually changes the *kind* of store, and it's
  motivated by an access-pattern mismatch (whole-blob write/read vs.
  DynamoDB's item-size cap), not by "just reuse what's already in the stack."

### Phase 1 — local substrate (2026-09-21)

Scaffolded the solution and got every piece of infrastructure this system
needs running and talking to each other, before writing a line of real
suggestion/aggregation logic. `SuggestX.Contracts` picked up its first two
DTOs (`SuggestionResponse`/`SuggestionItem` for the read path,
`SearchEventRequest` for ingestion); `SuggestX.ServiceDefaults` got an AWS
client factory (S3 + DynamoDB only — no SNS/SQS, since nothing in this
system is event-driven, unlike JameX) and hosting extensions for
health/OpenAPI/CORS/Redis. Five service projects were scaffolded
(`Gateway`, `SuggestionService`, `CollectionService`, `Aggregator`,
`TrieBuilder`), each currently just a health-check host — real logic starts
Phase 2.

**A real environment finding, not assumed away:** LocalStack's freemium tier
now requires a valid auth token for license activation even for plain
community services (S3, DynamoDB) — without one the container exits
immediately (code 55). Fixed with a gitignored `.env`, same convention as
the sibling JameX project, reusing that project's token since it's the same
owner's account. A second, more interesting bug followed from copying
JameX's `LOCALSTACK_HOST` setting without re-deriving why it had that value:
JameX sets it to match its externally-published port because a real browser
needs to resolve a presigned S3 URL against it. This project's ports are
deliberately shifted (LocalStack publishes `4567` on the host, but still
listens on `4566` inside its own container), and `LOCALSTACK_HOST` is
actually consumed by tooling running *inside* that container — including
the bootstrap script's own `awslocal` calls — so it needed to stay `4566`
regardless of what the host-side port is. Copying a working pattern without
checking whether the reason behind it still applies is exactly the mistake
this got caught making.

**Design talking point from this phase:** ports are shifted +1000 across
the whole project (`9080`-`9084` instead of `8080`-`8084`, `6380` instead of
`6379`, `4567` instead of `4566`) specifically so this system and JameX can
run side by side on one machine — a small, boring decision, but one that
matters in practice the moment a second project in the same pattern exists.

### Verification (Phase 1)

```bash
dotnet build SuggestX.slnx         # 0 warnings, 0 errors
docker compose up -d --build       # 8 containers: redis, zookeeper,
                                    # localstack, gateway, suggestion-service,
                                    # collection-service, aggregator,
                                    # trie-builder
```

Confirmed live: every service answers `GET /health/live` and
`GET /health/ready` (200). The Gateway's YARP routes proxy for real, not
just pass a health check — confirmed by comparing a direct 404 from
`SuggestionService` against the identical 404 arriving through
`/api/suggestions/...`, and by reading the Gateway's own log line
(`Proxying to http://suggestion-service:8080/...`) plus its active
health-check probes returning 200 on both clusters. `suggestx-raw-logs`,
`suggestx-trie-snapshots` (S3) and `suggestx-phrase-frequencies`
(DynamoDB) all exist, confirmed via `awslocal s3 ls` /
`awslocal dynamodb list-tables`. Redis answers `PONG`. A real ZooKeeper
znode was created, read back exactly, and deleted via `zkCli.sh` inside the
container — proving the coordination service genuinely works before any
application code depends on it in Phase 4.

### Phase 2 — Collection Service (2026-09-22)

> **Superseded 2026-09-25** — the in-memory buffer and flush worker
> described below were replaced end to end by Kinesis Data Firehose. This
> section is kept as an accurate record of what was actually built and
> verified at the time, not silently rewritten; see the entry further down
> for what replaced it and why.

Built the first real piece of the pipeline: the write path that turns a
user's search into durable evidence. `POST /search-events` validates and
buffers a submitted search term in memory (`ISearchEventBuffer`); a
`BackgroundService` (`SearchEventFlushWorker`) drains that buffer on a timer
and writes it as one line-delimited-JSON object to `suggestx-raw-logs`,
skipping the write entirely when there's nothing to flush.

**Two things worth calling out, not just "it flushes to S3":**

- **The object key is designed for Aggregator before Aggregator exists.**
  Each key is `{millisecond-timestamp}-{instance-guid}.jsonl` — timestamp
  first, so keys sort chronologically and Phase 3's Aggregator can use S3's
  `ListObjectsV2` with `StartAfter` to find only new objects, instead of
  reading every object in the bucket to discover which ones are recent. The
  GUID suffix means two instances (or the same instance flushing twice in
  the same millisecond) can never collide.
- **Graceful shutdown flushes what's left; an ungraceful kill doesn't.**
  `StopAsync` is overridden to run one last flush before the process exits
  on a clean SIGTERM. Verified by posting an event and then
  `docker compose stop`-ping the container mid-interval: the log showed
  `Application is shutting down...` immediately followed by the flush
  completing, and the event landed in S3. What this *doesn't* cover — a
  SIGKILL or crash — is written up honestly in `DESIGN.md`'s failure-mode
  table and open questions, not glossed over: closing that gap needs a
  write-ahead durability layer, a different ingest architecture, not a
  patch to the current buffer.

**Verified against the live stack, step by step, not just "it compiled":**
the debug count endpoint went 0 → 2 (two direct posts) → unchanged after a
rejected blank query → 3 after one more posted through the Gateway's real
proxy route. Three events landed in one S3 object after a flush cycle, with
every field correct on inspection; a second flush cycle produced a second,
distinct object rather than overwriting the first; an idle interval produced
no object at all; the shutdown-flush test above produced a third object
containing exactly the one event that was in flight.

### Collection Service rebuilt on Kinesis Data Firehose (2026-09-25)

The in-memory buffer/flush-worker design above had one gap it could never
close, acknowledged honestly rather than hidden: an *ungraceful* kill of
CollectionService (a crash, OOM, `SIGKILL`) lost whatever was buffered since
the last flush, because durability lived only in that one process's RAM.
Closing that gap for real meant moving durability out of the process
entirely — not writing a bigger retry loop around the same design.

**What changed:** `POST /search-events` now calls `PutRecordAsync` against
a Kinesis Data Firehose delivery stream (`suggestx-search-events`) and does
not return `202` until Firehose has durably accepted the record. Firehose
itself buffers records and batch-writes them into `suggestx-raw-logs` —
`SearchEventBuffer` and `SearchEventFlushWorker` are deleted, not
deprecated; CollectionService now owns no store and holds no state. A
publish failure returns `503`, not a silent drop, so the caller — not this
service — decides whether to retry.

**SQS was considered and rejected**, genuinely the more natural fit for
this codebase's own conventions (it's the pattern the sibling JameX project
already uses for durability). It lost specifically because the problem here
— buffer records, batch-write them to S3 — is Firehose's literal job
description, not a general work queue being repurposed. Choosing SQS would
have meant re-writing the same flush-worker logic just built, reading from
a durable queue instead of an in-memory one; Firehose removes that class of
code entirely rather than hardening it.

**A real LocalStack limitation, found by testing, not assumed either way:**
`BufferingHints.IntervalInSeconds` (10s here — AWS's real minimum for an S3
destination is 60s, already a demo-scale value) is not honored by
LocalStack's Firehose emulation at all. A 10-request concurrent burst
produced 10 separate S3 objects, not one combined one — every `PutRecord`
lands in S3 as its own object within about a second regardless of the
configured interval. This doesn't break correctness (Aggregator's
line-by-line parser handles one-line objects exactly as well as many-line
ones), but it means local testing can prove the delivery *pipeline* works —
accept, durably hand off, land in S3 with correct content — without being
able to demonstrate the batching/cost-reduction behavior that's the actual
real-world reason to prefer Firehose over one-object-per-event. That
specific claim is asserted from AWS's own documented behavior, not
something this local stack could verify either way.

**A real compatibility question, answered rather than assumed:** would
Aggregator's `ListObjectsV2`/`StartAfter` checkpoint logic — built against
CollectionService's own flat, timestamp-prefixed keys — still work against
Firehose's completely different `search-events/yyyy/MM/dd/HH/stream-
timestamp-uuid` key format? Tested directly: restarted Aggregator against
14 pre-existing Firehose-delivered objects and it caught up on all 14 in
one poll cycle; one more posted event moved the counters by exactly 1, not
15. No code changes needed — S3's lexicographic listing order still
correlates with chronological order at the hour-folder level, which is all
the checkpoint actually depends on.

**Verified against the live stack, end to end, through the Gateway**: valid
posts return `202` and a published-event counter increments; a blank query
is still rejected with `400`; a request through the Gateway's real proxy
route succeeds identically to a direct call; posted events appear in S3
under `search-events/...` with correct JSON content after the buffering
interval; a 10-event concurrent burst delivered every event correctly; and
Aggregator — built in Phase 3 against the *old* key format — read every one
of them without modification.

## Next up

See `PROGRESS.md` for the live, session-to-session state. The phase roadmap:
~~local substrate~~ → ~~Collection Service~~ → Aggregator → trie data
structure + Trie Builder → Suggestion Service → Gateway + frontend →
evaluation extras (personalization, client-side optimizations,
fault-tolerance verification).
