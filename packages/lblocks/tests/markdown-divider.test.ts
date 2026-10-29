import { describe, it, expect } from 'vitest'
import { renderDivider } from '../src/primitives/markdown-divider.js'
import type { MarkdownRenderOptions } from '../src/types/markdown-doc.js'

const opts: MarkdownRenderOptions = { theme: 'dark' }

describe('markdown-divider primitive', () => {
  it('renders as <hr>', () => {
    expect(renderDivider({ kind: 'divider' }, opts).tagName).toBe('HR')
  })

  it('has lb-md-divider class', () => {
    expect(renderDivider({ kind: 'divider' }, opts).classList.contains('lb-md-divider')).toBe(true)
  })

  it('has subtle border color', () => {
    const el = renderDivider({ kind: 'divider' }, opts)
    expect(el.style.borderTopColor).toBe('var(--lb-border-subtle)')
  })
})
