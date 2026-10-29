---
schema: lm-vault/1.0.0
---

# Code context for AI

## Purpose
Persistent context I give to coding agents across sessions: repo conventions, architecture decisions, things I want them to remember. The vault is the long-term memory for the AI that helps me build.

## Scope
INCLUDE: repo conventions, architecture decisions and rationale, patterns I want preserved, cross-session reminders, code-context captures from work sessions. EXCLUDE: ephemeral debugging notes, scratch.

## How agents should help
Treat this as your context library. When asked, summarise relevant prior decisions before suggesting new ones. Flag contradictions between what I'm asking and what's already decided. Maintain provenance: which decisions are stale, which are current.

## Don't surface
Anything tagged #scratch or #ephemeral; debugging logs older than a month.
