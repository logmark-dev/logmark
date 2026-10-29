import type { Entry, EntrySummary } from '../types/entry.js'
import type { ConceptPage, ConceptPageSummary } from '../types/concept-page.js'
import type { VaultSummary } from '../types/vault.js'
import type { LintScanResult } from '../types/lint.js'
import type { KnowledgeSettings } from '../types/settings.js'
import type { CapabilitiesResponse } from '../types/host.js'

const BASE = ''

/**
 * Thrown by `get`/`post` on a non-ok response (daemon-hosted-cockpit
 * degraded states, lmk-nhjr Task 4): carries the HTTP status so views can
 * distinguish a genuine 501 "not implemented under daemon hosting" from
 * any other failure and render the designed unavailable state instead of
 * the generic route-error/failure banner.
 */
export class ApiError extends Error {
  readonly status: number

  constructor(status: number, statusText: string) {
    super(`${status} ${statusText}`)
    this.name = 'ApiError'
    this.status = status
  }
}

async function get<T>(path: string): Promise<T> {
  const res = await fetch(BASE + path)
  if (!res.ok && res.status !== 404) throw new ApiError(res.status, res.statusText)
  return (await res.json()) as T
}
async function post<T>(path: string, body: unknown): Promise<T> {
  const res = await fetch(BASE + path, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(body),
  })
  if (!res.ok) throw new ApiError(res.status, res.statusText)
  return (await res.json()) as T
}
async function put<T>(path: string, body: unknown): Promise<T> {
  const res = await fetch(BASE + path, {
    method: 'PUT',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(body),
  })
  if (!res.ok) throw new Error(`${res.status} ${res.statusText}`)
  return (await res.json()) as T
}

export const api = {
  capabilities: () => get<CapabilitiesResponse>('/api/capabilities'),
  /**
   * Liveness of the host serving the vault: the daemon's real health route
   * (umbrella docs/reference/daemon-http-api-spec.md). Only called when the
   * host has no lint scan (rc6) - the Node cockpit server does not serve it.
   */
  hostHealth: () => get<{ status?: string; vaultPath?: string; uptimeSeconds?: number }>('/api/v0/health'),
  vaultSummary: () => get<{ summary: VaultSummary }>('/api/vault-summary'),
  listEntries: (qs = '') => get<{ entries: EntrySummary[] }>(`/api/entries${qs ? '?' + qs : ''}`),
  readEntry: (id: string) => get<{ entry: Entry | null; error?: string }>(`/api/entries/${encodeURIComponent(id)}`),
  search: (q: string) => get<{ results: EntrySummary[] }>(`/api/search?q=${encodeURIComponent(q)}`),
  listConcepts: () => get<{ pages: ConceptPageSummary[] }>('/api/concepts'),
  readConcept: (slug: string) => get<{ page: ConceptPage | null; error?: string }>(`/api/concepts/${encodeURIComponent(slug)}`),
  saveConcept: (slug: string, page: ConceptPage) => put<{ ok: boolean }>(`/api/concepts/${encodeURIComponent(slug)}`, { page }),
  constitution: () => get<{ body: string | null }>('/api/constitution'),
  saveConstitution: (body: string) => put<{ ok: boolean }>('/api/constitution', { body }),
  health: () => get<{ result: LintScanResult }>('/api/health'),
  healthRefresh: () => post<{ result: LintScanResult }>('/api/health/refresh', {}),
  settings: () => get<{ settings: KnowledgeSettings; inference: { active: string } }>('/api/settings'),
  saveSettings: (settings: KnowledgeSettings) => put<{ ok: boolean }>('/api/settings', { settings }),
  readPage: (path: string) => get<{ path?: string; body?: string; error?: string }>(`/api/pages?path=${encodeURIComponent(path)}`),
}
