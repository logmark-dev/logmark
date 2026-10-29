import { describe, it, expect } from 'vitest'
import { renderHeading } from '../src/primitives/markdown-heading.js'
import type { HeadingBlock, MarkdownRenderOptions } from '../src/types/markdown-doc.js'

const opts: MarkdownRenderOptions = { theme: 'dark' }

describe('markdown-heading primitive', () => {
  function block(level: 1 | 2 | 3 | 4 | 5 | 6, text: string): HeadingBlock {
    return { kind: 'heading', level, children: [{ kind: 'text', text }] }
  }

  it('renders h1 for level 1', () => {
    const el = renderHeading(block(1, 'Hi'), opts)
    expect(el.tagName).toBe('H1')
    expect(el.textContent).toBe('Hi')
  })

  it('renders h6 for level 6', () => {
    const el = renderHeading(block(6, 'Hi'), opts)
    expect(el.tagName).toBe('H6')
  })

  it('has lb-md-heading class', () => {
    const el = renderHeading(block(1, 'x'), opts)
    expect(el.classList.contains('lb-md-heading')).toBe(true)
  })

  it('has data-level attribute', () => {
    const el = renderHeading(block(3, 'x'), opts)
    expect(el.getAttribute('data-level')).toBe('3')
  })

  it('uses primary text color', () => {
    const el = renderHeading(block(1, 'x'), opts)
    expect(el.style.color).toBe('var(--lb-text-primary)')
  })

  it('renders inline emphasis inside heading', () => {
    const el = renderHeading(
      {
        kind: 'heading',
        level: 2,
        children: [
          { kind: 'text', text: 'A ' },
          { kind: 'emphasis', style: 'bold', children: [{ kind: 'text', text: 'B' }] },
        ],
      },
      opts
    )
    expect(el.querySelector('strong')?.textContent).toBe('B')
  })

  it('uses typography tokens by level', () => {
    const h1 = renderHeading(block(1, 'x'), opts)
    const h2 = renderHeading(block(2, 'x'), opts)
    const h6 = renderHeading(block(6, 'x'), opts)
    expect(h1.style.fontSize).toBe('var(--fs-display, 24px)')
    expect(h2.style.fontSize).toBe('var(--lb-font-size-header)')
    expect(h6.style.fontSize).toBe('var(--fs-hint, 11px)')
    expect(h1.style.fontWeight).toBe('var(--fw-semibold, 600)')
  })
})
