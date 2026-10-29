import { describe, expect, test, beforeEach } from 'vitest'
import { JSDOM } from 'jsdom'
import { renderEntryList } from '../../src/primitives/entry-list.js'

let dom: JSDOM
beforeEach(() => {
  dom = new JSDOM('<!doctype html><html><body></body></html>')
  globalThis.document = dom.window.document
})

describe('renderEntryList', () => {
  test('renders one row per item with title', () => {
    const root = renderEntryList({
      items: [
        { id: 'a1', title: 'First', subtitle: 'note', meta: '2026-04-10' },
        { id: 'a2', title: 'Second' },
      ],
    })
    expect(root.querySelectorAll('.lb-entry-list-item').length).toBe(2)
    expect(root.textContent).toContain('First')
    expect(root.textContent).toContain('Second')
  })

  test('attaches click handler when onItemClick provided', () => {
    let clicked: string | null = null
    const root = renderEntryList({
      items: [{ id: 'a1', title: 'Clickable' }],
      onItemClick: (id) => { clicked = id },
    })
    const item = root.querySelector('.lb-entry-list-item') as HTMLElement
    item.click()
    expect(clicked).toBe('a1')
  })

  test('renders empty state when items is empty', () => {
    const root = renderEntryList({ items: [], emptyMessage: 'No entries' })
    expect(root.textContent).toContain('No entries')
  })
})
