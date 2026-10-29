import { describe, it, expect } from 'vitest'
import { renderParagraph } from '../src/primitives/markdown-paragraph.js'
import type { ParagraphBlock, MarkdownRenderOptions } from '../src/types/markdown-doc.js'

const opts: MarkdownRenderOptions = { theme: 'dark' }

describe('markdown-paragraph primitive', () => {
  it('renders as <p>', () => {
    const block: ParagraphBlock = { kind: 'paragraph', children: [{ kind: 'text', text: 'hello' }] }
    expect(renderParagraph(block, opts).tagName).toBe('P')
  })

  it('has lb-md-paragraph class', () => {
    const block: ParagraphBlock = { kind: 'paragraph', children: [{ kind: 'text', text: 'x' }] }
    expect(renderParagraph(block, opts).classList.contains('lb-md-paragraph')).toBe(true)
  })

  it('renders inline content', () => {
    const block: ParagraphBlock = {
      kind: 'paragraph',
      children: [
        { kind: 'text', text: 'see ' },
        { kind: 'emphasis', style: 'italic', children: [{ kind: 'text', text: 'this' }] },
      ],
    }
    const el = renderParagraph(block, opts)
    expect(el.textContent).toBe('see this')
    expect(el.querySelector('em')?.textContent).toBe('this')
  })

  it('uses primary text color and body font size', () => {
    const block: ParagraphBlock = { kind: 'paragraph', children: [{ kind: 'text', text: 'x' }] }
    const el = renderParagraph(block, opts)
    expect(el.style.color).toBe('var(--lb-text-primary)')
    expect(el.style.fontSize).toBe('var(--lb-font-size-body)')
  })
})
