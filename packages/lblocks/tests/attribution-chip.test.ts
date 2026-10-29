import { describe, it, expect } from 'vitest'
import { renderAttributionChip } from '../src/primitives/attribution-chip.js'
import type { AttributionChipData } from '../src/types/reader.js'

describe('attribution-chip primitive', () => {
  it('renders no kind pill by default for human', () => {
    const el = renderAttributionChip({ kind: 'human' })
    expect(el.querySelector('.lb-attribution-chip-pill')).toBeNull()
  })

  it('renders no kind pill by default for agent (showKind defaults false)', () => {
    const el = renderAttributionChip({ kind: 'agent' })
    expect(el.querySelector('.lb-attribution-chip-pill')).toBeNull()
  })

  it('renders AGENTIC pill only when showKind=true and kind=agent', () => {
    const el = renderAttributionChip({ kind: 'agent', showKind: true })
    const pill = el.querySelector('.lb-attribution-chip-pill')
    expect(pill).not.toBeNull()
    const label = pill?.querySelector('.lb-attribution-chip-label')
    expect(label?.textContent).toBe('AGENTIC')
  })

  it('does NOT render a HUMAN pill even when showKind=true (dropped per HANDOFF)', () => {
    const el = renderAttributionChip({ kind: 'human', showKind: true })
    expect(el.querySelector('.lb-attribution-chip-pill')).toBeNull()
  })

  it('explicit label overrides AGENTIC default when shown', () => {
    const el = renderAttributionChip({
      kind: 'agent',
      showKind: true,
      label: 'ORACLE',
    })
    const label = el.querySelector('.lb-attribution-chip-label')
    expect(label?.textContent).toBe('ORACLE')
  })

  it('explicit label on a human kind does NOT trigger the pill', () => {
    // Humans never carry a kind pill, regardless of label.
    const el = renderAttributionChip({
      kind: 'human',
      label: 'CONTRIBUTOR',
    })
    expect(el.querySelector('.lb-attribution-chip-pill')).toBeNull()
  })

  it('aria-label reflects kind and pill state', () => {
    const human = renderAttributionChip({ kind: 'human' })
    const agentNoPill = renderAttributionChip({ kind: 'agent' })
    const agentWithPill = renderAttributionChip({ kind: 'agent', showKind: true })
    expect(human.getAttribute('aria-label')).toBe('human publisher')
    expect(agentNoPill.getAttribute('aria-label')).toBe('agent publisher')
    expect(agentWithPill.getAttribute('aria-label')).toBe('AGENTIC publisher')
  })

  it('cadence chip renders independent of kind pill', () => {
    const el = renderAttributionChip({ kind: 'human', cadence: 'weekly' })
    const cadence = el.querySelector('.lb-attribution-chip-cadence')
    expect(cadence?.textContent).toBe('weekly')
  })

  it('kind data attribute is set on root', () => {
    const a = renderAttributionChip({ kind: 'agent' })
    const h = renderAttributionChip({ kind: 'human' })
    expect((a as HTMLElement).dataset.kind).toBe('agent')
    expect((h as HTMLElement).dataset.kind).toBe('human')
  })

  it('classNames include lb-attribution-chip + kind variant', () => {
    const el = renderAttributionChip({ kind: 'agent', showKind: true })
    expect(el.classList.contains('lb-attribution-chip')).toBe(true)
    expect(el.classList.contains('lb-attribution-chip-agent')).toBe(true)
  })
})
