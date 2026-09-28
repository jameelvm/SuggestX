# SuggestX — build progress

## ▶ How to resume

Say this to Claude at the start of the next session:

> Read PROGRESS.md and CLAUDE.md in C:\System Design\TypeheadSuggestion\App,
> then start Phase 6 — Gateway + frontend. Build it in short modules,
> pausing after each one so I can review before you continue.

**Build in short modules.** One concept per module, verified and explained
before moving on — same discipline as JameX.

---

**Purpose of this file:** if a session is lost, this is the single place that
says where the build stopped and what happens next. Update the *Current
state* and *Next up* sections at the end of every session.

**Standing conventions**

1. At the end of every phase, update `README.md` with full teaching-style
   documentation of what that phase built — architecture, each module, the
   reasoning behind each choice, verification commands and design talking
   points. The README is revision material, not a change log.
2. Also extend `DESIGN.md` — new decisions in the register, new rows in the
   failure-mode table, new Q&A entries, an updated doc-to-code map (§5) and
   coverage map (§6).
3. Every phase leaves `dotnet build SuggestX.slnx` green.
4. Build in short modules, one concept each, pausing after every module.

---

## Current state

**Last updated:** 2026-09-27
**Phase 1 — local substrate. Complete, verified.**
**Phase 2 — Collection Service. Complete, verified — and since rebuilt on
Kinesis Data Firehose** in place of the original in-memory buffer/flush
worker, closing the ungraceful-kill data-loss gap `DESIGN.md` decision 9
left open. See decision 11 and this file's Phase 2 entry for the full
account, including what was considered and rejected (SQS) and a real
LocalStack emulation limitation found along the way.
**Phase 3 — Aggregator. Complete, verified.** All three modules done: reading
new raw logs on a timer, the actual map-reduce into
`suggestx-phrase-frequencies` (case-insensitive, atomic `ADD`, one write per
unique phrase per batch), and durable checkpoint persistence in DynamoDB —
proven across a genuine container restart, not just a fresh start.
**Phase 4 — Trie Builder. Complete, verified — all three modules.** Module 1
(the compressed trie itself, built from real DynamoDB data) verified against
real branching data — a shared-prefix split ("java" vs "jazz"), frequency
ranking, alphabetical tie-breaking, case normalization, and a true negative
all confirmed live. Module 2 (flatten `prefix → top-N` into a versioned
Redis namespace) verified live across real build cycles including the
blue/green old-version cleanup. Module 3 (S3 snapshot persistence + a real
ZooKeeper `current_version` znode) verified live across a genuine container
restart: the restarted process recovered version 3 from ZooKeeper (not a
reset to 1), published version 4 next, and correctly cleaned up exactly
version 3's Redis keys — proven directly against Redis, S3, and ZooKeeper,
not inferred from logs alone.
**Phase 5 — Suggestion Service. Complete, verified live.** The real read
path (ZooKeeper version poll → Redis `GET` → decision-6 over-bound
fallback) verified against `guitar`/`jazz`/`ja` and through the real
Gateway proxy. A real gap found by testing the ZooKeeper-outage failure
mode directly rather than trusting it — a long enough outage lets
TrieBuilder's Redis cleanup roll past the version SuggestionService is
still frozen on — accepted as a documented, bounded gap (decision 17)
rather than closed. Real latency measured end to end: p95 well under
10ms on every path tested, comfortably inside the doc's 200ms NFR.
Phase 6 (Gateway + frontend) next.**
**Local debugging (cross-cutting, not a phase) — set up and verified.**
Every service can now run under the Visual Studio debugger, on the exact
port its container publishes, with the Gateway automatically reaching
whichever one (container or local) is actually running. Full detail below;
guide lives in `DEBUGGING.md`.

Read all six source PDFs in full (`../*.pdf` — overview, requirements,
high-level design, data structure/trie, detailed design, evaluation).
`CLAUDE.md` and `DESIGN.md` written from that reading — architecture,
stack/stand-in table, and a full decision register (8 entries), failure-mode
table (7 rows), and a Q&A bank answering every embedded question the source
chapters posed.

**Phase 1 built and verified live:**
- Solution scaffold: `SuggestX.slnx`, `SuggestX.Contracts` (two DTO files —
  `SuggestionResponse`/`SuggestionItem`, `SearchEventRequest`),
  `SuggestX.ServiceDefaults` (AWS client factory — S3 + DynamoDB only, no
  SNS/SQS since nothing here is event-driven; options classes; hosting
  extensions for health/OpenAPI/CORS/Redis wiring).
- Five service projects, each building and running as a real container:
  `Gateway` (YARP, two routes), `SuggestionService`, `CollectionService`,
  `Aggregator`, `TrieBuilder` — all health-check-only so far, real logic
  starts Phase 2.
- `docker-compose.yml` + `infra/docker/Service.Dockerfile` (parameterised by
  `SERVICE` build arg, same convention as JameX) + `infra/localstack/init/01-bootstrap.sh`.
- **Verified against the live stack:** `dotnet build SuggestX.slnx` green,
  0 warnings/errors. All 8 containers (`redis`, `zookeeper`, `localstack`,
  and the 5 services) up and healthy. Every service's `/health/live` and
  `/health/ready` answered directly. The Gateway's YARP routes confirmed
  **actually proxying** (not short-circuiting) by comparing a direct 404 from
  `SuggestionService` against the same 404 arriving through
  `/api/suggestions/...`, and by reading the Gateway's own logs showing
  `Proxying to http://suggestion-service:8080/...` plus passing active health
  probes on both clusters. `suggestx-raw-logs` and `suggestx-trie-snapshots`
  buckets and the `suggestx-phrase-frequencies` table confirmed created via
  `awslocal s3 ls` / `awslocal dynamodb list-tables`. Redis answered `PONG`.
  A real ZooKeeper znode was created, read back, and deleted via `zkCli.sh`
  inside the container (`create` → `get` returned the exact value written →
  `delete`), proving the coordination service works before any application
  code depends on it.

**Key shape, for quick recall:** five services — `SuggestionService` (hot
read path, stateless, Redis `GET` only), `CollectionService` (owns raw search
logs in S3), `Aggregator` (owns aggregated frequencies in DynamoDB, batch
worker), `TrieBuilder` (owns trie snapshots in S3 + the served Redis
namespace + ZooKeeper version znodes, batch worker), `Gateway` (YARP +
small admin aggregation). Real ZooKeeper container, not simulated. No
relational database anywhere — see `DESIGN.md` §1 decision 1 for why that's
correct, not an oversight.

### Done

- [x] Read all six design-doc PDFs; requirements, high-level design, trie
      data structure, detailed design (suggestion service + assembler),
      evaluation.
- [x] `CLAUDE.md` written — architecture table, stack/stand-in table, layout,
      conventions.
- [x] `DESIGN.md` written — 8-entry decision register, 7-row failure-mode
      table, Q&A bank, open questions, doc-to-code map (10 rows as of Phase 1).
- [x] **Phase 1 — local substrate.** Solution scaffold, all 5 services
      containerised and healthy, docker-compose + LocalStack + Redis + real
      ZooKeeper verified live end to end. Full detail in "Current state" above.
- [x] **Phase 2 — Collection Service.** Originally delivered as two modules
      (in-memory buffer + `POST /search-events`, then a timed flush worker
      writing to S3 directly) and later **replaced end to end** by
      publishing to Kinesis Data Firehose instead, once the buffer/flush
      design's data-loss gap (see `DESIGN.md` decision 9) got a real fix
      rather than a bigger retry loop. Both versions are recorded here —
      the original build was real, verified work, not a wrong turn erased
      from history; see `DESIGN.md` decision 11 for the full reasoning on
      why it was replaced and what was considered instead (SQS).

      **Original design (superseded 2026-09-25):** `ISearchEventBuffer`/
      `SearchEventBuffer` wrapped a `ConcurrentQueue<BufferedSearchEvent>`;
      `SearchEventFlushWorker` (a `BackgroundService`) drained it on a
      10s timer (and once more on graceful shutdown) to one line-delimited
      JSON object per flush in `suggestx-raw-logs`, keyed
      `{timestamp}-{instanceId}.jsonl` so instances never collided.
      Verified at the time: count-endpoint tracking, correct S3 content,
      no-overwrite across flush cycles, no wasted writes on an empty
      buffer, and — the most valuable test — a mid-interval
      `docker compose stop` proving the graceful-shutdown flush actually
      prevented data loss for a *clean* kill. What it could never close:
      an *ungraceful* kill (SIGKILL, OOM, a crash) between flushes still
      lost whatever was buffered, because durability lived only in that
      one process's RAM.

      **Current design — Kinesis Data Firehose.** `SearchEventBuffer`,
      `SearchEventFlushWorker`, and `CollectionOptions` are deleted, not
      deprecated. `SearchEventsController` now builds a `SearchLogEntry`
      directly from the request and awaits `FirehoseSearchEventPublisher
      .PublishAsync` (`Services/`, wraps `IAmazonKinesisFirehose
      .PutRecordAsync`) before returning — 202 now means *Firehose durably
      accepted this record*, not *this process is holding it in memory*.
      A publish failure returns 503, not a silent drop — the caller, not
      this service, now owns the retry decision. `IPublishedEventStats`
      replaces the old buffered-count tracker with a published-count one,
      backing `GET /search-events/_debug/status`. The delivery stream
      itself (`suggestx-search-events`, S3 destination
      `suggestx-raw-logs/search-events/`, `BufferingHints` of 1MB/10s) is
      provisioned in `infra/localstack/init/01-bootstrap.sh`, idempotently
      (`describe-delivery-stream` first) for the same reason the DynamoDB
      table creation needed the same fix this session (see Environment
      notes).

      **Two real requirements found empirically, not from LocalStack's own
      docs (which don't render a usable API-coverage table through normal
      fetching):** an S3-destination delivery stream needs `sts` and `iam`
      enabled in `SERVICES` alongside `firehose` — Firehose's S3 delivery
      path calls `sts:AssumeRole` against the configured `RoleARN` even
      under emulation, and the very first `PutRecord` attempt failed
      outright without them. And: LocalStack's Firehose emulation does
      **not** honor `BufferingHints.IntervalInSeconds` — every `PutRecord`
      landed in S3 as its own separate object within about a second,
      confirmed under a 10-request concurrent burst that still produced 10
      separate objects rather than one combined one. Documented as a real,
      acknowledged gap in `DESIGN.md` decision 11 rather than assumed to
      match real AWS Firehose's actual batching behavior.

      **A real compatibility question, answered by testing rather than
      assumed either way:** would Aggregator's Phase 3 `ListObjectsV2`/
      `StartAfter` checkpoint logic — built against CollectionService's own
      flat, timestamp-prefixed key format — still work against Firehose's
      very different `search-events/yyyy/MM/dd/HH/streamname-timestamp-
      uuid` key format? Yes, unmodified: restarted Aggregator against 14
      pre-existing Firehose-delivered objects and it caught up on all 14 in
      one poll; posting one more moved the counters by exactly 1, not by
      15, confirming the checkpoint still filters correctly. The
      date-hierarchy prefix is still lexicographically time-ordered at the
      hour level, which is all `StartAfter` needs — same-second collisions
      between two objects' trailing UUIDs can reorder ties, but never skip
      or duplicate anything, and ordering among same-second events doesn't
      affect a frequency count anyway.

      **Verified against the live stack, end to end, through the Gateway**:
      a valid `POST` returned 202 and `publishedCount` incremented; a
      blank query was still rejected with 400 (validation is unchanged);
      a request through `http://localhost:9080/api/search-events`
      succeeded identically to a direct call; the record appeared in S3
      under `search-events/...` with exactly the right JSON content after
      the configured interval; a 3-event burst and a 10-event concurrent
      burst both delivered every event with correct content (as separate
      objects, per the LocalStack limitation above); Aggregator picked up
      every one of them without any code changes, checkpoint filtering
      intact.
- [x] **Local debugging setup (cross-cutting tooling, at the owner's
      request).** Every service got a `Properties/launchSettings.json` with
      a `"{Service} (local)"` profile bound to the *same* port number its
      container publishes (9080 Gateway, 9081 SuggestionService, 9082
      CollectionService, 9083 Aggregator, 9084 TrieBuilder) — deliberately
      not a separate port range like the sibling JameX project uses, since
      the requested workflow is "stop the container, run the same service
      locally on the port it just freed," which only makes sense if they
      share one port. The Gateway (`suggestx-host:host-gateway` alias,
      mirroring JameX's `jamex-host`) reaches whichever process — container
      or local debugger — is currently bound to that port on the host, with
      no configuration to toggle.

      **A real design correction made during verification, not before it:**
      the first version copied JameX's two-destination failover pattern
      (`container` + `local` entries, `FirstAlphabetical` load balancing to
      prefer the container). Testing it live showed the Gateway picking
      between the two destinations inconsistently even while both were
      healthy — never *incorrectly* (both labels always resolved to the
      same actual backend whenever the container was running, since Docker's
      own port-forward answers the host-route address too), but impossible
      to honestly document as deterministic. Root-caused to the fact that
      same-numbered ports make the two destinations structurally
      interchangeable whenever the container is up, which the dual-port
      JameX pattern never has to deal with. Fixed by simplifying to **one**
      destination per cluster, always reached via the host-gateway route —
      removes the ambiguity entirely rather than explaining around it.
      Recorded as `DESIGN.md` decision 10.

      **A second real bug, found by the verification itself, not despite
      it:** the first attempt at verifying the container→local→container
      round trip used `kill <pid>` on the `dotnet run` wrapper process to
      "stop" the local instance between states. `dotnet run` launches the
      compiled executable as a *child* process; killing the wrapper does
      not kill the child on Windows. Every subsequent test in that first
      pass — including the ones that appeared to prove the failover worked
      in both directions — was actually being served by that orphaned,
      never-truly-stopped child the entire time, not by whatever was
      supposedly being tested. Caught by noticing `docker compose ps`
      showed the container as **not running** during a test that was
      nonetheless returning successful responses. Fixed by finding the
      real listening PID via `netstat -ano | grep :PORT` /
      `Get-NetTCPConnection` before each kill, not trusting the launcher's
      own PID, and redoing every verification from a confirmed-clean state.

      **Verified against the live stack, honestly this time** (see
      `DEBUGGING.md`'s own "Verified" section for the full blow-by-blow):
      container-only → Gateway reaches the container, confirmed via its
      own buffered-event count incrementing; container stopped, local
      running → Gateway reaches the local process, confirmed the same way
      while `docker compose ps` showed the container absent; local killed
      (correct PID this time) and container restarted → Gateway reaches the
      container again, confirmed via a *fresh* buffered count on the
      restarted container; a local start attempted while the container
      still held the port failed to bind, exactly as designed, and left the
      container completely unaffected. Repeated for both Gateway-routed
      services (SuggestionService, CollectionService) — Aggregator and
      TrieBuilder have `launchSettings.json` profiles too but no Gateway
      route to verify through yet (Phase 6).

- [x] **Phase 3 — Aggregator.** Delivered as three modules, in the order
      below (Module 3 built out of order, ahead of Module 2, at the
      owner's request):
- [x] **Module 1 — read new raw-log objects on a timer.**
      `S3RawLogReader` (`Services/`) lists `suggestx-raw-logs` via
      `ListObjectsV2` with `StartAfter` set to the last processed key
      (handling pagination via `ContinuationToken`), reads and parses each
      new object's JSONL lines into `SearchLogEntry`, one malformed line
      logged and skipped rather than sinking the whole batch.
      `InMemoryAggregatorCheckpoint` (`Services/`) tracks the last processed
      key — in-memory only for now, so a restart currently reprocesses from
      the start of the bucket; durable persistence is Module 3, not silently
      assumed done early. `RawLogPollingWorker` (`Jobs/`, a
      `BackgroundService` on `Aggregator:PollIntervalSeconds`, default 15s)
      polls immediately on startup (unlike CollectionService's flush worker,
      which waits for its first tick — a fresh Aggregator may start well
      after raw logs already exist, so waiting before the first read would
      be a pure, avoidable delay), advances the checkpoint per-batch rather
      than once per cycle so a mid-cycle crash only re-reads the batches it
      hadn't finished, and catches read failures without crashing the poll
      loop. `AggregatorDebugController` exposes `GET /_debug/status`
      (batches/entries processed, last poll time, last processed key) —
      module 1 does nothing durable yet, so this is the only way to observe
      it working.

      **A real bug, found live, not by reading the SDK's docs closely
      enough first:** `ListObjectsV2Response.S3Objects` comes back `null`,
      not an empty list, when a page has no matches — which is the
      overwhelmingly common outcome of a poll cycle that finds nothing new.
      The first version's unguarded `response.S3Objects.Select(...)` threw
      `ArgumentNullException` on exactly that case; caught by the worker's
      own error handling (logged, didn't crash the loop, self-healed next
      tick), but "the normal case throws and is silently absorbed by a
      safety net meant for actual transient failures" is a real bug, not
      something to leave relying on resilience to paper over. Fixed with an
      explicit null/empty check before the `Select`.

      **Verified against the live stack:** on startup, immediately and
      correctly processed all 15 pre-existing objects from earlier Phase 2/
      debugging-session testing (24 entries total) in one poll, proving
      pagination and a null `afterKey` both work; posting one new event and
      waiting a cycle moved `batchesProcessed` from 15→16 and
      `entriesProcessed` from 24→25 — not 15→30 — proving `StartAfter`
      genuinely filters to only what's new, not re-reading the backlog
      every cycle; three consecutive idle poll cycles (45s, nothing posted)
      produced zero errors and zero log lines after the `S3Objects` fix,
      where the unfixed version had thrown on the very first idle cycle it
      hit; a final fresh event confirmed end-to-end after the fix,
      `batchesProcessed`/`entriesProcessed` advancing by exactly one and the
      correct query text appearing in the log line.
- [x] **Module 3 — durable checkpoint persistence.** `InMemoryAggregatorCheckpoint`
      deleted, replaced by `DynamoAggregatorCheckpoint` (`Services/`),
      backed by a new table it owns exclusively,
      `suggestx-aggregator-checkpoints` (partition key `checkpointId`,
      provisioned idempotently in `01-bootstrap.sh` the same way as the
      other tables) — kept separate from `suggestx-phrase-frequencies` so
      operational state never needs special-casing in anything that later
      scans the real frequency data. `IAggregatorCheckpoint` grew an
      `InitializeAsync` (loads the last known key once at startup into an
      in-memory cache) and changed `Advance` to an async `AdvanceAsync`
      that write-throughs to DynamoDB on every call — reads stay a cheap
      in-memory property access; only writes pay a DynamoDB round trip.
      `RawLogPollingWorker.ExecuteAsync` now awaits `InitializeAsync`
      before the polling loop starts, and awaits `AdvanceAsync` per batch
      (unchanged per-batch-not-per-cycle reasoning from Module 1 — a
      mid-cycle crash still only re-reads what it hadn't finished). A
      failed durable write is logged, not thrown — the in-memory cache
      already advanced, so the process keeps working correctly either way;
      only a restart before the next successful write would resume from an
      older point, safely re-reading a few already-processed batches.

      **A second occurrence of the exact same bug class as Module 1's,
      caught the same way — live, not from documentation:**
      `GetItemResponse.Item` is `null`, not an empty dictionary, when no
      item exists for the given key — identical shape to
      `ListObjectsV2Response.S3Objects` being `null` instead of empty for
      "no matches." The first version's unguarded
      `response.Item.TryGetValue(...)` threw `NullReferenceException` on
      every first-ever run (exactly the case that matters most — a brand
      new checkpoint table with nothing in it yet). Fixed with an explicit
      null/count check before touching the dictionary. Worth remembering
      as a pattern for *any* future AWS SDK response property, not just
      these two: several "get me one thing" APIs return `null` for "found
      nothing" rather than an empty collection, and assuming otherwise is
      an easy, repeatable mistake.

      **Verified against the live stack, the real test being a genuine
      container restart, not just a fresh start:** posted 2 events,
      confirmed both processed (`batchesProcessed: 2`); confirmed the
      checkpoint item actually existed in DynamoDB via
      `awslocal dynamodb get-item`, matching the debug endpoint's
      `lastProcessedKey` exactly; **restarted the Aggregator container**
      and confirmed the startup log read `Checkpoint loaded: resuming
      after <the exact same key>` — and, critically, the container's log
      for that run showed **no** `Read 1 entries from ...` lines for either
      of the 2 already-processed objects, proving they were genuinely
      skipped, not just that the counter looked right; posted one more
      event after the restart and confirmed `batchesProcessed: 1` (not 3),
      the final proof that only the truly new object was read.
- [x] **Module 2 — the actual map-reduce.** `IPhraseFrequencyWriter`/
      `DynamoPhraseFrequencyWriter` (`Services/`): for each batch, "map" is
      an in-memory `Dictionary<string, long>` counting each normalized
      phrase's occurrences within that one batch; "reduce" is one atomic
      DynamoDB `UpdateItem` (`ADD frequency :inc`) per *unique* phrase in
      the batch, not one write per entry — ten people searching the same
      thing in one S3 object costs one write, not ten. `ADD` both creates
      the row (initializing the count) and increments an existing one, so
      no separate existence check or read-before-write is needed, and no
      race between concurrent writers. Normalization
      (`query.Trim().ToLowerInvariant()`) lives in exactly one place — the
      doc's own stated assumption ("for simplicity, this data is
      case-insensitive") is enforced here and nowhere else; every later
      reader of this table (TrieBuilder included) can trust every `phrase`
      value is already normalized and never has to re-derive that.

      **Ordering is the real correctness property, not the ADD itself:**
      `RawLogPollingWorker` applies a batch's counts *before* advancing its
      checkpoint, and only advances if the write succeeded — reversing that
      order would let a crash between the two silently lose a batch's
      counts forever (checkpoint says "done," but the counts never landed).
      A failure now stops the whole poll cycle rather than skipping to the
      next batch, because checkpoints must advance strictly in order:
      skipping past a failed batch to a later one that succeeds would
      permanently strand the failed batch's counts (the checkpoint moves
      past its key, so it is never read again). The next poll cycle retries
      from the same checkpoint instead.

      **A residual gap, named rather than hidden:** there is still a narrow
      window — an actual process crash between a successful `ADD` and the
      checkpoint's own durable write — where a restart would re-read and
      re-count that one batch, double-counting it. Not closed here,
      deliberately: closing it needs either a per-object idempotency guard
      (a second table tracking "have I applied this exact S3 key's counts
      yet") or a transactional write spanning two DynamoDB tables, real
      added complexity for a rare, bounded (at most one batch's worth),
      self-correcting-over-time overcount — consistent with this project's
      running theme (decisions 2 and 9) of accepting small, bounded
      inaccuracy over defensive machinery for edge cases far rarer than the
      thing they'd protect against.

      **Verified against the live stack, checking DynamoDB directly, not
      just the debug endpoint:** posted "jazz piano" and "jazz age" as two
      separate events — both appeared as two separate rows with
      `frequency: 1` each; posted "jazz piano" again — its row's
      `frequency` went 1→2, confirming `ADD` accumulates correctly across
      separate poll cycles, not just within one batch; posted "JAZZ Piano"
      (mixed case) — it merged into the *same* `jazz piano` row
      (`frequency` 2→3) rather than creating a second row, confirming
      case-insensitive normalization works exactly as the doc assumes; zero
      errors across the entire sequence, confirmed against the full
      container log, not just the absence of an error status code.

- [x] **Phase 4 Module 1 — the compressed trie itself, built from real
      data.** `TrieNode`/`CompressedTrie` (`Domain/`): insertion is
      plain, uncompressed, one character per node — the doc presents
      compression as a distinct transformation over an already-built
      trie, not part of insertion itself, and splitting an existing
      multi-character segment mid-insert would be real extra complexity
      this two-phase approach (insert everything, then compress once)
      avoids entirely. `Compress` merges chains of single-child,
      **non-terminal** nodes into one multi-character segment — a
      terminal node is never merged past even with exactly one child,
      since it has to stay individually addressable (e.g. "CAT" is
      terminal but still has a child continuing to "CATS").
      `GetTopMatches(prefix, limit)` walks to the node a prefix lands on
      — handling a prefix that ends *partway through* a compressed
      segment, not just at a node boundary — then collects every
      descendant terminal and sorts by frequency descending, phrase
      alphabetically ascending as a deterministic tie-break.

      `DynamoPhraseFrequencyReader` (`Services/`) does a full `Scan` of
      `suggestx-phrase-frequencies` every build cycle (not an
      incremental/checkpointed read like Aggregator's — a trie rebuild
      needs the complete current dataset, not a delta), applying the
      now-familiar null-vs-empty defensiveness to `Items` and
      `LastEvaluatedKey` proactively this time, rather than hitting that
      bug a third time. `TrieBuildWorker` (`Jobs/`, a `BackgroundService`
      on `TrieBuilder:BuildIntervalSeconds`, default 20s) reads, builds,
      and publishes a finished trie via `ITrieHolder` — a reader can only
      ever see a previous complete trie or a new complete trie, never a
      partially-built one, the same blue/green principle as decision 8
      applied in-process instead of across Redis versions.
      `TrieBuilderDebugController` exposes `GET /_debug/status` and, more
      usefully, `GET /_debug/search?prefix=&limit=` — a direct line to
      the real `GetTopMatches` logic against real data, since nothing
      else can observe the trie working until SuggestionService exists in
      Phase 5.

      **Verified against the live stack, with real, non-trivial data —
      not a toy example:** seeded 7 phrases through the real pipeline
      (`guitar lesson`, `guitar solo`, `guitar chords`, `java tutorial`,
      `jazz age`, `jazz piano` ×6, `python programming`), producing a
      10-then-11-node compressed trie for 6-then-7 phrases. Confirmed via
      `_debug/search`: `"jazz"` returned both jazz phrases correctly
      ranked by frequency (piano, freq 6, before age, freq 1); `"ja"` —
      landing exactly on the branch point between "java" and "jazz" —
      correctly returned all three java/jazz phrases; `"guitar"` returned
      all three guitar phrases tied at frequency 1, broken alphabetically
      (chords, lesson, solo); `"xyz"` correctly returned no results, not
      an error; `"JAZZ"` (uppercase) returned the identical result to
      `"jazz"`, confirming prefix normalization. Posting one more event
      (`guitar chords`) and waiting a full cycle moved the trie from 6→7
      phrases and correctly added it to `"guitar"`'s results — proving the
      periodic rebuild genuinely picks up new data, not just that the
      first build worked.

- [x] **Phase 4 Module 2 — flatten `prefix → top-N` into a versioned Redis
      namespace.** `CompressedTrie.FlattenPrefixes(maxPrefixLength, topN)`
      (`Domain/CompressedTrie.cs`) walks the trie exactly once per build
      cycle: for every child node it computes that node's own top-N
      exactly once, then reuses the identical answer for every prefix
      length that lands inside that node's own segment (only recomputing
      at an actual branch point) — a prefix stopping mid-segment shares
      the same subtree as one stopping at the segment's end, so there's
      no reason to recompute per character. Bounded by
      `TrieBuilder:MaxPrefixLength` (default 6, decision 6) and
      `TrieBuilder:TopN` (default 10).

      `RedisFlattenedCachePublisher` (`Services/IFlattenedCachePublisher.cs`)
      writes the whole new version first — every `trie:v{N}:{prefix}` key,
      JSON-serialized `SuggestionItem` list — then deletes the *exact*
      prefix set it remembered writing last cycle under `trie:v{N-1}:*`,
      never a `KEYS`/`SCAN` sweep. New version fully in place before the
      old one is touched — decision 8's blue/green ordering, this time
      across Redis versions rather than in-process. `TrieBuildWorker`
      keeps the version counter and previous-prefix-set as private,
      in-memory state — deliberately not yet durable, since nothing reads
      a "current version" pointer until Module 3 introduces the
      ZooKeeper-coordinated one. `ITrieBuildStats`/`TrieBuildStats` track
      `CurrentVersion`/`FlattenedPrefixCount`/`LastBuildAt`, surfaced on
      `GET /_debug/status` alongside the existing phrase/node counts.

      **Verified against the live stack, across three real, consecutive
      build cycles** (20s apart, container logs read directly, not
      inferred): cycle 1 published version 1, 23 prefixes, 0 keys deleted
      (nothing to clean up yet); cycles 2 and 3 each published a new
      version, 23 prefixes, and deleted exactly the 23 keys from the
      version before it — confirmed directly against Redis
      (`redis-cli KEYS 'trie:*'`) that only the current version's keys
      ever exist at once, never a mix of two versions or zero. Inspected
      actual values, not just key presence: `trie:v3:j` and `trie:v3:ja`
      — both landing inside the same "j" segment before the java/jazz
      branch point — returned the byte-identical ranked JSON (jazz piano
      freq 6, java tutorial freq 1, jazz age freq 1), confirming the
      shared-segment reuse in `FlattenPrefixes` is real, not just
      theoretically correct; `trie:v3:guitar` returned all three guitar
      phrases tied at frequency 1 in the same alphabetical tie-break
      order as Module 1; `trie:v3:python` returned its single phrase.
      `GET /_debug/status` reported `currentVersion: 3`,
      `flattenedPrefixCount: 23`, matching the logs and Redis exactly.

- [x] **Phase 4 Module 3 — S3 snapshot persistence + a real ZooKeeper
      `current_version` znode, closing Module 2's accepted gap.** Added the
      `ZooKeeperNetEx` client (the standard .NET async ZooKeeper client —
      its API mirrors the Java client almost verbatim, `org.apache.zookeeper.*`
      namespace included) to `SuggestX.ServiceDefaults`, registered as a
      shared singleton the same way Redis/AWS clients are, since both
      TrieBuilder (writer) and the future SuggestionService (reader) need it
      — DESIGN.md decision 4.

      `S3TrieSnapshotStore` (`Services/ITrieSnapshotStore.cs`) writes each
      cycle's full flattened `prefix → top-N` result as one JSON object,
      `v{N}.json`, to `suggestx-trie-snapshots` — the same content just
      published to Redis, not a separate serialization of the raw trie,
      since that's genuinely enough to recover both the "what was
      published" state and (per the bucket's own doc comment) let a
      restarted TrieBuilder skip an expensive re-scan-and-rebuild if it
      ever needed to serve from the snapshot directly.

      `ZooKeeperVersionPublisher` (`Services/IZooKeeperVersionPublisher.cs`)
      owns the one znode this system writes today,
      `/suggestx/trie/current_version` — created as a plain UTF-8 integer
      string, ancestor znodes (`/suggestx`, `/suggestx/trie`) created
      idempotently first since ZooKeeper has no recursive create.
      `TrieBuildWorker` now, on startup, reads that znode once
      (`RecoverAsync`): if a version exists, it resumes `_version` from
      there and loads that version's S3 snapshot to seed
      `_previousPrefixes`, instead of starting fresh at 0/empty. After every
      successful Redis publish, it saves the S3 snapshot and flips the
      znode — **only after** Redis already has the new version live, the
      same blue/green ordering as Module 2's own Redis-key ordering, one
      level up (decision 8, decision 14). A failure saving to S3/ZooKeeper
      is logged and does not roll back the Redis publish, which already
      succeeded — only this cycle's *recovery* state is stale until the
      next successful one, an accepted small gap in the same spirit as
      decision 13's checkpoint race, not a correctness problem for what's
      actually being served.

      **Verified against the live stack, with a genuine container
      restart, not a simulated one:** confirmed the S3 object for each
      version (`awslocal s3 ls`/`cp`) matched Redis's published content
      exactly; confirmed the znode's value via `zkCli.sh get` tracked the
      current version after every cycle. Then restarted the running
      `trie-builder` container mid-sequence (`docker compose restart`)
      while ZooKeeper held version 3: the log showed `Recovered version 3
      from ZooKeeper with 28 known prefixes to clean up on next publish`
      immediately on startup, followed by the next cycle publishing
      version 4 — not resetting to 1 — and `Published trie version 4: 28
      prefixes written, 28 old keys removed`, confirmed directly against
      Redis that only `trie:v4:*` existed afterward, no leftover `v3`
      keys. `GET /_debug/status` reported `recoveredOnStartup: true,
      recoveredVersion: 3` alongside `currentVersion: 4`, matching the
      logs and Redis exactly. Also found and cleaned up real forensic
      evidence of the exact gap this module closes: a stray
      `trie:v653:*` key set from an ungraceful restart during earlier
      Module 1/2 development (before ZooKeeper tracking existed), never
      cleaned up because nothing durable remembered it needed to be —
      exactly the failure mode Module 3 now prevents going forward.

- [x] **Phase 5 Module 1 — the real read path: ZooKeeper version lookup,
      one Redis `GET`, the decision-6 over-bound fallback.**
      `CurrentVersionPoller` (`Jobs/`) polls
      `/suggestx/trie/current_version` on a timer (5s default) via a small,
      SuggestionService-local `ZooKeeperVersionReader` — polling rather
      than a ZooKeeper watch, for consistency with every other
      cross-service handoff in this system already being poll-based, and
      to avoid the real complexity of re-arming a one-shot ZK watch after
      every fire for a benefit (near-instant pickup vs. a few seconds'
      staleness) that doesn't matter given decision 7 already accepts a
      much larger staleness window upstream. The learned version is cached
      in `ICurrentTrieVersion` (`CurrentTrieVersionHolder`, the same
      volatile-field pattern as TrieBuilder's own `ITrieHolder`) — a
      request never triggers a ZooKeeper call itself.

      `RedisSuggestionReader` (`Services/ISuggestionReader.cs`) does
      exactly what decision 2 always meant literally: normalize the
      prefix (`Trim().ToLowerInvariant()`, matching Aggregator's own
      normalization so keys line up), one `StringGetAsync` against
      `trie:v{version}:{prefix}`, deserialize, done — no trie, no
      DynamoDB, no S3. Beyond `MaxPrefixLength` (decision 6's bound), it
      truncates to the longest prefix TrieBuilder actually flattened,
      fetches that, and filters the small candidate list down to phrases
      that still start with everything the user actually typed — a real
      degradation (a true top-N match outside the truncated prefix's own
      top-N is missed), not a silently wrong answer.
      `SuggestionsController` exposes `GET /suggestions?prefix=&limit=`,
      already reachable through the Gateway's existing
      `/api/suggestions/{**catch-all}` route from Phase 1.

      **Verified against the live stack, extensively:** `guitar`, `jazz`,
      and the `ja` branch point (landing on both "java" and "jazz")
      all returned correctly ranked results identical to TrieBuilder's own
      `/_debug/search`; `xyz` correctly returned an empty list, not an
      error; `JAZZ` (uppercase) returned results identical to `jazz`,
      confirming normalization; a missing `prefix` returned `400`; the
      same request through the real Gateway proxy (`/api/suggestions`)
      returned an identical response to the direct call. The over-bound
      fallback was verified precisely, not just "doesn't crash": `guitar
      lesso` (13 characters, past the 6-character bound) correctly
      truncated to `guitar`, fetched all three guitar phrases, and
      filtered down to exactly `guitar lesson` — proving the filter
      genuinely discriminates, not just truncates; `guitarzzz` (matches
      no real phrase) correctly returned empty rather than a false
      positive. `limit` was verified to actually bound the result count.
      Live version tracking was verified as an active loop, not a
      one-time read: `GET /suggestions/_debug/status` matched
      TrieBuilder's own reported version, and re-checking a few seconds
      later after TrieBuilder advanced showed SuggestionService's cached
      version catch up on its own, unprompted.

      **A real gap found by testing the ZooKeeper-unreachable claim
      live, not by inspection — see decision 17 and the corrected
      failure-mode row below:** stopping the real `zookeeper` container
      confirmed the poller logs the connection failure and keeps serving
      the last-known version (as the failure-mode table already claimed)
      — but because TrieBuilder's own Redis blue/green cleanup
      (Module 2) keeps running every build cycle regardless of whether
      *its* ZooKeeper writes are succeeding, an outage spanning more than
      one TrieBuilder build cycle lets Redis roll forward and delete the
      exact version SuggestionService is still frozen on, pointing it at
      keys that no longer exist. A `guitar` request during the outage
      window returned an empty list — a real, live-reproduced gap, not a
      hypothetical one. Restarting ZooKeeper let both sides self-heal
      within one cycle each with no manual intervention, confirmed live.
      This refines — not just confirms — what "degrades to stale" means
      in this table: true only for outages shorter than one TrieBuilder
      cycle.

### In progress

- [x] **Phase 5 — decision: accept the ZooKeeper-outage gap (decision 17)
      as a documented, bounded limitation** rather than build either
      candidate fix (pausing TrieBuilder's Redis cleanup during an outage,
      or giving SuggestionService a way to distinguish "should have data"
      from a genuine no-match). Consistent with decisions 9/13's standing
      pattern: a rare, self-healing, well-understood gap doesn't get
      defensive machinery built around it just because it's real.

- [x] **Phase 5 — real latency measurement**, not manual `curl` timing.
      200 sequential requests for a real, matching prefix (`guitar`)
      against `SuggestionService` directly: p50 4.08ms, p95 6.60ms, p99
      23.37ms, max 28.41ms, avg 4.96ms. The same 200 requests through the
      real Gateway proxy (`/api/suggestions`, the actual client-facing
      path): p50 6.63ms, p95 9.93ms, p99 22.91ms, max 29.76ms, avg
      7.67ms — a consistent, small (~2-3ms) proxy hop overhead, nothing
      surprising. 100 requests against the over-bound fallback path
      (`guitar lesso`, past the 6-character bound, exercising the
      truncate-fetch-filter logic) averaged 4.17ms, essentially identical
      to the direct-match cost — confirming the in-process filter really
      is the trivial O(≤`TopN`) operation decision 16 claimed, not a
      hidden cost. Every measured percentile, on every path, comfortably
      inside the doc's own "under 200ms" NFR — this was real end-to-end
      request latency through the actual running stack (Kestrel → Redis
      round trip → JSON serialization → Gateway proxy where applicable),
      not a synthetic microbenchmark.

**Phase 5 is now complete.**

### In progress

Nothing — Phase 5 fully verified: read path, the ZooKeeper-outage gap
decision, and real latency numbers.

### Next up (immediate)

**Phase 6 — Gateway + frontend**, not started:

1. Decide the frontend's shape: a real debounced search box (client-side
   debounce is one of the doc's own stated latency levers, not yet built
   anywhere in this system) calling `GET /api/suggestions?prefix=` through
   the Gateway, plus — per the doc's `addToDatabase(query)` API — firing
   `POST /api/search-events` once a search is actually submitted, closing
   the loop back into the write pipeline this whole system already runs.
2. An insights panel showing live system state: current trie version,
   flattened prefix count, aggregation lag — surfacing the various
   `/_debug/status` endpoints already built across every service rather
   than building new instrumentation from scratch.
3. Verify: a real browser session, typing a prefix, sees debounced
   requests and real suggestions; a submitted search is visible flowing
   through Collection → Aggregator → TrieBuilder → back into
   SuggestionService within one full pipeline cycle, watched live.

---

## Next up

Ordered. Each phase leaves the build green **and** updates `README.md` and
`DESIGN.md`.

1. ~~Local substrate~~ — done. Docker Compose, LocalStack (S3 + DynamoDB),
   real Redis, real ZooKeeper, solution scaffold, all 5 services
   containerised and health-checked.
2. ~~Collection Service~~ — done. `POST /search-events` + buffer, timed
   flush to S3, graceful-shutdown flush, all verified live.
3. ~~Aggregator~~ — done. S3 raw logs → DynamoDB frequency table, durably
   checkpointed batch worker, all verified live including across a real
   container restart.
4. ~~Trie data structure + Trie Builder~~ — done. Compressed trie in memory
   (Module 1), top-N flattening into a versioned Redis namespace (Module 2),
   and S3 snapshot persistence + a real ZooKeeper `current_version` znode
   (Module 3), all verified against real branching data, real build
   cycles, and a genuine container restart.
5. ~~Suggestion Service~~ — done. ZooKeeper version lookup → Redis `GET` →
   top-N response, verified live including the decision-6 over-bound
   fallback, real sub-10ms-p95 latency, and a real ZooKeeper-outage gap
   found live and accepted as documented (decision 17).
6. **Gateway + frontend** — YARP routing, a real debounced search box, an
   insights panel showing live trie version/partition state and aggregation
   lag. Not started.
7. **Evaluation extras** — personalization (blend client-cached recent
   searches with global ranking), client-side optimizations (debounce, input
   threshold, early connection, edge-cache headers), fault-tolerance
   verification (kill ZooKeeper/a Redis partition/TrieBuilder mid-cycle and
   confirm the failure-mode table's mitigations actually hold). Not started.

---

## Environment notes

- **LocalStack's `SERVICES` list needed `firehose,sts,iam`, not just
  `firehose`, for an S3-destination Firehose delivery stream to actually
  deliver.** `CreateDeliveryStream` and `PutRecord` both succeeded with
  only `firehose` enabled, which looked like enough — the failure only
  showed up as `Service 'sts' is not enabled` deep inside a `PutRecord`
  call, because Firehose's S3 delivery path calls `sts:AssumeRole` against
  the stream's configured `RoleARN` even under emulation. Worth checking
  for with any other LocalStack service that models IAM-role-based access
  to another service (Lambda destinations, cross-service event rules,
  etc.) — the dependency isn't always obvious from the one API call that's
  actually failing.
- **DynamoDB `create-table` in the bootstrap script wasn't idempotent, and
  this session was the first time it mattered.** LocalStack's DynamoDB data
  survives a container restart even on the free tier (its own SQLite-backed
  persistence, independent of the licensed Persistence feature — see the
  note further down). Every previous restart this project happened to also
  wipe or not touch that table; this session's `--force-recreate` while
  testing Firehose hit it for real: `ResourceInUseException: Table already
  exists`, non-zero exit, bootstrap script stopped before finishing.
  **Fixed by checking `describe-table` before `create-table`** — applied
  the same pattern proactively to the new `create-delivery-stream` call
  too, rather than waiting to hit the identical bug there as well.
- **LocalStack's Firehose emulation does not honor `BufferingHints
  .IntervalInSeconds`.** Every `PutRecord` — including under a 10-request
  concurrent burst — landed in S3 as its own separate object within about
  a second, never combined with others from the same window. Real AWS
  Firehose would batch these; this is purely an emulator limitation.
  Means local testing can prove the delivery *pipeline* works (record in,
  correct content out, durably) but cannot demonstrate the actual
  batching/cost-reduction behavior that's the real-world reason to choose
  Firehose over a naive one-object-per-event write. Full reasoning in
  `DESIGN.md` decision 11.
- **`dotnet run` launches the compiled executable as a child process — killing
  the wrapper doesn't kill the child, on Windows.** Cost a full round of
  local-debugging verification (see the Phase 2 entry above): `kill <wrapper
  pid>` left the actual `SuggestX.*.exe` still bound to its port, silently
  answering every subsequent "test" regardless of what was supposedly being
  exercised. **When you need to reliably stop a locally-run service, find
  the PID actually listening on its port** (`netstat -ano | grep :PORT` in
  Git Bash, or `Get-NetTCPConnection -LocalPort PORT` in PowerShell) and
  stop *that* PID — never assume the launcher's own PID covers it. A
  container that's supposedly involved in a test not showing up in
  `docker compose ps` while requests still succeed is the tell that
  something else is answering instead.
- **When running a service's compiled DLL directly (`dotnet path/to.dll`)
  instead of via `dotnet run`, the content root becomes the shell's current
  directory, not the project folder** — so `appsettings.json` sitting next
  to the DLL is silently not found, and every config value that isn't set
  another way comes back empty. Surfaced as a confusing AWS-credential
  resolution failure with no obvious link to the real cause. Always debug
  locally via `dotnet run --project <path> --launch-profile "<name>"` (or
  F5 in Visual Studio), never by invoking the built DLL directly.
- **Interview-prep framing removed from CLAUDE.md/DESIGN.md/README.md
  (2026-09-21, at the owner's request)** — this should read as an
  application's own documentation, not as interview-study material. Two
  passes: first removed the goal-framing language ("the goal is interview
  preparation through implementation" in both CLAUDE.md and DESIGN.md,
  reworded to "understanding through implementation"; "preparing for a
  system design interview" from CLAUDE.md's Owner context; "interview-style
  questions" from DESIGN.md's intro, now just "the questions the design doc
  itself poses"). A first attempt kept the literal source citation
  (*Grokking Modern System Design Interview*, the book/course these chapters
  are from) on the theory that naming a source isn't the same as framing a
  goal — the owner then asked for that gone too, so a second pass replaced
  every occurrence with a plain reference to the PDFs themselves
  (`` `../*.pdf` `` / "the Typeahead Suggestion System design chapters") in
  CLAUDE.md, DESIGN.md and README.md alike. Lesson: when asked to remove
  "all text related to X," a citation naming X is still text related to X —
  don't assume a narrower reading than what was actually asked for.
- **The project was renamed `Typeahead` → `SuggestX` (2026-09-21, at the
  owner's request)**, mirroring JameX's own pattern of a whimsical project
  name distinct from the topic folder (`TypeheadSuggestion/` — unrenamed,
  still holds the PDFs — vs. `SuggestX`, the app identity inside `App/`).
  Renamed: the solution (`SuggestX.slnx`), all 7 project
  folders/`.csproj`/namespaces (`SuggestX.Contracts`, `SuggestX.ServiceDefaults`
  and its two files `SuggestXOptions.cs`/`SuggestXHostingExtensions.cs`, and
  the 5 service projects), every AWS resource name (`suggestx-raw-logs`,
  `suggestx-trie-snapshots`, `suggestx-phrase-frequencies`), the ZooKeeper
  root path (`/suggestx`), and every Docker container/compose project name
  (`suggestx-*`). Left alone, deliberately: "typeahead" as the *domain* term
  in prose (e.g. README's "What is a typeahead suggestion system?") — only
  the project's own identity moved, not the vocabulary describing what kind
  of system it is. Rebuilt and re-ran the full Phase 1 verification battery
  (build, all 8 containers, health checks, Gateway proxy check, S3/DynamoDB/
  Redis/ZooKeeper) against the new names — all green, nothing broke.
- **The whole app was moved into an `App/` subfolder (2026-09-21), matching
  JameX's `Youtube/App` layout** — the six source PDFs stay one level up, at
  `TypeheadSuggestion/`, exactly mirroring `Youtube/*.pdf` next to
  `Youtube/App/`. Nothing in the docs needed path changes: every reference
  to the source PDFs already used `../*.pdf`, which is still correct one
  level down from `TypeheadSuggestion/` into `App/`. The git repo root
  (empty, no commits yet) moved with everything else, so it's now rooted at
  `App/`, not `TypeheadSuggestion/` — also matching JameX, where `Youtube`
  itself is not a git repo but `Youtube/App` is.
- **A stray `TypeheadSuggestion/System Design/` folder existed before this
  move, holding a duplicate copy of all six PDFs** — not created by any
  command in this session; most likely an editor or sync-tool artifact from
  the parent drive folder's own name ("System Design"). Confirmed it held
  nothing else before removing it, and moved the real PDFs back to
  `TypeheadSuggestion/` root as part of the same reorg.
- **Moving `src/` failed twice with lock errors before succeeding** — first
  `Permission denied`, traced to VS Code's C# Dev Kit extension running a
  background `Microsoft.VisualStudio.ProjectSystem.Server.BuildHost.dll`
  process that had the solution open and file handles held on project files
  (killed via its PID, found with `Get-CimInstance Win32_Process`, since
  `dotnet build-server shutdown` alone did not release it — that command
  only manages the VBCSCompiler/MSBuild servers, not VS Code's own project
  system host). Then `Device or resource busy`, traced to this session's own
  Bash *and* PowerShell tool processes both having their working directory
  parked inside `src/` from earlier commands — `cd`ing both out resolved it.
  When a directory move is inexplicably locked on Windows, check for a
  lingering `dotnet.exe`/build-host process before assuming the tool
  sessions' own `cwd` is the cause, and check both.
- Rebuilt `dotnet build SuggestX.slnx` and `docker compose up -d --build`
  fresh from the new `App/` location after the move — both green, all 8
  containers healthy, confirming the reorg didn't silently break anything
  (Docker's build `context: .` in `docker-compose.yml` is already relative
  to the compose file's own location, so it needed no changes).

- **JameX's compose stack is often running on the same machine and claims
  6379 (Redis), 4566 (LocalStack) and the 8080-8090 range.** This project's
  ports are deliberately shifted +1000 (`9080`-`9084`, `6380`, `4567`) so
  both can run at once. See `CLAUDE.md`'s Commands section for the full port
  table and the reasoning.
- **LocalStack's freemium tier requires a valid `LOCALSTACK_AUTH_TOKEN` for
  license activation even to use community services like S3/DynamoDB** —
  without one, the container exits immediately with code 55 ("License
  activation failed"). Fixed the same way JameX does: a gitignored `.env`
  with a personal token, read automatically by `docker compose` from the
  project root. This project reuses JameX's token (same owner, same
  account) rather than requiring a second one — copy `LOCALSTACK_AUTH_TOKEN`
  from `C:\System Design\Youtube\App\.env` if this project's `.env` is ever
  lost. **Writing a real token into a file, or running a command that
  depends on one, is flagged by the harness's auto-mode classifier as
  "credential materialization"** — the `docker compose up -d` command that
  actually starts the token-dependent container needed the owner to run it
  directly (via the `!` prefix) rather than the assistant running it.
- **`LOCALSTACK_HOST` must stay the container-internal port (`4566`), never
  the host-published one (`4567`), even though the two differ in this
  project's compose file.** It's what LocalStack's own internal tooling —
  including the `awslocal` CLI the bootstrap script runs *inside* the
  container — uses to build its endpoint URL, and 4567 doesn't exist from
  inside the container's own network namespace (it's a host-side port
  mapping only). Got this wrong on the first attempt (copied JameX's
  reasoning, where the two happen to be equal, without checking it still
  held once the ports were shifted) — the bootstrap script failed with
  `Could not connect to the endpoint URL: "http://localhost:4567/..."`
  until fixed. JameX's version of this variable exists for a *different*
  reason (presigned URLs must resolve for a real browser) that does not
  apply here at all, since nothing in this system ever hands S3 a browser
  a presigned URL — worth remembering before reflexively copying that
  pattern into a future AWS-backed project that has yet a third shape.
- **Local `dotnet build` on Windows leaves `obj/`/`bin/` directories full of
  Windows-specific `project.assets.json` paths that break the Linux Docker
  build if copied in.** First `docker compose up --build` failed with a
  bizarre `Unable to find fallback package folder 'C:\Program Files...'`
  error from inside the Linux container — root cause was `COPY src/ src/`
  picking up the host's stale `obj/` output ahead of the container's own
  `dotnet restore`. Fixed with a `.dockerignore` excluding `**/bin/` and
  `**/obj/`. Add this file *before* the first `docker compose build` on any
  future .NET project in this pattern, not after hitting the error.
- **`zkCli.sh <cmd> <args>` as separate `docker exec` arguments does not
  work on this ZooKeeper 3.9 image** — it errors with "Path must start with
  / character" even when the path clearly does. The working form pipes
  commands to the CLI's interactive stdin instead: `printf 'create /x y\nget
  /x\nquit\n' | docker exec -i suggestx-zookeeper zkCli.sh -server
  localhost:2181`.

---

## Open questions / deferred

See `DESIGN.md` §4 for the full list with reasoning. Summary:

- Hot-prefix imbalance under range partitioning — named by the source doc,
  not solved.
- Aggregator run-overlap if a batch takes longer than its own interval — no
  lock/lease yet.
- Fuzzy/typo-tolerant matching — a discussion question in the source, not a
  requirement.
- Personalization — designed, not built (Phase 7).
