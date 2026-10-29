import { describe, it, expect } from 'vitest'
import { renderInline } from '../src/primitives/markdown-inline.js'
import { parseInline, parseMarkdown } from '../src/engine/markdown-parser.js'
import type { InlineNode, MarkdownRenderOptions } from '../src/types/markdown-doc.js'

const opts: MarkdownRenderOptions = { theme: 'dark' }

describe('line-break renderer', () => {
  it('renders line-break node as <br>', () => {
    const nodes: InlineNode[] = [
      { kind: 'text', text: 'first' },
      { kind: 'line-break' },
      { kind: 'text', text: 'second' },
    ]
    const root = document.createElement('div')
    root.appendChild(renderInline(nodes, opts))
    expect(root.querySelector('br')).toBeTruthy()
    expect(root.textContent).toBe('firstsecond')
  })
})

describe('line-break parser', () => {
  it('parses <br> as line-break', () => {
    const nodes = parseInline('a<br>b')
    expect(nodes).toEqual([
      { kind: 'text', text: 'a' },
      { kind: 'line-break' },
      { kind: 'text', text: 'b' },
    ])
  })

  it('parses <br/> and <br /> as line-break', () => {
    expect(parseInline('a<br/>b')[1]).toEqual({ kind: 'line-break' })
    expect(parseInline('a<br />b')[1]).toEqual({ kind: 'line-break' })
  })

  it('drops other HTML tags silently', () => {
    expect(parseInline('a<span>x</span>b')).toEqual([
      { kind: 'text', text: 'axb' },
    ])
  })

  it('preserves source line breaks inside paragraph blocks', () => {
    const [block] = parseMarkdown('first\nsecond\nthird')
    expect(block?.kind).toBe('paragraph')
    if (!block || block.kind !== 'paragraph') throw new Error('Expected paragraph')
    expect(block.children).toEqual([
      { kind: 'text', text: 'first' },
      { kind: 'line-break' },
      { kind: 'text', text: 'second' },
      { kind: 'line-break' },
      { kind: 'text', text: 'third' },
    ])
  })
})
