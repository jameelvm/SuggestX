# SuggestX — system design

Companion to `README.md`. README explains *how* the system was built, phase by
phase; this file explains *why it is shaped this way* — every non-obvious
choice, every failure mode it was built to survive, and the questions the
design doc itself poses (each chapter ends with one or more "show answer"
prompts — answered here, against this build, not abstractly).

## §0 Source material

`../*.pdf` — the "Typeahead Suggestion System" design chapters, six in total:
overview, requirements, high-level design, data structure (trie), detailed
design (suggestion service + assembler), evaluation. Read in full
2026-09-17 before any scaffolding was written.

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

9. ~~A failed S3 flush is logged and dropped, not retried — and an
   ungraceful `CollectionService` kill can still lose buffered events.~~
   **SUPERSEDED by decision 11.** Phase 0's failure-mode table originally
   predicted "a bounded retry before drop"; Phase 2 built something simpler
   on purpose: a retry loop inside the flush worker risks the *next* timer
   tick firing while a retry of the *previous* one is still in flight,
   double-buffering complexity for a pipeline whose own stated design
   already accepted imperfect accuracy (see decision 2's framing:
   undercounting a trend is tolerable, corrupting one is not). The harder
   gap this decision originally accepted — an ungraceful kill (SIGKILL,
   OOM, a crashed process) losing whatever was in memory since the last
   successful flush, because `StopAsync`'s graceful-shutdown flush only
   ever ran on a clean SIGTERM — is exactly the gap decision 11 closes for
   real, not with a bigger retry loop but by removing the in-process buffer
   from the durability boundary entirely. Left here, struck through rather
   than deleted, because the reasoning for *why* a retry loop was rejected
   is still correct and still worth keeping.

10. **Local debugging reuses the container's own port, and the Gateway routes
    to it through a single host-routed destination — not the two-destination
    failover pattern JameX uses.** Not a doc-driven decision (nothing in the
    source chapters addresses local tooling); this one exists purely because
    of how the owner wanted to work with the code day to day, mirrored here
    because getting it right cost real debugging effort worth recording. The
    natural first instinct was to copy JameX's pattern exactly — two
    destinations per cluster (`container` + `local`), `FirstAlphabetical`
    load balancing to prefer the container. That's correct when local and
    container run on genuinely different ports, because the two destinations
    are then real, independently-addressable backends. Here they aren't:
    with the same port number on both sides, a running container's own
    published port answers *both* destination addresses (Docker's
    port-forward doesn't distinguish a request that arrived from outside the
    machine from one that looped back through `host-gateway`), so the two
    entries were provably the same backend whenever the container was up —
    and empirically, YARP's load-balancing choice between two destinations
    that are always simultaneously healthy was not the deterministic
    "alphabetically-first" behavior the policy name suggests, at least not
    as configured here. Every response was still correct (same backend
    either way), but the setup was undocumentable as designed. Collapsed to
    one destination per cluster, always reached via the host route — the
    same address correctly reaches the container (via its port-forward) or a
    local debugger (bound directly), with nothing left to choose between.
    Full account, including the verification process that caught this, in
    `PROGRESS.md`'s Phase 2 entry and `DEBUGGING.md`.

11. **CollectionService's in-memory buffer and timer-driven flush worker
    were replaced with Kinesis Data Firehose.** Not a doc-driven decision —
    the source chapters say "collection service logs the phrase, timestamp,
    and metadata," nothing more specific — but a direct response to
    decision 9's open gap: an ungraceful kill of `CollectionService` could
    lose events that were accepted (202 returned) but not yet flushed,
    because durability lived only in that one process's RAM. Firehose
    closes this the right way, not a bigger way: `POST /search-events` now
    calls `PutRecord` and does not return 202 until Firehose has durably
    accepted the record — durability moved out of this process entirely,
    so killing the process can no longer lose an already-accepted event.
    `SearchEventBuffer`, `SearchEventFlushWorker`, and their config
    (`CollectionOptions.FlushIntervalSeconds`) are deleted, not deprecated;
    `CollectionService` now owns no store and holds no state.

    **Considered and rejected: SQS.** Genuinely the more consistent choice
    with this codebase's own conventions (it's the sibling JameX project's
    pattern for exactly this kind of durability problem), and it would keep
    the batching/flush logic under this project's own code rather than
    handing a whole pipeline stage to a managed service. Firehose won
    anyway because the problem is *specifically* "buffer records, batch-write
    them to S3" — Firehose's literal job description — not a general work
    queue that happens to be used this way. Choosing SQS here would mean
    re-hand-rolling the same flush-worker logic decision 9 already built,
    just reading from a durable source instead of an in-memory one; Firehose
    removes that whole class of code rather than hardening it.

    **A real LocalStack emulation gap, found by testing, not assumed
    away:** `BufferingHints.IntervalInSeconds` (set to 10s here — AWS's own
    real minimum for an S3 destination is 60s, so this was already a
    demo-scale value) is not honored by LocalStack's Firehose emulation at
    all. Every `PutRecord` call was observed landing in S3 as its own
    separate, single-record object within roughly one second — including
    under a 10-request concurrent burst, which should have landed in one
    combined object if buffering worked. This does not break correctness
    here (Aggregator's line-by-line JSONL parser handles a one-line object
    exactly as well as a many-line one, and `ListObjectsV2`'s lexicographic
    ordering still correlates with chronological order well enough for the
    `StartAfter` checkpoint to work unmodified against Firehose's own
    `search-events/yyyy/MM/dd/HH/...` key format — verified directly, no
    code changes needed), but it means local testing cannot demonstrate the
    actual batching behavior that is the entire point of choosing Firehose
    over one-object-per-event — only that the delivery pipeline itself
    (accept → durable → land in S3 with correct content) works. That
    specific claim — "this reduces S3 PUT volume the way real Firehose
    would" — is asserted from AWS's own documented behavior, not verified
    against this local stack.

    **A second real requirement, found empirically, not documented clearly
    enough by LocalStack's own docs to plan for in advance:** an S3-backed
    delivery stream needs `sts` and `iam` enabled in LocalStack's
    `SERVICES` list alongside `firehose` — Firehose's S3 delivery path
    calls `sts:AssumeRole` against the configured `RoleARN` even under
    emulation, and fails outright without it.

12. **Aggregator's own read-progress checkpoint is durably persisted in a
    dedicated DynamoDB table, `suggestx-aggregator-checkpoints`, not the
    phrase-frequencies table.** Module 1 shipped this as an in-memory
    field deliberately, documented as an open gap rather than a finished
    feature — a restart reprocessed the whole raw-logs bucket from the
    start. Closing it needed exactly one durable value (a single S3 key),
    which argued for a small, separate table rather than folding a
    sentinel row into `suggestx-phrase-frequencies`: a checkpoint is
    operational state, not a phrase count, and mixing the two would mean
    every future thing that scans that table for real data (Module 2's own
    consumers, TrieBuilder, any analysis or export) would need to know to
    skip one special row forever. `IAggregatorCheckpoint` reads from an
    in-memory cache (cheap, no DynamoDB round trip per poll) and
    write-throughs to DynamoDB on every advance (so the durable copy is
    never more than one batch stale) — the same read-cache/write-through
    shape as decision 11's Firehose publisher being awaited before
    acking, applied to internal state instead of an external caller.

    **The exact same AWS SDK gotcha as decision 9/Module 1's `S3Objects`
    bug, in a different response type, caught the same way — live, not
    from documentation:** `GetItemResponse.Item` is `null`, not an empty
    dictionary, when no item exists for the given key. The first version's
    unguarded `.TryGetValue(...)` on it threw `NullReferenceException` on
    every first-ever run — exactly the case that matters most, a brand new
    table with nothing in it. Worth generalizing rather than treating as
    two unrelated one-off bugs: several AWS SDK response types return
    `null`, not an empty collection, for "nothing here," and it is a
    genuinely easy, repeatable mistake to assume otherwise for the next
    one too.

13. **Phrase counting is per-batch map-reduce (count within the batch, one
    atomic `ADD` per unique phrase), applied strictly before the
    checkpoint advances for that batch — and a narrow double-count race
    is accepted rather than closed.** The doc's own chapter frames this as
    "for simplicity, this data is case-insensitive," so normalization
    (`Trim().ToLowerInvariant()`) happens in exactly one place —
    `DynamoPhraseFrequencyWriter` — and nowhere else in the system ever
    needs to think about casing again, including TrieBuilder once it
    reads this table. `ADD` was chosen over a read-modify-write `PutItem`
    for the same reason it was chosen for the checkpoint and for
    CollectionService's original design (decision 9): it both creates and
    increments in one atomic server-side step, so concurrent writers (or a
    redelivered batch) can never race or silently reset a count.

    The ordering — apply counts, *then* advance the checkpoint, and only
    if the apply succeeded — is the actual correctness property, not the
    `ADD`'s own atomicity. Reversing it would let a crash between the two
    silently lose a batch's counts forever, since the checkpoint would
    claim the batch was handled when its counts never landed. A failed
    apply stops the whole poll cycle rather than skipping to the next
    batch, because checkpoints must advance strictly in order: skipping a
    failed batch to process a later one that succeeds would permanently
    strand the failed batch's counts behind an advanced checkpoint that
    can never point back to it.

    **What this does not close, on purpose:** a genuine process crash
    between a successful `ADD` and the checkpoint's own durable write
    would still cause a restart to re-read and re-count that one batch,
    over-counting it by however many entries it held. Not fixed here — a
    real fix needs either a per-object idempotency guard (tracking which
    exact S3 keys have already been applied, not just the most recent one)
    or a transaction spanning two DynamoDB tables, real complexity for a
    rare, self-bounded (at most one batch), one-time overcount. Consistent
    with decisions 2 and 9's running theme: this system accepts small,
    bounded inaccuracy in exchange for not building defensive machinery
    against failures far rarer than the thing they'd protect.

14. **The trie is flattened to `prefix → top-N` once per build cycle and
    published into Redis under a versioned key namespace
    (`trie:v{N}:{prefix}`), with the previous version's exact keys deleted
    only after the new version is fully written — and the version number
    itself is a private, in-memory counter for now, not yet a durable or
    coordinated one.** This is decision 2's central claim made concrete:
    `SuggestionService` (Phase 5) must never traverse a trie or pay a
    per-request lookup cost beyond one Redis `GET`, so every prefix it
    could ever be asked for has to already have a precomputed answer
    sitting in Redis before a request arrives.

    `CompressedTrie.FlattenPrefixes` computes each node's own top-N exactly
    once and reuses it for every prefix length that lands inside that
    node's own (possibly multi-character) segment, only recomputing at an
    actual branch point — a direct consequence of decision 2's compression:
    a user who stops typing mid-segment lands on exactly the same subtree
    as one who stops at the segment's boundary, so recomputing per
    character would be pure waste for an answer that's already identical.

    Publishing order is decision 8's blue/green principle applied to Redis
    keys instead of an in-process object reference: write the whole new
    version first, only then delete the old version's keys — by the exact
    prefix set remembered from the previous cycle, not a `KEYS`/`SCAN`
    sweep, since this service already knows precisely which keys it wrote
    last time and scanning the keyspace to rediscover that would be real,
    avoidable cost. Verified live across three consecutive build cycles:
    each published a new version's full key set before removing exactly
    the prior version's keys, so `redis-cli KEYS 'trie:*'` never showed a
    mix of two versions or a gap with none at all.

    **What this deliberately did not close yet, when Module 2 shipped:**
    the version counter and the "which prefixes did the last cycle
    publish" set were both plain in-memory fields on `TrieBuildWorker` —
    nothing durable or shared tracked "which version is current." A
    TrieBuilder restart would reset the counter to 1 and start
    overwriting `trie:v1:*` again. Closed by Module 3 below.

15. **Module 3 closes decision 14's gap: a full snapshot of each version's
    flattened cache is persisted to S3, and a real ZooKeeper znode
    (`/suggestx/trie/current_version`) tracks which version is current —
    both written only after that version's Redis keys are already fully
    live.** `S3TrieSnapshotStore` writes `v{N}.json` to
    `suggestx-trie-snapshots`, holding the identical `prefix → top-N`
    content just published to Redis — not a separate serialization of the
    raw trie, since the point (per `StorageOptions.TrieSnapshotsBucket`'s
    own doc comment) is letting a restarted process skip re-deriving that
    content from every historical DynamoDB frequency, not preserving the
    trie's internal node structure, which nothing downstream ever needs
    directly. `ZooKeeperVersionPublisher` owns the one znode this system
    writes today; `TrieBuildWorker` reads it once at startup
    (`RecoverAsync`) to resume its version counter and reload the
    previous version's known prefixes from its S3 snapshot, instead of
    resetting to 0/empty on every restart.

    Ordering is decision 8's blue/green principle applied one layer
    further out than decision 14's own Redis-key ordering: Redis publish
    succeeds first (the thing that actually serves the data), *then* the
    S3 snapshot is written, *then* the ZooKeeper znode flips — a version
    is never claimed as "current and recoverable" before it's actually
    both. A failure saving to S3/ZooKeeper is logged, not fatal: Redis
    already has the correct, live version regardless, so only this
    cycle's *recovery* state goes stale until the next cycle succeeds —
    the same "small bounded gap over defensive machinery" tradeoff as
    decision 13's checkpoint race, not a read-path correctness problem.

    The .NET client is `ZooKeeperNetEx` — the standard async .NET port,
    whose API mirrors the official Java client near-verbatim
    (`org.apache.zookeeper.*` namespace included), registered as a shared
    singleton in `SuggestX.ServiceDefaults` alongside Redis/AWS clients,
    since both TrieBuilder (the sole writer) and the future
    SuggestionService (a reader) need the same connection.

    **Verified against the live stack, across a genuine container
    restart, not a simulated one:** confirmed each version's S3 object
    matched Redis's published content exactly, and the znode's value
    tracked the current version after every cycle via `zkCli.sh get`.
    Restarted the running `trie-builder` container mid-sequence while
    ZooKeeper held version 3: it recovered version 3 on startup (logged
    explicitly), published version 4 next — not resetting to 1 — and
    correctly deleted exactly version 3's 28 Redis keys, leaving only
    `trie:v4:*`. Also found, as direct forensic evidence that decision
    14's gap was real and not merely theoretical, a stray `trie:v653:*`
    key set orphaned by an ungraceful restart during earlier Module 1/2
    development (before this znode existed) — never cleaned up because
    nothing durable remembered it needed to be. Removed manually as
    disposable local dev cache; the failure mode itself is what Module 3
    now prevents going forward.

16. **`SuggestionService` learns the current trie version by polling
    ZooKeeper on a timer (5s default), not by registering a ZooKeeper
    watch, and caches it in memory so no request ever makes its own
    ZooKeeper call.** Polling was chosen for consistency, not because a
    watch is wrong: every other cross-service handoff in this system is
    already poll-based (Aggregator polling S3, TrieBuilder polling
    DynamoDB) — a ZooKeeper watch would add real, distinct complexity (ZK
    watches are one-shot and must be explicitly re-armed after every
    fire, and a watch callback runs on the client library's own thread,
    with its own reasoning about ordering and reconnection) for a benefit
    — near-instant version pickup instead of up to ~5s of staleness —
    that doesn't matter given decision 7 already accepts a staleness
    window of minutes upstream of this. `RedisSuggestionReader` then does
    exactly what decision 2 always meant literally: normalize the prefix
    the same way Aggregator does, one Redis `GET` against
    `trie:v{version}:{prefix}`, return whatever comes back.

    Decision 6's over-bound fallback is implemented here, not deferred to
    a future phase: a prefix longer than `MaxPrefixLength` is truncated to
    the longest prefix TrieBuilder actually flattened, that prefix's
    (already-small, at most `TopN`) candidate list is fetched once, and
    filtered in-process to phrases that still start with everything the
    user actually typed. This is a real degradation — a phrase that would
    genuinely rank in the true top-N for the full prefix but fell outside
    the truncated prefix's own top-N is missed — not a silently wrong
    answer, and it costs nothing beyond the one Redis `GET` plus a trivial
    string-prefix filter over at most `TopN` items, not a traversal.

    **A real, accepted coupling, not closed here:** `SuggestionServiceOptions.MaxPrefixLength`
    duplicates `TrieBuilder:MaxPrefixLength` as a separately-configured
    default in an independently deployed service, with nothing enforcing
    the two stay equal. If they drift, the symptom is subtle — prefixes in
    the gap between the two configured bounds get the wrong behavior
    (either fetched directly when TrieBuilder never flattened that length,
    returning nothing, or needlessly truncated when TrieBuilder actually
    had the real answer) — not a crash. Accepted as a real limitation
    rather than building a mechanism (e.g. publishing the bound itself
    through ZooKeeper or Redis) to keep two independently deployed
    services' config in sync for a value that, in practice, essentially
    never changes.

    **Verified against the live stack:** `guitar`, `jazz`, and the `ja`
    branch point (the same shared-segment case Module 1/2 verified)
    returned identical results through `GET /suggestions` as through
    TrieBuilder's own `/_debug/search`; `xyz` returned an empty list, not
    an error; case normalization confirmed (`JAZZ` == `jazz`); a missing
    `prefix` returned `400`; the identical request through the real
    Gateway proxy returned an identical response. The over-bound fallback
    was verified precisely: `guitar lesso` (13 characters, past the
    6-character bound) correctly filtered down to exactly `guitar
    lesson`, not all three guitar phrases — proving the filter
    discriminates, not just truncates; `guitarzzz` correctly matched
    nothing. Live version tracking was confirmed as an active loop, not a
    one-time read at startup: `GET /suggestions/_debug/status` was
    re-checked after TrieBuilder advanced and showed the cached version
    catch up within one poll interval, unprompted.

    **Real latency measured, not assumed against the doc's own "under
    200ms" NFR:** 200 sequential requests for a real, matching prefix
    direct to `SuggestionService` — p50 4.08ms, p95 6.60ms, avg 4.96ms.
    The same 200 requests through the real Gateway proxy (the actual
    client-facing path) — p50 6.63ms, p95 9.93ms, avg 7.67ms, a small,
    consistent proxy-hop overhead. 100 requests against the over-bound
    fallback path averaged 4.17ms — essentially identical to the
    direct-match cost, confirming the in-process filter really is the
    trivial operation claimed above, not a hidden cost. Every measured
    percentile on every path is comfortably inside 200ms — real
    end-to-end request latency through the running stack, not a
    synthetic microbenchmark.

17. **A real gap in the "ZooKeeper unreachable degrades to stale, not
    broken" claim, found by testing it live rather than trusting the
    failure-mode table's own prior wording — not yet closed.** Stopping
    the real `zookeeper` container confirmed the first half of the claim:
    `CurrentVersionPoller` logs the connection failure and keeps serving
    the last-known version, exactly as designed. But `TrieBuildWorker`'s
    Redis blue/green cleanup (decision 14) does not pause when *its own*
    ZooKeeper writes are failing — it keeps building, publishing new Redis
    versions, and deleting the previous version's keys every cycle,
    entirely independent of ZooKeeper reachability. An outage spanning
    more than one TrieBuilder build cycle therefore lets Redis roll
    forward past the exact version `SuggestionService` is still frozen on
    — its keys get deleted out from under a "last known good" pointer
    that is no longer actually good. A live `guitar` request during such
    an outage returned an empty list, not stale-but-correct data — a real,
    reproduced failure, not a hypothetical one. Restarting ZooKeeper let
    both sides self-heal within one cycle each with no manual
    intervention, confirmed live — so the failure is real but bounded and
    self-correcting, not permanent.

    **Not fixed here, deliberately, pending a decision:** two directions
    are both plausible and neither is free. TrieBuilder could pause its
    Redis *cleanup* specifically (not its publish) while it can't reach
    ZooKeeper, trading "old keys pile up during an outage" for "no version
    a client might be reading ever gets deleted out from under it."
    Alternatively, `SuggestionService` could treat an empty Redis read
    for a version it's fairly confident should have data as a signal to
    re-poll immediately rather than wait a full interval — but an empty
    read is also the genuinely correct answer for a real "no suggestions
    for this prefix" case, so the two can't be told apart without extra
    machinery (e.g. a canary key per version, or checking whether *any*
    key under that version's namespace still exists, which is itself a
    `SCAN` this project has otherwise deliberately avoided). This refines
    the failure-mode table's own prior claim rather than just confirming
    it — see the corrected row below.

18. **The frontend (`web/`) is hand-written, not scaffolded by a CLI
    (`create-next-app`), mirroring JameX's own `web/` structure and
    conventions exactly rather than whatever a generator would produce.**
    Next.js 16 App Router, React 19, Tailwind v4, TypeScript strict — the
    same stack CLAUDE.md already named. The same server/browser
    Gateway-URL split as JameX (`GATEWAY_BASE_URL` vs.
    `NEXT_PUBLIC_GATEWAY_BASE_URL`) and the same `requestJson`/`ApiError`
    fetch core, pointed at SuggestX's own Gateway port (`9080`) instead
    of JameX's `8080` — the one thing that actually differs between the
    two, since every other architectural choice already matched.

    `useDebouncedValue` (300ms) is the doc's own stated client-side
    latency lever, built for real for the first time in this project —
    every prior phase exercised the backend directly. A monotonically
    increasing request-id ref in `SearchBox` guards against a slower
    earlier request's response arriving after a faster later one, a real
    race once debounce still allows two requests to overlap in flight,
    not a hypothetical one addressed defensively.

    **Two real problems this module surfaced, not just its own new
    code:** first, `SuggestXHostingExtensions.cs`'s shared CORS policy
    had listed JameX's ports (`3000`/`8080`) verbatim since early in the
    project, before any frontend existed to actually exercise it —
    nothing caught the mismatch until a real browser tried to call the
    Gateway from `localhost:3010` and would have been silently blocked.
    Second, ESLint's `react-hooks/set-state-in-effect` rule caught a
    synchronous `setState` call in `SearchBox`'s empty-query early-return
    branch — fixed by deriving what's rendered from the trimmed query at
    render time instead of imperatively clearing fetched state inside the
    effect. Both are exactly the kind of thing "verify in a real browser,
    not just that the code compiles" is meant to catch.

    **Verified live, in a real Chrome browser, not `curl`:** `guitar`
    rendered all three phrases correctly; typing `guitar lesso` (past the
    6-character bound) confirmed via network inspection that six rapid
    keystrokes produced exactly one request — debounce is real, not just
    present in the code — and the rendered result correctly matched only
    `guitar lesson`, the same over-bound fallback verified in decision 16.
    `jazz` rendered both phrases correctly ranked by frequency. Clearing
    the input cleared the results. Zero browser console errors across the
    session. `npm run build` (a real production build) and `npx eslint .`
    both pass clean.

19. **Submitting a search — Enter, or picking a rendered suggestion —
    fires the doc's `addToDatabase(query)` API, never a debounced
    keystroke.** `submitSearchEvent` (`lib/api/search-events.ts`) is a
    thin `POST /search-events`; `requestVoid`/`browserApiMutate` were
    added to the fetch core specifically because this endpoint returns a
    bare `202` with no body, mirroring JameX's own void-mutation pattern
    exactly rather than reusing `requestJson` and discarding whatever it
    parsed. `SearchBox` turns each suggestion row into a real `<button>`
    (`onClick`) rather than a `<li>`, since picking a suggestion is
    itself a genuine search submission, not just a display choice — the
    same signal Enter sends, through the same `submit(term)` function.

    This is the first module in the whole project that exercises the
    *entire* pipeline from a single external trigger, rather than one
    service in isolation. A small, real UI confirmation (`Logged
    "..."`) exists specifically so that trigger and its effect are both
    visible to whoever is watching, not just inferable from a network
    tab.

    **Verified against the live stack, watching a single real browser
    action propagate through every stage in order, not inferred from any
    one service's logs alone:** submitted a phrase (`banjo tutorial`)
    that had never existed in the system before — confirmed it showed no
    suggestions beforehand (a true negative, ruling out it already being
    cached). After submitting, watched, in sequence: a new Firehose-
    delivered object land in `suggestx-raw-logs` within seconds;
    `RawLogPollingWorker`/`DynamoPhraseFrequencyWriter` pick it up and
    write it into `suggestx-phrase-frequencies` (confirmed via
    `awslocal dynamodb get-item`); the next `TrieBuildWorker` cycle
    rebuild with one more phrase than before and publish the
    corresponding `trie:v{N}:banjo` key into Redis. Then, in the same
    browser tab with nothing reloaded or restarted, searched the same
    prefix again — the phrase now rendered as a real suggestion. A real
    environment issue surfaced along the way (CollectionService and
    Aggregator had both stopped running earlier in this long session,
    producing an initial `503`) — unrelated to this module's own code,
    fixed by `docker compose up -d`, and left in the verification record
    rather than quietly retried away.

20. **The insights panel polls every `/_debug/status` endpoint already
    built across every service, plus one new one, rather than building
    any new instrumentation.** Two Gateway routes were added
    (`/api/aggregator/{**catch-all}`, `/api/trie-builder/{**catch-all}`)
    since Aggregator and TrieBuilder never had a caller-facing API before
    this — only debug endpoints nothing reached through the Gateway yet.
    `TrieBuilder` gained exactly one new endpoint, `GET /_debug/tree`
    (`CompressedTrie.ToSnapshot`), a full, unbounded JSON dump of the
    live in-memory trie — fine at this project's demo scale (a few dozen
    nodes at most), explicitly not something a production system would
    expose this way at real scale, the same documented simplification as
    the full DynamoDB `Scan` in `DynamoPhraseFrequencyReader`.

    The graph itself is hand-rolled SVG, not a charting library — a
    standard small-tree layout (leaf-counter for position, parent sits at
    its children's midpoint), genuinely simple at this project's node
    count, and pulling in a real dependency for it would be exactly the
    unnecessary abstraction this project's own conventions warn against.
    Segments label the *edge* into a node, not the node itself, mirroring
    the compressed trie's own actual shape: a segment is what's consumed
    to reach a node, not a property of the node in isolation.

    **A real ESLint finding, not a style nit:** the newer
    `react-hooks/refs` rule caught `usePolling` writing to a ref *during
    render* — the common pre-Compiler "latest callback ref" idiom, now
    flagged. Fixed by moving that assignment into its own
    dependency-free `useEffect`, the React-recommended way to keep a ref
    current without touching it in the render body itself.

    **Verified against the live stack with two separate browser tabs —
    one driving the pipeline, one only ever polling and never
    reloaded — specifically to prove the panel is genuinely live, not a
    snapshot re-fetched on navigation:** confirmed the graph rendered
    real data with real branching (the actual "jame → el → jameel" and
    "pia → no → piano → read → piano read" compressed-node chains this
    session's own accumulated test data happened to contain, not a
    contrived example). Submitted a brand-new phrase in the second tab;
    without touching the first tab at all, its own 2s poll picked up
    every change on its own — published/processed counters incrementing,
    the trie's phrase and node counts increasing, and a real new node
    appearing in the graph. Also noticed and deliberately left as an
    honest artifact, not "fixed": `SuggestionService`'s serving version
    briefly trailed `TrieBuilder`'s by one cycle during the same
    observation — a small, real, visible instance of decision 16's own
    accepted polling-driven staleness, not a bug to chase down. Found and
    fixed one real rendering bug along the way: long terminal labels
    clipping against the SVG viewBox's edge, fixed by padding the
    viewBox for label width, not just node position.

    **Follow-up, after real use surfaced two more problems the first
    pass missed:** the page itself was capped at `max-w-5xl` (64rem),
    leaving most of a normal monitor blank — widened to fill the
    available width (`w-full` with responsive padding, no cap) instead.
    More significantly, the graph's original `viewBox` + `w-full`
    approach scaled the *entire* SVG, text included, down to fit
    whatever width its container happened to have — on anything less
    than a very wide screen this made every label smaller than intended,
    which was the actual readability complaint, not a font-size choice
    on its own. Fixed by rendering the SVG at a real, fixed pixel size
    (never scaled) inside a horizontally scrolling strip. That alone
    surfaced a second, previously-invisible bug: leaf slots were a
    uniform fixed width, so two adjacent long phrases — "mykonos
    perfume" and "mykonos summer perfume," genuinely adjacent siblings
    in this session's own test data — rendered with overlapping labels
    once no longer shrunk to fit. Fixed by sizing each leaf's slot from
    its own label's estimated width (character count × an average glyph
    width, not a real text measurement — unavailable during a server
    render) plus a fixed gap, rather than a single spacing constant for
    every leaf regardless of its phrase length. Verified live again
    after both fixes: the same "mykonos perfume"/"mykonos summer
    perfume" pair renders with clean, non-overlapping labels; the page
    visibly uses the full window width.

21. **Personalization is built exactly as this doc's own Q&A already
    committed to: a shared trie, never a per-user one, with a small
    client-side recent-search cache blended in at merge time inside
    `SuggestionService` — never a separate candidate source.**
    `SuggestionResponse` gained `PersonalizedPhrases` (which returned
    entries were reordered); `SuggestionItem` itself stays untouched,
    since it's also the exact shape TrieBuilder persists to Redis and S3
    — personalization has no business reshaping a type two unrelated
    services depend on for something that's purely a response-time
    display concern.

    The reordering happens *only* within the candidate set the flattened
    cache already returned for this prefix — boosted matches (present in
    the caller's own recent list) first, in their existing frequency
    order, then everything else, with `limit` applied only at the very
    end so a personally-relevant phrase ranked just outside the
    requested limit can still surface. Deliberately never looks up an
    exact phrase's real frequency in DynamoDB to justify injecting a
    candidate the trie's own top-N didn't already surface — that would
    mean `SuggestionService` touching a store this whole system's
    architecture (decision 2) says it never does, just to serve a
    stretch feature. A phrase the user has personally searched but that
    isn't among this prefix's globally-flattened top-N simply isn't
    something personalization can promote — a real, accepted limitation
    of staying inside the existing boundary, not an oversight.

    There is no server-side user profile, matching this system's
    explicit "no identity concept" design (CLAUDE.md, decision 1's
    reasoning extended): the browser's own `localStorage` (capped at 10
    entries, most-recent-first, case-insensitive dedupe) is resent as a
    plain `recent` query parameter on every suggestions request, and
    `SuggestionService` never stores or remembers it between requests.

    **Verified against the live stack, both directly and in a browser:**
    `guitar` normally returns `chords, lesson, solo` — three phrases tied
    at frequency 1, ordered only by the alphabetical tie-break. With
    `recent=guitar solo` it returns `solo, chords, lesson` with
    `personalizedPhrases: ["guitar solo"]` — confirmed case-insensitive
    matching, a non-matching recent phrase having no effect, and multiple
    recent phrases all promoting correctly, both direct to
    `SuggestionService` and through the real Gateway proxy. In an actual
    Chrome browser: typed `guitar` (baseline order, no badges), picked
    `guitar solo` as a real submission, retyped `guitar` — it rendered
    first with a visible "recent" badge, confirmed via network inspection
    that the request genuinely carried `recent=guitar+solo`.

22. **The doc's remaining client-side latency levers — input threshold,
    a local response cache, early connection, and edge-cache headers —
    built as four small, independent pieces, not one bundled mechanism.**
    Input threshold (`MIN_QUERY_LENGTH = 2`) is the same reasoning as
    debounce, applied to length instead of time: below two characters, a
    prefix is too unspecific for a suggestion to be worth fetching at
    all. Early connection is a server-rendered
    `<link rel="preconnect">`/`dns-prefetch"` to the Gateway's origin in
    `RootLayout` — rendered in the initial HTML specifically so the
    browser opens the TCP/TLS handshake while the page is still being
    parsed, not after the first debounced keystroke already needs it.

    The edge-cache header (`Cache-Control: public, max-age=5` on every
    `GET /suggestions` response) and the client's matching local cache
    are the same lever at two different hops, deliberately kept in sync:
    the client cache's TTL equals the server's own `max-age`, so the
    browser never holds a response staler than what the server itself
    already claims is fresh. `public`, not `private`, is a real, correct
    choice here, not carelessness — this system has no cookies or
    sessions (CLAUDE.md's "no identity concept"), so a personalized
    response is already fully determined by its own URL (the `recent`
    parameter *is* the personalization), which is exactly the property
    that makes a shared cache safe to key by URL normally.

    **A real debugging story worth recording, not smoothing over:**
    verifying the local cache through the dev server initially looked
    broken — every backspace-and-retype produced a fresh network
    request, every time. Root-caused via direct instrumentation
    (temporary logging inside the cache's own read/write functions) to
    two genuinely separate causes, neither a flaw in the cache logic:

    1. React's Strict Mode — on by default in Next.js development
       builds, off in production — intentionally double-invokes effects
       to help developers catch missing cleanup. Two near-simultaneous
       `fetchSuggestions` calls for the same prefix both read the cache
       before either's write had landed, so both raced past it as
       misses. Confirmed by the double disappearing entirely in a real
       `next build && next start` run.
    2. The 5-second TTL was, independently, consistently shorter than
       this session's own multi-step browser-automation verification
       process — each screenshot, console read, and network check in
       this environment costs real, multi-second wall-clock time, so
       entries kept legitimately expiring *between* test steps. Not a
       caching failure — correct, honest behavior against a "retype"
       far slower than any real person's.

    Resolved by testing against an actual production build, batching
    actions into one fast round trip (browser automation's own overhead
    was the confound, so minimizing it was the fix) and reading the
    instrumentation directly: a first call logged a miss then a write; a
    second call roughly two seconds later logged `hit=true` with no
    further write and no new network request — conclusive proof the
    mechanism works exactly as designed. Debug instrumentation was
    removed afterward, and the dev server (temporarily replaced by a
    production server for this specific verification) was restarted and
    re-confirmed working before moving on.

23. **Phase 7's fault-tolerance verification: "TrieBuilder crashes
    mid-build" is not one risk, it's two, with very different
    outcomes — and only one of them is actually mitigated.** Reading
    `TrieBuildWorker.BuildAsync` closely shows the real ordering:
    `cachePublisher.PublishAsync` (writes the new version's Redis keys
    *and* deletes the old version's) completes entirely before
    `versionPublisher.PublishCurrentVersionAsync` (the ZooKeeper flip)
    even starts. A crash *before* the Redis publish is exactly what
    decision 8's blue/green ordering protects against — ZooKeeper still
    points at the old, fully-intact version. A crash *after* the Redis
    publish succeeds but *before* the ZooKeeper flip is a different
    story: the old version's Redis keys are already gone by that point,
    but ZooKeeper hasn't been told the new version exists yet — it's
    still confidently pointing callers at a version whose keys no longer
    exist. The exact same shape of gap as decision 17 (a stale ZooKeeper
    pointer outliving the Redis data it points to), just triggered by a
    process crash instead of a ZooKeeper outage.

    **Verified live with a real, precisely-timed crash, not a thought
    experiment.** A temporary delay was added right at that exact
    boundary — after the Redis publish, before the S3 snapshot/ZooKeeper
    flip — specifically to widen a gap that normally closes in well
    under a second into a reliable several-second window to kill the
    process in. `docker kill` (a real SIGKILL, no graceful shutdown) was
    fired the instant the delay's log line appeared, confirmed by
    polling the container's own logs rather than guessing at timing.
    Immediately afterward: `zkCli.sh get` showed ZooKeeper frozen at the
    old version; `redis-cli KEYS` showed *only* the new version's keys
    (the old ones already deleted); and a real `guitar` search — a query
    that has returned correct results throughout this entire project —
    came back with an empty suggestion list. Not stale. Not an error.
    Silently, completely empty, for a query with genuine matches.

    Restarting `TrieBuilder` proved the self-healing side of the claim
    true: it correctly recovered the frozen old version from ZooKeeper,
    reloaded that version's known prefixes from its S3 snapshot (Module
    3), and republished forward from there — no duplicate version
    numbers, no confusion, no manual intervention beyond the restart
    itself. Once the next cycle's ZooKeeper flip completed, real queries
    started returning correct results again. The temporary delay was
    then removed entirely — confirmed via `git diff` producing no
    output, i.e. the file is byte-identical to what was already
    committed, not just "looks the same."

    **Not fixed here, deliberately — the same tradeoff as decision 17's
    own gap, not a new kind of risk being introduced:** self-healing
    within one build cycle, no permanent corruption, but a real window
    where correct queries return incorrectly empty results. The two
    candidate fixes already named for decision 17 apply identically
    here (pause the Redis cleanup step until the ZooKeeper flip is
    confirmed, or give `SuggestionService` a way to distinguish "should
    have data" from a genuine no-match) — recorded as the same open
    question, not duplicated as a separate one.

24. **A live phrase-frequency table, added after Phase 6 was already
    marked complete — recorded honestly as a later addition, not folded
    silently into that phase's own history.** Aggregator gained its
    first read access to `suggestx-phrase-frequencies`, a table it
    otherwise only ever writes: `DynamoPhraseFrequencySnapshotReader`, a
    full `Scan` mirroring TrieBuilder's own reader for the same table
    almost exactly (same null-vs-empty defensiveness, same "toy-scale
    simplification, not a bounded query" reasoning), behind a new `GET
    /_debug/frequencies` endpoint. Reachable at
    `/api/aggregator/_debug/frequencies` with zero new Gateway
    configuration — that route already existed from Phase 6 Module 3,
    added for the insights panel's own Aggregator status polling.

    The frontend piece (`web/src/app/frequencies/page.tsx`,
    `FrequencyTable`) deliberately reuses rather than reinvents: the
    same `usePolling`/`useNow` pair the insights panel already
    established, sorting and filtering done entirely client-side over
    the already-fetched rows rather than a new server-side search/sort
    endpoint — this project's data is a few dozen phrases at demo
    scale, and a real search API for that would be exactly the
    unnecessary abstraction this project's own conventions warn
    against.

    **Verified live, including the real end-to-end proof this project
    always insists on over a static snapshot:** sortable columns and
    the substring filter confirmed correct in a real browser. Then,
    without touching or reloading an already-open `/frequencies` tab,
    submitted a genuinely new phrase on the search page in a second tab
    and watched the first tab's own 2s poll pick it up on its own —
    row count 18 → 19, the new phrase rendering at frequency 1 — the
    same "two tabs, one driving, one only ever polling" verification
    shape already established for the insights panel (decision 20).

## §2 Failure-mode table

| Failure | Effect without mitigation | Mitigation in this build |
|---|---|---|
| `TrieBuilder` crashes before finishing its Redis publish | Partial/corrupt version could be served | Mitigated: the old version's Redis keys are only deleted after every new key is written, so a crash here leaves the previous version's keys, and ZooKeeper's pointer to them, completely untouched. Verified live via a precisely-timed kill. |
| `TrieBuilder` crashes after its Redis publish succeeds but before the ZooKeeper flip | The previous version's Redis keys are already gone (deleted as part of the same publish that wrote the new ones) but ZooKeeper still points at that now-nonexistent version | Not mitigated — found and reproduced live with a deliberate, precisely-timed crash (decision 23), not just theorized. Self-heals on restart (TrieBuilder resumes from ZooKeeper's still-old version and republishes forward), but real queries during the window return empty results, not stale ones — the same shape of gap as decision 17, with a different trigger. |
| `TrieBuilder` restarts | Its version counter and previous-prefix-set could reset, overwriting `trie:v1:*` again and orphaning the prior version's keys forever | Mitigated (Module 3, decision 15): startup reads the ZooKeeper `current_version` znode and reloads that version's S3 snapshot, resuming the counter and previous-prefix-set instead of resetting them. Verified live across a real container restart. |
| Aggregator falls behind (batch job takes longer than its own interval) | Frequencies grow stale, or two runs overlap and double-count | Aggregator checkpoints its S3 read offset per run; a run that overlaps the next start is a documented open question (see §4) rather than silently assumed away. |
| Aggregator crashes between a successful frequency `ADD` and its checkpoint's durable write | A restart re-reads and re-counts that one batch, over-counting it | Not mitigated — accepted as a rare, self-bounded (at most one batch) overcount rather than adding an idempotency guard or a cross-table transaction. See decision 13. |
| ZooKeeper unreachable, briefly (shorter than one TrieBuilder build cycle) | `SuggestionService` cannot learn the current version | Mitigated: it caches the last-known version in memory and keeps serving it — confirmed live by stopping the real container. |
| ZooKeeper unreachable for longer than one TrieBuilder build cycle | TrieBuilder's own Redis cleanup keeps rotating versions regardless of ZooKeeper reachability, eventually deleting the exact version SuggestionService is still frozen on | Not mitigated — found and reproduced live (decision 17), not just theorized. Self-heals within one cycle once ZooKeeper recovers, with no permanent corruption, but requests during the window can return empty results rather than merely stale ones. |
| A Redis partition is unreachable | Every query for that prefix range fails | Redis's own primary-replica replication (not hand-rolled app failover) is the mitigation — matches how a real deployment would actually solve this, rather than inventing bespoke failover code. |
| A hot prefix range gets disproportionate load (e.g., everything starting "S") | One partition's servers overload while others idle | Named directly in the source doc as range partitioning's real weakness. Left as an open, unsolved question here (see §4) rather than hidden — a hash-based secondary partitioning layer is the real answer and is out of scope for this build. |
| `PutRecord` to Firehose fails from `CollectionService` | Without care, the caller could get a false 202 for an event that was never durably accepted | `SearchEventsController` awaits `PutRecordAsync` before returning 202 and returns 503 on failure instead — the caller, not this service, decides whether to retry. See decision 11. |
| `CollectionService` is killed ungracefully (SIGKILL, crash, OOM) | — | No longer a distinct risk: the service holds no buffer and no state to lose. An event is either durably in Firehose (202 already returned) or it never was (the client got an error and knows to retry). See decision 11 — this row is kept to show the failure mode decision 9 accepted is now closed, not to describe a live gap. |
| LocalStack's Firehose emulation doesn't honor `BufferingHints` | Local testing can't observe real batching/buffering behavior, only whether the delivery pipeline itself works | Not mitigated, and can't be from this side — it's an emulator gap, not an application bug. Documented explicitly in decision 11 rather than assumed to match real AWS Firehose. |
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
  frames as a *ranking* adjustment, not a *candidate set* adjustment. Built
  this way in Phase 7 Module 1 (decision 21): a user's own recent-search
  cache (client-side, `localStorage`, small) blends with the shared global
  ranking at merge time in `SuggestionService`, rather than maintaining
  separate tries — verified live, reordering a real tied-frequency result
  based on a real client-submitted recent search.

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

- **Q: TrieBuilder does a full `Scan` of the frequency table every cycle —
  what would a production system do instead, at real (millions-of-phrases)
  scale?**
  A: A few real options, in rough order of how much they change the
  architecture:
  1. **Batch export instead of a live table read.** DynamoDB's own
     `ExportTableToPointInTime` (to S3) or, for the doc's original
     Cassandra choice, a `sstableloader`/bulk-export step, run on the same
     cadence as the rebuild. This removes read-capacity pressure from the
     live table entirely — the rebuild reads a snapshot file, not the
     table itself — at the cost of the export's own latency, which usually
     still comfortably fits a "every N minutes" rebuild cadence.
  2. **Incremental frequency deltas instead of a full re-scan.** Have
     Aggregator (which already writes every frequency change) also emit
     a compact change-log — e.g. to Kinesis/SQS, or a "changed since last
     export" marker — and have TrieBuilder fold only the deltas into its
     existing in-memory trie rather than reading everything and rebuilding
     from scratch. This is the option with the best steady-state read cost,
     but it's real complexity: node merging/splitting on a targeted update
     is a much harder algorithm than "build fresh from a full list," and
     getting it wrong risks the trie silently drifting from the source of
     truth in a way a full rebuild can never do (a full rebuild is
     self-correcting by construction).
  3. **Partition the read, not just the trie.** If the doc's own prefix-range
     partitioning (decision/§1) is in play, each TrieBuilder partition
     only needs to scan its own slice of the frequency table (e.g. via a
     GSI keyed by prefix range), not the whole table — cutting the per-node
     scan cost roughly by the partition count without needing incremental
     logic at all.
  4. **Widen the rebuild interval as the real lever, not just a code
     change.** The doc itself already accepts minutes of staleness
     (decision 7) — at real scale, the honest first move is usually
     "scan less often," which directly trades off against how stale
     suggestions are allowed to get, before reaching for the complexity of
     options 1–3.

  This project stays with the simple full-`Scan` approach deliberately —
  it's correct, easy to reason about, and cheap at the data volumes this
  build actually exercises; see the open-questions entry above for why
  changing it is out of scope here.

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
- ~~CollectionService has no write-ahead durability~~ — **resolved by
  decision 11.** `POST /search-events` now durably hands each event to
  Firehose before returning 202, so an ungraceful kill of `CollectionService`
  has nothing left in-process to lose.
- **LocalStack's Firehose emulation doesn't honor `BufferingHints`** — every
  `PutRecord` was observed landing in S3 as its own object almost
  immediately, not batched with others in the same interval (decision 11).
  Confirmed real via a 10-request concurrent burst that still produced 10
  separate objects. An emulator limitation, not something this project can
  fix from the application side; means local testing can verify the
  delivery pipeline works but not that batching reduces S3 PUT volume the
  way it would against real AWS Firehose.
- **Aggregator can over-count a batch on an ungraceful crash between a
  successful frequency `ADD` and its checkpoint's durable write** —
  decision 13. Not closed; a real fix needs a per-object idempotency guard
  or a cross-table transaction, both real complexity for a rare,
  self-bounded (at most one batch) overcount.
- **TrieBuilder does a full, unfiltered `Scan` of `suggestx-phrase-frequencies`
  every build cycle, not an incremental/delta read.** `DynamoPhraseFrequencyReader`
  (`Services/IPhraseFrequencyReader.cs`) pages through the entire table via
  `LastEvaluatedKey` and discards the whole in-memory result to rebuild a
  fresh trie next cycle too — deliberate, not an oversight: a compressed
  trie's node merging and ranking are whole-dataset computations, so a
  delta of "what changed since last time" can't be patched into an
  existing trie the way Aggregator patches S3 checkpoints. At this
  project's toy scale (a handful of phrases, a 20s cycle) this is free;
  at real scale (millions of unique phrases) a `Scan` on every cycle is
  a genuinely expensive, slow read pattern that a production system would
  not do this way. Not fixed here — a real fix means either read
  volume reduction (batch export instead of a live table scan, e.g.
  DynamoDB → S3 export) or restructuring the rebuild itself to be
  incremental rather than full, both real complexity out of scope for a
  project whose point is demonstrating the pipeline shape, not
  operating it at production data volume. See §3's Q&A entry on this for
  the production-design options actually considered.
- **A stale ZooKeeper `current_version` pointer can outlive the Redis
  data it points to, from two different real triggers** — a sustained
  ZooKeeper outage (decision 17) or a TrieBuilder crash landing in the
  gap between its Redis publish succeeding and its ZooKeeper flip
  (decision 23) — both found and reproduced live, not theorized. Both
  self-heal within one cycle; neither closed here. Two candidate fixes
  (pause TrieBuilder's Redis cleanup until the flip is confirmed, or
  have SuggestionService distinguish a "should have data" empty read
  from a genuine no-match) apply identically to both triggers, named in
  decision 17, neither implemented.
- **`SuggestionServiceOptions.MaxPrefixLength` must be kept manually in
  sync with `TrieBuilder:MaxPrefixLength`** — decision 16. Two
  independently deployed services agreeing on a config value with nothing
  enforcing it; accepted rather than built around, since the value is
  essentially static in practice.

## §5 Doc-to-code map

| Doc concept | Chapter | File(s) | Why this choice |
|---|---|---|---|
| Suggestion service | 3, 5 | `src/services/SuggestX.SuggestionService/` | `CurrentVersionPoller` polls ZooKeeper's `current_version` znode on a timer; `GET /suggestions?prefix=` (`SuggestionsController`) does one Redis `GET` against `trie:v{N}:{prefix}` via `RedisSuggestionReader`, with the decision-6 over-bound fallback (Phase 5 Module 1, decisions 16–17, verified live). SuggestionService is the first real reader of the ZooKeeper znode TrieBuilder writes. An optional `recent` parameter reorders (never injects) within that same candidate set for personalization (Phase 7 Module 1, decision 21). Every response also carries `Cache-Control: public, max-age=5` (Phase 7 Module 2, decision 22). |
| Collection service | 5 | `src/services/SuggestX.CollectionService/` | `POST /search-events` validates and publishes to the `suggestx-search-events` Firehose delivery stream, awaiting durable acceptance before returning 202. Owns no store, holds no state. Phase 2, complete; the original in-memory buffer/flush-worker design was replaced by decision 11. |
| Aggregator (MapReduce over HDFS) | 4, 5 | `src/services/SuggestX.Aggregator/` | `RawLogPollingWorker` reads new `suggestx-raw-logs` objects on a timer via a sortable-key checkpoint, durably persisted in `suggestx-aggregator-checkpoints` (DynamoDB). `IPhraseFrequencyWriter` maps and reduces each batch's phrases into atomic `ADD`s against `suggestx-phrase-frequencies`, case-insensitive. Phase 3, complete (Modules 1–3). `GET /_debug/frequencies` (`DynamoPhraseFrequencySnapshotReader`) gives Aggregator its first read access to that same table, for the frontend's live frequency table (decision 24). |
| Trie builder | 5 | `src/services/SuggestX.TrieBuilder/` | `TrieBuildWorker` reads all of `suggestx-phrase-frequencies` on a timer and builds a fresh `CompressedTrie` (Phase 4 Module 1, verified against real branching data via `GET /_debug/search`), flattens it to `prefix → top-N` and publishes a new versioned namespace into Redis via `RedisFlattenedCachePublisher` (Phase 4 Module 2, decision 14), then persists that same content to S3 (`S3TrieSnapshotStore`) and flips a ZooKeeper `current_version` znode (`ZooKeeperVersionPublisher`) — only after Redis already has it live — so a restart recovers the last published version instead of resetting to 1 (Phase 4 Module 3, decision 15, verified across a real container restart). Phase 4 is now complete. |
| Web servers / entry point | 3 | `src/services/SuggestX.Gateway/` | YARP proxy, four routes live: `/api/suggestions`, `/api/search-events` (Phase 1), plus `/api/aggregator` and `/api/trie-builder` (Phase 6 Module 3, decision 20 — debug-only, for the insights panel, both services' first caller-facing route of any kind). No auth layer, since the source doc has no identity concept at all. |
| Client (the doc's implicit browser/app calling both APIs) | 2, 5 | `web/` | Hand-written Next.js 16 App Router app (not CLI-scaffolded — see decision 18), mirroring JameX's own `web/` conventions. `SearchBox` debounces input (`useDebouncedValue`, 300ms) and enforces a minimum query length before fetching (Module 1; the input threshold, Phase 7 Module 2, decision 22) and calls `GET /api/suggestions` through the Gateway; submitting a search calls `POST /api/search-events` and records it in a `localStorage` recent-search cache (Module 2, decision 19; the cache itself, Phase 7 Module 1, decision 21); `/insights` polls every service's `/_debug/status` plus TrieBuilder's new `/_debug/tree` every 2s and renders a real SVG graph of the live trie (Module 3, decision 20). `fetchSuggestions` also holds a short-TTL local response cache and `RootLayout` preconnects to the Gateway's origin (Phase 7 Module 2, decision 22). `/frequencies` polls Aggregator's own table and renders it as a real sortable, filterable table (decision 24). All verified live in a real browser. |
| HDFS | 4, 5 | `suggestx-raw-logs` (S3, LocalStack), written by the `suggestx-search-events` Firehose delivery stream, not directly by a service | `infra/localstack/init/01-bootstrap.sh`. See decision 3 (why S3) and decision 11 (why Firehose writes it instead of CollectionService). |
| Cassandra | 4, 5 | `suggestx-phrase-frequencies` (DynamoDB, LocalStack) | Same script. See decision 3. |
| MongoDB (trie doc store) | 5 | `suggestx-trie-snapshots` (S3, LocalStack) | Same script. See decision 3 — S3, not a document store, deliberately. |
| ZooKeeper | 5 | `zookeeper:3.9` container (`docker-compose.yml`); client wired in `SuggestX.ServiceDefaults/ZooKeeper/ZooKeeperClientFactory.cs` | Real container, not simulated. See decision 4. Znode round-trip verified manually via `zkCli.sh` in Phase 1; TrieBuilder is now the first real application-level writer (`current_version` znode, Phase 4 Module 3, decision 15). SuggestionService becomes the first reader in Phase 5. |
| Redis (trie cache) | 3, 5 | `redis:7-alpine` container | Unchanged from the doc. `trie:*` namespace is written by TrieBuilder starting Phase 4. |

## §6 Coverage map

| Design-doc concept | Status |
|---|---|
| Compressed trie | ✅ Built and verified (Phase 4 Module 1) |
| Flattened prefix → top-N cache (Redis) | ✅ Built and verified (Phase 4 Module 2) |
| Trie partitioning by prefix range | ⬜ Designed, not built |
| Offline trie updates (MapReduce-style) | ⬜ Designed, not built |
| Collection service | ✅ Built and verified (Phase 2) |
| Aggregator | ✅ Built and verified (Phase 3) |
| Trie builder — S3 snapshot + ZooKeeper-coordinated version swap | ✅ Built and verified (Phase 4 Module 3) |
| Suggestion service (Redis-backed) | ✅ Built and verified (Phase 5 Module 1) |
| Client-side optimization — debounce | ✅ Built and verified (Phase 6 Module 1) |
| Client-side optimizations — input threshold, local cache, early connection, edge cache | ✅ Built and verified (Phase 7 Module 2) |
| Personalization | ✅ Built and verified (Phase 7 Module 1) |
