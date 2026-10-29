import type { PlaintextBlock, MarkdownRenderOptions } from '../types/markdown-doc.js'

export function renderPlaintext(
  block: PlaintextBlock,
  _options: MarkdownRenderOptions
): HTMLElement {
  const pre = document.createElement('pre')
  pre.className = 'lb-md-plaintext'
  pre.style.fontFamily = 'var(--lb-font-mono)'
  pre.style.fontSize = 'var(--lb-font-size-body)'
  pre.style.color = 'var(--lb-text-primary)'
  pre.style.backgroundColor = 'var(--lb-bg-secondary)'
  pre.style.padding = '12px'
  pre.style.margin = '0'
  pre.style.whiteSpace = 'pre'
  pre.style.overflowX = 'auto'
  pre.textContent = block.text
  return pre
}
