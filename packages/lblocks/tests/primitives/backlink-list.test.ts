import { describe, expect, it, vi } from 'vitest'
import { renderBacklinkList } from '../../src/primitives/backlink-list.js'

describe('renderBacklinkList', () => {
  it('renders backlinks with labels and excerpts', () => {
    const el = renderBacklinkList({
      backlinks: [
        { id: 'entry-1', label: 'Native Viewer', excerpt: 'Referenced from a decision', sourceType: 'entry' },
      ],
    }, { density: 'standard' })

    expect(el.className).toContain('lb-backlink-list')
    expect(el.textContent).toContain('Native Viewer')
    expect(el.textContent).toContain('Referenced from a decision')
    expect(el.querySelector('[data-backlink-id="entry-1"]')).toBeTruthy()
  })

  it('emits backlink-open action when a backlink is clicked', () => {
    const onAction = vi.fn()
    const el = renderBacklinkList({
      backlinks: [
        { id: 'entry-1', label: 'Native Viewer', sourceType: 'entry' },
      ],
    }, { density: 'standard', onAction })

    const row = el.querySelector('[data-backlink-id="entry-1"]') as HTMLElement
    row.click()
    expect(onAction).toHaveBeenCalledWith({
      type: 'backlink-open',
      target: 'entry-1',
      source: 'backlink-list',
    })
  })

  it('emits backlink-open action from keyboard activation', () => {
    const onAction = vi.fn()
    const el = renderBacklinkList({
      backlinks: [
        { id: 'entry-1', label: 'Native Viewer', sourceType: 'entry' },
      ],
    }, { density: 'standard', onAction })

    const row = el.querySelector('[data-backlink-id="entry-1"]') as HTMLElement
    row.dispatchEvent(new KeyboardEvent('keydown', { key: ' ', bubbles: true }))

    expect(row.getAttribute('role')).toBe('button')
    expect(row.getAttribute('tabindex')).toBe('0')
    expect(onAction).toHaveBeenCalledWith({
      type: 'backlink-open',
      target: 'entry-1',
      source: 'backlink-list',
    })
  })
})
