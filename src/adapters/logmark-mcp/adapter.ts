import type { IVaultAdapter, AdapterCapabilities } from '../adapter.js'
import type { Entry, EntrySummary, EntryFilter, SearchOpts } from '../../types/entry.js'
import type { ConceptPage, ConceptPageSummary } from '../../types/concept-page.js'
import type { VaultSummary, Route } from '../../types/vault.js'
import type { FeedItem, Decision } from '../../types/feed.js'
import { LogMarkMcpClient } from './client.js'
import { readBootstrap } from './bootstrap.js'
import {
  mcpListEntries,
  mcpReadEntry,
  mcpSearchEntries,
  mcpVaultSummary,
  mcpGetRoutes,
  mcpGetFeedItems,
  mcpGetDecisions,
  mcpExecuteBehavior,
} from './tool-mapping.js'
import {
  listConceptPagesFs,
  readConceptPageFs,
  writeConceptPageFs,
} from '../vanilla/concept-page-store.js'
import { readConstitutionFs, writeConstitutionFs } from '../vanilla/constitution.js'
import { defaultSettingsDir, readSettingsFs, writeSettingsFs } from '../../services/settings-store.js'
import type { KnowledgeSettings } from '../../types/settings.js'
import { VaultEventHub } from '../vault-event-hub.js'
import type { VaultEvent } from '../../types/vault-events.js'

const CAPS: AdapterCapabilities = {
  hasNotation: true,
  hasFolderSemantics: true,
  hasBehaviorCatalog: true,
  hasFeedSystem: true,
  hasSessions: true,
}

export interface LogMarkMcpAdapterOptions {
  exePath: string
  bootstrapPath?: string
  vaultPath?: string
  settingsDir?: string
}

export class LogMarkMcpAdapter implements IVaultAdapter {
  readonly name = 'logmark-mcp' as const
  readonly capabilities = CAPS
  private client: LogMarkMcpClient
  private vaultPath: string | null = null
  private events: VaultEventHub | null = null

  constructor(private readonly opts: LogMarkMcpAdapterOptions) {
    this.client = new LogMarkMcpClient({ exePath: opts.exePath })
  }

  async connect(): Promise<void> {
    if (this.opts.vaultPath) {
      this.vaultPath = this.opts.vaultPath
    } else {
      const boot = await readBootstrap(this.opts.bootstrapPath)
      if (!boot) throw new Error('LogMark bootstrap.json not found; cannot resolve vault')
      this.vaultPath = boot.vaultPath
    }
    await this.client.connect()
  }

  async disconnect(): Promise<void> {
    await this.client.disconnect()
    this.vaultPath = null
    await this.events?.stop()
    this.events = null
  }

  subscribeVaultEvents(listener: (ev: VaultEvent) => void): () => void {
    this.requireVault()
    this.events ??= new VaultEventHub(this.vaultPath!)
    return this.events.subscribe(listener)
  }

  isConnected(): boolean { return this.client.isConnected() }

  async listEntries(filter?: EntryFilter): Promise<EntrySummary[]> {
    return mcpListEntries(this.client, filter)
  }

  async readEntry(id: string): Promise<Entry | null> {
    return mcpReadEntry(this.client, id)
  }

  async searchEntries(query: string, opts?: SearchOpts): Promise<EntrySummary[]> {
    return mcpSearchEntries(this.client, query, opts)
  }

  async vaultSummary(): Promise<VaultSummary> {
    const summary = await mcpVaultSummary(this.client)
    // Pass-4 frame Q / D2: the tree wants the FULL route list. When the
    // vault_summary tool doesn't carry it, enrich from get_routes (which
    // has no per-route type counts - by_type stays absent, the degraded
    // tier the tree is designed for).
    if (!summary.routes) {
      try {
        const routes = await mcpGetRoutes(this.client)
        if (routes.length > 0) {
          summary.routes = routes.map((route) => ({
            path: route.path,
            count: route.entry_count,
            ...(route.name ? { name: route.name } : {}),
          }))
        }
      } catch {
        // get_routes unavailable - consumers fall back to top_routes.
      }
    }
    return summary
  }

  async listConceptPages(): Promise<ConceptPageSummary[]> {
    this.requireVault()
    return listConceptPagesFs(this.vaultPath!)
  }

  async readConceptPage(slug: string): Promise<ConceptPage | null> {
    this.requireVault()
    return readConceptPageFs(this.vaultPath!, slug)
  }

  async writeConceptPage(slug: string, page: ConceptPage): Promise<void> {
    this.requireVault()
    return writeConceptPageFs(this.vaultPath!, slug, page)
  }

  // Phase 1 limitation: rollback across MCP writes is deferred. Sequential write only.
  async writeConceptPagesAtomic(pages: ConceptPage[]): Promise<void> {
    for (const page of pages) {
      await this.writeConceptPage(page.slug, page)
    }
  }

  async readConstitution(): Promise<string | null> {
    this.requireVault()
    return readConstitutionFs(this.vaultPath!)
  }

  async writeConstitution(body: string): Promise<void> {
    this.requireVault()
    return writeConstitutionFs(this.vaultPath!, body)
  }

  async readSettings(): Promise<KnowledgeSettings> {
    return readSettingsFs(this.opts.settingsDir ?? defaultSettingsDir())
  }

  async writeSettings(settings: KnowledgeSettings): Promise<void> {
    return writeSettingsFs(this.opts.settingsDir ?? defaultSettingsDir(), settings)
  }

  executeBehavior(behaviorId: string, input: unknown): Promise<unknown> {
    return mcpExecuteBehavior(this.client, behaviorId, input)
  }

  getFeedItems(): Promise<FeedItem[]> {
    return mcpGetFeedItems(this.client)
  }

  getRoutes(): Promise<Route[]> {
    return mcpGetRoutes(this.client)
  }

  getDecisions(filter?: { scope?: string; status?: string }): Promise<Decision[]> {
    return mcpGetDecisions(this.client, filter)
  }

  private requireVault(): void {
    if (!this.vaultPath) throw new Error('LogMarkMcpAdapter not connected')
  }
}
