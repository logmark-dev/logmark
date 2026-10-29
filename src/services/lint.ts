import type { IVaultAdapter } from '../adapters/adapter.js'
import type { LintScanResult, StaleItem } from '../types/lint.js'
import { sha256 } from '../lib/hashing.js'

export class LintService {
  constructor(private readonly adapter: IVaultAdapter) {}

  async scan(): Promise<LintScanResult> {
    const concepts = await this.adapter.listConceptPages()
    const items: StaleItem[] = []
    for (const summary of concepts) {
      const page = await this.adapter.readConceptPage(summary.slug)
      if (!page) continue
      for (const source of page.sources) {
        const entry = await this.adapter.readEntry(source.id)
        if (entry === null) {
          items.push({ kind: 'source-deleted', concept_slug: page.slug, source })
          continue
        }
        const currentHash = sha256(entry.body)
        if (currentHash !== source.snapshot_hash) {
          items.push({
            kind: 'source-changed',
            concept_slug: page.slug,
            source,
            changed_at: entry.updatedAt,
          })
        }
      }
    }
    items.sort((a, b) => a.concept_slug.localeCompare(b.concept_slug))
    return {
      items,
      scanned_at: new Date().toISOString(),
      concept_count: concepts.length,
      stale_count: items.length,
    }
  }
}
