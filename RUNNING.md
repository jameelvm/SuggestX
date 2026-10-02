# Running SuggestX locally

Everything needed to get the full stack (backend services + frontend) up
and talking to each other on a clean machine.

## Prerequisites

- Docker Desktop
- .NET 10 SDK (only needed if you'll build/debug a service outside its
  container — see `DEBUGGING.md`)
- Node.js 20+ (for the frontend, which runs on the host, not in a container)

## 1. LocalStack auth token

LocalStack's free tier requires a valid auth token to start, even for the
community services this project uses (S3, DynamoDB, Firehose, STS, IAM).

Create a gitignored `.env` in the repo root:

```bash
LOCALSTACK_AUTH_TOKEN=<your token>
```

Get a token from [LocalStack's site](https://app.localstack.cloud) — the
free account tier is enough.

## 2. Backend stack

```bash
docker compose up -d --build
```

This builds and starts 8 containers: `redis`, `zookeeper`, `localstack`,
`gateway`, `suggestion-service`, `collection-service`, `aggregator`,
`trie-builder`. LocalStack's init script provisions the S3 buckets,
DynamoDB table, and Firehose delivery stream on first boot.

Confirm everything is healthy:

```bash
curl http://localhost:9080/health/live   # Gateway
curl http://localhost:9081/health/live   # SuggestionService
curl http://localhost:9082/health/live   # CollectionService
```

Watch the pipeline running live:

```bash
docker compose logs -f aggregator     # frequency aggregation cycles
docker compose logs -f trie-builder   # trie rebuild + version swap
```

## 3. Frontend

The frontend runs on the host (not containerized yet):

```bash
cd web
cp .env.local.example .env.local   # points at the Gateway on localhost:9080
npm install
npm run dev
```

Open `http://localhost:3010`.

## Port map

| What | Port |
|---|---|
| Frontend (`web/`) | 3010 |
| Gateway | 9080 |
| SuggestionService | 9081 |
| CollectionService | 9082 |
| Aggregator | 9083 (health/status only, no public route yet) |
| TrieBuilder | 9084 (health/status only, no public route yet) |
| Redis | 6380 |
| ZooKeeper | 2181 |
| LocalStack | 4567 (host) / 4566 (inside its own container — see `CLAUDE.md`) |

Every port is shifted +1000 from the "obvious" default so this project can
run alongside its sibling (JameX) on the same machine without conflicts.

## Tearing down

```bash
docker compose down          # stop + remove containers, keep volumes
docker compose down -v       # also wipe Redis/LocalStack/ZooKeeper data
```

## Next steps

- To step through a service in the debugger instead of its container, see
  `DEBUGGING.md`.
- For why each store/port/service is shaped the way it is, see `DESIGN.md`.
- For live build state, see `PROGRESS.md`.
