import type { PrimitiveOptions } from '../types/primitives.js'
import type { BacklinkListData } from '../types/reader.js'

export function renderBacklinkList(
  data: BacklinkListData,
  options: Partial<PrimitiveOptions> = {}
): HTMLElement {
  const root = document.createElement('div')
  root.className = 'lb-backlink-list'
  root.style.display = 'flex'
  root.style.flexDirection = 'column'
  root.style.gap = '6px'

  if (data.backlinks.length === 0) {
    const empty = document.createElement('div')
    empty.className = 'lb-backlink-list-empty'
    empty.textContent = 'No backlinks'
    empty.style.color = 'var(--lb-text-muted)'
    empty.style.padding = '10px 0'
    root.appendChild(empty)
    return root
  }

  for (const backlink of data.backlinks) {
    const row = document.createElement('div')
    row.className = 'lb-backlink-list-item'
    row.dataset.backlinkId = backlink.id
    row.style.padding = options.density === 'compact' ? '8px 10px' : '10px 12px'
    row.style.border = '1px solid var(--lb-border)'
    row.style.borderRadius = 'var(--lb-radius)'
    row.style.background = 'var(--lb-bg-secondary)'
    row.style.cursor = options.onAction ? 'pointer' : 'default'
    row.style.outline = 'none'

    const emitOpen = () => {
      options.onAction?.({
        type: 'backlink-open',
        target: backlink.id,
        source: 'backlink-list',
      })
    }

    const label = document.createElement('div')
    label.className = 'lb-backlink-list-label'
    label.textContent = backlink.label
    label.style.fontWeight = '600'
    label.style.color = 'var(--lb-text-primary)'
    row.appendChild(label)

    const meta = document.createElement('div')
    meta.className = 'lb-backlink-list-meta'
    meta.textContent = backlink.sourceType
    meta.style.fontSize = '12px'
    meta.style.color = 'var(--lb-text-muted)'
    row.appendChild(meta)

    if (backlink.excerpt) {
      const excerpt = document.createElement('div')
      excerpt.className = 'lb-backlink-list-excerpt'
      excerpt.textContent = backlink.excerpt
      excerpt.style.marginTop = '4px'
      excerpt.style.color = 'var(--lb-text-secondary)'
      row.appendChild(excerpt)
    }

    if (options.onAction) {
      row.setAttribute('role', 'button')
      row.tabIndex = 0
      row.addEventListener('click', emitOpen)
      row.addEventListener('keydown', (event) => {
        if (event.key !== 'Enter' && event.key !== ' ') return
        event.preventDefault()
        emitOpen()
      })
    }

    root.appendChild(row)
  }

  return root
}
