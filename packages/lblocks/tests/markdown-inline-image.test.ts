import { describe, it, expect } from 'vitest'
import { parseInline } from '../src/engine/markdown-parser.js'

describe('inline image parser', () => {
  it('parses inline ![alt](src) as inline-image', () => {
    const nodes = parseInline('see ![cat](/cat.png) here')
    expect(nodes).toHaveLength(3)
    expect(nodes[0]).toEqual({ kind: 'text', text: 'see ' })
    expect(nodes[1]).toEqual({ kind: 'inline-image', src: '/cat.png', alt: 'cat' })
    expect(nodes[2]).toEqual({ kind: 'text', text: ' here' })
  })

  it('parses image-inside-link [![alt](img)](url) as link wrapping inline-image', () => {
    const nodes = parseInline('[![logo](/logo.png)](https://example.com)')
    expect(nodes).toHaveLength(1)
    expect(nodes[0]).toMatchObject({
      kind: 'link',
      href: 'https://example.com',
      children: [{ kind: 'inline-image', src: '/logo.png', alt: 'logo' }],
    })
  })

  it('preserves image title attribute', () => {
    const nodes = parseInline('![a](/a.png "tip")')
    expect(nodes[0]).toEqual({ kind: 'inline-image', src: '/a.png', alt: 'a', title: 'tip' })
  })
})
