import { describe, it, expect, vi } from 'vitest'
import { renderLabelValueList } from '../src/primitives/label-value-list.js'
import type { LabelValueListData } from '../src/types/primitives.js'
import type { PrimitiveOptions } from '../src/types/primitives.js'

function makeOptions(overrides?: Partial<PrimitiveOptions>): PrimitiveOptions {
  return { density: 'standard', ...overrides }
}

describe('label-value-list primitive', () => {
  it('renders correct item count', () => {
    const data: LabelValueListData = {
      items: [
        { label: 'Tasks', value: 12 },
        { label: 'Ideas', value: 7 },
        { label: 'Decisions', value: 3 },
      ],
    }
    const el = renderLabelValueList(data, makeOptions())
    const rows = el.querySelectorAll('.lb-lv-row')
    expect(rows.length).toBe(3)
  })

  it('renders labels', () => {
    const data: LabelValueListData = {
      items: [
        { label: 'Tasks', value: 12 },
        { label: 'Ideas', value: 7 },
      ],
    }
    const el = renderLabelValueList(data, makeOptions())
    const labels = el.querySelectorAll('.lb-lv-label')
    expect(labels[0].textContent).toBe('Tasks')
    expect(labels[1].textContent).toBe('Ideas')
  })

  it('renders values', () => {
    const data: LabelValueListData = {
      items: [
        { label: 'Tasks', value: 12 },
        { label: 'Ideas', value: 7 },
      ],
    }
    const el = renderLabelValueList(data, makeOptions())
    const values = el.querySelectorAll('.lb-lv-value')
    expect(values[0].textContent).toBe('12')
    expect(values[1].textContent).toBe('7')
  })

  it('rows have space-between layout', () => {
    const data: LabelValueListData = {
      items: [{ label: 'Tasks', value: 12 }],
    }
    const el = renderLabelValueList(data, makeOptions())
    const row = el.querySelector('.lb-lv-row') as HTMLElement
    expect(row.style.display).toBe('flex')
    expect(row.style.justifyContent).toBe('space-between')
  })

  it('handles empty list', () => {
    const data: LabelValueListData = { items: [] }
    expect(() => renderLabelValueList(data, makeOptions())).not.toThrow()
    const el = renderLabelValueList(data, makeOptions())
    const rows = el.querySelectorAll('.lb-lv-row')
    expect(rows.length).toBe(0)
  })

  it('row has cursor pointer and data-action when action is provided', () => {
    const onAction = vi.fn()
    const data: LabelValueListData = {
      items: [{ label: 'Tasks', value: 12, action: { type: 'filter', target: 'task' } }],
    }
    const el = renderLabelValueList(data, makeOptions({ onAction }))
    const row = el.querySelector('.lb-lv-row') as HTMLElement
    expect(row.style.cursor).toBe('pointer')
    expect(row.dataset.action).toBe('filter')
  })

  it('clicking row with action calls onAction', () => {
    const onAction = vi.fn()
    const data: LabelValueListData = {
      items: [{ label: 'Tasks', value: 12, action: { type: 'filter', target: 'task' } }],
    }
    const el = renderLabelValueList(data, makeOptions({ onAction }))
    const row = el.querySelector('.lb-lv-row') as HTMLElement
    row.click()
    expect(onAction).toHaveBeenCalledOnce()
    expect(onAction).toHaveBeenCalledWith({ type: 'filter', target: 'task', source: 'label-value-list' })
  })

  it('row has no cursor when no action', () => {
    const data: LabelValueListData = {
      items: [{ label: 'Tasks', value: 12 }],
    }
    const el = renderLabelValueList(data, makeOptions())
    const row = el.querySelector('.lb-lv-row') as HTMLElement
    expect(row.style.cursor).not.toBe('pointer')
  })

  it('label uses text-primary color', () => {
    const data: LabelValueListData = {
      items: [{ label: 'Tasks', value: 12 }],
    }
    const el = renderLabelValueList(data, makeOptions())
    const label = el.querySelector('.lb-lv-label') as HTMLElement
    expect(label.style.color).toBe('var(--lb-text-primary)')
  })

  it('value uses text-secondary color', () => {
    const data: LabelValueListData = {
      items: [{ label: 'Tasks', value: 12 }],
    }
    const el = renderLabelValueList(data, makeOptions())
    const value = el.querySelector('.lb-lv-value') as HTMLElement
    expect(value.style.color).toBe('var(--lb-text-secondary)')
  })
})
