# Architecture

## The rings

LogMark's product surface splits into rings, from the format spec outward to the paid services layered on top of it. This repository ships the open rings; everything above them is a consumer of what is defined here.

| Ring | Contents | Source | Price |
|---|---|---|---|
| 0: Spec | notation, frontmatter, provenance, vault layout, block envelope schema | open docs | - |
| 1: Kernel | parse/route/store/search/lint/organize/export/agent-memory + MCP (kernel tool set) + CLI (kernel commands) + `logmark serve` daemon | open (C#) | free |
| 1.5: Render | lblocks primitives + theme engine | open (TypeScript) | free |
| 2: Cockpit | the browser UI: view + ratify + configure, embedded in the daemon binary | open (TypeScript) | free |
| 3: Body | the LogMark desktop app: capture surface, presence, native views | closed | free tier |
| 4: Judgment | digests, agents, flows, custom protocols, behaviors, sensitivity and private routes, Ink producers - closed producers that emit open-renderable blocks | closed | paid (per capability) |
| 5: Services | cloud inference, Ink hosting, sync relay, Telegram and mobile relays, integrations | closed | paid |

Rings 0 through 2 are exactly what lives in this repository. Rings 3 through 5 are the LogMark desktop app and the hosted services around it - out of scope here, and covered by the scope statement in the README.

## The membrane

The boundary between open and closed is not a flag anywhere in this code. It is the typed block envelope (`ViewBlock`): a producer emits a typed, versioned block of data, and a renderer displays it without needing to know which producer made it or whether that producer is open or closed. The cockpit in this repository is a renderer. It can display the output of paid features - a digest, a synthesis, an agent's proposal - without containing any of the logic that produced them, because it never contains the producer at all when running as the open build.

Concretely: a closed installation registers extra producers through an explicit registration seam (`AddProducers`), the same shape used by the MCP tool registry for adding tools. The open daemon and open MCP server simply have no producers registered beyond the kernel's own. The gate is whether a producer exists in the running process, not a license check inside shared code. Unknown or newer block types degrade gracefully in the renderer instead of throwing, so a closed producer can ship a block type this open renderer has never seen and the cockpit still shows something sensible rather than breaking.

## Host and serving roles

`logmark serve` and the LogMark desktop app can both act as the resident host for a given vault - watching it for changes, running the producer host, and owning the single writer to the vault's event log - but only one of them holds that role for a given vault at a time, enforced by a vault-scoped mutex.

The HTTP/SSE serving role (the API the cockpit talks to) travels with whichever process holds the host role: the daemon and the app serve an identical API on the same loopback port for a given vault, so a cockpit browser tab survives a handover between them without noticing. Handoff is app-priority: when the desktop app opens a vault the daemon is already hosting, it signals the daemon to yield, the daemon relinquishes the host role within about a second, and it resumes standby once the app either takes over or exits. The practical effect: your agents keep working against `logmark serve` when the desktop app is closed, and the desktop app always wins when it is open.

## Two compatibility contracts

Everything in this ecosystem is free to release on its own schedule and its own version sequence, with exactly two exceptions - the two numbers anyone building against LogMark actually needs to track:

1. **Vault format version.** The spec: notation, frontmatter, and layout (the `lm-vault` repository). A vault written to a given format version opens correctly in any compliant implementation of that version.
2. **Envelope schema version.** The `ViewBlock` shape and its conformance fixtures (`schemas/viewblocks/` in this repository, normative source at `packages/lblocks/src/types/viewblock.ts`). A renderer built against a given envelope version can display any producer's blocks at that version, with graceful degradation for anything newer.

Everything else - the desktop app's release cadence, the web platform's tags, this binary's own version sequence, `@logmark/lblocks`'s npm cadence - moves independently and simply declares which contract versions it speaks.

## Project map

```
src/
  LogMark.Kernel/      parse, route, store, search, lint, organize, export,
                        agent memory - the core engine, zero closed references
  LogMark.Daemon/       logmark serve + logmark mcp + CLI composition root;
                        the unified logmark binary is built from here
  LogMark.CLI/          kernel-verb command implementations
  LogMark.Mcp/          the MCP stdio server (kernel tool set in this build)
  LogMark.TestUtilities/ shared test fixtures and helpers
  ui/                   the cockpit SPA (view, ratify, configure)
  adapters/             cockpit data adapters (filesystem, daemon HTTP/SSE)
  types/, lib/          cockpit shared types and utilities
  services/             cockpit-side file operations with no judgment
                        attached (concepts, constitution, lint, settings)
packages/lblocks/       self-rendering view-block primitives + theme engine,
                         the normative source for the envelope schema
schemas/viewblocks/     JSON Schema + conformance fixtures for the envelope
```

Kernel, daemon, CLI, and MCP server compile into one self-contained .NET binary named `logmark`; the cockpit SPA builds separately and is embedded into that same binary as static assets.
