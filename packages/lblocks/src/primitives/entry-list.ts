import type { EntryListData } from '../types/primitives.js'

export function renderEntryList(data: EntryListData): HTMLElement {
  const root = document.createElement('div')
  root.className = 'lb-entry-list'

  if (data.items.length === 0) {
    const empty = document.createElement('div')
    empty.className = 'lb-entry-list-empty'
    empty.textContent = data.emptyMessage ?? 'No items'
    empty.style.padding = '12px'
    empty.style.opacity = '0.6'
    root.appendChild(empty)
    return root
  }

  for (const item of data.items) {
    const row = document.createElement('div')
    row.className = 'lb-entry-list-item'
    row.dataset.id = item.id
    row.style.padding = '10px 12px'
    row.style.borderBottom = '1px solid var(--lb-border, rgba(255,255,255,0.06))'
    row.style.cursor = data.onItemClick ? 'pointer' : 'default'

    const top = document.createElement('div')
    top.className = 'lb-entry-list-title'
    top.style.fontWeight = '600'
    top.textContent = item.title
    if (item.badge) {
      const badge = document.createElement('span')
      badge.className = 'lb-entry-list-badge'
      badge.textContent = item.badge
      badge.style.marginLeft = '8px'
      badge.style.fontSize = '0.8em'
      badge.style.opacity = '0.7'
      top.appendChild(badge)
    }
    row.appendChild(top)

    if (item.subtitle) {
      const sub = document.createElement('div')
      sub.className = 'lb-entry-list-subtitle'
      sub.style.fontSize = '0.9em'
      sub.style.opacity = '0.75'
      sub.textContent = item.subtitle
      row.appendChild(sub)
    }
    if (item.meta) {
      const meta = document.createElement('div')
      meta.className = 'lb-entry-list-meta'
      meta.style.fontSize = '0.8em'
      meta.style.opacity = '0.5'
      meta.textContent = item.meta
      row.appendChild(meta)
    }

    if (data.onItemClick) {
      const handler = data.onItemClick
      row.addEventListener('click', () => handler(item.id))
    }
    root.appendChild(row)
  }
  return root
}
