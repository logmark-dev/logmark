import type { PrimitiveOptions } from '../types/primitives.js'
import type { TypeStripData } from '../types/reader.js'

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

export function renderTypeStrip(
  data: TypeStripData,
  options: Partial<PrimitiveOptions> = {}
): HTMLElement {
  const root = document.createElement('div')
  root.className = 'lb-type-strip'
  root.style.display = 'flex'
  root.style.flexWrap = 'wrap'
  root.style.gap = '6px'

  for (const item of data.types) {
    const color = entryTypeColor(item.type)
    const chip = document.createElement('button')
    chip.type = 'button'
    chip.className = item.selected ? 'lb-type-strip-item selected' : 'lb-type-strip-item'
    chip.dataset.type = item.type
    chip.setAttribute('aria-pressed', item.selected ? 'true' : 'false')
    chip.style.display = 'inline-flex'
    chip.style.alignItems = 'center'
    chip.style.gap = '6px'
    chip.style.padding = options.density === 'compact' ? '4px 8px' : '6px 10px'
    chip.style.border = item.selected ? `1px solid ${color}` : '1px solid var(--lb-border)'
    chip.style.borderRadius = 'var(--lb-radius)'
    chip.style.background = item.selected
      ? `color-mix(in srgb, ${color} 18%, var(--lb-bg-secondary))`
      : 'var(--lb-bg-secondary)'
    chip.style.color = item.selected ? 'var(--lb-text-primary)' : 'var(--lb-text-secondary)'
    chip.style.cursor = options.onAction ? 'pointer' : 'default'

    const dot = document.createElement('span')
    dot.className = 'lb-type-strip-dot'
    dot.style.width = '7px'
    dot.style.height = '7px'
    dot.style.borderRadius = 'var(--lb-radius-full)'
    dot.style.background = color
    chip.appendChild(dot)

    const label = document.createElement('span')
    label.className = 'lb-type-strip-label'
    label.textContent = item.type
    chip.appendChild(label)

    const count = document.createElement('span')
    count.className = 'lb-type-strip-count'
    count.textContent = String(item.count)
    count.style.opacity = '0.75'
    chip.appendChild(count)

    if (options.onAction) {
      chip.addEventListener('click', () => {
        options.onAction?.({
          type: item.actionType ?? 'type-filter',
          target: item.target ?? item.type,
          source: 'type-strip',
        })
      })
    }

    root.appendChild(chip)
  }

  return root
}
