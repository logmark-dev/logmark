---
schema: lm-vault/1.0.0
---

# My vault

A shared memory for me and the agents I work with. Every entry is a dated, typed, attributable claim in plain markdown that I own.

## Scope

INCLUDE: everything I choose to keep EXCLUDE: secrets, credentials, anything I would not want in a file on disk

## How agents should help

- Orient first: call `vault_summary`, then `get_project_context` for the route you are working in.
- Write one claim per entry. Type it (`t:` task, `i:` idea, `b:` block, `d:` decision, `q:` question, `g:` gap). Route it with `+route` when it belongs to a project.
- Never edit yesterday's entry to change its meaning. Add a new dated entry; use `update_entry` only to advance lifecycle (resolve, freeze, dismiss).
- Your entries carry `source:` automatically. Do not impersonate a human entry.
- Ask before creating a new route. Reuse the routes `get_routes` returns.

## Frozen vault-level decisions

(none yet)

## Don't surface

(none yet)

## Conventions

- Tags are lowercase. Faceted tags use `/` (`#surface/mcp`).
- Decisions get `#frozen` when final, `#active` while under review.
