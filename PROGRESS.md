# SuggestX — build progress

## ▶ How to resume

Say this to Claude at the start of the next session:

> Read PROGRESS.md and CLAUDE.md in C:\System Design\TypeheadSuggestion\App,
> then start Phase 2 — Collection Service. Build it in short modules, pausing
> after each one so I can review before you continue.

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

**Last updated:** 2026-09-21
**Phase 1 — local substrate. Complete, verified.**

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

### In progress

Nothing — awaiting go-ahead to start Phase 2 (Collection Service).

### Next up (immediate)

**Phase 2 — Collection Service.** Proposed module breakdown:

1. `POST /search-events` accepting `SearchEventRequest`, in-memory
   per-instance buffer.
2. Timed flush to `suggestx-raw-logs` as a line-delimited JSON object,
   keyed so concurrent instances never contend on the same S3 key (e.g.
   `{instanceId}/{flushTimestamp}.jsonl`).
3. Verify: post real events, confirm they land in S3 (`awslocal s3 cp`/`ls`),
   confirm two instances (or two rapid flush cycles) never collide on a key.

---

## Next up

Ordered. Each phase leaves the build green **and** updates `README.md` and
`DESIGN.md`.

1. ~~Local substrate~~ — done. Docker Compose, LocalStack (S3 + DynamoDB),
   real Redis, real ZooKeeper, solution scaffold, all 5 services
   containerised and health-checked.
2. **Collection Service** — ingest search events, batch-flush to S3. Not
   started.
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
