import type { PrimitiveOptions } from '../types/primitives.js'
import type { TypeDistributionBarData } from '../types/reader.js'
import { entryTypeColor } from '../util/entry-type-tokens.js'

export function renderTypeDistributionBar(
  data: TypeDistributionBarData,
  options: Partial<PrimitiveOptions> = {}
): HTMLElement {
  const root = document.createElement('div')
  root.className = 'lb-type-distribution-bar'
  root.style.display = 'flex'
  root.style.flexDirection = 'column'
  root.style.gap = '8px'

  const track = document.createElement('div')
  track.className = 'lb-type-distribution-bar-track'
  track.setAttribute('role', 'group')
  track.setAttribute('aria-label', 'Entry type distribution')
  track.style.display = 'flex'
  track.style.height = '8px'
  track.style.overflow = 'hidden'
  track.style.borderRadius = 'var(--lb-radius-sm)'
  track.style.background = 'var(--lb-bg-tertiary)'
  for (const segment of data.segments) {
    const active = data.activeType === segment.type
    const color = entryTypeColor(segment.type)
    const button = document.createElement('button')
    button.type = 'button'
    button.className = active
      ? 'lb-type-distribution-bar-segment lb-type-distribution-bar-segment-active'
      : 'lb-type-distribution-bar-segment'
    button.dataset.type = segment.type
    button.setAttribute('aria-label', `${segment.type}: ${segment.count}`)
    button.setAttribute('aria-pressed', active ? 'true' : 'false')
    button.style.flexGrow = String(Math.max(0, segment.count))
    button.style.flexBasis = '0'
    button.style.minWidth = segment.count > 0 ? '2px' : '0'
    button.style.border = '0'
    button.style.background = color
    button.style.opacity = active ? '1' : '0.7'
    button.style.boxShadow = active ? 'inset 0 0 0 1px var(--lb-text-primary)' : 'none'
    button.style.cursor = options.onAction ? 'pointer' : 'default'
    if (options.onAction) {
      button.addEventListener('click', () => {
        options.onAction?.({
          type: 'type-filter',
          target: segment.type,
          source: 'type-distribution-bar',
        })
      })
    }
    track.appendChild(button)
  }
  root.appendChild(track)

  const legend = document.createElement('div')
  legend.className = 'lb-type-distribution-bar-legend'
  legend.setAttribute('role', 'group')
  legend.setAttribute('aria-label', 'Tag type filters')
  legend.style.display = 'flex'
  legend.style.flexWrap = 'wrap'
  legend.style.gap = '8px 12px'
  for (const segment of data.segments) {
    const active = data.activeType === segment.type
    const color = entryTypeColor(segment.type)
    const item = document.createElement('button')
    item.type = 'button'
    item.className = active
      ? 'lb-type-distribution-bar-legend-item lb-type-distribution-bar-legend-item-active'
      : 'lb-type-distribution-bar-legend-item'
    item.dataset.type = segment.type
    item.setAttribute('aria-pressed', active ? 'true' : 'false')
    item.style.display = 'inline-flex'
    item.style.alignItems = 'center'
    item.style.gap = '6px'
    item.style.border = active ? '1px solid var(--lb-accent)' : '1px solid transparent'
    item.style.borderRadius = 'var(--lb-radius)'
    item.style.background = active
      ? 'color-mix(in srgb, var(--lb-accent) 12%, transparent)'
      : 'transparent'
    item.style.color = 'var(--lb-text-secondary)'
    item.style.padding = '2px 5px'
    item.style.fontSize = 'var(--lb-font-size-label)'
    item.style.cursor = options.onAction ? 'pointer' : 'default'
    const dot = document.createElement('span')
    dot.className = 'lb-type-distribution-bar-dot'
    dot.setAttribute('aria-hidden', 'true')
    dot.style.width = '6px'
    dot.style.height = '6px'
    dot.style.borderRadius = 'var(--lb-radius-full)'
    dot.style.background = color
    item.appendChild(dot)
    const label = document.createElement('span')
    label.textContent = segment.type
    item.appendChild(label)
    const count = document.createElement('span')
    count.textContent = String(segment.count)
    count.style.fontFamily = 'var(--lb-font-mono)'
    count.style.color = 'var(--lb-text-primary)'
    item.appendChild(count)
    if (options.onAction) {
      item.addEventListener('click', () => {
        options.onAction?.({
          type: 'type-filter',
          target: segment.type,
          source: 'type-distribution-bar',
        })
      })
    }
    legend.appendChild(item)
  }
  root.appendChild(legend)
  return root
}
