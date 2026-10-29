import type { ParagraphBlock, MarkdownRenderOptions } from '../types/markdown-doc.js'
import { renderInline } from './markdown-inline.js'

export function renderParagraph(
  block: ParagraphBlock,
  options: MarkdownRenderOptions
): HTMLElement {
  const el = document.createElement('p')
  el.className = 'lb-md-paragraph'
  el.style.color = 'var(--lb-text-primary)'
  el.style.fontFamily = 'var(--lb-font-family)'
  el.style.fontSize = 'var(--lb-font-size-body)'
  el.style.lineHeight = '1.55'
  el.style.margin = '0 0 12px 0'
  el.appendChild(renderInline(block.children, options))
  return el
}
