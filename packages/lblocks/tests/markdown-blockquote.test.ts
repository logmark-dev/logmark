import { describe, it, expect } from 'vitest'
import { renderBlockquote } from '../src/primitives/markdown-blockquote.js'
import type { BlockquoteBlock, MarkdownRenderOptions, MarkdownBlock } from '../src/types/markdown-doc.js'

const opts: MarkdownRenderOptions = { theme: 'dark' }

describe('markdown-blockquote primitive', () => {
  it('renders as <blockquote>', () => {
    const block: BlockquoteBlock = {
      kind: 'blockquote',
      children: [{ kind: 'paragraph', children: [{ kind: 'text', text: 'x' }] }],
    }
    expect(renderBlockquote(block, opts, fakeCompose).tagName).toBe('BLOCKQUOTE')
  })

  it('has lb-md-blockquote class', () => {
    const block: BlockquoteBlock = {
      kind: 'blockquote',
      children: [{ kind: 'paragraph', children: [{ kind: 'text', text: 'x' }] }],
    }
    expect(
      renderBlockquote(block, opts, fakeCompose).classList.contains('lb-md-blockquote')
    ).toBe(true)
  })

  it('uses left border in accent color', () => {
    const block: BlockquoteBlock = {
      kind: 'blockquote',
      children: [{ kind: 'paragraph', children: [{ kind: 'text', text: 'x' }] }],
    }
    const el = renderBlockquote(block, opts, fakeCompose)
    expect(el.style.borderLeftColor).toBe('var(--lb-quote-border)')
  })

  it('renders nested children via composeBlock', () => {
    const calls: MarkdownBlock[] = []
    const compose = (b: MarkdownBlock): HTMLElement => {
      calls.push(b)
      const el = document.createElement('p')
      el.textContent = 'nested'
      return el
    }
    const block: BlockquoteBlock = {
      kind: 'blockquote',
      children: [
        { kind: 'paragraph', children: [{ kind: 'text', text: 'a' }] },
        { kind: 'paragraph', children: [{ kind: 'text', text: 'b' }] },
      ],
    }
    const el = renderBlockquote(block, opts, compose)
    expect(calls).toHaveLength(2)
    expect(el.querySelectorAll('p').length).toBe(2)
  })

  it('falls back to inline-only when composeBlock not provided', () => {
    const block: BlockquoteBlock = {
      kind: 'blockquote',
      children: [{ kind: 'paragraph', children: [{ kind: 'text', text: 'x' }] }],
    }
    const el = renderBlockquote(block, opts)
    expect(el.textContent).toBe('x')
  })
})

// fakeCompose used in early tests where contents don't matter
function fakeCompose(_b: MarkdownBlock): HTMLElement {
  const p = document.createElement('p')
  p.textContent = 'fake'
  return p
}
