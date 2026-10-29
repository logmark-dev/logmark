import { describe, it, expect } from 'vitest'
import { renderInline } from '../src/primitives/markdown-inline.js'
import { parseInline } from '../src/engine/markdown-parser.js'
import type { InlineNode, MarkdownRenderOptions } from '../src/types/markdown-doc.js'

const opts: MarkdownRenderOptions = { theme: 'dark' }

describe('strikethrough renderer', () => {
  it('renders strikethrough emphasis as <del>', () => {
    const nodes: InlineNode[] = [
      { kind: 'emphasis', style: 'strikethrough', children: [{ kind: 'text', text: 'gone' }] },
    ]
    const root = document.createElement('div')
    root.appendChild(renderInline(nodes, opts))
    const del = root.querySelector('del')
    expect(del).toBeTruthy()
    expect(del?.textContent).toBe('gone')
  })

  it('applies line-through styling', () => {
    const nodes: InlineNode[] = [
      { kind: 'emphasis', style: 'strikethrough', children: [{ kind: 'text', text: 'x' }] },
    ]
    const root = document.createElement('div')
    root.appendChild(renderInline(nodes, opts))
    const del = root.querySelector('del') as HTMLElement
    expect(del.style.textDecoration).toBe('line-through')
  })
})

describe('strikethrough parser', () => {
  it('parses ~~text~~ as strikethrough emphasis', () => {
    const nodes = parseInline('~~deleted~~')
    expect(nodes).toEqual([
      { kind: 'emphasis', style: 'strikethrough', children: [{ kind: 'text', text: 'deleted' }] },
    ])
  })

  it('parses strikethrough mixed with bold', () => {
    const nodes = parseInline('**keep** ~~drop~~')
    expect(nodes).toHaveLength(3)
    expect(nodes[0]).toMatchObject({ kind: 'emphasis', style: 'bold' })
    expect(nodes[2]).toMatchObject({ kind: 'emphasis', style: 'strikethrough' })
  })
})
