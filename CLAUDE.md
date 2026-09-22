# SuggestX

A working typeahead/autocomplete system built to internalise the system design in
`../*.pdf` (the "Typeahead Suggestion System" chapters, 1–6). The goal is
**understanding through implementation**: every component in the design doc
has a real, runnable counterpart here.

## Read this first

- **`PROGRESS.md`** — live build state. What is done, what is next, where the
  last session stopped. **Update it at the end of every working session.**
- **`README.md`** — end-to-end teaching documentation. **Update it at the end of
  every phase**, explaining what was built and why, with verification steps and
  design talking points. It is revision material, not a change log.
- **`DESIGN.md`** — the system design summary: decision register, failure-mode
  table, Q&A bank and a doc-to-code coverage map. **Extend it at the end of
  every phase.** README explains *how* it was built; DESIGN explains *why it is
  shaped this way*.

## Owner context

The author is a C#/.NET developer with AWS, PostgreSQL and DynamoDB experience,
and has already built a companion project (`JameX`, a YouTube clone) using
this exact pattern. Prefer idiomatic .NET and real AWS service APIs over
bespoke abstractions — the code should double as an answer to "how would you
actually build this on AWS?".

## What this system actually is

The functional requirement is narrow — **suggest the top N frequent/relevant
completions for a prefix, in under 200ms** — but the design doc's interesting
content is entirely about *how you keep that one read path fast while a much
heavier write/aggregation pipeline runs behind it without ever touching the
read path directly*. That split (hot synchronous read vs. offline batch
pipeline feeding it) is the whole system. Five chapters build up to it:

1. **Requirements** — one FR (top-N prefix suggestions), three NFRs (latency,
   fault tolerance, scalability), and resource estimates at Google-search scale
   (3.5B queries/day → 21.9TB/year of unique query storage, ~164K servers at a
   realistic 3-char/sec typing rate).
2. **High-level design** — clients → LB → web servers → a **suggestion
   service** (reads top-10 from a Redis cache) and an **assembler** (writes
   trending queries to a NoSQL store). Two APIs: `getSuggestions(prefix)`,
   `addToDatabase(query)`.
3. **Data structure** — a compressed trie (merge single-child chains),
   frequency counts at terminal nodes, partitioned by prefix range across
   servers, updated **offline** (never on the read path) via a periodic
   MapReduce job over logged queries.
4. **Detailed design** — the assembler is three components: **Collection
   service** (logs phrase+timestamp to HDFS), **Aggregator** (MapReduce job,
   HDFS → Cassandra, every ~15 min), **Trie builder** (Cassandra → an in-memory
   trie, persisted to a NoSQL doc store for recovery, published to the
   suggestion service's Redis cache via ZooKeeper coordination).
5. **Evaluation** — latency comes from trie compression + offline updates +
   partitioning + caching, not from any one clever trick; fault tolerance from
   replication; scalability from adding partitions/servers. Plus client-side
   levers (debounce, input threshold, local cache, early connection, edge
   cache) and personalization (server-stored, client-cached, blended with
   global ranking).

## Architecture

**Service-oriented**, five services, each owning its data exclusively. The
read path (`Suggestion Service`) never writes; the write/aggregation pipeline
(`Collection` → `Aggregator` → `TrieBuilder`) never serves a client request
directly — it only ever publishes a new cache version for `Suggestion Service`
to pick up. This mirrors the doc's central claim: trie updates must never sit
in a user's request path.

| Service | Owns exclusively | Reads from |
|---|---|---|
| Gateway | — (YARP routing + a small admin/insights aggregation) | — |
| SuggestionService | — (stateless read path) | Redis (flattened top-N cache), ZooKeeper (version/partition znodes) |
| CollectionService | `suggestx-raw-logs` (S3) | — |
| Aggregator | `suggestx-phrase-frequencies` (DynamoDB) | `suggestx-raw-logs` (S3, read-only) |
| TrieBuilder | `suggestx-trie-snapshots` (S3), the `trie:*` Redis namespace, ZooKeeper `/suggestx/**` znodes | `suggestx-phrase-frequencies` (DynamoDB, read-only) |

No service reads another's database directly; the only cross-service coupling
is TrieBuilder writing into the Redis namespace and ZooKeeper znodes that
SuggestionService reads — treated as its published output contract, not a
shared store two services both own.

### Why there's no relational database anywhere

Unlike JameX (Postgres per service for entities with relationships), nothing
in this system is relational — there are no entities with foreign keys, just a
log, an aggregated counter table, a snapshot blob store, a flattened cache, and
a coordination service. That absence is itself a real finding, not an
oversight: a typeahead system is a pure read-optimized pipeline, and it shows
in the store choices. See `DESIGN.md` decision register.

### The trie's actual home

The compressed trie as an in-memory data structure lives **only** inside
`TrieBuilder` — it is built fresh each aggregation cycle, walked once to
compute the top-N completions for every prefix up to a bounded length, and
that flattened `prefix → top-N` projection is what's written to Redis. The
doc's "suggestion service retrieves the top ten from the Redis cache" is taken
literally: `SuggestionService` never reconstructs or traverses a trie itself,
it does one `GET` per request. The trie's own traversal cost is paid once,
offline, by `TrieBuilder` — not once per keystroke by every user. This is the
single biggest design decision in the whole system; see `DESIGN.md` §1.

## Stack

| Layer | Choice | Stands in for (per the doc) |
|---|---|---|
| Services | .NET 10, ASP.NET Core MVC controllers + `BackgroundService` workers | — |
| Gateway | YARP | "web servers" |
| Raw query log | S3 (`suggestx-raw-logs`), batched line-delimited JSON | HDFS |
| Aggregated frequencies | DynamoDB (`suggestx-phrase-frequencies`) | Cassandra |
| Trie snapshots (durability/recovery) | S3 (`suggestx-trie-snapshots`) | the doc's MongoDB trie store — a blob store fits a serialized trie better than a KV item's 400KB cap |
| Served cache | Redis, flattened `prefix → top-N` keys | Redis (unchanged) |
| Coordination / version publishing | real Apache ZooKeeper container | ZooKeeper (unchanged — run for real, not abstracted away) |
| Frontend | Next.js App Router — a real debounced search box + an insights panel | — |
| Local runtime | Docker Compose + LocalStack (S3, DynamoDB) | — |

Everything AWS runs on LocalStack locally, through the genuine AWS SDK for
.NET, same as JameX. Repointing at a real account is a config change, not a
code change.

## Layout

Sibling to this folder, one level up (`../*.pdf`), sit the six source PDFs —
same convention as JameX's `Youtube/*.pdf` next to `Youtube/App/`. Everything
else lives inside this folder:

```
App/
├── CLAUDE.md             # this file
├── PROGRESS.md           # session state — read and update every session
├── README.md             # end-to-end teaching docs, updated every phase
├── DESIGN.md             # decision register, failure-mode table, Q&A bank
├── docker-compose.yml
├── .env                  # LOCALSTACK_AUTH_TOKEN — gitignored, personal
├── infra/
│   ├── docker/           # Service.Dockerfile (parameterised)
│   └── localstack/init/  # buckets, DynamoDB table
├── src/
│   ├── shared/
│   │   ├── SuggestX.Contracts/       # DTOs; no infrastructure deps
│   │   └── SuggestX.ServiceDefaults/ # AWS clients, Redis, health (ZooKeeper client lands with TrieBuilder, Phase 4)
│   └── services/
│       ├── SuggestX.Gateway/
│       ├── SuggestX.SuggestionService/
│       ├── SuggestX.CollectionService/
│       ├── SuggestX.Aggregator/
│       └── SuggestX.TrieBuilder/
└── web/                  # Next.js frontend
```

## Conventions

- **One service owns a store.** No service reads another's database. If it
  needs data it does not own, it calls the owner's API — or, for the
  Aggregator/TrieBuilder pipeline, reads the previous stage's store read-only
  by explicit design (documented above), since that pipeline is intentionally
  a linear offline chain, not a request/response graph.
- **Layered inside each service**: `Api/` (controllers) → `Services/`
  (application logic) → `Repositories/` (S3/DynamoDB/Redis/ZooKeeper access)
  → `Domain/`. Background workers get a `Jobs/` folder instead of `Api/`.
- **Controllers, not minimal APIs**, matching JameX, for the same reason:
  consistent model-state validation and ProblemDetails shape.
- `SuggestX.Contracts` holds only what crosses a boundary.
- AWS resources are named `suggestx-*`; ZooKeeper znodes live under
  `/suggestx/`.
- Anything that exists purely to demonstrate a design-doc concept (trie
  compression, the primary-secondary version swap, prefix partitioning)
  carries a comment naming the chapter it comes from.
- Batch cadences are configurable and set short for local demoing (seconds,
  not the doc's real-world 15 minutes) — always documented as such where it
  appears, never silently assumed equivalent.

## Commands

```bash
docker compose up -d --build             # full stack
dotnet build SuggestX.slnx               # compile
docker compose logs -f aggregator        # watch the batch aggregation cycle
docker compose logs -f trie-builder      # watch trie rebuild + version swap
```

Ports are shifted +1000 from the "obvious" numbers, deliberately: this
project's sibling, JameX, already runs a compose stack claiming 8080-8090,
6379 and 4566 on the same machine, and both are meant to be runnable at once.

Ports: web `3010` (planned, Phase 6), gateway `9080`, suggestion `9081`,
collection `9082`, aggregator `9083` (health/status only), trie-builder
`9084` (health/status only), ZooKeeper `2181` (no conflict — JameX doesn't
run one), Redis `6380`, LocalStack `4567` (container-internal port stays the
standard `4566`; only the host-side publish shifts — see the
`LOCALSTACK_HOST` comment in `docker-compose.yml` for why those two must not
be confused).

`LOCALSTACK_AUTH_TOKEN` lives in a gitignored `.env` (same convention as
JameX) — LocalStack's freemium tier still requires a valid token for license
activation even for community services like S3/DynamoDB. This project reuses
the same personal token as JameX (same owner, same account).
