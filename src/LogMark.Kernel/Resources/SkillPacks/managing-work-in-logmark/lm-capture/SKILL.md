---
name: lm-capture
description: Use whenever a task, idea, log note, open question, or knowledge gap arises during work, to record it as a typed LogMark entry routed to the right project. Picks the entry prefix and applies the project's tag conventions. Use this for everything except decisions (use decide) and blockers (use flag-blocker).
---

# Capture

File a single typed entry fast. Notation is in the `lm-notation` skill.

## Steps

1. **Pick the type** by what the thing is: `t:` actionable, `i:` idea worth keeping (the backlog), `q:` open question, `g:` knowledge gap, or no prefix for a plain log.
2. **Route it.** Add `+route` (or `+route/scope`) for the project it belongs to. If unsure which route, call `get_routes`. Stay within the constitution's `## Scope`.
3. **Tag it** with any `#tags` the project's `logmark/_system/tags.md` registry or the constitution's `## Conventions` call for (for example `#adr`, a release tag). Do not invent tags.
4. **Write it** with `create_entry`, or `backfill_batch` when seeding several at once.

Keep entries terse - one line of substance. Capture is the fast path; do not stop to deliberate (that is `decide`).
