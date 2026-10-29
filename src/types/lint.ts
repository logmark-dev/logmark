import type { ConceptPageSource } from './concept-page.js'

export interface StaleItem {
  kind: 'source-changed' | 'source-deleted' | 'parse-error'
  concept_slug: string
  source?: ConceptPageSource
  changed_at?: string
  error?: string
}

export interface LintScanResult {
  items: StaleItem[]
  scanned_at: string
  concept_count: number
  stale_count: number
}
