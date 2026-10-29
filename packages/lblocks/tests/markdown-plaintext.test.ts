import { describe, it, expect } from 'vitest'
import { renderPlaintext } from '../src/primitives/markdown-plaintext.js'
import type { PlaintextBlock, MarkdownRenderOptions } from '../src/types/markdown-doc.js'

const opts: MarkdownRenderOptions = { theme: 'dark' }

describe('plaintext block renderer', () => {
  it('renders text inside a <pre>', () => {
    const block: PlaintextBlock = { kind: 'plaintext', text: 'line 1\nline 2' }
    const el = renderPlaintext(block, opts)
    expect(el.tagName).toBe('PRE')
    expect(el.textContent).toBe('line 1\nline 2')
  })

  it('uses monospace font and preserves whitespace', () => {
    const block: PlaintextBlock = { kind: 'plaintext', text: '  indented' }
    const el = renderPlaintext(block, opts)
    expect(el.style.fontFamily).toContain('mono')
    expect(el.style.whiteSpace).toBe('pre')
  })

  it('does not interpret markdown-like content', () => {
    const block: PlaintextBlock = { kind: 'plaintext', text: '# not a heading\n**not bold**' }
    const el = renderPlaintext(block, opts)
    expect(el.querySelector('h1')).toBeNull()
    expect(el.querySelector('strong')).toBeNull()
    expect(el.textContent).toBe('# not a heading\n**not bold**')
  })
})
