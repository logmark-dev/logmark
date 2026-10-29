---
name: lm-flag-blocker
description: Use when work is stuck on an unresolved obstacle, to record it as a typed LogMark block entry, or when a previously raised blocker is cleared, to resolve it. Blocks are the what-is-stuck signal that orient reads back at the start of the next session.
---

# Flag blocker

Make stuck-ness visible. Notation is in the `lm-notation` skill.

## Raise

Write a `b:` entry stating what is blocked and, briefly, what would unblock it. Route it to the relevant `+route` with `create_entry`. Open blocks surface in orient automatically.

## Resolve

When the obstacle clears, call `update_entry(entry_id, status: "resolved")` (or `status: "dismissed"` if it no longer matters). The open-blocks view that orient reads (via `get_project_context`) treats a block as open until it carries `#resolved` or `#dismissed`.

Do not resolve a block you did not actually clear; leave it for orient to keep surfacing.
