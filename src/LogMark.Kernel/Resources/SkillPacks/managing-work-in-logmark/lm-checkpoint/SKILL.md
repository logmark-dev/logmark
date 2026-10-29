---
name: lm-checkpoint
description: Use at the end of a work session to write a handoff and reconcile, so the next session's orient picks up cleanly. Records current status, what is in flight, and the next action as typed LogMark entries, and closes open tasks or blocks that this session completed or unblocked. Use this before you stop working, not mid-task.
---

# Checkpoint

Leave the next session (yours or another agent's) a clean starting point. Notation is in the `lm-notation` skill.

## Steps

1. **Open or close a session** if the vault uses sessions: `start_session` at the beginning groups the session's writes; `end_session` here closes it.
2. **Write the handoff** as entries routed to the active `+route`:
   - a log entry summarizing status (what got done this session), tagged `#handoff`,
   - a `t:` task for the next concrete action if one is open.

   The `#handoff` tag is what the next session's orientation resolves to when it reconstructs where you left off; an untagged log is only found by being newest, which fails as soon as another agent writes to the route after you.
3. **Reconcile before you leave.** Compare the session against the route's open tasks and blocks (`get_tasks`, `get_blocks`):
   - Work this session completed or unblocked: close it now via `update_entry` - tasks `complete: true` with a resolution note citing the session; blocks `status: resolved` (or `dismissed` if overtaken). This is the only auto-close case - the evidence is from this session.
   - Work this session made moot rather than finished: dismiss it, tasks included (`status: dismissed` plus a `resolution` naming what overtook it). Do not reach for `complete: true` to clear it - that records work that never happened.
   - Anything that merely looks stale or superseded: leave it open, mention it in the handoff, and let `lm-groom-backlog` triage it.
4. **Do not dump raw context.** A handoff is a few typed entries, not a transcript. Status, in-flight, next action - that is enough for orient to reconstruct from.

## A compaction is not a checkpoint

When the host compacts a long session it records its own checkpoint stub and links it to your most recent handoff. You do not need to duplicate that summary. What the stub cannot do is reconcile - closing what this session finished, dismissing what it made moot - so that remains the part of this skill that only you can perform.
