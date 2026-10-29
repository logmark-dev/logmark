import { describe, it, expect } from 'vitest'
import { renderCodeblock } from '../src/primitives/markdown-codeblock.js'
import type { CodeBlock, MarkdownRenderOptions } from '../src/types/markdown-doc.js'

const opts: MarkdownRenderOptions = { theme: 'dark' }

describe('markdown-codeblock primitive', () => {
  it('renders <pre> wrapping <code>', () => {
    const block: CodeBlock = { kind: 'codeblock', text: 'x' }
    const el = renderCodeblock(block, opts)
    expect(el.tagName).toBe('PRE')
    expect(el.querySelector('code')).toBeTruthy()
  })

  it('preserves whitespace and newlines', () => {
    const block: CodeBlock = { kind: 'codeblock', text: 'line1\n  line2\n' }
    expect(renderCodeblock(block, opts).querySelector('code')?.textContent).toBe('line1\n  line2\n')
  })

  it('escapes HTML special characters', () => {
    const block: CodeBlock = { kind: 'codeblock', text: '<script>alert(1)</script>' }
    const code = renderCodeblock(block, opts).querySelector('code')!
    expect(code.textContent).toBe('<script>alert(1)</script>')
    expect(code.querySelector('script')).toBeNull()
  })

  it('records language as data-language attribute', () => {
    const block: CodeBlock = { kind: 'codeblock', text: 'x', language: 'typescript' }
    const code = renderCodeblock(block, opts).querySelector('code')!
    expect(code.getAttribute('data-language')).toBe('typescript')
  })

  it('omits data-language when no language', () => {
    const block: CodeBlock = { kind: 'codeblock', text: 'x' }
    expect(
      renderCodeblock(block, opts).querySelector('code')!.hasAttribute('data-language')
    ).toBe(false)
  })

  it('uses mono font and code background', () => {
    const block: CodeBlock = { kind: 'codeblock', text: 'x' }
    const pre = renderCodeblock(block, opts)
    expect(pre.style.fontFamily).toBe('var(--lb-font-mono)')
    expect(pre.style.backgroundColor).toBe('var(--lb-code-bg)')
  })

  it('has lb-md-codeblock class', () => {
    const block: CodeBlock = { kind: 'codeblock', text: 'x' }
    expect(renderCodeblock(block, opts).classList.contains('lb-md-codeblock')).toBe(true)
  })
})
