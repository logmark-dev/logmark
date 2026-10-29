/** Small safe-DOM builders (textContent only, never markup strings). */

export function el<K extends keyof HTMLElementTagNameMap>(
  tag: K,
  className?: string,
  text?: string
): HTMLElementTagNameMap[K] {
  const node = document.createElement(tag)
  if (className) node.className = className
  if (text !== undefined) node.textContent = text
  return node
}

/** `<div class="section-head"><h2>title</h2><span class="section-meta">meta</span></div>` */
export function sectionHead(title: string, meta: string): HTMLElement {
  const head = el('div', 'section-head')
  head.append(el('h2', undefined, title), el('span', 'section-meta', meta))
  return head
}

/** `<div class="{className}"><span class="k">k</span><span class="v">v</span></div>` */
export function kvRow(k: string, v: string, className = 'stat-row'): HTMLElement {
  const row = el('div', className)
  row.append(el('span', 'k', k), el('span', 'v', v))
  return row
}

/** Two-line cell: `<span>primary<br><span class="section-meta">secondary</span></span>` */
export function twoLine(primary: string, secondary: string): HTMLElement {
  const cell = el('span')
  cell.append(document.createTextNode(primary), el('br'), el('span', 'section-meta', secondary))
  return cell
}

/**
 * Truth band for windowed lists (release review P0-4): whenever a view
 * renders fewer entries than exist, say so explicitly. Returns null when
 * the window covers everything (or totals are unknown).
 */
export function windowNote(shown: number, total: number | undefined, noun: string): HTMLElement | null {
  if (total === undefined || shown >= total || shown === 0) return null
  const note = el('p', 'section-meta window-note', `showing the ${shown} most recent of ${total.toLocaleString()} ${noun} - search reaches everything`)
  return note
}
