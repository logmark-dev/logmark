import type { IVaultAdapter, AdapterCapabilities } from '../adapter.js'
import type { Entry, EntrySummary, EntryFilter, SearchOpts } from '../../types/entry.js'
import type { ConceptPage, ConceptPageSummary } from '../../types/concept-page.js'
import type { VaultSummary } from '../../types/vault.js'
import type { KnowledgeSettings } from '../../types/settings.js'
import type { VaultEvent } from '../../types/vault-events.js'
import { isHostInfo, type HostInfo } from '../../types/host.js'

const NO_CAPS: AdapterCapabilities = {
  hasNotation: false,
  hasFolderSemantics: false,
  hasBehaviorCatalog: false,
  hasFeedSystem: false,
  hasSessions: false,
}

/**
 * The daemon's `capabilities` record, passed through as reported. The
 * additive `hasConceptProducer` flag is carried only when the daemon sends a
 * real boolean, so an older daemon that omits it stays "absent" (concept
 * surfaces shown) rather than collapsing to false.
 */
export function parseKernelCapabilities(raw: unknown): AdapterCapabilities {
  const caps = (typeof raw === 'object' && raw !== null ? raw : {}) as Record<string, unknown>
  const { hasConceptProducer, ...rest } = caps
  return {
    ...NO_CAPS,
    ...(rest as Partial<AdapterCapabilities>),
    ...(typeof hasConceptProducer === 'boolean' ? { hasConceptProducer } : {}),
  }
}

export interface KernelHttpAdapterOptions {
  /** Daemon base URL, e.g. http://127.0.0.1:4747 */
  baseUrl: string
}

/**
 * IVaultAdapter over the daemon HTTP/SSE API v0
 * (umbrella docs/reference/daemon-http-api-spec.md). Capabilities and vault
 * path are reported by the daemon at connect time, not hardcoded. The
 * optional producer-backed methods (behaviors, feed, routes, decisions) are
 * deliberately absent in v0.
 */
export class KernelHttpAdapter implements IVaultAdapter {
  readonly name = 'kernel-http' as const
  private caps: AdapterCapabilities = NO_CAPS
  private vaultPath: string | null = null
  private hostInfo: HostInfo | null = null
  private connected = false

  constructor(private readonly opts: KernelHttpAdapterOptions) {}

  get capabilities(): AdapterCapabilities {
    return this.caps
  }

  /**
   * The daemon's host identity (rc6, design 4.1): `oss-cli` for LogMark
   * Engine, `desktop` for the desktop app. Null before connect, after
   * disconnect, or when the daemon omits or malforms it (older builds).
   */
  get host(): HostInfo | null {
    return this.hostInfo
  }

  async connect(): Promise<void> {
    const data = await this.get<{ capabilities: unknown; vaultPath: string; host?: unknown }>(
      '/api/v0/capabilities'
    )
    this.caps = parseKernelCapabilities(data.capabilities)
    this.vaultPath = data.vaultPath
    this.hostInfo = isHostInfo(data.host)
      ? { providerId: data.host.providerId, product: data.host.product }
      : null
    this.connected = true
  }

  async disconnect(): Promise<void> {
    this.connected = false
    this.vaultPath = null
    this.hostInfo = null
    this.caps = NO_CAPS
    this.sseAbort?.abort()
    this.sseAbort = null
    this.sseListeners.clear()
  }

  isConnected(): boolean {
    return this.connected
  }

  subscribeVaultEvents(listener: (ev: VaultEvent) => void): () => void {
    this.sseListeners.add(listener)
    if (!this.sseAbort) this.startSse()
    return () => {
      this.sseListeners.delete(listener)
      if (this.sseListeners.size === 0) {
        this.sseAbort?.abort()
        this.sseAbort = null
      }
    }
  }

  async listEntries(filter?: EntryFilter): Promise<EntrySummary[]> {
    const qs = new URLSearchParams()
    if (filter?.type) qs.set('type', filter.type)
    if (filter?.route) qs.set('route', filter.route)
    if (filter?.tag) qs.set('tag', filter.tag)
    if (filter?.since) qs.set('since', filter.since)
    if (filter?.limit !== undefined) qs.set('limit', String(filter.limit))
    const suffix = qs.size > 0 ? `?${qs.toString()}` : ''
    return (await this.get<{ entries: EntrySummary[] }>(`/api/v0/entries${suffix}`)).entries
  }

  async readEntry(id: string): Promise<Entry | null> {
    return (await this.get<{ entry: Entry | null }>(`/api/v0/entries/${encodeURIComponent(id)}`)).entry
  }

  async searchEntries(query: string, opts?: SearchOpts): Promise<EntrySummary[]> {
    const qs = new URLSearchParams({ q: query })
    if (opts?.limit !== undefined) qs.set('limit', String(opts.limit))
    if (opts?.semantic) qs.set('semantic', 'true')
    return (await this.get<{ results: EntrySummary[] }>(`/api/v0/search?${qs.toString()}`)).results
  }

  async vaultSummary(): Promise<VaultSummary> {
    return (await this.get<{ summary: VaultSummary }>('/api/v0/vault/summary')).summary
  }

  async listConceptPages(): Promise<ConceptPageSummary[]> {
    return (await this.get<{ pages: ConceptPageSummary[] }>('/api/v0/concepts')).pages
  }

  async readConceptPage(slug: string): Promise<ConceptPage | null> {
    return (
      await this.get<{ page: ConceptPage | null }>(`/api/v0/concepts/${encodeURIComponent(slug)}`)
    ).page
  }

  async writeConceptPage(slug: string, page: ConceptPage): Promise<void> {
    await this.send('PUT', `/api/v0/concepts/${encodeURIComponent(slug)}`, { page })
  }

  async writeConceptPagesAtomic(pages: ConceptPage[]): Promise<void> {
    for (const page of pages) {
      await this.writeConceptPage(page.slug, page)
    }
  }

  async readConstitution(): Promise<string | null> {
    return (await this.get<{ body: string | null }>('/api/v0/constitution')).body
  }

  async writeConstitution(body: string): Promise<void> {
    await this.send('PUT', '/api/v0/constitution', { body })
  }

  async readSettings(): Promise<KnowledgeSettings> {
    return (await this.get<{ settings: KnowledgeSettings }>('/api/v0/settings')).settings
  }

  async writeSettings(settings: KnowledgeSettings): Promise<void> {
    await this.send('PUT', '/api/v0/settings', { settings })
  }

  private readonly sseListeners = new Set<(ev: VaultEvent) => void>()
  private sseAbort: AbortController | null = null

  private startSse(): void {
    const controller = new AbortController()
    this.sseAbort = controller
    void this.runSse(controller)
  }

  private async runSse(controller: AbortController): Promise<void> {
    let backoff = 500
    while (!controller.signal.aborted && this.sseListeners.size > 0) {
      try {
        const res = await fetch(`${this.opts.baseUrl}/api/v0/events`, {
          signal: controller.signal,
        })
        if (!res.ok || !res.body) throw new Error(`events stream: ${res.status}`)
        backoff = 500
        await this.consumeSse(res.body)
      } catch {
        if (controller.signal.aborted) return
      }
      await new Promise((r) => setTimeout(r, backoff))
      backoff = Math.min(backoff * 2, 10_000)
    }
  }

  private async consumeSse(body: ReadableStream<Uint8Array>): Promise<void> {
    const reader = body.getReader()
    const decoder = new TextDecoder()
    let buffer = ''
    for (;;) {
      const { done, value } = await reader.read()
      if (done) return
      buffer += decoder.decode(value, { stream: true })
      let idx: number
      while ((idx = buffer.indexOf('\n\n')) >= 0) {
        const chunk = buffer.slice(0, idx)
        buffer = buffer.slice(idx + 2)
        this.dispatchSse(chunk)
      }
    }
  }

  private dispatchSse(chunk: string): void {
    let event: string | undefined
    let data = ''
    for (const line of chunk.split('\n')) {
      if (line.startsWith('event:')) event = line.slice(6).trim()
      else if (line.startsWith('data:')) data += line.slice(5).trim()
    }
    if (event !== 'vault-event' || !data) return
    try {
      const ev = JSON.parse(data) as VaultEvent
      for (const l of this.sseListeners) l(ev)
    } catch {
      // Malformed event payloads are ignored, never fatal.
    }
  }

  private async get<T>(path: string): Promise<T> {
    const res = await fetch(this.opts.baseUrl + path)
    if (!res.ok) throw await problemError(res, `GET ${path}`)
    return (await res.json()) as T
  }

  private async send(method: 'PUT' | 'POST', path: string, body: unknown): Promise<void> {
    const res = await fetch(this.opts.baseUrl + path, {
      method,
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify(body),
    })
    if (!res.ok) throw await problemError(res, `${method} ${path}`)
  }
}

/** An error carrying a parsed RFC 9457 problem code, when the daemon sent one. */
export class KernelHttpError extends Error {
  constructor(
    message: string,
    readonly status: number,
    readonly code?: string
  ) {
    super(message)
    this.name = 'KernelHttpError'
  }
}

/**
 * Build an error from a failed response, surfacing the daemon's RFC 9457
 * problem+json `detail`/`code` when present and falling back to status text.
 */
async function problemError(res: Response, what: string): Promise<KernelHttpError> {
  let detail = res.statusText
  let code: string | undefined
  if (res.headers.get('content-type')?.includes('application/problem+json')) {
    try {
      const p = (await res.json()) as { detail?: string; code?: string }
      if (typeof p.detail === 'string') detail = p.detail
      if (typeof p.code === 'string') code = p.code
    } catch {
      // Non-JSON or malformed problem body: keep the status-text fallback.
    }
  }
  return new KernelHttpError(`kernel-http ${what}: ${res.status} ${detail}`, res.status, code)
}
