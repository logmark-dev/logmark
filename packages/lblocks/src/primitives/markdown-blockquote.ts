import type {
  BlockquoteBlock,
  ComposeBlockFn,
  MarkdownRenderOptions,
  ParagraphBlock,
} from '../types/markdown-doc.js'
import { renderInline } from './markdown-inline.js'

export function renderBlockquote(
  block: BlockquoteBlock,
  options: MarkdownRenderOptions,
  composeBlock?: ComposeBlockFn
): HTMLElement {
  const el = document.createElement('blockquote')
  el.className = 'lb-md-blockquote'
  el.style.borderLeft = '3px solid'
  el.style.borderLeftColor = 'var(--lb-quote-border)'
  el.style.padding = '4px 0 4px 12px'
  el.style.margin = '12px 0'
  el.style.color = 'var(--lb-text-secondary)'
  el.style.fontStyle = 'italic'

  if (composeBlock) {
    for (const child of block.children) {
      el.appendChild(composeBlock(child))
    }
  } else {
    // Fallback for callers using this primitive without a compositor.
    // (The compositor in Task 7 always supplies composeBlock.)
    for (const child of block.children) {
      if (child.kind === 'paragraph') {
        const para = child as ParagraphBlock
        const span = document.createElement('span')
        span.appendChild(renderInline(para.children, options))
        el.appendChild(span)
      }
    }
  }

  return el
}
