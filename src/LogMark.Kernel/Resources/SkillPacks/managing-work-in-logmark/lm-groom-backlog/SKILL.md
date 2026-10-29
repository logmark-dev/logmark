---
name: lm-groom-backlog
description: Use periodically, per the vault's grooming cadence, to curate the backlog - triage open ideas and reconcile stale tasks and blocks. Promotes viable ideas, proposes closures for tasks that are done or dead, collapses duplicates, reroutes inbox strays, and retypes mistyped entries (done-statements to log, durable constraints to gaps). Everything is proposed as one numbered batch for the developer to approve; nothing is applied without confirmation. This is a periodic activity, not part of the per-session loop.
---

# Groom backlog

Curate the vault's open work: ideas, tasks, and blocks. Notation is in the `lm-notation` skill. Cadence is set in the constitution's `## Conventions`. Grooming is entirely Tier B (cross-session inference): propose, get the developer's approval, then apply. Never close or retype anything without confirmation. Apply changes via `update_entry` and the organize verbs, never by editing vault markdown directly.

## Steps

1. **Gather.** Open tasks and blocks via `get_tasks` and `get_blocks` (optionally by `+route`); open ideas via `search` for `i:` entries or `list_recent`.
2. **Check addressability before proposing anything.** If any gathered item carries `unaddressable: true`, stop here. Those entries have no persisted id anchor, so every closure, retype, reroute, and retag against them fails - two earlier grooming runs each lost roughly 19 of 22 approved actions to exactly this, item for item. Report how many are affected and ask the developer to run `lm entry mint-anchors --apply` (or apply the `missing-entry-anchor` issues in the app's Lint lens). Minting assigns fresh ids, so every id gathered above goes stale: start the run over from step 1 afterwards rather than carrying ids across the fix.
3. **Triage ideas** against the constitution's purpose and scope:
   - Ready to act - promote to a task (`t:`), routed.
   - A choice the project should lock - hand to `lm-decide`.
   - A whole new area of work - propose a route (`update_routes`, with the developer's nod).
   - Still speculative - leave it as an idea. Do not delete ideas; demotion is leaving them as ideas.
4. **Triage tasks and blocks.** Classify each open item:
   - Done - evidence (vault activity, git history, the conversation) says the work happened - propose closing with a resolution note naming the evidence.
   - Dead - overtaken by events, no longer worth doing - propose dismissing it: `update_entry` with `status: dismissed` and a `resolution` naming why. Reserve `complete: true` for work that actually happened; marking an overtaken task complete records a falsehood in the vault.
   - Duplicate - the same item captured more than once - propose keeping one and dismissing the rest, each resolution pointing at the survivor's id.
   - Mistyped - a completed-work statement recorded as `t:` - propose retype to log. A durable limit or environmental constraint recorded as `b:` - propose retype to `g:`.
   - Stray - an unrouted `_inbox` item that belongs to a route - propose rerouting it there.
   - Still real - leave it open.
5. **Propose.** Present one numbered batch - closures, retypes, reroutes, promotions - each with a one-line reason. Wait for approval; apply only what the developer confirms.
6. **Report** what changed and what was left, so the next orient is clean. Report what each mutation actually returned, not what it was asked to do: a call that reports success while changing nothing is the failure this ritual exists to catch.
