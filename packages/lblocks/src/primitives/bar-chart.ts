import type { BarChartData } from '../types/primitives.js'
import type { PrimitiveOptions } from '../types/primitives.js'

export function renderBarChart(data: BarChartData, options: PrimitiveOptions): HTMLElement {
  const container = document.createElement('div')
  container.className = 'lb-bar-chart'

  for (const bar of data.bars) {
    const row = document.createElement('div')
    row.className = 'lb-bar-row'
    row.style.display = 'flex'
    row.style.alignItems = 'center'
    row.style.gap = '8px'
    row.style.marginBottom = '4px'

    const hasAction = Boolean(bar.action && options.onAction)

    if (hasAction && bar.action) {
      row.style.cursor = 'pointer'
      row.dataset.action = bar.action.type
      row.addEventListener('click', () => {
        options.onAction!({ type: bar.action!.type, target: bar.action!.target, source: 'bar-chart' })
      })
    }

    // Label
    const label = document.createElement('div')
    label.className = 'lb-bar-label'
    label.textContent = bar.label
    label.style.width = '80px'
    label.style.textAlign = 'right'
    label.style.fontSize = 'var(--lb-font-size-body)'
    label.style.color = bar.action ? 'var(--lb-accent)' : 'var(--lb-text-secondary)'
    row.appendChild(label)

    // Track
    const track = document.createElement('div')
    track.className = 'lb-bar-track'
    track.style.flex = '1'
    track.style.backgroundColor = 'var(--lb-bg-secondary)'
    track.style.height = '14px'
    track.style.borderRadius = '2px'
    track.style.overflow = 'hidden'

    // Fill
    const fill = document.createElement('div')
    fill.className = 'lb-bar-fill'
    const maxValue = bar.maxValue ?? 0
    const pct = maxValue > 0 ? Math.min(100, (bar.value / maxValue) * 100) : 0
    fill.style.width = `${pct}%`
    fill.style.height = '100%'
    fill.style.backgroundColor = 'var(--lb-accent)'
    if (bar.action) {
      fill.style.opacity = '0.5'
    }
    track.appendChild(fill)
    row.appendChild(track)

    // Value
    const valueEl = document.createElement('div')
    valueEl.className = 'lb-bar-value'
    valueEl.style.color = 'var(--lb-text-secondary)'
    valueEl.style.fontSize = 'var(--lb-font-size-body)'
    valueEl.style.textAlign = 'right'
    valueEl.style.flexShrink = '0'

    if (bar.suffix) {
      valueEl.style.width = '64px'
      valueEl.textContent = `${bar.value} ${bar.suffix}`
    } else {
      valueEl.style.width = '28px'
      valueEl.textContent = String(bar.value)
    }

    row.appendChild(valueEl)
    container.appendChild(row)
  }

  return container
}
