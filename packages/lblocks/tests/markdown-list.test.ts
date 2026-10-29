import { describe, it, expect } from 'vitest'
import { renderList } from '../src/primitives/markdown-list.js'
import type { ListBlock, MarkdownBlock, MarkdownRenderOptions } from '../src/types/markdown-doc.js'

const opts: MarkdownRenderOptions = { theme: 'dark' }

function compose(b: MarkdownBlock): HTMLElement {
  if (b.kind === 'paragraph') {
    const p = document.createElement('p')
    p.textContent = b.children.map((c) => (c.kind === 'text' ? c.text : '')).join('')
    return p
  }
  if (b.kind === 'list') {
    return renderList(b, opts, compose)
  }
  const div = document.createElement('div')
  return div
}

describe('markdown-list primitive', () => {
  it('renders unordered list as <ul>', () => {
    const block: ListBlock = {
      kind: 'list',
      ordered: false,
      items: [{ children: [{ kind: 'paragraph', children: [{ kind: 'text', text: 'a' }] }] }],
    }
    expect(renderList(block, opts, compose).tagName).toBe('UL')
  })

  it('renders ordered list as <ol>', () => {
    const block: ListBlock = {
      kind: 'list',
      ordered: true,
      items: [{ children: [{ kind: 'paragraph', children: [{ kind: 'text', text: 'a' }] }] }],
    }
    expect(renderList(block, opts, compose).tagName).toBe('OL')
  })

  it('honors start attribute on ordered list', () => {
    const block: ListBlock = {
      kind: 'list',
      ordered: true,
      start: 5,
      items: [{ children: [{ kind: 'paragraph', children: [{ kind: 'text', text: 'a' }] }] }],
    }
    const el = renderList(block, opts, compose)
    expect(el.getAttribute('start')).toBe('5')
  })

  it('renders one <li> per item', () => {
    const block: ListBlock = {
      kind: 'list',
      ordered: false,
      items: [
        { children: [{ kind: 'paragraph', children: [{ kind: 'text', text: 'a' }] }] },
        { children: [{ kind: 'paragraph', children: [{ kind: 'text', text: 'b' }] }] },
        { children: [{ kind: 'paragraph', children: [{ kind: 'text', text: 'c' }] }] },
      ],
    }
    expect(renderList(block, opts, compose).querySelectorAll('li').length).toBe(3)
  })

  it('renders nested list inside item', () => {
    const block: ListBlock = {
      kind: 'list',
      ordered: false,
      items: [
        {
          children: [
            { kind: 'paragraph', children: [{ kind: 'text', text: 'outer' }] },
            {
              kind: 'list',
              ordered: false,
              items: [
                { children: [{ kind: 'paragraph', children: [{ kind: 'text', text: 'inner' }] }] },
              ],
            },
          ],
        },
      ],
    }
    const el = renderList(block, opts, compose)
    expect(el.querySelector('li ul li')?.textContent).toBe('inner')
  })

  it('has lb-md-list class', () => {
    const block: ListBlock = {
      kind: 'list',
      ordered: false,
      items: [{ children: [{ kind: 'paragraph', children: [{ kind: 'text', text: 'x' }] }] }],
    }
    expect(renderList(block, opts, compose).classList.contains('lb-md-list')).toBe(true)
  })

  it('falls back to inline-only when no composeBlock', () => {
    const block: ListBlock = {
      kind: 'list',
      ordered: false,
      items: [
        { children: [{ kind: 'paragraph', children: [{ kind: 'text', text: 'fallback' }] }] },
      ],
    }
    const el = renderList(block, opts)
    expect(el.querySelector('li')?.textContent).toBe('fallback')
  })
})
