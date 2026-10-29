import type { LabelValueListData } from '../types/primitives.js'
import type { PrimitiveOptions } from '../types/primitives.js'

export function renderLabelValueList(data: LabelValueListData, options: PrimitiveOptions): HTMLElement {
  const container = document.createElement('div')
  container.className = 'lb-label-value-list'

  for (const item of data.items) {
    const row = document.createElement('div')
    row.className = 'lb-lv-row'
    row.style.display = 'flex'
    row.style.justifyContent = 'space-between'
    row.style.alignItems = 'center'
    row.style.marginBottom = '4px'

    const hasAction = Boolean(item.action && options.onAction)

    if (hasAction && item.action) {
      row.style.cursor = 'pointer'
      row.dataset.action = item.action.type
      row.addEventListener('click', () => {
        options.onAction!({ type: item.action!.type, target: item.action!.target, source: 'label-value-list' })
      })
    }

    const label = document.createElement('div')
    label.className = 'lb-lv-label'
    label.textContent = item.label
    label.style.fontSize = 'var(--lb-font-size-body)'
    label.style.color = 'var(--lb-text-primary)'

    const value = document.createElement('div')
    value.className = 'lb-lv-value'
    value.textContent = String(item.value)
    value.style.fontSize = 'var(--lb-font-size-body)'
    value.style.color = 'var(--lb-text-secondary)'

    row.appendChild(label)
    row.appendChild(value)
    container.appendChild(row)
  }

  return container
}
