# Contributing

Thank you for considering a contribution. Some honest terms up front, so expectations match reality.

## How this repository is maintained

This repository is published from the maintainer's private tree, where the open engine and the closed LogMark desktop app develop side by side. Syncs are append-only snapshots - public history is never rewritten.

What that means for your pull request: when a PR is approved, it is first applied to the private tree with your authorship preserved (`Co-authored-by`), then merged here. Every subsequent sync therefore already contains your change - and the publish pipeline enforces this: it refuses to push any snapshot that would revert a change merged in this repository. Approved work cannot be silently undone by a sync.

The practical effect on you is a short delay between approval and merge while the change lands in the private tree, and it means your fix ships in the desktop app too, not just in the open binary.

## Contributor License Agreement

LogMark Engine is AGPL-3.0-only, and the same code also ships in the closed LogMark desktop app and under commercial licenses. That only works if the maintainer can license every line under those terms, so each contributor signs the [Contributor License Agreement](CLA.md) once, before their first pull request is merged. The CLA bot comments on your first pull request with the one-line reply that signs it.

The CLA is a license, not a transfer: you keep the copyright in your contribution and may use it however you like. It also confirms the contribution is yours to give.

## This is a solo-maintained project

Triage runs on a budget of roughly two hours a week. That is enough to review small, focused pull requests and respond to issues on a weekly cadence - it is not enough to shepherd large refactors, design debates, or multi-week reviews. If your change is more than a few hundred lines, open an issue first and describe the problem before writing the code; a short conversation up front saves both of us a large diff that has to be reworked or declined later.

## What makes a PR easy to merge

- **Small and focused.** One fix or one small feature per PR. Split large changes into a sequence of small ones if you can.
- **Tests included.** New behavior needs a test; a bug fix needs a regression test that fails before the fix and passes after.
- **Conformance fixtures stay green in both runtimes.** The envelope schema and the vault format are shared contracts between the .NET kernel and the TypeScript cockpit/lblocks side. A change to `schemas/viewblocks/` or to vault-format parsing must pass its fixtures under both `dotnet test` and `vitest run` before it merges - a fixture passing in only one runtime is a drift bug waiting to ship.
- **No emojis, no em-dashes.** Use a spaced hyphen ( - ) instead of an em-dash. Plain, direct language in docs and commit messages.

## Where a change belongs: kernel or closed

If your contribution adds a new capability and you are not sure whether it belongs in this open kernel or in a closed producer, apply the frozen test: **does it read or write vault markdown/files with no judgment attached?**

- Mechanical parsing, formatting, routing, storage, and search: kernel-homed, open.
- Anything that scores, ranks, synthesizes, or decides on behalf of the user - salience, summarization, autonomous proposals - is judgment, and belongs in a closed producer registered through the `AddProducers` seam, not in this repository.

This is the same rule the maintainer applies internally when a tool or verb graduates from closed to open, so PRs that add new MCP tools or CLI verbs will be reviewed against it too. The open tool and verb set only grows over time - nothing that ships here is later pulled back out.

## Reporting bugs

Open an issue with: what you ran, what you expected, what happened instead, and your platform (`win-x64` / `osx-arm64` / `linux-x64`). Include the output of `logmark --version`.

For anything that might be a security issue, see [SECURITY.md](SECURITY.md) instead of opening a public issue.

## Development

Prerequisite: the .NET SDK version pinned in `global.json` (installing the latest .NET 9 SDK satisfies it), plus Node 20+ if you want the cockpit UI.

```
dotnet build logmark.sln
dotnet test logmark.sln
```

That builds and tests the engine; the daemon binary lands at `src/LogMark.Daemon/bin/Debug/net8.0/logmark` (`logmark.exe` on Windows) and serves API-only. To bake the cockpit UI into it, build the SPA first, then rebuild the daemon:

```
npm install
npm run build
dotnet build src/LogMark.Daemon
```

`npm run build` chains the lblocks primitives (`tsc`), the cockpit (`vite build`), and the embed copy into `src/LogMark.Daemon/cockpit-dist/` - the daemon's csproj embeds that folder when it exists.

## Scope

Issues and PRs about the LogMark desktop app, cloud services, Ink, Telegram, or mobile belong at [logmark.dev](https://logmark.dev), not here - see the scope statement in [README.md](README.md).
