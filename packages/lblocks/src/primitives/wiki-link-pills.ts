import type { PrimitiveOptions } from '../types/primitives.js'
import type { WikiLinkPillsData, WikiLinkPillItem } from '../types/reader.js'

const SOURCE = 'wiki-link-pills'

function emit(options: Partial<PrimitiveOptions>, target: string): void {
  options.onAction?.({ type: 'page-open', target, source: SOURCE })
}

function renderPill(item: WikiLinkPillItem, options: Partial<PrimitiveOptions>): HTMLElement {
  const button = document.createElement('button')
  button.type = 'button'
  button.className = 'lb-wiki-link-pill'
  button.dataset.target = item.target
  button.setAttribute('aria-label', `Open page ${item.label}`)
  button.style.display = 'inline-grid'
  button.style.gridTemplateColumns = 'minmax(0, 1fr) auto'
  button.style.alignItems = 'center'
  button.style.gap = '8px'
  button.style.maxWidth = '100%'
  button.style.minHeight = options.density === 'compact' ? '28px' : '34px'
  button.style.padding = options.density === 'compact' ? '4px 8px' : '6px 10px'
  button.style.border = '1px solid var(--lb-border)'
  button.style.borderRadius = 'var(--lb-radius-sm)'
  button.style.background = 'var(--lb-bg-secondary)'
  button.style.color = 'var(--lb-text-primary)'
  button.style.font = 'inherit'
  button.style.cursor = options.onAction ? 'pointer' : 'default'
  button.style.textAlign = 'left'
  const text = document.createElement('span')
  text.className = 'lb-wiki-link-pill-text'
  text.style.display = 'inline-flex'
  text.style.alignItems = 'center'
  text.style.minWidth = '0'
  const label = document.createElement('span')
  label.className = 'lb-wiki-link-pill-label'
  label.textContent = item.label
  label.style.minWidth = '0'
  label.style.overflow = 'hidden'
  label.style.textOverflow = 'ellipsis'
  label.style.whiteSpace = 'nowrap'
  text.appendChild(label)
  button.appendChild(text)
  if (item.backlinkCount !== null && item.backlinkCount !== undefined) {
    const count = document.createElement('span')
    count.className = 'lb-wiki-link-pill-count'
    count.textContent = String(item.backlinkCount)
    count.setAttribute('aria-label', `${item.backlinkCount} backlinks`)
    count.style.color = 'var(--lb-text-muted)'
    count.style.fontFamily = 'var(--lb-font-mono)'
    count.style.fontSize = 'var(--lb-font-size-label)'
    button.appendChild(count)
  }
  if (item.excerpt) {
    button.title = item.excerpt
  }
  if (options.onAction) {
    button.addEventListener('click', () => emit(options, item.target))
    button.addEventListener('keydown', (event) => {
      if (event.key !== 'Enter' && event.key !== ' ') return
      event.preventDefault()
      emit(options, item.target)
    })
  }
  return button
}

export function renderWikiLinkPills(
  data: WikiLinkPillsData,
  options: Partial<PrimitiveOptions> = {}
): HTMLElement {
  const root = document.createElement('div')
  root.className = 'lb-wiki-link-pills'
  if (data.links.length === 0) {
    const empty = document.createElement('p')
    empty.className = 'lb-wiki-link-pills-empty'
    empty.textContent = data.emptyMessage ?? 'No linked pages.'
    empty.style.margin = '0'
    empty.style.color = 'var(--lb-text-muted)'
    root.appendChild(empty)
    return root
  }
  const list = document.createElement('div')
  list.className = 'lb-wiki-link-pills-list'
  list.setAttribute('role', 'list')
  list.style.display = 'flex'
  list.style.flexWrap = 'wrap'
  list.style.gap = '6px'
  list.style.minWidth = '0'
  for (const item of data.links) {
    const row = document.createElement('span')
    row.className = 'lb-wiki-link-pills-item'
    row.setAttribute('role', 'listitem')
    row.style.minWidth = '0'
    row.appendChild(renderPill(item, options))
    list.appendChild(row)
  }
  root.appendChild(list)
  return root
}
