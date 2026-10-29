import type { HeadingBlock, MarkdownRenderOptions } from '../types/markdown-doc.js'
import { renderInline } from './markdown-inline.js'

const HEADING_SIZES: Record<1 | 2 | 3 | 4 | 5 | 6, string> = {
  1: 'var(--fs-display, 24px)',
  2: 'var(--lb-font-size-header)',
  3: 'var(--fs-h2, 14px)',
  4: 'var(--fs-body, 13px)',
  5: 'var(--fs-meta, 12px)',
  6: 'var(--fs-hint, 11px)',
}

export function renderHeading(
  block: HeadingBlock,
  options: MarkdownRenderOptions
): HTMLElement {
  const tag = `h${block.level}` as const
  const el = document.createElement(tag)
  el.className = 'lb-md-heading'
  el.setAttribute('data-level', String(block.level))
  el.style.color = 'var(--lb-text-primary)'
  el.style.fontFamily = 'var(--lb-font-family)'
  el.style.fontSize = HEADING_SIZES[block.level]
  el.style.fontWeight = 'var(--fw-semibold, 600)'
  el.style.margin = '16px 0 8px 0'
  el.style.lineHeight = '1.3'
  el.appendChild(renderInline(block.children, options))
  return el
}
