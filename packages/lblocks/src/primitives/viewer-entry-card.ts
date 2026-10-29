import type { PrimitiveOptions } from '../types/primitives.js'
import type { ViewerEntryCardData } from '../types/reader.js'
import { parseMarkdown } from '../engine/markdown-parser.js'
import { composeMarkdown } from '../engine/markdown-compositor.js'

const TYPE_LABELS: Record<string, string> = {
  task: 'task',
  idea: 'idea',
  block: 'block',
  decision: 'decision',
  log: 'log',
  question: 'question',
  gap: 'gap',
  analogy: 'analogy',
}

const ENTRY_TYPE_TOKEN: Record<string, string> = {
  log: 'log',
  diary: 'log',
  task: 'task',
  idea: 'idea',
  block: 'block',
  decision: 'decision',
  question: 'question',
  gap: 'gap',
  analogy: 'analogy',
}

function entryTypeColor(type: string): string {
  return `var(--lb-entry-${ENTRY_TYPE_TOKEN[type.toLowerCase()] ?? 'log'})`
}

function formatDate(timestamp: string): string {
  const date = new Date(timestamp)
  if (Number.isNaN(date.getTime())) return timestamp
  return date.toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' })
}

export function renderViewerEntryCard(
  data: ViewerEntryCardData,
  options: Partial<PrimitiveOptions> = {}
): HTMLElement {
  const root = document.createElement('article')
  root.className = `lb-viewer-entry-card lb-viewer-entry-card-${data.entryType}`
  root.dataset.entryId = data.id
  root.style.display = 'grid'
  root.style.gridTemplateColumns = '4px minmax(0, 1fr)'
  root.style.gap = '10px'
  root.style.padding = options.density === 'compact' ? '8px 10px' : '10px 12px'
  root.style.border = '1px solid var(--lb-border)'
  root.style.borderRadius = 'var(--lb-radius)'
  root.style.background = 'var(--lb-bg-secondary)'
  root.style.cursor = options.onAction ? 'pointer' : 'default'

  const color = entryTypeColor(data.entryType)

  const rail = document.createElement('div')
  rail.className = 'lb-viewer-entry-card-rail'
  rail.style.borderRadius = 'var(--lb-radius-sm)'
  rail.style.background = color
  root.appendChild(rail)

  const body = document.createElement('div')
  body.className = 'lb-viewer-entry-card-body'
  body.style.minWidth = '0'

  const meta = document.createElement('div')
  meta.className = 'lb-viewer-entry-card-meta'
  meta.style.display = 'flex'
  meta.style.flexWrap = 'wrap'
  meta.style.gap = '8px'
  meta.style.fontSize = 'var(--lb-font-size-small)'
  meta.style.color = 'var(--lb-text-muted)'

  const type = document.createElement('span')
  type.textContent = TYPE_LABELS[data.entryType] ?? data.entryType
  type.style.color = color
  meta.appendChild(type)

  const time = document.createElement('time')
  time.dateTime = data.timestamp
  time.textContent = formatDate(data.timestamp)
  meta.appendChild(time)

  if (data.route) {
    const route = document.createElement('span')
    route.textContent = data.route
    meta.appendChild(route)
  }

  for (const tag of data.tags) {
    const chip = document.createElement(options.onTagClick ? 'button' : 'span')
    chip.textContent = `#${tag}`
    chip.className = 'lb-viewer-entry-card-tag'
    chip.style.border = '0'
    chip.style.background = 'transparent'
    chip.style.color = 'var(--lb-signal-positive)'
    chip.style.font = 'inherit'
    chip.style.padding = '0'
    if (options.onTagClick) {
      chip.setAttribute('type', 'button')
      chip.setAttribute('aria-label', `Filter by #${tag}`)
      chip.setAttribute('title', `Filter by #${tag}`)
      chip.style.cursor = 'pointer'
      chip.addEventListener('click', (ev) => {
        ev.preventDefault()
        ev.stopPropagation()
        options.onTagClick?.(tag)
      })
    }
    meta.appendChild(chip)
  }

  body.appendChild(meta)

  const content = composeMarkdown({
    version: 1,
    blocks: parseMarkdown(data.content),
  }, {
    theme: options.theme ?? 'dark',
    onLinkClick: options.onLinkClick,
    onTagClick: options.onTagClick,
  })
  content.classList.add('lb-viewer-entry-card-content')
  content.style.maxWidth = 'none'
  content.style.marginTop = '6px'
  content.style.padding = '0'
  content.style.backgroundColor = 'transparent'
  body.appendChild(content)

  root.appendChild(body)

  if (options.onAction) {
    root.addEventListener('click', () => {
      options.onAction?.({
        type: 'entry-focus',
        target: data.id,
        source: 'viewer-entry-card',
      })
    })
  }

  return root
}
