import { describe, it, expect } from 'vitest'
import { resolve, BLOCK_PRIMITIVE_MAP } from '../src/engine/resolver.js'
import { loadFixture } from './helpers.js'
import type { ViewBlock } from '../src/types/viewblock.js'
import type { BarChartData, LabelValueListData, NarrativeData } from '../src/types/primitives.js'

describe('resolver', () => {
  describe('BLOCK_PRIMITIVE_MAP', () => {
    it('has all 4 founding block types', () => {
      expect('activity-chart' in BLOCK_PRIMITIVE_MAP).toBe(true)
      expect('type-breakdown' in BLOCK_PRIMITIVE_MAP).toBe(true)
      expect('route-distribution' in BLOCK_PRIMITIVE_MAP).toBe(true)
      expect('narrative-summary' in BLOCK_PRIMITIVE_MAP).toBe(true)
    })
  })

  describe('activity-chart', () => {
    it('typical-week resolves to bar-chart with 7 bars', () => {
      const block = loadFixture('activity-chart', 'typical-week') as ViewBlock
      const resolved = resolve(block)
      expect(resolved.primitive).toBe('bar-chart')
      const data = resolved.data as BarChartData
      expect(data.bars).toHaveLength(7)
    })

    it('typical-week first bar is { label: Mon, value: 12 }', () => {
      const block = loadFixture('activity-chart', 'typical-week') as ViewBlock
      const resolved = resolve(block)
      const data = resolved.data as BarChartData
      expect(data.bars[0].label).toBe('Mon')
      expect(data.bars[0].value).toBe(12)
    })

    it('typical-week sectionLabel is ACTIVITY', () => {
      const block = loadFixture('activity-chart', 'typical-week') as ViewBlock
      const resolved = resolve(block)
      expect(resolved.sectionLabel).toBe('ACTIVITY')
    })
  })

  describe('type-breakdown', () => {
    it('mixed-types resolves to label-value-list with 5 items', () => {
      const block = loadFixture('type-breakdown', 'mixed-types') as ViewBlock
      const resolved = resolve(block)
      expect(resolved.primitive).toBe('label-value-list')
      const data = resolved.data as LabelValueListData
      expect(data.items).toHaveLength(5)
    })

    it('mixed-types first item is { label: log, value: 24 }', () => {
      const block = loadFixture('type-breakdown', 'mixed-types') as ViewBlock
      const resolved = resolve(block)
      const data = resolved.data as LabelValueListData
      expect(data.items[0].label).toBe('log')
      expect(data.items[0].value).toBe(24)
    })

    it('mixed-types sectionLabel is ENTRY TYPES', () => {
      const block = loadFixture('type-breakdown', 'mixed-types') as ViewBlock
      const resolved = resolve(block)
      expect(resolved.sectionLabel).toBe('ENTRY TYPES')
    })
  })

  describe('route-distribution', () => {
    it('multi-route resolves to bar-chart with 4 bars', () => {
      const block = loadFixture('route-distribution', 'multi-route') as ViewBlock
      const resolved = resolve(block)
      expect(resolved.primitive).toBe('bar-chart')
      const data = resolved.data as BarChartData
      expect(data.bars).toHaveLength(4)
    })

    it('multi-route first bar label is +inbox', () => {
      const block = loadFixture('route-distribution', 'multi-route') as ViewBlock
      const resolved = resolve(block)
      const data = resolved.data as BarChartData
      expect(data.bars[0].label).toBe('+inbox')
    })

    it('multi-route first bar suffix is (40%)', () => {
      const block = loadFixture('route-distribution', 'multi-route') as ViewBlock
      const resolved = resolve(block)
      const data = resolved.data as BarChartData
      expect(data.bars[0].suffix).toBe('(40%)')
    })

    it('multi-route sectionLabel is ROUTES', () => {
      const block = loadFixture('route-distribution', 'multi-route') as ViewBlock
      const resolved = resolve(block)
      expect(resolved.sectionLabel).toBe('ROUTES')
    })
  })

  describe('narrative-summary', () => {
    it('llm-summary resolves to narrative with badge L2', () => {
      const block = loadFixture('narrative-summary', 'llm-summary') as ViewBlock
      const resolved = resolve(block)
      expect(resolved.primitive).toBe('narrative')
      const data = resolved.data as NarrativeData
      expect(data.badge).toBe('L2')
    })

    it('template-summary resolves to narrative with badge null', () => {
      const block = loadFixture('narrative-summary', 'template-summary') as ViewBlock
      const resolved = resolve(block)
      const data = resolved.data as NarrativeData
      expect(data.badge).toBeNull()
    })

    it('llm-summary sectionLabel is SUMMARY', () => {
      const block = loadFixture('narrative-summary', 'llm-summary') as ViewBlock
      const resolved = resolve(block)
      expect(resolved.sectionLabel).toBe('SUMMARY')
    })
  })

  describe('degradation', () => {
    it('resolves unknown block types to the unknown-block placeholder', () => {
      const block = { type: 'unknown-type', version: 1, data: {} } as unknown as ViewBlock
      const resolved = resolve(block)
      expect(resolved.primitive).toBe('unknown-block')
      expect(resolved.data).toEqual({ type: 'unknown-type' })
    })
  })
})
