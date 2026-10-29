---
name: lm-bootstrap
description: Use once per vault, when orient reports the vault is unestablished (no constitution, no entries) or an established vault is missing a Conventions section. Populates a new LogMark vault from the project's own artifacts and a short developer interview, then writes the constitution, routes, tag registry, and seed entries. This is the only skill that authors the vault constitution.
---

# Bootstrap

Fill an empty vault from knowledge that already exists outside it: the project's files and the developer's intent. Notation and constitution sections are in the `lm-notation` skill.

## 1. Detect state

Call `get_constitution`.

- `Established: false` and no entries -> full bootstrap (steps 2-6).
- `Established: true` but `Conventions` is empty -> a pre-skills vault adopting this layer: do ONLY step 3's convention questions and step 4's constitution write (appending a `## Conventions` section); skip seeding.
- `Established: true` with conventions -> nothing to do; tell the caller to run orient.

## 2. Ingest project artifacts (autonomous)

Read the project: README, `docs/`, any ADR or decisions folder, a roadmap or backlog, the directory structure, recent `git log`, and package manifests. These are the richest source of decisions, workstreams, and open work.

## 3. Interview the developer

Ask for what artifacts cannot reveal, briefly:

- Purpose: what is this project, in one or two sentences.
- Workstreams: the major areas of work (these become routes/scopes).
- Locked decisions: choices already final.
- Stuck: anything currently blocked.
- Conventions: how releases are tagged, whether ADRs are kept and under what tag/route, how often to groom the idea backlog, when a decision counts as final.

## 4. Author the vault

- `update_routes` - the workstreams as routes and scopes.
- `update_tags` - the tag registry from the conventions.
- `update_constitution` - the full `logmark/vault.md`: title and purpose, `## Scope`, `## How agents should help`, `## Conventions` (from the interview), `## Frozen vault-level decisions` (the foundational locked choices), and `## Don't surface`.

## 5. Seed entries

Write the final tag at creation; do not rely on a later transition (the interview IS the confirmation):

- Decisions the developer confirmed -> `decide` as `#frozen`. Foundational, vault-wide ones go into the constitution's `## Frozen vault-level decisions`; route-specific ones into their route.
- Decisions you inferred but the developer did not confirm -> `decide` as `#active`, for later confirmation.
- Open obstacles -> `flag-blocker`. Idea backlog and a few orienting logs -> `capture`, routed appropriately.

## 6. Checkpoint

Run `checkpoint` so the next session's orient reads a populated vault.
