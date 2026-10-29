import type { PrimitiveOptions } from '../types/primitives.js'
import type { ViewerEntryListData } from '../types/reader.js'
import { renderViewerEntryCard } from './viewer-entry-card.js'

export function renderViewerEntryList(
  data: ViewerEntryListData,
  options: Partial<PrimitiveOptions> = {}
): HTMLElement {
  const root = document.createElement('div')
  root.className = 'lb-viewer-entry-list'
  root.style.display = 'flex'
  root.style.flexDirection = 'column'
  root.style.gap = options.density === 'compact' ? '6px' : '8px'

  if (data.title) {
    const title = document.createElement('h3')
    title.className = 'lb-viewer-entry-list-title'
    title.textContent = data.title
    title.style.margin = '0 0 2px'
    title.style.fontSize = '13px'
    title.style.fontWeight = '700'
    title.style.color = 'var(--lb-text-secondary)'
    root.appendChild(title)
  }

  if (data.entries.length === 0) {
    const empty = document.createElement('div')
    empty.className = 'lb-viewer-entry-list-empty'
    empty.textContent = data.emptyMessage ?? 'No entries'
    empty.style.padding = '12px'
    empty.style.color = 'var(--lb-text-muted)'
    empty.style.border = '1px solid var(--lb-border)'
    empty.style.borderRadius = '8px'
    root.appendChild(empty)
    return root
  }

  for (const entry of data.entries) {
    root.appendChild(renderViewerEntryCard(entry, options))
  }

  return root
}
