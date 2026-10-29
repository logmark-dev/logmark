import type { PrimitiveOptions } from '../types/primitives.js'
import type { TopicShelfData } from '../types/reader.js'
import { renderAttributionChip } from './attribution-chip.js'

/**
 * topic-shelf -- Hero + topic-mini shapes for the same topic atom.
 *
 * Two shapes per spec: hero is the one featured topic at the top of the
 * front door; topic-mini fills the 2-column grid below.
 *
 * Anatomy:
 *   Header (topic name + counts + optional more-link) above a horizontal
 *   scroll of vault cards. Each vault card carries title + attribution +
 *   entry count.
 *
 * Tokens:
 *   Card: --lb-bg-secondary background, --lb-border border. Topic name:
 *   --lb-signal-positive. Meta: --lb-text-secondary. Entry count:
 *   --lb-text-dim.
 *
 * States:
 *   Mini hovers to --lb-card-elevated (host responsibility). Hero
 *   itself doesn't hover -- only the feature card inside it does.
 *
 * Density:
 *   Hero is once per surface. Mini is grid-sized -- fits 2 across at
 *   desktop, 1 at mobile.
 *
 * Content:
 *   Hero feature is a pull-quote from one entry (the most recent on this
 *   topic). Supporting rows are the next 2-3 entries. Mini shows the
 *   topic header + 2 entries + footer.
 *
 * A11y:
 *   Topic name is heading-equivalent; the primitive renders <section>
 *   so host can apply h1/h2 semantics via the surrounding context.
 *
 * Motion:
 *   Mini transitions 150ms ease-out on hover. Hero doesn't animate.
 *
 * Responsiveness:
 *   Hero collapses 2-col to 1-col at 960px. Mini grid drops to 1-col
 *   at 600px. Scroll row is overflow-x: auto.
 *
 * Composition:
 *   Front door only. Don't use elsewhere -- viewer-entry-row covers
 *   feed contexts.
 *
 * Variants:
 *   Hero (one per surface) vs mini (many per surface). Single primitive
 *   today; variant exposed by host (composition pattern, not prop).
 *
 * Data:
 *   { topic: string, entryCount: number, vaultCount: number,
 *     vaults: TopicShelfVault[], moreHref?: string | null }
 *
 * Boundaries:
 *   Mini does not show a sparkline; the hero does. Sparkline lives
 *   where momentum is the story.
 *
 * Anti-patterns:
 *   Don't use horizontal scroll inside these. The vertical hierarchy IS
 *   the design. Don't put more than one hero on the front door.
 */
export function renderTopicShelf(
  data: TopicShelfData,
  _options: Partial<PrimitiveOptions> = {}
): HTMLElement {
  const root = document.createElement('section')
  root.className = 'lb-topic-shelf'
  root.style.marginBottom = '32px'

  // Header row
  const header = document.createElement('div')
  header.className = 'lb-topic-shelf-header'
  header.style.display = 'flex'
  header.style.alignItems = 'baseline'
  header.style.gap = '10px'
  header.style.marginBottom = '12px'
  const tagEl = document.createElement('span')
  tagEl.className = 'lb-topic-shelf-tag'
  tagEl.textContent = `#${data.topic}`
  tagEl.style.fontSize = 'var(--lb-font-size-body)'
  tagEl.style.fontWeight = '700'
  tagEl.style.color = 'var(--lb-signal-positive)'
  header.appendChild(tagEl)
  const meta = document.createElement('span')
  meta.className = 'lb-topic-shelf-meta'
  meta.textContent = `${data.entryCount} entries · ${data.vaultCount} vaults`
  meta.style.fontSize = 'var(--lb-font-size-small)'
  meta.style.color = 'var(--lb-text-secondary)'
  header.appendChild(meta)
  if (data.moreHref) {
    const spacer = document.createElement('span')
    spacer.style.flex = '1'
    header.appendChild(spacer)
    const more = document.createElement('a')
    more.className = 'lb-topic-shelf-more'
    more.setAttribute('href', data.moreHref)
    more.textContent = `More on #${data.topic}`
    more.style.fontSize = 'var(--lb-font-size-small)'
    more.style.color = 'var(--lb-text-secondary)'
    more.style.textDecoration = 'none'
    header.appendChild(more)
  }
  root.appendChild(header)

  // Scroll row
  const scroll = document.createElement('div')
  scroll.className = 'lb-topic-shelf-scroll'
  scroll.style.display = 'flex'
  scroll.style.gap = '12px'
  scroll.style.overflowX = 'auto'
  scroll.style.paddingBottom = '4px'
  for (const vault of data.vaults) {
    const card = document.createElement(vault.href ? 'a' : 'div')
    card.className = 'lb-topic-shelf-card'
    if (vault.href) {
      card.setAttribute('href', vault.href)
      card.style.textDecoration = 'none'
    }
    card.style.flexShrink = '0'
    card.style.width = '200px'
    card.style.padding = '12px'
    card.style.border = '1px solid var(--lb-border)'
    card.style.borderRadius = 'var(--lb-radius)'
    card.style.background = 'var(--lb-bg-secondary)'
    card.style.display = 'flex'
    card.style.flexDirection = 'column'
    card.style.gap = '6px'
    const titleEl = document.createElement('div')
    titleEl.className = 'lb-topic-shelf-card-title'
    titleEl.textContent = vault.title
    titleEl.style.fontSize = 'var(--lb-font-size-body)'
    titleEl.style.fontWeight = '600'
    titleEl.style.color = 'var(--lb-text-primary)'
    titleEl.style.overflow = 'hidden'
    titleEl.style.textOverflow = 'ellipsis'
    titleEl.style.whiteSpace = 'nowrap'
    card.appendChild(titleEl)
    const chipRow = document.createElement('div')
    chipRow.style.display = 'flex'
    chipRow.style.alignItems = 'center'
    chipRow.style.gap = '6px'
    const handleEl = document.createElement('span')
    handleEl.textContent = `@${vault.publisherHandle}`
    handleEl.style.fontSize = 'var(--lb-font-size-small)'
    handleEl.style.color = 'var(--lb-text-secondary)'
    chipRow.appendChild(handleEl)
    chipRow.appendChild(renderAttributionChip({ kind: vault.publisherKind }))
    card.appendChild(chipRow)
    const entryCount = document.createElement('div')
    entryCount.textContent = `${vault.entryCount} entries`
    entryCount.style.fontSize = 'var(--lb-font-size-small)'
    entryCount.style.color = 'var(--lb-text-dim)'
    card.appendChild(entryCount)
    scroll.appendChild(card)
  }
  root.appendChild(scroll)

  return root
}
