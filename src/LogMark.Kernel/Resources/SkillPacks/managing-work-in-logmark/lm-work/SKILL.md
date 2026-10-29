---
name: lm-work
description: Read first when you sit down to work on a project whose memory lives in a LogMark vault. Frames the work loop (orient, then capture/decide/flag as work happens, then checkpoint at the end) and dispatches to the lm-* operation skills. The vault is the project's external memory across sessions and agents.
---

# Managing work in LogMark

A LogMark vault is this project's external memory: typed entries (`t:` task, `i:` idea, `b:` block, `d:` decision, `q:` question, `g:` gap) filed into `+routes` and tagged with `#tags`. You read it to recover context and write to it so the next session - yours or another agent's - starts informed. Notation and the vault constitution are described in the `lm-notation` skill (read it once per session).

## The loop

1. **Orient first.** Run the `lm-orient` skill. It reconstructs project state from three tiers (constitution, frozen decisions, recent activity). If orient reports the vault is unestablished, run `lm-bootstrap` before any other work.
2. **Capture as you go.** When a task, idea, log, question, or gap arises, use `lm-capture`. When a choice is made, use `lm-decide`. When you hit an obstacle, use `lm-flag-blocker`.
3. **Checkpoint at the end.** Before you stop, run `lm-checkpoint` - it writes the handoff and closes any open tasks or blocks this session completed, so the next session's orient picks up cleanly.
4. **Groom periodically.** Per the vault's grooming cadence (in the constitution's Conventions), run `lm-groom-backlog` to triage open ideas and reconcile stale tasks and blocks.

## Operation skills

- `lm-orient` - read project state (start of session).
- `lm-bootstrap` - establish a new or convention-less vault (one time).
- `lm-capture` - file a typed entry (task/idea/log/question/gap).
- `lm-decide` - record a decision and its freeze state.
- `lm-flag-blocker` - raise or resolve a block.
- `lm-checkpoint` - write a session-end handoff.
- `lm-groom-backlog` - curate the backlog: ideas, stale tasks, blocks.

## Conventions live in the vault, not in you

You have no per-project settings. Everything project-specific - the tag scheme, ADR convention, release tagging, grooming cadence, what to surface and what to hide - lives in the vault constitution (`logmark/vault.md`) and the tag registry (`logmark/_system/tags.md`). Read them; do not invent conventions. `lm-orient` and `lm-bootstrap` handle reading and (for bootstrap) writing them.
