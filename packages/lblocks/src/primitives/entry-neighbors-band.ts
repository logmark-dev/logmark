import type { PrimitiveOptions } from '../types/primitives.js'
import type { EntryNeighborsBandData, EntryNeighborRef, EntryTopicNeighbor } from '../types/reader.js'
import { renderAttributionChip } from './attribution-chip.js'
import { entryTypeColor } from '../util/entry-type-tokens.js'

function neighborCard(ref: EntryNeighborRef, dirLabel: string): HTMLElement {
  // Each in-vault neighbor renders as a card matching the kit's
  // `.neighbor-card` shape (DesignSystem/ui_kits/ink/ink.css:794-813):
  // padded container with border + radius, eyebrow label (mono, caps) on
  // top, multi-line preview below clamped to 2 lines. A 3px left rail
  // keyed to the entry-type color marks "same vault".
  const card = document.createElement(ref.href ? 'a' : 'div')
  card.className = 'lb-entry-neighbors-vault-card'
  if (ref.href) {
    card.setAttribute('href', ref.href)
    card.style.textDecoration = 'none'
  }
  card.style.display = 'block'
  card.style.padding = '14px'
  card.style.paddingLeft = '14px'
  card.style.borderRadius = 'var(--lb-radius)'
  card.style.border = '1px solid var(--lb-border-subtle)'
  card.style.borderLeft = `3px solid ${entryTypeColor(ref.type)}`
  card.style.background = 'var(--lb-bg-secondary)'
  card.style.color = 'var(--lb-text-primary)'

  const label = document.createElement('span')
  label.className = 'lb-entry-neighbors-vault-label'
  label.textContent = dirLabel
  label.style.display = 'block'
  label.style.fontFamily = 'var(--lb-font-mono)'
  label.style.fontSize = '10px'
  label.style.letterSpacing = '0.08em'
  label.style.color = 'var(--lb-text-muted)'
  label.style.textTransform = 'uppercase'
  label.style.marginBottom = '4px'
  card.appendChild(label)

  const preview = document.createElement('div')
  preview.className = 'lb-entry-neighbors-vault-preview'
  preview.textContent = ref.preview
  preview.style.fontSize = 'var(--lb-font-size-meta)'
  preview.style.lineHeight = '1.45'
  preview.style.color = 'var(--lb-text-secondary)'
  // 2-line clamp via the standard line-clamp recipe.
  preview.style.display = '-webkit-box'
  preview.style.setProperty('-webkit-line-clamp', '2')
  preview.style.setProperty('-webkit-box-orient', 'vertical')
  preview.style.overflow = 'hidden'
  card.appendChild(preview)

  return card
}

function topicCard(neighbor: EntryTopicNeighbor): HTMLElement {
  const card = document.createElement(neighbor.href ? 'a' : 'div')
  card.className = 'lb-entry-neighbors-topic-card'
  if (neighbor.href) {
    card.setAttribute('href', neighbor.href)
    card.style.textDecoration = 'none'
  }
  // `<a>` defaults to `display: inline` -- without `block` the padding /
  // border do not form a card box. Same recipe as neighborCard above.
  card.style.display = 'block'
  card.style.padding = '12px 14px'
  card.style.border = '1px solid var(--lb-border-subtle)'
  card.style.borderLeft = `3px solid ${entryTypeColor(neighbor.type)}`
  card.style.borderRadius = '0 var(--lb-radius) var(--lb-radius) 0'
  card.style.background = 'var(--lb-bg-secondary)'
  card.style.marginBottom = '8px'
  const preview = document.createElement('div')
  preview.textContent = neighbor.preview
  preview.style.fontSize = 'var(--lb-font-size-small)'
  preview.style.lineHeight = '1.45'
  preview.style.color = 'var(--lb-text-primary)'
  // 2-line clamp -- match neighborCard. Prevents the topic-card row from
  // truncating mid-preview with a horizontal ellipsis (which read as broken).
  preview.style.display = '-webkit-box'
  preview.style.setProperty('-webkit-line-clamp', '2')
  preview.style.setProperty('-webkit-box-orient', 'vertical')
  preview.style.overflow = 'hidden'
  card.appendChild(preview)
  const meta = document.createElement('div')
  meta.style.display = 'flex'
  meta.style.alignItems = 'center'
  meta.style.gap = '6px'
  meta.style.marginTop = '4px'
  const handle = document.createElement('span')
  handle.textContent = `@${neighbor.publisherHandle}`
  handle.style.fontSize = '11px'
  handle.style.color = 'var(--lb-text-secondary)'
  meta.appendChild(handle)
  meta.appendChild(renderAttributionChip({ kind: neighbor.publisherKind }))
  const vault = document.createElement('span')
  vault.textContent = neighbor.vaultTitle
  vault.style.fontSize = '11px'
  vault.style.color = 'var(--lb-text-dim)'
  vault.style.overflow = 'hidden'
  vault.style.textOverflow = 'ellipsis'
  vault.style.whiteSpace = 'nowrap'
  meta.appendChild(vault)
  card.appendChild(meta)
  return card
}

/**
 * entry-neighbors-band -- Two bands at the foot of an entry deep-link.
 *
 * One walks the vault (prev/next), one walks the topic across the
 * network.
 *
 * Anatomy:
 *   Two stacked sections, each with an eyebrow heading and a row of
 *   cards. In-vault row is a 2-up grid (prev | next) of full cards with
 *   a 3px left rail keyed to entry-type color. When only one in-vault
 *   neighbor exists, the row collapses to a single full-width card.
 *   Cross-vault row stacks topic cards vertically; each topic card uses
 *   the same 3px entry-type left border accent.
 *
 * Tokens:
 *   Card chrome: --lb-border-subtle + --lb-radius + --lb-bg-secondary.
 *   Preview text: --lb-text-secondary on in-vault, --lb-text-primary on
 *   topic. Eyebrow label: --lb-text-muted in mono via --lb-font-mono.
 *   Entry-type rail: --lb-entry-* family.
 *
 * States:
 *   Card hovers to --lb-card-elevated (host-controlled). Label inside
 *   ("Prev", "Next") stays muted.
 *
 * Density:
 *   Two cards in the in-vault row (prev / next). 2-3 cards in the
 *   cross-vault row.
 *
 * Content:
 *   Each card shows the entry preview (clamped to ~160 chars by host)
 *   with a labeled prefix ("Prev", "Next", or "@publisher" + vault for
 *   cross-vault).
 *
 * A11y:
 *   Each card carries an aria-label encoding entry id; clickable region
 *   is the full card (anchor when href present, span otherwise).
 *
 * Motion:
 *   150ms hover transition (host CSS). No card-flip or slide.
 *
 * Responsiveness:
 *   Sections and rows stack vertically below 600px via host CSS.
 *
 * Composition:
 *   Bottom of entry deep-link (3.5 in the surface inventory).
 *   Nowhere else.
 *
 * Variants:
 *   None.
 *
 * Data:
 *   { vaultPrev?: EntryNeighborRef | null,
 *     vaultNext?: EntryNeighborRef | null,
 *     topicTag?: string | null,
 *     topicNeighbors?: EntryTopicNeighbor[] }
 *
 * Boundaries:
 *   When there are no in-vault neighbors (entry is alone), the in-vault
 *   row is suppressed entirely -- never render a half-band.
 *
 * Anti-patterns:
 *   Never inject "more from publisher" content here -- that's a separate
 *   atom (related entries by same author), not implemented in this round.
 */
export function renderEntryNeighborsBand(
  data: EntryNeighborsBandData,
  _options: Partial<PrimitiveOptions> = {}
): HTMLElement {
  const root = document.createElement('div')
  root.className = 'lb-entry-neighbors-band'

  const hasVaultNeighbors = data.vaultPrev != null || data.vaultNext != null
  if (hasVaultNeighbors) {
    const sec = document.createElement('section')
    sec.className = 'lb-entry-neighbors-vault-section'
    sec.style.marginBottom = '24px'
    const heading = document.createElement('h3')
    heading.textContent = 'In this vault'
    heading.style.margin = '0 0 12px'
    heading.style.fontSize = 'var(--lb-font-size-small)'
    heading.style.fontWeight = '700'
    heading.style.textTransform = 'uppercase'
    heading.style.letterSpacing = '0.06em'
    heading.style.color = 'var(--lb-text-secondary)'
    sec.appendChild(heading)

    // 2-up grid for prev/next. When only one neighbor exists, it occupies
    // the single column at full width. Matches the kit's `.neighbors-row`
    // (DesignSystem/ui_kits/ink/views-reader.jsx:470, ink.css:789-792).
    const row = document.createElement('div')
    row.className = 'lb-entry-neighbors-vault-row'
    row.style.display = 'grid'
    const bothPresent = data.vaultPrev != null && data.vaultNext != null
    row.style.gridTemplateColumns = bothPresent ? '1fr 1fr' : '1fr'
    row.style.gap = '12px'
    if (data.vaultPrev) row.appendChild(neighborCard(data.vaultPrev, '← prev'))
    if (data.vaultNext) row.appendChild(neighborCard(data.vaultNext, 'next →'))
    sec.appendChild(row)
    root.appendChild(sec)
  }

  const hasTopicNeighbors =
    data.topicTag && data.topicNeighbors && data.topicNeighbors.length > 0
  if (hasTopicNeighbors) {
    const sec = document.createElement('section')
    sec.className = 'lb-entry-neighbors-topic-section'
    const heading = document.createElement('h3')
    heading.style.margin = '0 0 8px'
    heading.style.fontSize = 'var(--lb-font-size-small)'
    heading.style.fontWeight = '700'
    heading.style.textTransform = 'uppercase'
    heading.style.letterSpacing = '0.06em'
    heading.style.color = 'var(--lb-text-secondary)'
    const tagSpan = document.createElement('span')
    tagSpan.textContent = `#${data.topicTag}`
    tagSpan.style.color = 'var(--lb-signal-positive)'
    heading.textContent = 'More on '
    heading.appendChild(tagSpan)
    sec.appendChild(heading)
    for (const neighbor of data.topicNeighbors!) {
      sec.appendChild(topicCard(neighbor))
    }
    root.appendChild(sec)
  }

  return root
}
