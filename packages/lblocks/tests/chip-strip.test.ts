import { describe, it, expect } from 'vitest'
import { renderChipStrip } from '../src/primitives/chip-strip.js'
import type { ChipStripData } from '../src/types/reader.js'

describe('chip-strip primitive', () => {
  it('renders a sort tablist with chips per option', () => {
    const data: ChipStripData = {
      value: 'recent',
      role: 'sort',
      options: [
        { value: 'recent', label: 'Most recent' },
        { value: 'frequency', label: 'Most entries' },
      ],
    }
    const el = renderChipStrip(data)
    expect(el.getAttribute('role')).toBe('tablist')
    expect(el.classList.contains('lb-chip-strip-sort')).toBe(true)
    const chips = el.querySelectorAll('.chip')
    expect(chips.length).toBe(2)
  })

  it('renders a filter group (not tablist)', () => {
    const data: ChipStripData = {
      value: 'agentic',
      role: 'filter',
      options: [
        { value: 'all', label: 'All' },
        { value: 'agentic', label: 'Agentic' },
        { value: 'human', label: 'Human' },
      ],
    }
    const el = renderChipStrip(data)
    expect(el.getAttribute('role')).toBe('group')
    expect(el.classList.contains('lb-chip-strip-filter')).toBe(true)
  })

  it('marks the matching chip active and aria-pressed', () => {
    const data: ChipStripData = {
      value: 'b',
      role: 'sort',
      options: [
        { value: 'a', label: 'A' },
        { value: 'b', label: 'B' },
        { value: 'c', label: 'C' },
      ],
    }
    const el = renderChipStrip(data)
    const chips = el.querySelectorAll('.chip')
    expect(chips[0].classList.contains('active')).toBe(false)
    expect(chips[1].classList.contains('active')).toBe(true)
    expect(chips[2].classList.contains('active')).toBe(false)
    expect(chips[1].getAttribute('aria-pressed')).toBe('true')
    expect(chips[0].getAttribute('aria-pressed')).toBe('false')
  })

  it('chips carry both lb- and ink-facing classes', () => {
    const data: ChipStripData = {
      value: 'a',
      role: 'sort',
      options: [{ value: 'a', label: 'A' }],
    }
    const el = renderChipStrip(data)
    const chip = el.querySelector('button')
    expect(chip?.classList.contains('lb-chip-strip-chip')).toBe(true)
    expect(chip?.classList.contains('chip')).toBe(true)
  })

  it('exposes chip values via data-value', () => {
    const data: ChipStripData = {
      value: 'x',
      role: 'sort',
      options: [{ value: 'x', label: 'X' }, { value: 'y', label: 'Y' }],
    }
    const el = renderChipStrip(data)
    const chips = el.querySelectorAll('.chip')
    expect((chips[0] as HTMLElement).dataset.value).toBe('x')
    expect((chips[1] as HTMLElement).dataset.value).toBe('y')
  })

  it('chip role is "tab" for sort, "button" for filter', () => {
    const sort = renderChipStrip({
      value: 'a',
      role: 'sort',
      options: [{ value: 'a', label: 'A' }],
    })
    const filter = renderChipStrip({
      value: 'a',
      role: 'filter',
      options: [{ value: 'a', label: 'A' }],
    })
    expect(sort.querySelector('button')?.getAttribute('role')).toBe('tab')
    expect(filter.querySelector('button')?.getAttribute('role')).toBe('button')
  })
})
