import { describe, expect, it, vi } from 'vitest'
import { renderViewerEntryCard } from '../../src/primitives/viewer-entry-card.js'
import type { ViewerEntryCardData } from '../../src/types/reader.js'

const entry: ViewerEntryCardData = {
  id: 'entry-1',
  entryType: 'decision',
  content: 'Ship the viewer with [[Native Viewer]] parity.',
  timestamp: '2026-05-20T10:00:00Z',
  route: '+ink',
  tags: ['viewer'],
  isCompleted: false,
  links: { wiki: ['Native Viewer'], routes: ['+ink'], tags: ['viewer'], blockRefs: [], external: [] },
}

describe('renderViewerEntryCard', () => {
  it('renders entry type, route, tags, and content', () => {
    const el = renderViewerEntryCard(entry, { density: 'standard' })
    expect(el.className).toContain('lb-viewer-entry-card')
    expect(el.textContent).toContain('decision')
    expect(el.textContent).toContain('+ink')
    expect(el.textContent).toContain('#viewer')
    expect(el.textContent).toContain('Ship the viewer')
    expect(el.querySelector<HTMLElement>('.lb-viewer-entry-card-rail')?.style.background)
      .toBe('var(--lb-entry-decision)')
  })

  it('emits entry-focus action when clicked', () => {
    const onAction = vi.fn()
    const el = renderViewerEntryCard(entry, { density: 'standard', onAction })
    el.click()
    expect(onAction).toHaveBeenCalledWith({
      type: 'entry-focus',
      target: 'entry-1',
      source: 'viewer-entry-card',
    })
  })

  it('emits tag-filter actions from meta and inline tags', () => {
    const onTagClick = vi.fn()
    const el = renderViewerEntryCard(
      { ...entry, content: 'Ship #viewer now.' },
      { density: 'standard', onTagClick }
    )
    const metaButton = el.querySelector<HTMLButtonElement>(
      '.lb-viewer-entry-card-tag[aria-label="Filter by #viewer"]'
    )
    const inlineButton = el.querySelector<HTMLButtonElement>('.lb-md-tag-link')
    expect(metaButton).toBeTruthy()
    expect(inlineButton).toBeTruthy()
    expect(metaButton?.textContent).toBe('#viewer')
    expect(inlineButton?.textContent).toBe('#viewer')
    expect(inlineButton?.title).toBe('Filter by #viewer')

    metaButton?.click()
    inlineButton?.click()

    expect(onTagClick).toHaveBeenCalledTimes(2)
    expect(onTagClick).toHaveBeenNthCalledWith(1, 'viewer')
    expect(onTagClick).toHaveBeenNthCalledWith(2, 'viewer')
  })
})
