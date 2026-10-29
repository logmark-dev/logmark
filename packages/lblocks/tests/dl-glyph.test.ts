import { describe, it, expect } from 'vitest'
import { renderDlGlyph } from '../src/primitives/dl-glyph.js'
import type { DlGlyphData } from '../src/types/reader.js'

describe('dl-glyph primitive', () => {
  it('renders a small glyph button by default (size omitted)', () => {
    const data: DlGlyphData = { vault: 'ai-capex-boom-2026-05' }
    const el = renderDlGlyph(data)
    expect(el.tagName).toBe('BUTTON')
    expect(el.classList.contains('lb-dl-glyph')).toBe(true)
    expect(el.classList.contains('lb-dl-glyph-sm')).toBe(true)
    expect(el.dataset.vault).toBe('ai-capex-boom-2026-05')
  })

  it('renders the large variant with visible label', () => {
    const data: DlGlyphData = { vault: 'my-vault', size: 'lg' }
    const el = renderDlGlyph(data)
    expect(el.classList.contains('lb-dl-glyph-lg')).toBe(true)
    const label = el.querySelector('.lb-dl-glyph-label')
    expect(label).not.toBeNull()
    expect(label?.textContent).toBe('Download Vault')
  })

  it('sm variant has no visible label, only the arrow svg', () => {
    const el = renderDlGlyph({ vault: 'v', size: 'sm' })
    expect(el.querySelector('.lb-dl-glyph-label')).toBeNull()
    expect(el.querySelector('svg')).not.toBeNull()
  })

  it('aria-label encodes the vault name for screen readers', () => {
    const el = renderDlGlyph({ vault: 'cool-vault-xyz' })
    expect(el.getAttribute('aria-label')).toContain('cool-vault-xyz')
    expect(el.getAttribute('aria-label')).toMatch(/LogMark vault/i)
  })

  it('tooltip via title attribute (both variants)', () => {
    const sm = renderDlGlyph({ vault: 'v', size: 'sm' })
    const lg = renderDlGlyph({ vault: 'v', size: 'lg' })
    expect(sm.title).toMatch(/\.logmark/)
    expect(lg.title).toMatch(/\.logmark/)
    expect(sm.title).toBe(lg.title)
  })

  it('exposes archiveUrl in dataset when provided', () => {
    const el = renderDlGlyph({
      vault: 'v',
      archiveUrl: 'https://logmark.ink/v/me/v/archive.logmark',
    })
    expect(el.dataset.archiveUrl).toBe('https://logmark.ink/v/me/v/archive.logmark')
  })

  it('omits archiveUrl dataset when not provided', () => {
    const el = renderDlGlyph({ vault: 'v' })
    expect(el.dataset.archiveUrl).toBeUndefined()
  })
})
