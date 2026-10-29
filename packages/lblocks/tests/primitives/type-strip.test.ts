import { describe, expect, it, vi } from 'vitest'
import { renderTypeStrip } from '../../src/primitives/type-strip.js'

describe('renderTypeStrip', () => {
  it('renders type counts and selected state', () => {
    const el = renderTypeStrip({
      types: [
        { type: 'decision', count: 3, selected: true },
        { type: 'task', count: 2 },
      ],
    }, { density: 'standard' })

    expect(el.className).toContain('lb-type-strip')
    expect(el.textContent).toContain('decision')
    expect(el.textContent).toContain('3')
    expect(el.textContent).toContain('task')
    const selected = el.querySelector('[data-type="decision"]')
    const rest = el.querySelector('[data-type="task"]')
    expect(selected?.className).toContain('selected')
    expect(selected?.getAttribute('aria-pressed')).toBe('true')
    expect(rest?.getAttribute('aria-pressed')).toBe('false')
    expect(selected?.querySelector('.lb-type-strip-dot')).toBeTruthy()
    expect(rest?.querySelector('.lb-type-strip-dot')).toBeTruthy()
  })

  it('emits type-filter action when a type is clicked', () => {
    const onAction = vi.fn()
    const el = renderTypeStrip({
      types: [{ type: 'decision', count: 3 }],
    }, { density: 'standard', onAction })

    const button = el.querySelector('[data-type="decision"]') as HTMLElement
    button.click()
    expect(onAction).toHaveBeenCalledWith({
      type: 'type-filter',
      target: 'decision',
      source: 'type-strip',
    })
  })
})
