---
name: lm-notation
description: The LogMark notation grammar and vault-conventions reference used by the other lm-* work skills. Covers entry types (t/i/b/d/q/g), routing with +route and cross-cutting #tags, the decision freeze lifecycle (#active/#frozen as a reading convention not an app gate), episodic vs semantic memory, and the vault constitution sections. Read it once per session before capturing or deciding.
---

# LogMark notation reference

The grammar the work skills share. Cited by every skill in this pack.

## Entry types

A line's prefix sets its type. No prefix means a plain log entry.

| Prefix | Short | Meaning |
|---|---|---|
| `task:` | `t:` | An actionable item. |
| `idea:` | `i:` | A thought worth preserving; the idea backlog. |
| `block:` | `b:` | Explicitly stuck on something. |
| `decision:` | `d:` | A choice made. |
| `question:` | `q:` | An open inquiry. |
| `gap:` | `g:` | Known knowledge debt. |
| (none) | | A log entry. |

## Routing and tags

- `+route` files an entry into a project; `+route/scope` into a scope within it. Routes are the PRIMARY structure (where an entry lives). Defined in `logmark/_system/routes.md`. Only a declared route routes: any other `+word` in the text (a quoted "+Security", "+USD 5,999") stays as plain text.
- `#tag` is a free-form cross-cutting label. Tags are the SECONDARY structure (cross-cutting recall), not load-bearing in the app. The project's tag vocabulary and meanings live in `logmark/_system/tags.md`. Common conventions: status tags (`#active`, `#frozen`, `#resolved`), aspect tags (`#security`, `#breaking`), and project-defined tags (for example `#adr`, release `#vX_Y_Z`). Apply only tags the registry defines or the constitution's Conventions specify.

## Decision lifecycle (a convention, not an app gate)

A decision is `#active` when proposed and unconfirmed, `#frozen` once the human confirms it. Nothing in the app enforces this; it is a reading-and-writing discipline. Treat `#frozen` decisions as durable facts and `#active` ones as proposals to confirm in conversation. Never silently flip another entry's tag; only act on explicit human instruction, using `update_entry(entry_id, status: "frozen")` to confirm or `update_entry(entry_id, status: "rejected")` to reject.

## Episodic vs semantic memory

- **Episodic** - what happened: logs, tasks, working entries. Retrieved by recency (recent activity).
- **Semantic** - what is durably true: frozen decisions. Retrieved by relevance (by route), not by date, so it never falls out of a recent-activity window.

Orient reads both: the durable/semantic layer (constitution + per-route frozen decisions) AND the recent/episodic layer.

## The vault constitution (logmark/vault.md)

The project's own configuration that a generic skill reads. Sections:

- `# Title` and a purpose paragraph - what the vault is and why it exists.
- `## Scope` - `INCLUDE:` / `EXCLUDE:` lines naming in- and out-of-bounds routes and tags.
- `## How agents should help` - posture for agents working this vault.
- `## Conventions` - process rules: tag scheme, ADR convention, release tagging, grooming cadence, freeze discipline.
- `## Frozen vault-level decisions` - foundational decisions pinned here so they are always surfaced and never decay.
- `## Don't surface` - things to keep out of summaries.

Read it with `get_constitution`. Write it with `update_constitution` (bootstrap only).
