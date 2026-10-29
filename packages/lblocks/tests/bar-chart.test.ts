import { describe, it, expect, vi } from 'vitest'
import { renderBarChart } from '../src/primitives/bar-chart.js'
import type { BarChartData } from '../src/types/primitives.js'
import type { PrimitiveOptions } from '../src/types/primitives.js'

function makeOptions(overrides?: Partial<PrimitiveOptions>): PrimitiveOptions {
  return { density: 'standard', ...overrides }
}

describe('bar-chart primitive', () => {
  it('renders correct bar count', () => {
    const data: BarChartData = {
      bars: [
        { label: 'A', value: 10, maxValue: 10 },
        { label: 'B', value: 5, maxValue: 10 },
        { label: 'C', value: 7, maxValue: 10 },
      ],
    }
    const el = renderBarChart(data, makeOptions())
    const bars = el.querySelectorAll('.lb-bar-row')
    expect(bars.length).toBe(3)
  })

  it('renders labels', () => {
    const data: BarChartData = {
      bars: [
        { label: 'Tasks', value: 8, maxValue: 10 },
        { label: 'Ideas', value: 4, maxValue: 10 },
      ],
    }
    const el = renderBarChart(data, makeOptions())
    const labels = el.querySelectorAll('.lb-bar-label')
    expect(labels[0].textContent).toBe('Tasks')
    expect(labels[1].textContent).toBe('Ideas')
  })

  it('renders values', () => {
    const data: BarChartData = {
      bars: [{ label: 'A', value: 42, maxValue: 100 }],
    }
    const el = renderBarChart(data, makeOptions())
    const valueEl = el.querySelector('.lb-bar-value')
    expect(valueEl?.textContent).toContain('42')
  })

  it('renders suffix when provided', () => {
    const data: BarChartData = {
      bars: [{ label: 'A', value: 40, maxValue: 100, suffix: '(40%)' }],
    }
    const el = renderBarChart(data, makeOptions())
    const valueEl = el.querySelector('.lb-bar-value')
    expect(valueEl?.textContent).toContain('(40%)')
  })

  it('bar fill width proportional to max value', () => {
    const data: BarChartData = {
      bars: [{ label: 'A', value: 50, maxValue: 100 }],
    }
    const el = renderBarChart(data, makeOptions())
    const fill = el.querySelector('.lb-bar-fill') as HTMLElement
    expect(fill.style.width).toBe('50%')
  })

  it('bar fill at 100% when value equals maxValue', () => {
    const data: BarChartData = {
      bars: [{ label: 'A', value: 10, maxValue: 10 }],
    }
    const el = renderBarChart(data, makeOptions())
    const fill = el.querySelector('.lb-bar-fill') as HTMLElement
    expect(fill.style.width).toBe('100%')
  })

  it('handles all-zero values without division error', () => {
    const data: BarChartData = {
      bars: [
        { label: 'A', value: 0, maxValue: 0 },
        { label: 'B', value: 0, maxValue: 0 },
      ],
    }
    expect(() => renderBarChart(data, makeOptions())).not.toThrow()
    const el = renderBarChart(data, makeOptions())
    const fills = el.querySelectorAll('.lb-bar-fill') as NodeListOf<HTMLElement>
    fills.forEach(fill => {
      expect(fill.style.width).toBe('0%')
    })
  })

  it('bar label uses accent color when action is declared', () => {
    const data: BarChartData = {
      bars: [{ label: 'A', value: 5, maxValue: 10, action: { type: 'filter', target: 'A' } }],
    }
    const el = renderBarChart(data, makeOptions())
    const label = el.querySelector('.lb-bar-label') as HTMLElement
    expect(label.style.color).toBe('var(--lb-accent)')
  })

  it('bar label uses text-secondary when no action', () => {
    const data: BarChartData = {
      bars: [{ label: 'A', value: 5, maxValue: 10 }],
    }
    const el = renderBarChart(data, makeOptions())
    const label = el.querySelector('.lb-bar-label') as HTMLElement
    expect(label.style.color).toBe('var(--lb-text-secondary)')
  })

  it('bar row has cursor pointer when action is provided and onAction is set', () => {
    const onAction = vi.fn()
    const data: BarChartData = {
      bars: [{ label: 'A', value: 5, maxValue: 10, action: { type: 'filter', target: 'A' } }],
    }
    const el = renderBarChart(data, makeOptions({ onAction }))
    const row = el.querySelector('.lb-bar-row') as HTMLElement
    expect(row.style.cursor).toBe('pointer')
  })

  it('bar row has no cursor when no action', () => {
    const data: BarChartData = {
      bars: [{ label: 'A', value: 5, maxValue: 10 }],
    }
    const el = renderBarChart(data, makeOptions())
    const row = el.querySelector('.lb-bar-row') as HTMLElement
    expect(row.style.cursor).not.toBe('pointer')
  })

  it('bar row has data-action attribute when action is provided', () => {
    const onAction = vi.fn()
    const data: BarChartData = {
      bars: [{ label: 'A', value: 5, maxValue: 10, action: { type: 'filter', target: 'A' } }],
    }
    const el = renderBarChart(data, makeOptions({ onAction }))
    const row = el.querySelector('.lb-bar-row') as HTMLElement
    expect(row.dataset.action).toBe('filter')
  })

  it('clicking a bar row with action calls onAction', () => {
    const onAction = vi.fn()
    const data: BarChartData = {
      bars: [{ label: 'A', value: 5, maxValue: 10, action: { type: 'filter', target: 'A' } }],
    }
    const el = renderBarChart(data, makeOptions({ onAction }))
    const row = el.querySelector('.lb-bar-row') as HTMLElement
    row.click()
    expect(onAction).toHaveBeenCalledOnce()
    expect(onAction).toHaveBeenCalledWith({ type: 'filter', target: 'A', source: 'bar-chart' })
  })

  it('bar fill has reduced opacity when action is declared', () => {
    const data: BarChartData = {
      bars: [{ label: 'A', value: 5, maxValue: 10, action: { type: 'filter', target: 'A' } }],
    }
    const el = renderBarChart(data, makeOptions())
    const fill = el.querySelector('.lb-bar-fill') as HTMLElement
    expect(fill.style.opacity).toBe('0.5')
  })

  it('value element has wider width when suffix is present', () => {
    const data: BarChartData = {
      bars: [{ label: 'A', value: 40, maxValue: 100, suffix: '(40%)' }],
    }
    const el = renderBarChart(data, makeOptions())
    const valueEl = el.querySelector('.lb-bar-value') as HTMLElement
    expect(valueEl.style.width).toBe('64px')
  })

  it('value element has narrower width when no suffix', () => {
    const data: BarChartData = {
      bars: [{ label: 'A', value: 40, maxValue: 100 }],
    }
    const el = renderBarChart(data, makeOptions())
    const valueEl = el.querySelector('.lb-bar-value') as HTMLElement
    expect(valueEl.style.width).toBe('28px')
  })
})
