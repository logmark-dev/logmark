# LogMark.Daemon (`logmark serve`)

The open headless vault host (substrate split Phase 3). References `LogMark.Kernel` only - closed assemblies are structurally absent, enforced by `LogMark.Daemon.Tests/DaemonPurityTests`.

## Run

```powershell
dotnet run --project src/LogMark.Daemon -- serve --vault C:\path\to\vault [--port 4747]
```

- Port defaults to 4747 (the cockpit's `LK_DAEMON_URL` default); also settable via `LOGMARK_DAEMON_PORT`. Binds 127.0.0.1 only.
- Standby semantics (design 4.3, amended 2026-07-13): the daemon polls the per-vault host lock with jittered backoff (1-5s). If another process (the desktop app) holds the vault, it waits and reacquires when the holder exits.
- Auto-yield (design 4.3 daemon auto-yield follow-up): while hosting, the daemon holds the lock via `VaultHostLock.TryAcquireDetached` (safe to release from a different thread) and watches a per-vault `VaultHostSignal` event-driven, not polled. When the desktop app announces it wants the role back, the daemon logs `yield requested by desktop app`, cancels the hosting loop, tears down HTTP/producers/vault services, and releases the lock - then enters a grace window before resuming standby: it polls the want-signal every **250ms** for up to **15s**, exiting early the moment the app clears the signal (meaning it has reacquired) or on daemon shutdown. Both the poll interval and the grace cap are parameters on `WaitForHandoffGraceAsync` (defaults 250ms / 15s), not hardcoded constants, so tests never have to sleep the real 15s. A plain Ctrl+C shutdown is unaffected by any of this - it is distinguished from a yield by checking whether the shutdown token (not just the linked yield token) was cancelled.
- Serves the daemon HTTP/SSE API v0: umbrella `docs/reference/daemon-http-api-spec.md`.

## Verify (Phase 3 acceptance) - maintainer layout (private umbrella workspace)

This recipe needs the cockpit repo and its fixture vault, which exist only in the private umbrella workspace (`LogMark.Knowledge/` is a sibling of this repo there). It does not apply to a public clone.

The conformance bar is the cockpit's adapter contract suite:

```bash
# 1. Boot the daemon over a COPY of the acceptance fixture
cp -r ../LogMark.Knowledge/dev/fixtures/logmark-vault /tmp/lm-acceptance
dotnet run --project src/LogMark.Daemon -- serve --vault /tmp/lm-acceptance --port 4790

# 2. From LogMark.Knowledge/: run the opt-in real-daemon suite (15 cases)
LK_REAL_DAEMON_URL=http://127.0.0.1:4790 npx vitest run tests/unit/adapters/kernel-http.real-daemon.test.ts

# 3. SSE spot check
curl -N http://127.0.0.1:4790/api/v0/events   # then touch a .md in the vault
```

First verified run: 2026-07-13, 15/15 passed; SSE emitted `entries-changed` with correct relative paths plus heartbeat.

## Use with the cockpit

Two ways to get the cockpit UI in front of the daemon's API.

**A. Node cockpit server against the daemon's API** - maintainer layout (private umbrella workspace; dev loop, hot reload):

```bash
cd ../LogMark.Knowledge
LK_ADAPTER=kernel-http LK_DAEMON_URL=http://127.0.0.1:4747 npm start
```

**B. Embedded cockpit, baked into the binary** (one self-contained process, no separate Node server). This works from a public clone: the cockpit sources sit at the repository root, and `build/embed-cockpit.mjs` copies `dist/ui` into `src/LogMark.Daemon/cockpit-dist`, which the daemon project embeds at compile time.

```bash
# From the repository root
npm install
npm run build                    # build:lblocks, then build:ui (vite build -> dist/ui),
                                 # then build:embed (node build/embed-cockpit.mjs)
dotnet build src/LogMark.Daemon --configuration Release

# Run it (logmark.exe on Windows)
src/LogMark.Daemon/bin/Release/net8.0/logmark serve --vault <path-to-vault>
```

`npm run build` runs the three steps in order; to run them individually use `npm run build:ui` then `node build/embed-cockpit.mjs` (the lblocks package must already be built). GET `/` (and every other non-`/api` path) then serves the cockpit SPA directly from the daemon.

The embedded UI is a compile-time snapshot: rebuilding the cockpit has no effect on an already-built daemon binary - rebuild the daemon to pick up a newer cockpit. A daemon built without the cockpit step still serves the full `/api/*` surface; non-`/api` paths return a small inline "API only" notice page rather than a 404 or 500. In the private workspace the binary is named `logmark-daemon` (the `logmark` name applies under `OssBuild=true`, which the public tree sets).

## Layout

- `Program.cs` - `serve` verb, standby acquire loop, auto-yield/handoff-grace, shutdown ordering
- `Http/DaemonHttpServer.cs` - loopback HttpListener host (CaptureBridge pattern); dispatch chain is v0 -> cockpit compat -> embedded cockpit static/SPA server
- `Http/V0Routes.cs` - the v0 endpoint table over kernel services
- `Http/CockpitCompatRoutes.cs` - the cockpit SPA's `/api/*` contract, aliased onto v0 (plus `/api/pages` via `Http/CockpitPageResolver.cs`)
- `Http/CockpitStaticServer.cs` - serves the embedded cockpit SPA (exact asset match, index.html as SPA fallback, or the API-only notice when no cockpit was baked in); resources come from this project's `cockpit-dist/` folder
- `Http/SseChannel.cs` + `Http/VaultChangeWatcher.cs` - vault events -> SSE
- `Cockpit/ConceptPageStore.cs` + `Cockpit/SettingsStore.cs` - pure fs stores (concepts/*.md frontmatter pages; logmark/_system/cockpit-settings.json)
