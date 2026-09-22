# SuggestX — system design

Companion to `README.md`. README explains *how* the system was built, phase by
phase; this file explains *why it is shaped this way* — every non-obvious
choice, every failure mode it was built to survive, and the questions the
design doc itself poses (each chapter ends with one or more "show answer"
prompts — answered here, against this build, not abstractly).

## §0 Source material

`../*.pdf` — Grokking Modern System Design Interview, "Typeahead Suggestion
System," six chapters: overview, requirements, high-level design, data
structure (trie), detailed design (suggestion service + assembler),
evaluation. Read in full 2026-09-17 before any scaffolding was written.

## §1 Decision register

Numbered chronologically. Each entry: the decision, the alternative(s)
considered, and why this one won.

1. **No relational database anywhere.** Every other service-oriented build in
   this pattern (see JameX) has at least one Postgres-backed entity with
   relationships. This system genuinely has none — a query log, a frequency
   counter, a trie snapshot blob, a flattened cache, and a coordination
   service, none of which relate to each other via foreign keys. Considered
   forcing a relational store in anyway (e.g., for the raw log) for
   consistency with the sibling project, and rejected it: the doc is explicit
   that HDFS/S3-style append-only object storage is the right shape for the
   log, and inventing a relational need where none exists would be exactly the
   kind of unnecessary abstraction the project's own conventions warn against.

2. **The trie itself lives only inside `TrieBuilder`; `SuggestionService` does
   a flat cache `GET`, never a traversal.** The doc's high-level design
   diagram literally shows the suggestion service reading "top ten popular
   queries" from Redis, not walking a trie over the network per keystroke.
   Taking that literally means: `TrieBuilder` builds the compressed trie in
   memory each cycle, walks it once to precompute `prefix → top-N` for every
   prefix up to a bounded length, and writes that flattened projection to
   Redis. `SuggestionService` becomes trivially fast (one Redis `GET`,
   horizontally scalable with zero coordination) at the cost of a bounded
   prefix length — see decision 6.

3. **HDFS → S3, Cassandra → DynamoDB, MongoDB (trie doc store) → S3.** The
   first two mirror JameX's already-established stand-ins (S3 for durable raw
   blobs, DynamoDB for wide-column/high-throughput counters). The third
   deliberately does **not** mirror "use DynamoDB for everything NoSQL": a
   serialized trie snapshot is a large opaque blob, not a queryable document,
   and DynamoDB's 400KB item cap makes it a poor fit at any real scale. S3
   (object per partition per version) is the more honest AWS-idiomatic choice
   for "persist this blob for recovery," even though the doc names MongoDB
   specifically — the doc's own access pattern (write whole snapshot, read
   whole snapshot on recovery, never query into it) is exactly what object
   storage is for.

4. **ZooKeeper runs for real, as a genuine Docker container, not simulated as
   "a Redis key."** A single version-pointer key in Redis would functionally
   cover the "which version is current" need, but it would erase a distinct
   architectural concept the doc explicitly names — a coordination/config
   service separate from the cache — and this project's stated goal is
   understanding through implementation, which means the concept deserves a
   real running instance and a real client library, not a shortcut.
   ZooKeeper owns two things: the partition map
   (`/suggestx/partitions`) and, per partition, a `current_version` znode
   that `TrieBuilder` flips atomically after a successful cutover.

5. **Trie partitioning by prefix range, not by hash.** The doc's own example
   (`A–M` / `N–Z`) is range-based, and range partitioning is what makes "route
   a query starting with U to the shard that owns N–Z" a simple, explainable
   lookup rather than a hash function the frontend/gateway would also need to
   know. The doc names range partitioning's real weakness itself (uneven load
   across letters) — documented as an open, deliberately unsolved question
   rather than quietly hidden; see the failure-mode table.

6. **Flattened cache is bounded to a maximum prefix length (default 6
   characters), configurable.** Precomputing top-N for every possible prefix
   is only tractable up to a bound — beyond ~5-6 characters the number of
   distinct prefixes explodes while the marginal value of a further-refined
   suggestion list drops (a user who's typed 6 correct characters is already
   most of the way to a match). Below the bound: served from Redis. At/above
   it: `SuggestionService` falls back to querying the *last* cached prefix
   still within the bound and filtering client-visible results by the
   remaining suffix — a real degradation, not a silent one, and documented as
   such rather than left to surprise someone at chapter's end.

7. **Aggregation cadence and prefix-length bound are both configuration, set
   short for local demoing.** The doc's real cadence (~15 min) makes for a
   very boring local demo. Every place a cadence appears in code or docs says
   explicitly that it's a demo-scale value, not a claim about production
   cadence.

8. **Blue/green version swap, not primary-secondary in-place mutation.** The
   trie data structure chapter names both techniques (replica replacement vs.
   primary-secondary swap) as options. This build uses replica
   replacement/blue-green: `TrieBuilder` writes an entirely new version
   namespace (`trie:v{N}:*` in Redis, a new S3 object), validates it, then
   flips the ZooKeeper pointer — old version's keys are only evicted after the
   swap succeeds, so a failed build never affects what's currently being
   served. Chosen over primary-secondary in-place swap because it needs no
   locking on the read path at all; the tradeoff (paying for two versions'
   worth of Redis memory briefly) is cheap at local/demo scale and cheap in
   real deployments too, relative to the cost of a read-path outage.

## §2 Failure-mode table

| Failure | Effect without mitigation | Mitigation in this build |
|---|---|---|
| `TrieBuilder` crashes mid-build | Partial/corrupt version could be served | Blue/green swap (decision 8): the ZooKeeper pointer only flips after the full new version is written and validated, so a crash mid-build leaves the previously-served version untouched. |
| Aggregator falls behind (batch job takes longer than its own interval) | Frequencies grow stale, or two runs overlap and double-count | Aggregator checkpoints its S3 read offset per run; a run that overlaps the next start is a documented open question (see §4) rather than silently assumed away. |
| ZooKeeper unreachable | `SuggestionService` cannot learn the current version | Suggestion Service caches the last-known version/partition map in memory and continues serving it; a ZooKeeper outage degrades to "suggestions may go stale," not "suggestions stop." |
| A Redis partition is unreachable | Every query for that prefix range fails | Redis's own primary-replica replication (not hand-rolled app failover) is the mitigation — matches how a real deployment would actually solve this, rather than inventing bespoke failover code. |
| A hot prefix range gets disproportionate load (e.g., everything starting "S") | One partition's servers overload while others idle | Named directly in the source doc as range partitioning's real weakness. Left as an open, unsolved question here (see §4) rather than hidden — a hash-based secondary partitioning layer is the real answer and is out of scope for this build. |
| S3 raw-log write fails from `CollectionService` | A user's search event is lost, undercounting a real trend | In-memory buffer with a bounded retry before drop; documented as best-effort, matching the doc's own framing that offline aggregation trades perfect accuracy for read-path speed. |
| `SuggestionService` instance restarts | Cold start with no cached version/partition map | Reads current state from ZooKeeper on startup before serving; documented startup-ordering dependency (ZooKeeper must be reachable at boot, even though it's not required per-request after that). |

## §3 Q&A bank

Seeded from the design doc's own embedded questions, answered against this
build specifically (not left abstract).

### Data structure and trie mechanics

- **Q: How would you efficiently update sub-tree structures to accommodate new
  data ingestion?**
  A: You don't mutate the live trie in place at all — see decision 8. The new
  data lands in the *next* offline build cycle; the currently-served trie
  never sees a partial update. "Efficient update" here means "efficient full
  rebuild on a schedule," not incremental patching of a live structure.

- **Q: Is there another way to minimize trie traversal time beyond merging
  single-branch nodes?**
  A: Yes — don't traverse at request time at all. Decision 2 removes the
  read-path traversal entirely by precomputing every prefix's answer offline.
  Compression still matters, but only for `TrieBuilder`'s own in-memory build
  and walk cost, not for anything a user is waiting on.

- **Q: Can a trie be used for approximate matches or spelling correction?**
  A: Not directly with prefix-exact traversal — a trie only ever narrows to
  nodes matching the literal typed characters. Approximate matching needs a
  different structure or a layer on top (e.g., BK-trees, edit-distance-bounded
  trie walks, or a separate phonetic/fuzzy index consulted only when the exact
  prefix trie returns nothing). Out of scope for this build; noted as a
  stretch item.

- **Q: If prefix frequencies keep increasing, can the counters overflow?**
  A: DynamoDB's `ADD` on a numeric attribute is a 64-bit float/decimal under
  the hood in practice — overflow at real-world query volumes is not a
  practical concern the way a 32-bit counter would be. The real risk this
  build actually guards against is the *cross-store idempotency* one (a
  redelivered/replayed log batch double-counting), which is why Aggregator's
  checkpointing (§2) matters more here than integer width.

- **Q: What are the trade-offs of storing search frequencies in the trie nodes
  themselves?**
  A: Couples ranking data to structural data — every frequency update, however
  small, is entangled with the same object a traversal reads. This build
  avoids the question by never updating the live trie in place at all
  (decision 2/8): frequencies live in DynamoDB, structurally separate from any
  trie, and only get folded into a trie once, at build time.

### Partitioning and coordination

- **Q: Where is the mapping between prefixes and their primary/secondary
  storage kept, and who directs requests to the right shard?**
  A: ZooKeeper (`/suggestx/partitions`), decision 4/5. `SuggestionService`
  reads the map on startup and on change-watch, and does its own
  prefix-range lookup locally before hitting Redis — no separate routing tier
  needed, since the lookup is a cheap in-memory range check once the map is
  cached.

### Personalization and evaluation

- **Q: Should the trie be built per-user or shared among all users?**
  A: Shared — a per-user trie at any real scale multiplies storage and build
  cost by the user count for a feature (personalization) that the doc itself
  frames as a *ranking* adjustment, not a *candidate set* adjustment. This
  build's stretch personalization phase blends a user's own recent-search
  cache (client-side, small) with the shared global ranking at merge time in
  `SuggestionService`, rather than maintaining separate tries.

- **Q: What trade-offs exist between offline processing and real-time
  updates?**
  A: Real-time updates would make a just-searched term instantly suggestible
  to everyone, at the cost of putting write amplification directly in the
  read path (decision 2 is precisely the choice to not do this) and of much
  harder consistency reasoning during partition swaps. Offline processing
  accepts a bounded staleness window (this build's aggregation cadence,
  decision 7) in exchange for a read path with no write-path dependency at
  all — matching the doc's own stated reasoning almost verbatim (scale +
  relevance-doesn't-change-that-fast).

## §4 Open questions / deferred

- **Hot-prefix imbalance under range partitioning** — named in the doc, not
  solved here. A real fix (secondary hash-based sub-partitioning within a hot
  range) is out of scope.
- **Aggregator overlap if a run takes longer than its own interval** — flagged
  in §2, no lock/lease mechanism built yet.
- **Fuzzy/typo-tolerant matching** — the doc raises it as a discussion
  question, not a requirement; not built.
- **Personalization** — designed above, not yet built; tracked as a later
  phase in `PROGRESS.md`.

## §5 Doc-to-code map

| Doc concept | Chapter | File(s) | Why this choice |
|---|---|---|---|
| Suggestion service | 3, 5 | `src/services/SuggestX.SuggestionService/` | Scaffolded, health-check only so far; real Redis-`GET` read path arrives Phase 5. |
| Collection service | 5 | `src/services/SuggestX.CollectionService/` | Scaffolded; S3 batch-flush logic arrives Phase 2. |
| Aggregator (MapReduce over HDFS) | 4, 5 | `src/services/SuggestX.Aggregator/` | Scaffolded as an API host for health/status now; the real `BackgroundService` batch worker arrives Phase 3. |
| Trie builder | 5 | `src/services/SuggestX.TrieBuilder/` | Scaffolded; the compressed trie + blue/green swap arrives Phase 4. |
| Web servers / entry point | 3 | `src/services/SuggestX.Gateway/` | YARP proxy, two routes (`/api/suggestions`, `/api/search-events`) live; no auth layer, since the source doc has no identity concept at all. |
| HDFS | 4, 5 | `suggestx-raw-logs` (S3, LocalStack) | `infra/localstack/init/01-bootstrap.sh`. See decision 3. |
| Cassandra | 4, 5 | `suggestx-phrase-frequencies` (DynamoDB, LocalStack) | Same script. See decision 3. |
| MongoDB (trie doc store) | 5 | `suggestx-trie-snapshots` (S3, LocalStack) | Same script. See decision 3 — S3, not a document store, deliberately. |
| ZooKeeper | 5 | `zookeeper:3.9` container (`docker-compose.yml`) | Real container, not simulated. See decision 4. Znode round-trip verified manually via `zkCli.sh` in Phase 1; the application-level client arrives with TrieBuilder in Phase 4. |
| Redis (trie cache) | 3, 5 | `redis:7-alpine` container | Unchanged from the doc. `trie:*` namespace is written by TrieBuilder starting Phase 4. |

## §6 Coverage map

| Design-doc concept | Status |
|---|---|
| Compressed trie | ⬜ Designed, not built |
| Trie partitioning by prefix range | ⬜ Designed, not built |
| Offline trie updates (MapReduce-style) | ⬜ Designed, not built |
| Collection service | ⬜ Designed, not built |
| Aggregator | ⬜ Designed, not built |
| Trie builder + ZooKeeper-coordinated swap | ⬜ Designed, not built |
| Suggestion service (Redis-backed) | ⬜ Designed, not built |
| Client-side optimizations (debounce, input threshold, local cache, early connection, edge cache) | ⬜ Designed, not built |
| Personalization | ⬜ Designed, not built |
