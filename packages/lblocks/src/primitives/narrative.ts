import type { NarrativeData } from '../types/primitives.js'
import type { PrimitiveOptions } from '../types/primitives.js'

export function renderNarrative(data: NarrativeData, _options: PrimitiveOptions): HTMLElement {
  const container = document.createElement('div')
  container.className = 'lb-narrative'

  if (data.badge) {
    const header = document.createElement('div')
    header.className = 'lb-narrative-header'
    header.style.display = 'flex'
    header.style.alignItems = 'center'
    header.style.marginBottom = '4px'

    const badge = document.createElement('span')
    badge.className = 'lb-narrative-badge'
    badge.textContent = data.badge
    badge.style.fontSize = '9px'
    badge.style.backgroundColor = 'var(--lb-bg-secondary)'
    badge.style.color = 'var(--lb-signal-inquiry)'
    badge.style.padding = '2px 6px'
    badge.style.borderRadius = '3px'

    header.appendChild(badge)
    container.appendChild(header)
  }

  const text = document.createElement('div')
  text.className = 'lb-narrative-text'
  text.textContent = data.text
  text.style.fontSize = 'var(--lb-font-size-body)'
  text.style.lineHeight = '1.5'
  text.style.color = 'var(--lb-text-primary)'

  container.appendChild(text)
  return container
}
