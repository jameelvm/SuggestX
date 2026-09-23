# Debugging in Visual Studio

Run any service under the debugger without editing configuration.

---

## The idea

Every service has **two homes**, both on the **same port**:

| | Where | Port (example: CollectionService) |
|---|---|---|
| **Container** | `docker compose` | `9082` |
| **Local** | F5 in Visual Studio | `9082` |

This project deliberately reuses the container's own published port for the
local debug profile — matching the workflow it's built for: **stop the
container, then run the same service locally on the port it just freed.**
The two can never both be bound at once; whichever one starts second simply
fails to bind, which is the point — there's no ambiguity about which one is
"live."

The Gateway routes each cluster to **one address**, reached via the host
(`suggestx-host`, a Docker Desktop alias for the host machine — see
"How it actually works" below). Whatever's currently bound to that port on
the host answers — the container's own published port, or your debugger,
whichever is running:

```
docker compose stop collection-service   →  port 9082 frees up
F5 in Visual Studio                      →  your debugger binds 9082
                                          →  Gateway now reaches it
docker compose start collection-service  →  fails to bind until you stop
                                             the debugger first
```

Nothing to reconfigure, and nothing to choose between — there's only ever
one thing to reach.

---

## Port map

| Service | Port | Routed through Gateway? |
|---|---|---|
| Gateway | 9080 | — (it *is* the Gateway) |
| SuggestionService | 9081 | Yes — `/api/suggestions/...` |
| CollectionService | 9082 | Yes — `/api/search-events/...` |
| Aggregator | 9083 | Not yet (no public API until Phase 6) |
| TrieBuilder | 9084 | Not yet (no public API until Phase 6) |

Infrastructure is always the container: Redis `6380`, LocalStack `4567`,
ZooKeeper `2181`.

---

## To debug one service

```bash
# 1. Bring up everything
docker compose up -d

# 2. Stop only the service you want to step through
docker compose stop collection-service

# 3. In Visual Studio, set that project as startup and press F5
#    (profile: "CollectionService (local)")
```

Requests to `http://localhost:9080/api/search-events` now reach your
debugger. Breakpoints hit on requests from curl, Scalar, or a browser.

When you're done:

```bash
docker compose start collection-service
```

If you try to `start` it back up **before** stopping the debugger, the
container will fail to bind its port — that's expected, not a bug; it's the
same port your local process is still holding, and Docker's own error will
say so plainly.

---

## Why no configuration switching is needed

Each service has host-friendly defaults committed in `appsettings.json`:

```jsonc
"Aws": { "ServiceUrl": "http://localhost:4567" },
"ConnectionStrings": { "Redis": "localhost:6380" },
"ZooKeeper": { "ConnectionString": "localhost:2181" }
```

Compose sets the *same keys* as environment variables with container
addresses:

```yaml
Aws__ServiceUrl: "http://localstack:4566"
ConnectionStrings__Redis: "redis:6379"
ZooKeeper__ConnectionString: "zookeeper:2181"
```

**Environment variables beat appsettings** in ASP.NET's configuration order,
so the container uses container addresses and your local process uses
`localhost` — from one file, with nothing to toggle. This is exactly why
Phase 1 chose ports for LocalStack/Redis/ZooKeeper that stay constant
regardless of which services are containerized: the infrastructure tier
never moves, only the application tier does.

---

## Calling services

**Through the Gateway** — exercises routing, and follows container/local
automatically, for the two services that have a route:

```
POST http://localhost:9080/api/search-events
GET  http://localhost:9080/api/suggestions?prefix=jaz
```

**Directly** — bypasses the Gateway, useful for isolating a problem, and the
*only* way to reach Aggregator/TrieBuilder today:

```
GET http://localhost:9082/search-events/_debug/count   whichever is bound to 9082
```

Every service also serves interactive API docs at `/scalar` in Development —
the launch profiles open it automatically.

---

## Caveats

**Local instances share the same stores as the containers.** LocalStack
(S3/DynamoDB), Redis, and ZooKeeper are always the containerized instances —
a local debug run reads and writes the exact same data. Usually what you
want; a destructive change under the debugger is a destructive change
everywhere.

**Aggregator and TrieBuilder aren't routed through the Gateway yet.** Their
background workers (arriving Phase 3 and Phase 4) can be debugged by setting
breakpoints and running the local profile directly — there's no HTTP
request to send to trigger them, since they run on their own timer.

**Run the local profile with `dotnet run` from Visual Studio, or from a
shell with `--project`/`--launch-profile` — never `dotnet <path-to-dll>`
directly.** Running the built DLL directly sets the content root to the
current working directory rather than the project's own folder, so
`appsettings.json` next to the DLL is silently not found, and every config
value that isn't set some other way comes back empty (surfaced here as a
confusing AWS-credentials failure with no obvious connection to the actual
cause). `dotnet run` gets this right automatically.

**If you kill a local instance from a script or another shell, kill the
actual process, not the `dotnet run` wrapper.** `dotnet run` launches the
compiled executable as a *child* process; stopping the wrapper does not stop
the child on Windows. Find the real PID by the port it's bound to
(`netstat -ano | findstr :9082`, or `Get-NetTCPConnection -LocalPort 9082`
in PowerShell) and stop that one. An orphaned child left running like this
is invisible in Visual Studio's own UI (which only ever launched the
wrapper) and will keep answering requests indefinitely, making the Gateway
*look* like it's still finding your debugger long after you've closed it —
which is exactly what happened while writing and verifying this doc.

**This project has no message-queue consumers**, unlike the sibling JameX
project — so there's no equivalent of "two instances competing for the same
message" to worry about here. The same-port exclusivity already prevents two
instances of one service from running at once at all.

---

## How it actually works

In `docker-compose.yml`, the Gateway container gets an extra host alias:

```yaml
extra_hosts:
  - "suggestx-host:host-gateway"
```

`host-gateway` is a special value Docker Desktop resolves to the host
machine's own address. The Gateway's cluster config then points at that
alias, on the same port the service publishes:

```jsonc
ReverseProxy__Clusters__collectionService__Destinations__instance__Address: "http://suggestx-host:9082/"
```

Whatever's bound to port 9082 **on the host** answers that request — which
is either the container (Docker published that exact port there) or your
local debugger (bound directly, since it's running on the host already).
Either way, the Gateway doesn't need to know or care which one it is.

### Why this isn't the usual two-destination failover pattern

The classic version of this idea (see the sibling JameX project) lists
**two** destinations per cluster — one for the container's direct in-network
address, one for a *separate* local debug port — and uses a load-balancing
policy to prefer the container when both happen to be reachable. That shape
makes sense when local and container run on genuinely different ports, since
they're then two real, independently-addressable backends.

This project tried that shape first and rolled it back. With same-numbered
ports, a running container's own published port answers the "local" address
too — Docker's port-forward doesn't care whether the request arrived from
outside the machine or looped back from another container via
`host-gateway`. So both destinations were provably always the same backend
whenever the container was up, and the load-balancing policy
(`FirstAlphabetical`) picked between them inconsistently in testing — never
incorrectly (every response was still correct, since both labels pointed at
the identical process), but impossible to honestly document as
deterministic, and needlessly confusing to read in the logs. One
destination removes the question rather than explaining around it.

`/health/live` is the right probe path either way: it reports only whether
the process is up and never touches a dependency, so a store blip doesn't
make the Gateway think the service has vanished.

### One Docker Desktop gotcha

Docker Desktop publishes **both** an IPv4 and an IPv6 record for a host
alias. .NET tries IPv6 first, there's no IPv6 route to the host, and every
probe fails with `Network is unreachable` — which reads like the service is
down rather than like a networking problem. The Gateway container therefore
sets `DOTNET_SYSTEM_NET_DISABLEIPV6: "1"`.

---

## Verified

All three states below were confirmed against the live stack — each by
posting a real request through the Gateway and independently confirming
*which* process actually handled it (via that process's own buffered-event
count going up, not just the HTTP status code, since a 202 alone can't tell
you which backend produced it):

```
container running,  local not running     → Gateway reaches the container  ✅
container stopped,  local running         → Gateway reaches your debugger  ✅
container running,  local start attempted → local fails to bind (port taken) ✅
```

The first pass at this verification was contaminated by an orphaned local
process from an earlier test that outlived the shell command used to "stop"
it (see the caveat above) — every state looked like it passed, but for the
wrong reason: the same leftover process answered every request regardless
of what was actually being tested. Caught by checking `docker compose ps`
mid-test and noticing the container the requests were supposedly reaching
wasn't running at all. Redone cleanly afterward with the process confirmed
killed by PID (found via the port it held, not the launcher's PID) before
each state transition.
