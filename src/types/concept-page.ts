export interface ConceptPageSource {
  id: string                    // entry id
  snapshot_hash: string         // sha256(entry.body) at distillation time
  distilled_at: string          // ISO 8601
}

export interface ConceptPage {
  slug: string                  // filename: concepts/<slug>.md
  title: string
  body: string                  // markdown body (post-frontmatter)
  /** Lifecycle state representing progressive maturation: draft → mature → archived. */
  status: 'draft' | 'mature' | 'archived'
  /** Quality flag set when the most recent distillation produced uncited claims; independent of status and indicates the page should not be trusted as a final reference until citations are filled in. */
  draft?: boolean
  density?: number              // PIS-style 0-5
  tags: string[]
  sources: ConceptPageSource[]
  related_concepts: string[]    // other concept slugs
  created: string               // ISO 8601
  last_distilled: string        // ISO 8601
  distillation_version: number
}

export interface ConceptPageSummary {
  slug: string
  title: string
  status: ConceptPage['status']
  tags: string[]
  last_distilled: string
  source_count: number
  related_concepts?: string[]
  distillation_version?: number
  density?: number
  draft?: boolean
  stale_count?: number
  related_count?: number
  backlink_count?: number
}
