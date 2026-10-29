import type { DividerBlock, MarkdownRenderOptions } from '../types/markdown-doc.js'

export function renderDivider(
  _block: DividerBlock,
  _options: MarkdownRenderOptions
): HTMLElement {
  const hr = document.createElement('hr')
  hr.className = 'lb-md-divider'
  hr.style.border = 'none'
  hr.style.borderTopWidth = '1px'
  hr.style.borderTopStyle = 'solid'
  hr.style.borderTopColor = 'var(--lb-border-subtle)'
  hr.style.margin = '20px 0'
  return hr
}
