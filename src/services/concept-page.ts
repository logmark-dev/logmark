import type { IVaultAdapter } from '../adapters/adapter.js'
import type { ConceptPage, ConceptPageSummary } from '../types/concept-page.js'

export class ConceptPageService {
  constructor(private readonly adapter: IVaultAdapter) {}

  list(): Promise<ConceptPageSummary[]> {
    return this.adapter.listConceptPages()
  }

  read(slug: string): Promise<ConceptPage | null> {
    return this.adapter.readConceptPage(slug)
  }

  save(page: ConceptPage): Promise<void> {
    return this.adapter.writeConceptPage(page.slug, page)
  }

  saveAtomic(pages: ConceptPage[]): Promise<void> {
    return this.adapter.writeConceptPagesAtomic(pages)
  }
}
