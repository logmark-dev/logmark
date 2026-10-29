import { relative, sep } from 'node:path'
import type { VaultEvent, VaultEventKind } from '../types/vault-events.js'

/**
 * Path-to-kind mapping shared by every vault-event producer (adapters, mock
 * daemon, and later the real daemon - see docs/reference/daemon-http-api-spec.md
 * in the umbrella repo). Non-markdown paths produce no event.
 */
export function classifyVaultPath(vaultRoot: string, absolutePath: string): VaultEventKind | null {
  if (!absolutePath.endsWith('.md')) return null
  const rel = relative(vaultRoot, absolutePath).split(sep).join('/')
  if (rel === 'vault.md' || rel.endsWith('/vault.md')) return 'constitution-changed'
  if (rel === 'constitution.md' || rel.endsWith('/constitution.md')) return 'constitution-changed'
  // Canonical `_concepts/` (contentRoot-relative, per ConceptPageStore) alongside
  // the legacy `concepts/` dir - mirrors VaultChangeWatcher.Classify.
  if (rel.startsWith('_concepts/') || rel.includes('/_concepts/')) return 'concepts-changed'
  if (rel.startsWith('concepts/') || rel.includes('/concepts/')) return 'concepts-changed'
  return 'entries-changed'
}

/** Group a debounced batch of changed paths into at most one event per kind. */
export function eventsForPaths(vaultRoot: string, absolutePaths: string[]): VaultEvent[] {
  const byKind = new Map<VaultEventKind, string[]>()
  for (const p of absolutePaths) {
    const kind = classifyVaultPath(vaultRoot, p)
    if (!kind) continue
    const list = byKind.get(kind) ?? []
    list.push(relative(vaultRoot, p).split(sep).join('/'))
    byKind.set(kind, list)
  }
  return Array.from(byKind.entries()).map(([kind, paths]) => ({ kind, paths }))
}
