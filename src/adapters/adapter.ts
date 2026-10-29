import type { Entry, EntrySummary, EntryFilter, SearchOpts } from '../types/entry.js'
import type { ConceptPage, ConceptPageSummary } from '../types/concept-page.js'
import type { VaultSummary, Route } from '../types/vault.js'
import type { FeedItem, Decision } from '../types/feed.js'
import type { KnowledgeSettings } from '../types/settings.js'
import type { VaultEvent } from '../types/vault-events.js'

export interface AdapterCapabilities {
  hasNotation: boolean
  hasFolderSemantics: boolean
  hasBehaviorCatalog: boolean
  hasFeedSystem: boolean
  hasSessions: boolean
  /**
   * Whether a concept producer (the closed dream loop) writes concept pages
   * on the host behind this adapter. The daemon always reports it
   * explicitly: `true` on the desktop host, `false` on the open daemon
   * (`logmark serve`). Absent (Node-side adapters, older daemons) means
   * unknown, and the cockpit keeps its concept surfaces; only an explicit
   * `false` hides them (src/ui/lib/host.ts).
   */
  hasConceptProducer?: boolean
  /**
   * Host-surface flags (rc6, design 4.2): set by the HOST serving the
   * cockpit, never by a vault adapter. The Node cockpit server merges
   * NODE_HOST_FEATURES (src/types/host.ts) into /api/capabilities; the
   * daemon never sends them, so they are deliberately absent from the
   * OpenAPI Capabilities schema. Absent means not served.
   */
  hasDistillation?: boolean
  hasLintScan?: boolean
  hasInferenceSettings?: boolean
}

export interface IVaultAdapter {
  readonly name: 'logmark-mcp' | 'vanilla' | 'kernel-http'
  readonly capabilities: AdapterCapabilities

  connect(): Promise<void>
  disconnect(): Promise<void>
  isConnected(): boolean

  // Read - both adapters
  listEntries(filter?: EntryFilter): Promise<EntrySummary[]>
  readEntry(id: string): Promise<Entry | null>
  searchEntries(query: string, opts?: SearchOpts): Promise<EntrySummary[]>
  vaultSummary(): Promise<VaultSummary>

  // Concept pages - both adapters
  listConceptPages(): Promise<ConceptPageSummary[]>
  readConceptPage(slug: string): Promise<ConceptPage | null>
  writeConceptPage(slug: string, page: ConceptPage): Promise<void>
  writeConceptPagesAtomic(pages: ConceptPage[]): Promise<void>

  // Constitution - both adapters
  readConstitution(): Promise<string | null>
  writeConstitution(body: string): Promise<void>

  // Settings (configure surface) - both adapters
  readSettings(): Promise<KnowledgeSettings>
  writeSettings(settings: KnowledgeSettings): Promise<void>

  // Live updates - optional; absence means no live refresh in the UI.
  // Returns an unsubscribe function.
  subscribeVaultEvents?(listener: (ev: VaultEvent) => void): () => void

  // LogMark-only - vanilla returns null/throws
  executeBehavior?(behaviorId: string, input: unknown): Promise<unknown>
  getFeedItems?(): Promise<FeedItem[]>
  getRoutes?(): Promise<Route[]>
  getDecisions?(filter?: { scope?: string; status?: string }): Promise<Decision[]>
}
