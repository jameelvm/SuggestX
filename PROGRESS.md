# SuggestX — build progress

## ▶ How to resume

Say this to Claude at the start of the next session:

> Read PROGRESS.md and CLAUDE.md in C:\System Design\TypeheadSuggestion\App,
> then continue Phase 3 — Aggregator, Module 2 (map-reduce into DynamoDB).
> Build it in short modules, pausing after each one so I can review before
> you continue.

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

**Last updated:** 2026-09-23
**Phase 1 — local substrate. Complete, verified.**
**Phase 2 — Collection Service. Complete, verified.**
**Phase 3 — Aggregator. Module 1 (read new raw logs on a timer) complete,
verified. Module 2 (map-reduce into DynamoDB) next.**
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
- [x] **Phase 2 — Collection Service.** Delivered as two modules:

      1. **`POST /search-events` + in-memory buffer.**
         `ISearchEventBuffer`/`SearchEventBuffer` (`Services/`) wraps a
         `ConcurrentQueue<BufferedSearchEvent>` — `Enqueue` from the
         controller, `DrainAll` for the flush worker, `Count` backing a
         debug endpoint. `BufferedSearchEvent` (`Domain/`) assigns
         `ReceivedAt` server-side at receipt time — never trusts a
         client-supplied timestamp. `SearchEventsController` (`Api/`)
         exposes `POST /search-events` (rejects a blank/whitespace-only
         query with 400, otherwise 202 Accepted — "accepted" means
         buffered, not yet durable) and a dev-only
         `GET /search-events/_debug/count`.
      2. **Timed flush to S3 + graceful-shutdown flush.**
         `SearchEventFlushWorker` (`Jobs/`, a `BackgroundService`) drains
         the buffer on a `PeriodicTimer` (`Collection:FlushIntervalSeconds`,
         default 10s — demo-scale, not a production claim) and writes it as
         one line-delimited-JSON object to `suggestx-raw-logs`, skipping
         the write entirely when the buffer is empty. Each object's key is
         `{yyyyMMddHHmmssfff}-{instanceId}.jsonl` — millisecond timestamp
         prefix first so keys sort chronologically (Aggregator, Phase 3,
         can use S3's `ListObjectsV2` `StartAfter` instead of reading every
         object to find new ones), a per-process GUID suffix so two
         instances can never collide even flushing at the same millisecond.
         `StopAsync` is overridden to flush one last time on graceful
         shutdown, so a container restart between timer ticks can't
         silently drop buffered events. A failed S3 write is logged and
         swallowed, not rethrown — matching the pipeline's documented
         best-effort framing (a dropped batch undercounts a trend, never
         corrupts one) and, just as important, so a transient S3 blip can't
         crash the flush loop and take every *subsequent* batch down with
         it. The line format is `SearchLogEntry` (`SuggestX.Contracts`, not
         internal to CollectionService) — Aggregator is the other side of
         this exact wire format, so it's a real cross-service contract, not
         a local implementation detail.

      **Verified against the live stack, including through the Gateway**:
      count started at 0; two direct `POST`s brought it to 2; a
      whitespace-only query was rejected with 400 and did not increment the
      count; a `POST` through `http://localhost:9080/api/search-events`
      (the Gateway's route) brought the count to 3 — confirming the full
      path from the public route to the buffer, not just the controller in
      isolation. Three posted events appeared in S3 after one flush cycle
      with exactly the right content (`awslocal s3 cp ... -`, inspected
      line by line) and the buffer count dropped back to 0; a second flush
      cycle produced a second, distinct key rather than overwriting the
      first; an idle interval with nothing posted produced **no** new
      object (empty-buffer skip confirmed); posting one event and then
      `docker compose stop`-ping the container mid-interval produced a
      *third* object containing that exact event, with the container log
      showing `Application is shutting down...` immediately followed by the
      flush completing — proving the graceful-shutdown path actually
      prevents data loss, not just that the code compiles.
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

### In progress

**Phase 3 — Aggregator.**

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

### Next up (immediate)

**Phase 3 — Aggregator**, continued:

2. An in-process map-reduce over each batch's phrases, atomically `ADD`ing
   counts into `suggestx-phrase-frequencies` (DynamoDB) — never a plain
   `PutItem`, so a redelivered/reprocessed batch increments rather than
   resets a real count.
3. Checkpoint persistence (which key was last processed) so a restart
   resumes instead of reprocessing everything from the start of the bucket.
4. Verify: post real search events, let Aggregator's cycle run, confirm
   DynamoDB counts increment correctly; re-run the same batch and confirm
   idempotency; verify a restart resumes from the checkpoint, not from zero.

---

## Next up

Ordered. Each phase leaves the build green **and** updates `README.md` and
`DESIGN.md`.

1. ~~Local substrate~~ — done. Docker Compose, LocalStack (S3 + DynamoDB),
   real Redis, real ZooKeeper, solution scaffold, all 5 services
   containerised and health-checked.
2. ~~Collection Service~~ — done. `POST /search-events` + buffer, timed
   flush to S3, graceful-shutdown flush, all verified live.
3. **Aggregator** — S3 raw logs → DynamoDB frequency table, checkpointed
   batch worker. Not started.
4. **Trie data structure + Trie Builder** — compressed trie in memory, top-N
   flattening into Redis, S3 snapshot for recovery, ZooKeeper blue/green
   version swap. Not started.
5. **Suggestion Service** — read path: ZooKeeper partition/version lookup →
   Redis `GET` → top-N response. Not started.
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
