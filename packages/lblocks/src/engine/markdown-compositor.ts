import type {
  LBlocksDocument,
  MarkdownBlock,
  MarkdownRenderOptions,
} from '../types/markdown-doc.js'
import { renderHeading } from '../primitives/markdown-heading.js'
import { renderParagraph } from '../primitives/markdown-paragraph.js'
import { renderDivider } from '../primitives/markdown-divider.js'
import { renderBlockquote } from '../primitives/markdown-blockquote.js'
import { renderList } from '../primitives/markdown-list.js'
import { renderCodeblock } from '../primitives/markdown-codeblock.js'
import { renderTable } from '../primitives/markdown-table.js'
import { renderImage } from '../primitives/markdown-image.js'
import { renderFrontmatterCard } from '../primitives/markdown-frontmatter-card.js'
import { renderChatTranscript } from '../primitives/chat-transcript.js'
import { renderThreadWiki } from '../primitives/thread-wiki.js'
import { renderPlaintext } from '../primitives/markdown-plaintext.js'

export function composeMarkdown(
  doc: LBlocksDocument,
  options: MarkdownRenderOptions
): HTMLElement {
  const root = document.createElement('div')
  root.className = 'lb-md-root'
  root.style.fontFamily = 'var(--lb-font-family)'
  root.style.color = 'var(--lb-text-primary)'
  root.style.backgroundColor = 'var(--lb-bg-primary)'
  root.style.padding = '20px 24px'
  // No max-width: let tables and other blocks use the full host width.
  // Prose readability on wide monitors is a known tradeoff; a per-block
  // prose cap can be added later if it becomes a problem.

  for (const block of doc.blocks) {
    root.appendChild(composeBlock(block, options))
  }
  return root
}

function composeBlock(block: MarkdownBlock, options: MarkdownRenderOptions): HTMLElement {
  switch (block.kind) {
    case 'heading':
      return renderHeading(block, options)
    case 'paragraph':
      return renderParagraph(block, options)
    case 'divider':
      return renderDivider(block, options)
    case 'blockquote':
      return renderBlockquote(block, options, (b) => composeBlock(b, options))
    case 'list':
      return renderList(block, options, (b) => composeBlock(b, options))
    case 'codeblock':
      return renderCodeblock(block, options)
    case 'table':
      return renderTable(block, options)
    case 'image':
      return renderImage(block, options)
    case 'frontmatter':
      return renderFrontmatterCard(block, options)
    case 'chat-transcript':
      return renderChatTranscript(block, options)
    case 'thread-wiki':
      return renderThreadWiki(block, options)
    case 'plaintext':
      return renderPlaintext(block, options)
  }
}
