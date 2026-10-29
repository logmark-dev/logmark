---
name: lm-decide
description: Use when a decision is made or proposed during project work, to record it as a typed LogMark decision entry. Write it frozen when the developer has confirmed it, active when you inferred it and it is unconfirmed. Foundational vault-wide decisions are pinned into the constitution; route-specific ones go into their route.
---

# Decide

Record a choice as a `d:` entry. Notation and the decision lifecycle are in the `lm-notation` skill.

## Freeze state (a convention, not an app gate)

- The developer confirmed it in conversation -> tag `#frozen`. The interview during bootstrap counts as confirmation.
- You inferred it from context and it is unconfirmed -> tag `#active`, so orient surfaces it for the developer to confirm later.

Never silently change an existing decision's tag. On explicit human instruction, advance it with `update_entry(entry_id, status: "frozen")` to confirm, or `update_entry(entry_id, status: "rejected")` to reject. Acting only on explicit instruction, never on inference, still holds.

## Where it goes

- **Vault-wide and foundational** - a decision that shapes the whole project. Pin it into the constitution's `## Frozen vault-level decisions` via `update_constitution` (read current with `get_constitution` first, then write the amended markdown). These never decay out of a recent-activity window.
- **Route-specific** - write it as a `d:` entry routed to its `+route` with `create_entry`.

State the decision and, briefly, its rationale.
