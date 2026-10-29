import { describe, it, expect } from 'vitest'
import { renderConceptPage } from '../src/primitives/concept-page.js'
import type { ConceptPageData } from '../src/types/viewblock.js'

function makeData(overrides: Partial<ConceptPageData> = {}): ConceptPageData {
  return {
    slug: 'free-energy-principle',
    title: 'Free Energy Principle',
    status: 'draft',
    tags: ['cognition', 'neuroscience'],
    body: '## Core claim\n\nOrganisms minimize surprise.\n\n- Perception updates the model\n- Action changes the world',
    sourceCount: 12,
    relatedConcepts: ['active-inference'],
    ...overrides,
  }
}

describe('concept-page primitive', () => {
  it('renders the title', () => {
    const el = renderConceptPage(makeData())
    const title = el.querySelector('.lb-concept-page-title')
    expect(title?.textContent).toBe('Free Energy Principle')
  })

  it('renders a status chip carrying the per-status class', () => {
    const draft = renderConceptPage(makeData({ status: 'draft' }))
    const chip = draft.querySelector('.lb-status-chip')
    expect(chip).toBeTruthy()
    expect(chip?.classList.contains('lb-status-draft')).toBe(true)
    expect(chip?.textContent).toBe('draft')

    const mature = renderConceptPage(makeData({ status: 'mature' }))
    expect(mature.querySelector('.lb-status-chip')?.classList.contains('lb-status-mature')).toBe(true)

    const archived = renderConceptPage(makeData({ status: 'archived' }))
    expect(archived.querySelector('.lb-status-chip')?.classList.contains('lb-status-archived')).toBe(true)
  })

  it('renders a tag row with one tag element per tag', () => {
    const el = renderConceptPage(makeData())
    const row = el.querySelector('.lb-concept-page-tags')
    expect(row).toBeTruthy()
    const tags = row!.querySelectorAll('.lb-concept-page-tag')
    expect(tags.length).toBe(2)
    expect(tags[0].textContent).toBe('#cognition')
    expect(tags[1].textContent).toBe('#neuroscience')
  })

  it('omits the tag row when there are no tags', () => {
    const el = renderConceptPage(makeData({ tags: [] }))
    expect(el.querySelector('.lb-concept-page-tags')).toBeNull()
  })

  it('renders the markdown body content as parsed markdown, not raw source', () => {
    const el = renderConceptPage(makeData())
    const body = el.querySelector('.lb-concept-page-body')
    expect(body).toBeTruthy()
    expect(body!.textContent).toContain('Organisms minimize surprise.')
    expect(body!.textContent).toContain('Perception updates the model')
    // The heading markdown syntax must not leak through as literal text
    expect(body!.textContent).not.toContain('## Core claim')
    expect(body!.querySelector('h2')?.textContent).toContain('Core claim')
    expect(body!.querySelectorAll('li').length).toBe(2)
  })

  it('renders a sources footer mentioning the source count', () => {
    const el = renderConceptPage(makeData({ sourceCount: 12 }))
    const footer = el.querySelector('.lb-concept-page-sources')
    expect(footer).toBeTruthy()
    expect(footer!.textContent).toContain('12')
    expect(footer!.textContent?.toLowerCase()).toContain('source')
  })

  it('renders related concepts as slug pills', () => {
    const el = renderConceptPage(makeData({ relatedConcepts: ['active-inference', 'predictive-processing'] }))
    const pills = el.querySelectorAll('.lb-concept-page-related-item')
    expect(pills.length).toBe(2)
    expect((pills[0] as HTMLElement).dataset.slug).toBe('active-inference')
  })

  it('omits the related section when relatedConcepts is empty', () => {
    const el = renderConceptPage(makeData({ relatedConcepts: [] }))
    expect(el.querySelector('.lb-concept-page-related')).toBeNull()
  })
})
