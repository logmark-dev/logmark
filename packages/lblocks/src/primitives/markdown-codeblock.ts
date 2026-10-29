import type { CodeBlock, MarkdownRenderOptions } from '../types/markdown-doc.js'

export function renderCodeblock(
  block: CodeBlock,
  _options: MarkdownRenderOptions
): HTMLElement {
  const pre = document.createElement('pre')
  pre.className = 'lb-md-codeblock'
  pre.style.fontFamily = 'var(--lb-font-mono)'
  pre.style.fontSize = '12px'
  pre.style.backgroundColor = 'var(--lb-code-bg)'
  pre.style.color = 'var(--lb-text-primary)'
  pre.style.padding = '12px'
  pre.style.borderRadius = 'var(--lb-radius)'
  pre.style.margin = '12px 0'
  pre.style.overflow = 'auto'
  pre.style.lineHeight = '1.45'

  const code = document.createElement('code')
  code.textContent = block.text
  if (block.language) {
    code.setAttribute('data-language', block.language)
  }
  pre.appendChild(code)
  return pre
}
