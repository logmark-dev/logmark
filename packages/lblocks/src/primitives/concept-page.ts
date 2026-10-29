import type { ConceptPageData } from '../types/viewblock.js'
import type { PrimitiveOptions } from '../types/primitives.js'
import type { MarkdownBlock, MarkdownRenderOptions } from '../types/markdown-doc.js'
import { parseMarkdown } from '../engine/markdown-parser.js'
import { renderHeading } from './markdown-heading.js'
import { renderParagraph } from './markdown-paragraph.js'
import { renderList } from './markdown-list.js'
import { renderCodeblock } from './markdown-codeblock.js'
import { renderBlockquote } from './markdown-blockquote.js'
import { renderTable } from './markdown-table.js'
import { renderImage } from './markdown-image.js'
import { renderDivider } from './markdown-divider.js'

const STATUS_COLOR: Record<ConceptPageData['status'], string> = {
  draft: 'var(--lb-signal-alert)',
  mature: 'var(--lb-signal-positive)',
  archived: 'var(--lb-text-dim)',
}

/**
 * concept-page -- A full concept page distilled from vault entries, the
 * canonical reading surface of the LogMark.Knowledge cockpit.
 *
 * Anatomy:
 *   Header (h1 title + meta row with status chip and tag row) over a
 *   markdown-rendered body, then a related-concepts pill row and a sources
 *   footer stating how many vault entries the page distills.
 *
 * Tokens:
 *   Title: --lb-text-primary. Meta labels and tags: --lb-text-secondary.
 *   Status chip tone comes from the signal family (status feedback, never
 *   the entry-type family): draft --lb-signal-alert, mature
 *   --lb-signal-positive, archived --lb-text-dim. Related pills:
 *   --lb-bg-secondary on --lb-radius-full. Footer: --lb-text-muted over a
 *   --lb-border-subtle top rule.
 *
 * States:
 *   Static. Related pills carry data-slug so hosts may delegate navigation;
 *   the primitive itself attaches no handlers.
 *
 * Density:
 *   Single density; the body is prose and does not compress.
 *
 * Content:
 *   body is markdown source, parsed by the package parser. Title is the
 *   page's display name; tags render with a leading '#'.
 *
 * A11y:
 *   Semantic article/header/section/footer landmarks; the status chip is
 *   plain text inside the meta row so screen readers read it inline.
 *
 * Motion:
 *   None.
 *
 * Responsiveness:
 *   Fluid; prose wraps, pill rows wrap via flex-wrap.
 *
 * Composition:
 *   Rendered full-weight. Consumed by the Knowledge cockpit concept route;
 *   also composes as a ViewBlock card via the 'concept-page' resolver entry.
 *
 * Variants:
 *   status: 'draft' | 'mature' | 'archived' (chip class lb-status-<status>).
 *
 * Data:
 *   ConceptPageData { slug, title, status, tags[], body (markdown),
 *   sourceCount (int >= 0), relatedConcepts[] } -- see
 *   schemas/viewblocks/concept-page.json.
 *
 * Boundaries:
 *   For a one-line concept reference inside another surface, use a wiki
 *   link pill, not this page. This primitive owns a whole region.
 *
 * Anti-patterns:
 *   Never pass pre-rendered HTML in body -- it is markdown source. Never
 *   tint the status chip with entry-type colors; status is signal-family.
 */
export function renderConceptPage(
  data: ConceptPageData,
  options: Partial<PrimitiveOptions> = {}
): HTMLElement {
  const mdOptions: MarkdownRenderOptions = {
    theme: options.theme ?? 'dark',
    onLinkClick: options.onLinkClick,
    onTagClick: options.onTagClick,
  }

  const root = document.createElement('article')
  root.className = 'lb-concept-page'
  root.style.fontFamily = 'var(--lb-font-family)'
  root.style.color = 'var(--lb-text-primary)'

  // Header: title + meta row (status chip, tags)
  const header = document.createElement('header')
  header.className = 'lb-concept-page-header'
  header.style.marginBottom = '12px'

  const title = document.createElement('h1')
  title.className = 'lb-concept-page-title'
  title.textContent = data.title
  title.style.margin = '0 0 8px 0'
  title.style.fontSize = 'var(--lb-font-size-header)'
  title.style.fontWeight = '600'
  title.style.color = 'var(--lb-text-primary)'
  header.appendChild(title)

  const meta = document.createElement('div')
  meta.className = 'lb-concept-page-meta'
  meta.style.display = 'flex'
  meta.style.alignItems = 'center'
  meta.style.flexWrap = 'wrap'
  meta.style.gap = '8px'
  meta.style.fontSize = 'var(--lb-font-size-small)'

  const statusChip = document.createElement('span')
  statusChip.className = `lb-status-chip lb-status-${data.status}`
  statusChip.textContent = data.status
  statusChip.style.padding = '1px 8px'
  statusChip.style.border = '1px solid currentColor'
  statusChip.style.borderRadius = 'var(--lb-radius-full)'
  statusChip.style.color = STATUS_COLOR[data.status]
  statusChip.style.textTransform = 'uppercase'
  statusChip.style.letterSpacing = '0.04em'
  meta.appendChild(statusChip)

  if (data.tags.length > 0) {
    const tagRow = document.createElement('span')
    tagRow.className = 'lb-concept-page-tags'
    tagRow.style.display = 'inline-flex'
    tagRow.style.flexWrap = 'wrap'
    tagRow.style.gap = '6px'
    for (const tag of data.tags) {
      const tagEl = document.createElement('span')
      tagEl.className = 'lb-concept-page-tag'
      tagEl.textContent = `#${tag}`
      tagEl.style.color = 'var(--lb-text-secondary)'
      tagRow.appendChild(tagEl)
    }
    meta.appendChild(tagRow)
  }

  header.appendChild(meta)
  root.appendChild(header)

  // Body: markdown source rendered through the package markdown path
  const body = document.createElement('section')
  body.className = 'lb-concept-page-body'
  body.style.fontSize = 'var(--lb-font-size-body)'
  for (const block of parseMarkdown(data.body)) {
    body.appendChild(dispatchBlock(block, mdOptions))
  }
  root.appendChild(body)

  // Related concepts: static slug pills (hosts delegate via data-slug)
  if (data.relatedConcepts.length > 0) {
    const related = document.createElement('section')
    related.className = 'lb-concept-page-related'
    related.style.marginTop = '16px'
    related.style.display = 'flex'
    related.style.flexWrap = 'wrap'
    related.style.gap = '6px'
    for (const slug of data.relatedConcepts) {
      const pill = document.createElement('span')
      pill.className = 'lb-concept-page-related-item'
      pill.dataset.slug = slug
      pill.textContent = slug
      pill.style.padding = '2px 10px'
      pill.style.borderRadius = 'var(--lb-radius-full)'
      pill.style.backgroundColor = 'var(--lb-bg-secondary)'
      pill.style.color = 'var(--lb-text-secondary)'
      pill.style.fontSize = 'var(--lb-font-size-small)'
      related.appendChild(pill)
    }
    root.appendChild(related)
  }

  // Sources footer: attribution line naming the distillation source count
  const footer = document.createElement('footer')
  footer.className = 'lb-concept-page-sources'
  footer.textContent =
    data.sourceCount === 1
      ? 'Distilled from 1 source entry'
      : `Distilled from ${data.sourceCount} source entries`
  footer.style.marginTop = '16px'
  footer.style.paddingTop = '8px'
  footer.style.borderTop = '1px solid var(--lb-border-subtle)'
  footer.style.fontSize = 'var(--lb-font-size-small)'
  footer.style.color = 'var(--lb-text-muted)'
  root.appendChild(footer)

  return root
}

function dispatchBlock(block: MarkdownBlock, options: MarkdownRenderOptions): HTMLElement {
  switch (block.kind) {
    case 'heading': return renderHeading(block, options)
    case 'paragraph': return renderParagraph(block, options)
    case 'list': return renderList(block, options, (b) => dispatchBlock(b, options))
    case 'codeblock': return renderCodeblock(block, options)
    case 'blockquote': return renderBlockquote(block, options, (b) => dispatchBlock(b, options))
    case 'table': return renderTable(block, options)
    case 'image': return renderImage(block, options)
    case 'divider': return renderDivider(block, options)
    default: {
      const span = document.createElement('span')
      span.textContent = `(unsupported block: ${(block as { kind: string }).kind})`
      return span
    }
  }
}
