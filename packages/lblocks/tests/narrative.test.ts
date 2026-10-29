import { describe, it, expect } from 'vitest'
import { renderNarrative } from '../src/primitives/narrative.js'
import type { NarrativeData } from '../src/types/primitives.js'
import type { PrimitiveOptions } from '../src/types/primitives.js'

function makeOptions(overrides?: Partial<PrimitiveOptions>): PrimitiveOptions {
  return { density: 'standard', ...overrides }
}

describe('narrative primitive', () => {
  it('renders text content', () => {
    const data: NarrativeData = { text: 'You had a productive session today.' }
    const el = renderNarrative(data, makeOptions())
    const textEl = el.querySelector('.lb-narrative-text')
    expect(textEl?.textContent).toBe('You had a productive session today.')
  })

  it('renders badge when provided', () => {
    const data: NarrativeData = { text: 'Some text', badge: 'T2' }
    const el = renderNarrative(data, makeOptions())
    const badge = el.querySelector('.lb-narrative-badge')
    expect(badge).not.toBeNull()
    expect(badge?.textContent).toBe('T2')
  })

  it('does not render badge when badge is null', () => {
    const data: NarrativeData = { text: 'Some text', badge: null }
    const el = renderNarrative(data, makeOptions())
    const badge = el.querySelector('.lb-narrative-badge')
    expect(badge).toBeNull()
  })

  it('does not render badge when badge is omitted', () => {
    const data: NarrativeData = { text: 'Some text' }
    const el = renderNarrative(data, makeOptions())
    const badge = el.querySelector('.lb-narrative-badge')
    expect(badge).toBeNull()
  })

  it('text element has 1.5 line-height', () => {
    const data: NarrativeData = { text: 'Some text' }
    const el = renderNarrative(data, makeOptions())
    const textEl = el.querySelector('.lb-narrative-text') as HTMLElement
    expect(textEl.style.lineHeight).toBe('1.5')
  })

  it('text uses text-primary color', () => {
    const data: NarrativeData = { text: 'Some text' }
    const el = renderNarrative(data, makeOptions())
    const textEl = el.querySelector('.lb-narrative-text') as HTMLElement
    expect(textEl.style.color).toBe('var(--lb-text-primary)')
  })

  it('badge uses signal-inquiry color when rendered', () => {
    const data: NarrativeData = { text: 'Some text', badge: 'T2' }
    const el = renderNarrative(data, makeOptions())
    const badge = el.querySelector('.lb-narrative-badge') as HTMLElement
    expect(badge.style.color).toBe('var(--lb-signal-inquiry)')
  })

  it('badge uses bg-secondary background when rendered', () => {
    const data: NarrativeData = { text: 'Some text', badge: 'T2' }
    const el = renderNarrative(data, makeOptions())
    const badge = el.querySelector('.lb-narrative-badge') as HTMLElement
    expect(badge.style.backgroundColor).toBe('var(--lb-bg-secondary)')
  })

  it('badge has 9px font size', () => {
    const data: NarrativeData = { text: 'Some text', badge: 'T2' }
    const el = renderNarrative(data, makeOptions())
    const badge = el.querySelector('.lb-narrative-badge') as HTMLElement
    expect(badge.style.fontSize).toBe('9px')
  })

  it('badge header row is rendered before text when badge is present', () => {
    const data: NarrativeData = { text: 'Some text', badge: 'T2' }
    const el = renderNarrative(data, makeOptions())
    const children = Array.from(el.children)
    const badgeIndex = children.findIndex(c => c.classList.contains('lb-narrative-header'))
    const textIndex = children.findIndex(c => c.classList.contains('lb-narrative-text'))
    expect(badgeIndex).toBeLessThan(textIndex)
  })
})
