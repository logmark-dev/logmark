import { describe, it, expect } from 'vitest'
import type { LBlocksDocument, HeadingBlock, InlineNode } from '../src/types/markdown-doc.js'

describe('LBlocksDocument schema', () => {
  it('accepts a minimal document', () => {
    const doc: LBlocksDocument = {
      version: 1,
      blocks: [],
    }
    expect(doc.version).toBe(1)
    expect(doc.blocks).toHaveLength(0)
  })

  it('accepts a heading with inline children', () => {
    const heading: HeadingBlock = {
      kind: 'heading',
      level: 1,
      children: [{ kind: 'text', text: 'Hello' }],
    }
    expect(heading.level).toBe(1)
    expect(heading.children[0].kind).toBe('text')
  })

  it('discriminates inline nodes by kind', () => {
    const nodes: InlineNode[] = [
      { kind: 'text', text: 'plain' },
      { kind: 'emphasis', style: 'bold', children: [{ kind: 'text', text: 'b' }] },
      { kind: 'code-span', text: 'code' },
      { kind: 'link', href: 'https://x', children: [{ kind: 'text', text: 'link' }] },
    ]
    expect(nodes.map((n) => n.kind)).toEqual(['text', 'emphasis', 'code-span', 'link'])
  })

  it('accepts a list with nested children', () => {
    const doc: LBlocksDocument = {
      version: 1,
      blocks: [
        {
          kind: 'list',
          ordered: false,
          items: [
            {
              children: [
                { kind: 'paragraph', children: [{ kind: 'text', text: 'one' }] },
                {
                  kind: 'list',
                  ordered: false,
                  items: [{ children: [{ kind: 'paragraph', children: [{ kind: 'text', text: 'nested' }] }] }],
                },
              ],
            },
          ],
        },
      ],
    }
    expect(doc.blocks[0].kind).toBe('list')
  })
})
