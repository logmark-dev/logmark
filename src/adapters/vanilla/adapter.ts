import type { IVaultAdapter, AdapterCapabilities } from '../adapter.js'
import type { Entry, EntrySummary, EntryFilter, SearchOpts } from '../../types/entry.js'
import type { ConceptPage, ConceptPageSummary } from '../../types/concept-page.js'
import type { VaultSummary } from '../../types/vault.js'
import { listEntriesFs, readEntryFs } from './entry-reader.js'
import { searchEntriesFs } from './search.js'
import { vaultSummaryFs } from './vault-summary.js'
import {
  listConceptPagesFs,
  readConceptPageFs,
  writeConceptPageFs,
  writeConceptPagesAtomicFs,
} from './concept-page-store.js'
import { readConstitutionFs, writeConstitutionFs } from './constitution.js'
import { defaultSettingsDir, readSettingsFs, writeSettingsFs } from '../../services/settings-store.js'
import type { KnowledgeSettings } from '../../types/settings.js'
import { VaultEventHub } from '../vault-event-hub.js'
import type { VaultEvent } from '../../types/vault-events.js'

const CAPS: AdapterCapabilities = {
  hasNotation: false,
  hasFolderSemantics: false,
  hasBehaviorCatalog: false,
  hasFeedSystem: false,
  hasSessions: false,
}

export class VanillaMarkdownAdapter implements IVaultAdapter {
  readonly name = 'vanilla' as const
  readonly capabilities = CAPS
  private connected = false
  private events: VaultEventHub | null = null

  constructor(
    private readonly vaultPath: string,
    private readonly settingsDir: string = defaultSettingsDir()
  ) {}

  async connect(): Promise<void> { this.connected = true }
  async disconnect(): Promise<void> {
    this.connected = false
    await this.events?.stop()
    this.events = null
  }
  isConnected(): boolean { return this.connected }

  subscribeVaultEvents(listener: (ev: VaultEvent) => void): () => void {
    this.events ??= new VaultEventHub(this.vaultPath)
    return this.events.subscribe(listener)
  }

  async listEntries(filter?: EntryFilter): Promise<EntrySummary[]> {
    const all = await listEntriesFs(this.vaultPath)
    return applyFilter(all, filter)
  }

  async readEntry(id: string): Promise<Entry | null> {
    return readEntryFs(this.vaultPath, id)
  }

  async searchEntries(query: string, opts?: SearchOpts): Promise<EntrySummary[]> {
    return searchEntriesFs(this.vaultPath, query, opts)
  }

  async vaultSummary(): Promise<VaultSummary> {
    return vaultSummaryFs(this.vaultPath)
  }

  async listConceptPages(): Promise<ConceptPageSummary[]> {
    return listConceptPagesFs(this.vaultPath)
  }

  async readConceptPage(slug: string): Promise<ConceptPage | null> {
    return readConceptPageFs(this.vaultPath, slug)
  }

  async writeConceptPage(slug: string, page: ConceptPage): Promise<void> {
    return writeConceptPageFs(this.vaultPath, slug, page)
  }

  async writeConceptPagesAtomic(pages: ConceptPage[]): Promise<void> {
    await writeConceptPagesAtomicFs(this.vaultPath, pages)
  }

  async readConstitution(): Promise<string | null> {
    return readConstitutionFs(this.vaultPath)
  }

  async writeConstitution(body: string): Promise<void> {
    return writeConstitutionFs(this.vaultPath, body)
  }

  async readSettings(): Promise<KnowledgeSettings> {
    return readSettingsFs(this.settingsDir)
  }

  async writeSettings(settings: KnowledgeSettings): Promise<void> {
    return writeSettingsFs(this.settingsDir, settings)
  }
}

function applyFilter(
  entries: EntrySummary[],
  filter?: EntryFilter
): EntrySummary[] {
  if (!filter) return entries
  let out = entries
  if (filter.type) out = out.filter((e) => e.type === filter.type)
  if (filter.route) out = out.filter((e) => e.route === filter.route)
  if (filter.tag) out = out.filter((e) => e.tags.includes(filter.tag!))
  if (filter.since) out = out.filter((e) => e.updatedAt >= filter.since!)
  if (filter.limit) out = out.slice(0, filter.limit)
  return out
}
