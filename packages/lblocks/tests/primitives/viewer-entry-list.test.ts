import { describe, expect, it, vi } from 'vitest'
import { renderViewerEntryList } from '../../src/primitives/viewer-entry-list.js'
import type { ViewerEntryCardData } from '../../src/types/reader.js'

const entries: ViewerEntryCardData[] = [
  {
    id: 'entry-1',
    entryType: 'decision',
    content: 'd: Use reusable reader blocks',
    timestamp: '2026-05-20T10:00:00Z',
    route: '+ink',
    tags: ['viewer'],
    isCompleted: false,
    links: { wiki: [], routes: ['+ink'], tags: ['viewer'], blockRefs: [], external: [] },
  },
  {
    id: 'entry-2',
    entryType: 'task',
    content: 't: Check mobile density',
    timestamp: '2026-05-20T11:00:00Z',
    route: '+mobile',
    tags: ['phone'],
    isCompleted: true,
    links: { wiki: [], routes: ['+mobile'], tags: ['phone'], blockRefs: [], external: [] },
  },
]

describe('renderViewerEntryList', () => {
  it('renders each entry with optional title', () => {
    const el = renderViewerEntryList({ title: 'Recent entries', entries }, { density: 'standard' })
    expect(el.className).toContain('lb-viewer-entry-list')
    expect(el.textContent).toContain('Recent entries')
    expect(el.querySelectorAll('.lb-viewer-entry-card').length).toBe(2)
    expect(el.textContent).toContain('Check mobile density')
  })

  it('renders empty state when no entries are present', () => {
    const el = renderViewerEntryList({ entries: [], emptyMessage: 'No entries in this view' }, { density: 'standard' })
    expect(el.textContent).toContain('No entries in this view')
  })

  it('forwards entry-focus actions from cards', () => {
    const onAction = vi.fn()
    const el = renderViewerEntryList({ entries }, { density: 'standard', onAction })
    const card = el.querySelector('[data-entry-id="entry-2"]') as HTMLElement
    card.click()
    expect(onAction).toHaveBeenCalledWith({
      type: 'entry-focus',
      target: 'entry-2',
      source: 'viewer-entry-card',
    })
  })
})
