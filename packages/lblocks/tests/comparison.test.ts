import { describe, it, expect } from 'vitest'
import { renderComparison } from '../src/primitives/comparison.js'
import type { ComparisonData } from '../src/types/primitives.js'
import type { PrimitiveOptions } from '../src/types/primitives.js'

function makeOptions(overrides?: Partial<PrimitiveOptions>): PrimitiveOptions {
  return { density: 'standard', ...overrides }
}

describe('comparison primitive', () => {
  it('renders text content', () => {
    const data: ComparisonData = { text: 'Tasks completed', delta: 15, direction: 'up' }
    const el = renderComparison(data, makeOptions())
    const textEl = el.querySelector('.lb-comparison-text')
    expect(textEl?.textContent).toBe('Tasks completed')
  })

  it('text uses text-secondary color', () => {
    const data: ComparisonData = { text: 'Tasks completed', delta: 15, direction: 'up' }
    const el = renderComparison(data, makeOptions())
    const textEl = el.querySelector('.lb-comparison-text') as HTMLElement
    expect(textEl.style.color).toBe('var(--lb-text-secondary)')
  })

  it('positive delta renders with + sign', () => {
    const data: ComparisonData = { text: 'Tasks completed', delta: 15, direction: 'up' }
    const el = renderComparison(data, makeOptions())
    const delta = el.querySelector('.lb-comparison-delta')
    expect(delta?.textContent).toBe('(+15%)')
  })

  it('positive delta uses signal-positive color', () => {
    const data: ComparisonData = { text: 'Tasks completed', delta: 15, direction: 'up' }
    const el = renderComparison(data, makeOptions())
    const delta = el.querySelector('.lb-comparison-delta') as HTMLElement
    expect(delta.style.color).toBe('var(--lb-signal-positive)')
  })

  it('negative delta renders with - sign', () => {
    const data: ComparisonData = { text: 'Tasks completed', delta: 8, direction: 'down' }
    const el = renderComparison(data, makeOptions())
    const delta = el.querySelector('.lb-comparison-delta')
    expect(delta?.textContent).toBe('(-8%)')
  })

  it('negative delta uses signal-negative color', () => {
    const data: ComparisonData = { text: 'Tasks completed', delta: 8, direction: 'down' }
    const el = renderComparison(data, makeOptions())
    const delta = el.querySelector('.lb-comparison-delta') as HTMLElement
    expect(delta.style.color).toBe('var(--lb-signal-negative)')
  })

  it('zero delta renders as neutral (0%)', () => {
    const data: ComparisonData = { text: 'Tasks completed', delta: 0, direction: 'same' }
    const el = renderComparison(data, makeOptions())
    const delta = el.querySelector('.lb-comparison-delta')
    expect(delta?.textContent).toBe('(0%)')
  })

  it('zero delta uses text-muted color', () => {
    const data: ComparisonData = { text: 'Tasks completed', delta: 0, direction: 'same' }
    const el = renderComparison(data, makeOptions())
    const delta = el.querySelector('.lb-comparison-delta') as HTMLElement
    expect(delta.style.color).toBe('var(--lb-text-muted)')
  })

  it('renders as inline layout with text and delta side by side', () => {
    const data: ComparisonData = { text: 'Tasks completed', delta: 15, direction: 'up' }
    const el = renderComparison(data, makeOptions())
    expect(el.style.display).toBe('flex')
  })

  it('renders delta element', () => {
    const data: ComparisonData = { text: 'Tasks completed', delta: 15, direction: 'up' }
    const el = renderComparison(data, makeOptions())
    const delta = el.querySelector('.lb-comparison-delta')
    expect(delta).not.toBeNull()
  })
})
