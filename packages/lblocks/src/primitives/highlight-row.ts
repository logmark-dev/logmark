import type { PrimitiveOptions } from '../types/primitives.js'
import type { HighlightRowData, HighlightRowEntry } from '../types/reader.js'

const KIND_COLOR: Record<HighlightRowEntry['kind'], string> = {
  actionable: 'var(--lb-signal-alert)',
  today: 'var(--lb-text-muted)',
  discovery: 'var(--lb-signal-inquiry)',
}

const KIND_BG: Record<HighlightRowEntry['kind'], string> = {
  actionable: 'color-mix(in srgb, var(--lb-signal-alert) 10%, transparent)',
  today: 'color-mix(in srgb, var(--lb-text-muted) 10%, transparent)',
  discovery: 'color-mix(in srgb, var(--lb-signal-inquiry) 10%, transparent)',
}

function renderRow(entry: HighlightRowEntry, options: Partial<PrimitiveOptions>): HTMLElement {
  const row = document.createElement('article')
  row.className = entry.isActive ? 'lb-highlight-row lb-highlight-row-active' : 'lb-highlight-row'
  row.dataset.entryId = entry.id
  row.dataset.kind = entry.kind
  row.setAttribute('aria-label', entry.title)
  row.style.display = 'grid'
  row.style.gridTemplateColumns = entry.actionLabel ? 'minmax(0, 1fr) auto' : 'minmax(0, 1fr)'
  row.style.gap = '12px'
  row.style.alignItems = 'center'
  row.style.borderLeft = `2px solid ${KIND_COLOR[entry.kind]}`
  row.style.borderRadius = '0 var(--lb-radius) var(--lb-radius) 0'
  row.style.background = entry.isActive ? KIND_BG[entry.kind] : 'var(--lb-bg-secondary)'
  row.style.padding = options.density === 'compact' ? '10px 12px' : '12px 16px'
  row.style.margin = '0 0 6px'

  const body = document.createElement('div')
  body.className = 'lb-highlight-row-body'
  body.style.minWidth = '0'
  const title = document.createElement('div')
  title.className = 'lb-highlight-row-title'
  title.textContent = entry.title
  title.style.fontSize = 'var(--lb-font-size-body)'
  title.style.fontWeight = '500'
  title.style.lineHeight = '1.25'
  title.style.color = 'var(--lb-text-primary)'
  title.style.overflow = 'hidden'
  title.style.textOverflow = 'ellipsis'
  title.style.whiteSpace = 'nowrap'
  body.appendChild(title)
  if (entry.sub) {
    const sub = document.createElement('div')
    sub.className = 'lb-highlight-row-sub'
    sub.textContent = entry.sub
    sub.style.marginTop = '2px'
    sub.style.fontSize = 'var(--lb-font-size-small)'
    sub.style.lineHeight = '1.55'
    sub.style.color = 'var(--lb-text-secondary)'
    body.appendChild(sub)
  }
  row.appendChild(body)

  if (entry.actionLabel) {
    const action = document.createElement('button')
    action.type = 'button'
    action.className = 'lb-highlight-row-action'
    action.textContent = entry.actionLabel
    action.style.border = '1px solid color-mix(in srgb, var(--lb-accent) 35%, transparent)'
    action.style.borderRadius = 'var(--lb-radius)'
    action.style.background = 'color-mix(in srgb, var(--lb-accent) 10%, transparent)'
    action.style.color = 'var(--lb-accent)'
    action.style.font = 'inherit'
    action.style.fontSize = 'var(--lb-font-size-small)'
    action.style.fontWeight = '500'
    action.style.padding = '5px 11px'
    action.style.cursor = options.onAction ? 'pointer' : 'default'
    if (options.onAction) {
      action.addEventListener('click', (event) => {
        event.preventDefault()
        event.stopPropagation()
        options.onAction?.({
          type: 'highlight-action',
          target: entry.id,
          source: 'highlight-row',
        })
      })
    }
    row.appendChild(action)
  }

  if (options.onAction) {
    row.setAttribute('role', 'button')
    row.tabIndex = 0
    row.style.cursor = 'pointer'
    const emitFocus = (): void => {
      options.onAction?.({
        type: 'entry-focus',
        target: entry.id,
        source: 'highlight-row',
      })
    }
    row.addEventListener('click', emitFocus)
    row.addEventListener('keydown', (event) => {
      if (event.key !== 'Enter' && event.key !== ' ') return
      event.preventDefault()
      emitFocus()
    })
  }
  return row
}

export function renderHighlightRow(
  data: HighlightRowData,
  options: Partial<PrimitiveOptions> = {}
): HTMLElement {
  const root = document.createElement('div')
  root.className = 'lb-highlight-row-list'
  root.style.display = 'flex'
  root.style.flexDirection = 'column'
  for (const entry of data.entries) {
    root.appendChild(renderRow(entry, options))
  }
  return root
}
