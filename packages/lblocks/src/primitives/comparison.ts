import type { ComparisonData } from '../types/primitives.js'
import type { PrimitiveOptions } from '../types/primitives.js'

export function renderComparison(data: ComparisonData, _options: PrimitiveOptions): HTMLElement {
  const container = document.createElement('div')
  container.className = 'lb-comparison'
  container.style.display = 'flex'
  container.style.alignItems = 'center'
  container.style.gap = '6px'

  const text = document.createElement('span')
  text.className = 'lb-comparison-text'
  text.textContent = data.text
  text.style.color = 'var(--lb-text-secondary)'
  text.style.fontSize = 'var(--lb-font-size-body)'

  const delta = document.createElement('span')
  delta.className = 'lb-comparison-delta'
  delta.style.fontSize = 'var(--lb-font-size-body)'

  if (data.direction === 'up') {
    delta.textContent = `(+${data.delta}%)`
    delta.style.color = 'var(--lb-signal-positive)'
  } else if (data.direction === 'down') {
    delta.textContent = `(-${data.delta}%)`
    delta.style.color = 'var(--lb-signal-negative)'
  } else {
    delta.textContent = `(0%)`
    delta.style.color = 'var(--lb-text-muted)'
  }

  container.appendChild(text)
  container.appendChild(delta)
  return container
}
