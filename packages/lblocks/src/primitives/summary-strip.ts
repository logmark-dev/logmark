import type { SummaryStripData, SummaryStripMetric } from '../types/reader.js'

function deltaColor(metric: SummaryStripMetric): string {
  if (metric.deltaTone === 'positive') return 'var(--lb-success, var(--lb-accent))'
  if (metric.deltaTone === 'negative' || metric.deltaTone === 'warning')
    return 'var(--lb-warning, var(--lb-accent))'
  return 'var(--lb-text-muted)'
}

export function renderSummaryStrip(data: SummaryStripData): HTMLElement {
  const root = document.createElement('div')
  root.className = 'lb-summary-strip'
  if (data.metrics.length === 0) {
    const empty = document.createElement('p')
    empty.className = 'lb-summary-strip-empty'
    empty.textContent = data.emptyMessage ?? 'No metrics available.'
    empty.style.margin = '0'
    empty.style.color = 'var(--lb-text-muted)'
    empty.style.fontSize = 'var(--lb-font-size-body)'
    root.appendChild(empty)
    return root
  }
  const list = document.createElement('div')
  list.className = 'lb-summary-strip-list'
  list.setAttribute('role', 'list')
  list.style.display = 'grid'
  list.style.gridTemplateColumns = 'repeat(auto-fit, minmax(112px, 1fr))'
  list.style.gap = '8px'
  list.style.minWidth = '0'
  for (const metric of data.metrics) {
    const item = document.createElement('div')
    item.className = 'lb-summary-strip-item'
    item.setAttribute('role', 'listitem')
    item.style.minWidth = '0'
    item.style.padding = '10px 12px'
    item.style.border = '1px solid var(--lb-border-subtle)'
    item.style.borderRadius = 'var(--lb-radius-sm)'
    item.style.background = 'var(--lb-bg-secondary)'
    const label = document.createElement('p')
    label.className = 'lb-summary-strip-label'
    label.textContent = metric.label
    label.style.margin = '0 0 4px'
    label.style.color = 'var(--lb-text-muted)'
    label.style.fontFamily = 'var(--lb-font-mono)'
    label.style.fontSize = 'var(--lb-font-size-label)'
    label.style.textTransform = 'uppercase'
    label.style.letterSpacing = '0.02em'
    item.appendChild(label)
    const value = document.createElement('p')
    value.className = 'lb-summary-strip-value'
    value.textContent = metric.value
    value.style.margin = '0'
    value.style.color = 'var(--lb-text-primary)'
    value.style.fontSize = 'var(--lb-font-size-header)'
    value.style.fontWeight = '600'
    value.style.lineHeight = '1.1'
    item.appendChild(value)
    if (metric.delta) {
      const delta = document.createElement('p')
      delta.className = 'lb-summary-strip-delta'
      delta.textContent = metric.delta
      delta.style.margin = '5px 0 0'
      delta.style.color = deltaColor(metric)
      delta.style.fontSize = 'var(--lb-font-size-label)'
      item.appendChild(delta)
    }
    list.appendChild(item)
  }
  root.appendChild(list)
  return root
}
