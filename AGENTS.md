# AGENTS.md

logmark is a local-first markdown vault engine: an agent-shared memory substrate you own. This repo is the open kernel, its daemon, CLI and MCP server, the block renderer (lblocks), the cockpit, and the versioned contracts.

## Setup and build

Requires the .NET SDK pinned in `global.json` and Node 20+.

- Kernel, daemon, CLI, MCP (C#): `dotnet build logmark.sln`. Run the daemon with `dotnet run --project src/LogMark.Daemon -- serve --vault <path>` (serves loopback `http://127.0.0.1:4747`).
- Renderer (`packages/lblocks`): `npm install && npm run build`.
- Cockpit SPA (`src/ui`, built by the root `package.json`): `npm install && npm run build` at the repo root, then `dotnet build src/LogMark.Daemon` to re-embed it. Without this step the daemon runs API-only.
- Release archives: `node build/dist.mjs`. Releases are cut from the maintainer pipeline, not by hand in a PR.

## Test

- C#: `dotnet test logmark.sln` (serialize with `-m:1` on Windows, where suites contend on the vault). Single suite: `dotnet test logmark.sln --filter FullyQualifiedName~CockpitCompatRoutesTests`.
- lblocks: `npm test` in `packages/lblocks` (vitest).
- Vault format: the fixtures in `spec-fixtures/` each carry an `expected.json` that the kernel parser must reproduce. Run the kernel tests after touching parsing, routing, or notation.

## Conformance is the contract

Two versioned contracts, each backed by fixtures. The rule that resolves every disagreement: **TypeScript is normative.** The JSON Schemas and the C# implementation are generated-from or verified-against the TS types, never the reverse.

- ViewBlock envelope: edit the `BlockType` union in `packages/lblocks/src/types/viewblock.ts` FIRST, then update the schemas and add a fixture under `schemas/viewblocks/fixtures/`.
- Daemon v0 API: the machine contract is `schemas/openapi/daemon-v0.json`.
- Vault format: `spec-fixtures/` (see its README). The spec prose lives in the `lm-vault` repository.

## Project map

- `src/LogMark.Kernel/` - the engine: vault read/write, notation, routing, search, indexing. No judgment, no licensing, no UI.
- `src/LogMark.Daemon/` - `logmark serve`: hosts a vault and serves the v0 HTTP/SSE API. Composition only; logic lives in the kernel.
- `src/LogMark.CLI/` - the `logmark <verb>` command surface, thin over the kernel.
- `src/LogMark.Mcp/` - the MCP server (`logmark mcp`), thin over the kernel.
- `src/*.Tests/`, `src/LogMark.TestUtilities/` - the C# test projects.
- `src/ui/` - the cockpit SPA (view, ratify, configure) over the daemon API.
- `src/adapters/` - TypeScript adapters the cockpit uses to reach the daemon.
- `packages/lblocks/` - self-rendering TS block primitives and the envelope types.
- `schemas/` - JSON Schemas, the OpenAPI doc, and the fixture corpus.
- `spec-fixtures/` - the vault format fixtures.
- `bundles/` - the open vault bundle (seed `vault.md`).
- `VERSION` - the single OSS version source.
- `build/dist.mjs` - builds the self-contained archive for one platform.

## Conventions

- No emojis, no em-dashes (use a single spaced hyphen), active voice.
- Match the surrounding file's style; keep files focused.
- Commit format: `type: brief summary` (e.g. `feat:`, `fix:`, `docs:`).
- CLI docs say `logmark <verb>`. The one-shot form is always `npx @logmark/cli` (the unscoped npm package is an unrelated logging library).
- Errors on the HTTP surface are RFC 9457 `application/problem+json`; missing reads return `200` with a null payload, not `404`.

## Boundaries

- Always: run the relevant test suite before committing a schema, notation, or surface change.
- Ask first: changing a versioned contract (envelope `specVersion`, the `/v0` path) - these are public interfaces.
- Never: commit secrets or credentials; edit generated files by hand (`block-types.json`, the envelope enum); add a dependency on any closed LogMark component (the kernel stays pure - the daemon purity tests enforce it; the kernel purity test lives in the desktop suite).
- Never: change `VERSION` in a PR; releases bump it.
