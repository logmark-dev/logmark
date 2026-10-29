import type { PrimitiveOptions } from '../types/primitives.js'
import type { TopicRailData } from '../types/reader.js'

function makeNavEl(
  url: string | null,
  label: string,
  ariaLabel: string,
  navKey: 'prev' | 'next' | 'exit',
  disabled: boolean,
  topic: string,
  actionType: string,
  options: Partial<PrimitiveOptions>
): HTMLElement {
  let el: HTMLAnchorElement | HTMLButtonElement
  if (url && !disabled) {
    el = document.createElement('a')
    el.href = url
    el.setAttribute('aria-label', ariaLabel)
  } else {
    el = document.createElement('button')
    el.type = 'button'
    el.setAttribute('aria-label', ariaLabel)
    if (disabled) {
      el.disabled = true
      el.setAttribute('aria-disabled', 'true')
    }
  }
  el.className = `lb-topic-rail-nav lb-topic-rail-nav-${navKey}`
  el.dataset.nav = navKey
  el.textContent = label
  el.style.display = 'inline-flex'
  el.style.alignItems = 'center'
  el.style.justifyContent = 'center'
  el.style.padding = '2px 8px'
  el.style.fontSize = 'var(--lb-font-size-small)'
  el.style.fontWeight = '500'
  el.style.border = '1px solid var(--lb-border)'
  el.style.borderRadius = 'var(--lb-radius)'
  el.style.background = 'transparent'
  el.style.color = disabled ? 'var(--lb-text-dim)' : 'var(--lb-text-secondary)'
  el.style.cursor = disabled ? 'default' : 'pointer'
  el.style.textDecoration = 'none'
  if (!disabled && actionType && options.onAction) {
    el.addEventListener('click', (event) => {
      if (!url) event.preventDefault()
      options.onAction?.({ type: actionType, target: topic, source: 'topic-rail' })
    })
  }
  return el
}

/**
 * topic-rail -- Conditional reader chrome.
 *
 * Appears when ?topic=<tag> is set on the vault reader; walks the
 * filtered set without leaving the vault.
 *
 * Anatomy:
 *   Tag link (amber) · counter (mono) · prev/next buttons · exit (x) button.
 *
 * Tokens:
 *   Background: --lb-bg-secondary (host can layer 90deg gradient from
 *   --accent-tinted-bg to a 35% workspace mix). Border bottom:
 *   --lb-border. Tag chip: --lb-signal-positive. Counter: --lb-text-secondary
 *   with tabular numerals. Nav buttons: --lb-text-secondary border,
 *   --lb-text-dim when disabled.
 *
 * States:
 *   Prev/next/exit hover -- background warms, color shifts to text-primary.
 *   Disabled state for prev at index 0, next at end -- opacity 0.4,
 *   cursor not-allowed (host CSS).
 *
 * Density:
 *   Single size. Padding 6px 12px.
 *
 * Content:
 *   Tag link reads "#tagname". Counter reads "N of M". Buttons: less-than,
 *   greater-than, multiplication-sign glyphs with aria-labels.
 *
 * A11y:
 *   role="navigation", aria-label "Topic navigation: #${tag}". Counter is
 *   aria-live="polite" so prev/next changes announce.
 *
 * Motion:
 *   Position changes are instant; no slide. Only the underlying
 *   selected-entry panel animates.
 *
 * Responsiveness:
 *   Flex-wrap is set so the rail breaks to two lines on viewports under
 *   600px (tag + counter row 1, buttons row 2).
 *
 * Composition:
 *   Above the reader lens tabs. Below the breadcrumb. Always full-width
 *   of the reader main column.
 *
 * Variants:
 *   None. The rail is one thing.
 *
 * Data:
 *   { topic: string, current: number, total: number,
 *     hasPrev: boolean, hasNext: boolean,
 *     prevUrl?: string | null, nextUrl?: string | null,
 *     exitUrl?: string | null }
 *
 * Boundaries:
 *   Only renders when topic filter is active. Never persists across
 *   navigation away from the vault reader.
 *
 * Anti-patterns:
 *   Don't use the rail to filter by anything other than #tag -- type
 *   filters, date filters, and route filters belong inside the lens UI,
 *   not in chrome.
 */
export function renderTopicRail(
  data: TopicRailData,
  options: Partial<PrimitiveOptions> = {}
): HTMLElement {
  const root = document.createElement('div')
  root.className = 'lb-topic-rail'
  root.setAttribute('role', 'navigation')
  root.setAttribute('aria-label', `Topic navigation: #${data.topic}`)
  root.style.display = 'flex'
  root.style.alignItems = 'center'
  root.style.gap = '8px'
  root.style.padding = '6px 12px'
  root.style.background = 'var(--lb-bg-secondary)'
  root.style.borderBottom = '1px solid var(--lb-border)'
  root.style.fontSize = 'var(--lb-font-size-small)'
  root.style.flexWrap = 'wrap'

  // Topic chip
  const chip = document.createElement('span')
  chip.className = 'lb-topic-rail-chip'
  chip.textContent = `#${data.topic}`
  chip.style.fontWeight = '600'
  chip.style.color = 'var(--lb-signal-positive)'
  chip.style.marginRight = '4px'
  root.appendChild(chip)

  // Divider
  const divider = document.createElement('span')
  divider.className = 'lb-topic-rail-divider'
  divider.textContent = '·'
  divider.setAttribute('aria-hidden', 'true')
  divider.style.color = 'var(--lb-text-dim)'
  root.appendChild(divider)

  // Position counter
  const counter = document.createElement('span')
  counter.className = 'lb-topic-rail-counter'
  counter.textContent = `${data.current} of ${data.total}`
  counter.setAttribute('aria-live', 'polite')
  counter.style.color = 'var(--lb-text-secondary)'
  counter.style.fontVariantNumeric = 'tabular-nums'
  root.appendChild(counter)

  // Spacer
  const spacer = document.createElement('span')
  spacer.style.flex = '1'
  root.appendChild(spacer)

  // Prev button
  const prevEl = makeNavEl(
    data.prevUrl ?? null,
    '‹',
    `Previous entry on #${data.topic}`,
    'prev',
    !data.hasPrev,
    data.topic,
    'topic-rail-prev',
    options
  )
  root.appendChild(prevEl)

  // Next button
  const nextEl = makeNavEl(
    data.nextUrl ?? null,
    '›',
    `Next entry on #${data.topic}`,
    'next',
    !data.hasNext,
    data.topic,
    'topic-rail-next',
    options
  )
  root.appendChild(nextEl)

  // Exit button
  const exitEl = makeNavEl(
    data.exitUrl ?? null,
    '×',
    `Exit topic #${data.topic}`,
    'exit',
    false,
    data.topic,
    'topic-rail-exit',
    options
  )
  exitEl.style.marginLeft = '4px'
  root.appendChild(exitEl)

  return root
}
