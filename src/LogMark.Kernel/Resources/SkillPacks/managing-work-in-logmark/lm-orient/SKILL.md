---
name: lm-orient
description: Use at the start of a work session, or whenever you need to reconstruct project state, to read a LogMark vault as memory. Surfaces what has been worked on, what is important (frozen decisions), what is stuck (open blocks), what is next (open tasks and groomed ideas), and what is done. Reads the vault constitution, per-route frozen decisions, and recent activity. If the vault is unestablished, says so, so you can bootstrap.
---

# Orient

Reconstruct project state before doing any work. Read in three tiers, never by recency alone. Notation and the constitution are described in the `lm-notation` skill.

## Three-tier read

1. **Constitution (durable, pinned).** Call `get_constitution`. It returns the vault's purpose, scope, conventions, `Frozen vault-level decisions`, and `Don't surface`. If it reports `Established: false`, STOP and report that the vault is unestablished - the caller should run `bootstrap`.
2. **Per-route semantic.** For each route in scope, call `get_project_context` with the route. To surface proposals as well as durable facts, pass `status_tags: ["#frozen", "#active"]` (the default is `#frozen` only). This returns that route's frozen and active decisions (durable, by relevance not date), open blocks, and active tasks.
3. **Recent episodic.** Call `list_recent` for what happened lately.

## Continuing, not starting

When the injected context opens with `Continuing work.` or `Continuing work in +<route>.`, this session is a continuation: the handoff (under "Where you left off:") and the in-flight work are already in front of you, and the backlog has been deliberately withheld. Lead from the handoff and keep going. Do not re-run the three-tier read to reconstruct state you were just handed.

The context ends with a count of the open items it did not list. Pull them with `get_project_context` or `get_blocks` only when the current work actually needs them - the reason they are a count rather than a list is that re-reading them is what pulls a resuming session off its task.

## Output

Summarize for the developer, honoring `Don't surface`:

- **Worked on / done** - recent activity and completed tasks.
- **Important** - frozen decisions (vault-level first, then per-route).
- **Stuck** - open blocks.
- **Next** - open tasks and any groomed, ready ideas.
- **Hygiene** - if open tasks number above ~30, or the oldest open task or block is older than ~30 days, say so and suggest running `lm-groom-backlog`. Orient never closes anything itself.

Treat `#frozen` decisions as durable facts and `#active` decisions as proposals to confirm in conversation. Do not re-tag anything here; orient only reads.
