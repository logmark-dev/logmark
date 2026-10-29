import { describe, it, expect } from 'vitest'
import { renderSectionHeader } from '../src/primitives/section-header.js'

describe('section-header primitive', () => {
  it('renders label text', () => {
    const el = renderSectionHeader({ label: 'Activity' })
    expect(el.textContent).toBe('Activity')
  })

  it('has lb-section-header class', () => {
    const el = renderSectionHeader({ label: 'Activity' })
    expect(el.classList.contains('lb-section-header')).toBe(true)
  })

  it('uses CSS var for font size', () => {
    const el = renderSectionHeader({ label: 'Activity' })
    expect(el.style.fontSize).toBe('var(--lb-font-size-label)')
  })

  it('uses CSS var for accent color', () => {
    const el = renderSectionHeader({ label: 'Activity' })
    expect(el.style.color).toBe('var(--lb-accent)')
  })

  it('has uppercase text transform', () => {
    const el = renderSectionHeader({ label: 'Activity' })
    expect(el.style.textTransform).toBe('uppercase')
  })

  it('has letter spacing', () => {
    const el = renderSectionHeader({ label: 'Activity' })
    expect(el.style.letterSpacing).toBe('0.02em')
  })

  it('has margin bottom', () => {
    const el = renderSectionHeader({ label: 'Activity' })
    expect(el.style.marginBottom).toBe('8px')
  })

  it('renders as a div element', () => {
    const el = renderSectionHeader({ label: 'Activity' })
    expect(el.tagName).toBe('DIV')
  })
})
