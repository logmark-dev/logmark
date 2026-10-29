import { describe, it, expect } from 'vitest'
import { composeMarkdown } from '../src/engine/markdown-compositor.js'
import { renderMarkdown } from '../src/index.js'
import type { LBlocksDocument, MarkdownRenderOptions } from '../src/types/markdown-doc.js'

const opts: MarkdownRenderOptions = { theme: 'dark' }

describe('markdown-compositor', () => {
  it('composes empty document to empty root', () => {
    const doc: LBlocksDocument = { version: 1, blocks: [] }
    const root = composeMarkdown(doc, opts)
    expect(root.classList.contains('lb-md-root')).toBe(true)
    expect(root.children.length).toBe(0)
  })

  it('composes single heading', () => {
    const doc: LBlocksDocument = {
      version: 1,
      blocks: [{ kind: 'heading', level: 1, children: [{ kind: 'text', text: 'Title' }] }],
    }
    const root = composeMarkdown(doc, opts)
    expect(root.children).toHaveLength(1)
    expect(root.querySelector('h1')?.textContent).toBe('Title')
  })

  it('composes multi-block document in order', () => {
    const doc: LBlocksDocument = {
      version: 1,
      blocks: [
        { kind: 'heading', level: 1, children: [{ kind: 'text', text: 'Hi' }] },
        { kind: 'paragraph', children: [{ kind: 'text', text: 'body' }] },
        { kind: 'divider' },
        { kind: 'paragraph', children: [{ kind: 'text', text: 'after' }] },
      ],
    }
    const root = composeMarkdown(doc, opts)
    expect(root.children).toHaveLength(4)
    expect(root.children[0].tagName).toBe('H1')
    expect(root.children[1].tagName).toBe('P')
    expect(root.children[2].tagName).toBe('HR')
    expect(root.children[3].tagName).toBe('P')
  })

  it('composes nested blockquote with paragraph inside', () => {
    const doc: LBlocksDocument = {
      version: 1,
      blocks: [
        {
          kind: 'blockquote',
          children: [{ kind: 'paragraph', children: [{ kind: 'text', text: 'inside' }] }],
        },
      ],
    }
    const root = composeMarkdown(doc, opts)
    expect(root.querySelector('blockquote p')?.textContent).toBe('inside')
  })

  it('composes nested list with paragraph and inner list', () => {
    const doc: LBlocksDocument = {
      version: 1,
      blocks: [
        {
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
                    {
                      children: [
                        { kind: 'paragraph', children: [{ kind: 'text', text: 'inner' }] },
                      ],
                    },
                  ],
                },
              ],
            },
          ],
        },
      ],
    }
    const root = composeMarkdown(doc, opts)
    expect(root.querySelector('ul li ul li p')?.textContent).toBe('inner')
  })

  it('composes table with alignments and inline emphasis', () => {
    const doc: LBlocksDocument = {
      version: 1,
      blocks: [
        {
          kind: 'table',
          header: { cells: [{ children: [{ kind: 'text', text: 'h' }] }] },
          rows: [
            {
              cells: [
                {
                  children: [
                    { kind: 'emphasis', style: 'bold', children: [{ kind: 'text', text: 'B' }] },
                  ],
                },
              ],
            },
          ],
          alignments: ['center'],
        },
      ],
    }
    const root = composeMarkdown(doc, opts)
    expect(root.querySelector('table tbody td strong')?.textContent).toBe('B')
  })

  it('composes frontmatter card', () => {
    const doc: LBlocksDocument = {
      version: 1,
      blocks: [{ kind: 'frontmatter', entries: [{ key: 'title', value: 'X' }] }],
    }
    const root = composeMarkdown(doc, opts)
    expect(root.querySelector('.lb-md-frontmatter dt')?.textContent).toBe('title')
  })
})

describe('renderMarkdown public API', () => {
  it('applies theme and replaces container children', () => {
    const container = document.createElement('div')
    container.appendChild(document.createElement('span')) // existing child to be replaced
    const doc: LBlocksDocument = {
      version: 1,
      blocks: [{ kind: 'paragraph', children: [{ kind: 'text', text: 'fresh' }] }],
    }
    renderMarkdown(doc, container, opts)
    expect(container.querySelector('span')).toBeNull()
    expect(container.querySelector('.lb-md-root .lb-md-paragraph')?.textContent).toBe('fresh')
    // Theme tokens are applied to container
    expect(container.style.getPropertyValue('--lb-text-primary')).toBe('rgb(var(--text, 228 228 231))')
  })

  it('applies light theme tokens when requested', () => {
    const container = document.createElement('div')
    const doc: LBlocksDocument = { version: 1, blocks: [] }
    renderMarkdown(doc, container, { theme: 'light' })
    expect(container.style.getPropertyValue('--lb-text-primary')).toBe('rgb(var(--text, 30 30 46))')
  })
})
