import { describe, it, expect, beforeEach } from 'vitest'
import { compose } from '../src/engine/compositor.js'
import { createContainer } from './helpers.js'
import type { ViewBlock, RenderOptions } from '../src/types/viewblock.js'

function makeBlock(type: string, weight?: 'full' | 'half', data?: unknown): ViewBlock {
  return {
    type: type as ViewBlock['type'],
    version: 1,
    data: data ?? { text: 'test narrative text for testing purposes', inference: null },
    meta: weight ? { renderHints: { weight } } : undefined,
  }
}

function makeNarrativeBlock(weight?: 'full' | 'half'): ViewBlock {
  return makeBlock('narrative-summary', weight, {
    text: 'test narrative text for testing purposes',
    inference: null,
  })
}

function makeActivityBlock(weight?: 'full' | 'half'): ViewBlock {
  return makeBlock('activity-chart', weight, {
    days: [
      { label: 'Mon', count: 1 },
      { label: 'Tue', count: 2 },
      { label: 'Wed', count: 3 },
      { label: 'Thu', count: 4 },
      { label: 'Fri', count: 5 },
      { label: 'Sat', count: 6 },
      { label: 'Sun', count: 7 },
    ],
  })
}

function makeTypeBreakdownBlock(weight?: 'full' | 'half'): ViewBlock {
  return makeBlock('type-breakdown', weight, {
    types: [{ type: 'log', count: 5 }],
  })
}

const defaultOptions: RenderOptions = { theme: 'dark' }

describe('compositor', () => {
  let _container: HTMLElement

  beforeEach(() => {
    _container = createContainer()
  })

  describe('layout', () => {
    it('full-weight block gets its own lb-row-full row', () => {
      const root = compose([makeNarrativeBlock('full')], defaultOptions)
      const rows = root.querySelectorAll('.lb-row')
      expect(rows).toHaveLength(1)
      expect(rows[0].classList.contains('lb-row-full')).toBe(true)
    })

    it('two half-weight blocks pair into one lb-row-half with 2 children', () => {
      const blocks = [makeActivityBlock('half'), makeTypeBreakdownBlock('half')]
      const root = compose(blocks, defaultOptions)
      const rows = root.querySelectorAll('.lb-row')
      expect(rows).toHaveLength(1)
      expect(rows[0].classList.contains('lb-row-half')).toBe(true)
      expect(rows[0].children).toHaveLength(2)
    })

    it('three half-weight blocks: first row has 2, second row has 1', () => {
      const blocks = [
        makeActivityBlock('half'),
        makeTypeBreakdownBlock('half'),
        makeNarrativeBlock('half'),
      ]
      const root = compose(blocks, defaultOptions)
      const rows = root.querySelectorAll('.lb-row')
      expect(rows).toHaveLength(2)
      expect(rows[0].children).toHaveLength(2)
      expect(rows[1].children).toHaveLength(1)
    })

    it('mixed full/half/half/full: 3 rows', () => {
      const blocks = [
        makeNarrativeBlock('full'),
        makeActivityBlock('half'),
        makeTypeBreakdownBlock('half'),
        makeNarrativeBlock('full'),
      ]
      const root = compose(blocks, defaultOptions)
      const rows = root.querySelectorAll('.lb-row')
      expect(rows).toHaveLength(3)
      expect(rows[0].classList.contains('lb-row-full')).toBe(true)
      expect(rows[1].classList.contains('lb-row-half')).toBe(true)
      expect(rows[2].classList.contains('lb-row-full')).toBe(true)
    })

    it('block with no meta.renderHints defaults to full weight', () => {
      const block: ViewBlock = {
        type: 'narrative-summary',
        version: 1,
        data: { text: 'no hints block', inference: null },
      }
      const root = compose([block], defaultOptions)
      const rows = root.querySelectorAll('.lb-row')
      expect(rows[0].classList.contains('lb-row-full')).toBe(true)
    })

    it('empty block list renders without error with 0 rows', () => {
      const root = compose([], defaultOptions)
      expect(root.querySelectorAll('.lb-row')).toHaveLength(0)
    })
  })

  describe('root element', () => {
    it('root has lb-root class', () => {
      const root = compose([], defaultOptions)
      expect(root.classList.contains('lb-root')).toBe(true)
    })

    it('root has maxWidth 620px', () => {
      const root = compose([], defaultOptions)
      expect(root.style.maxWidth).toBe('620px')
    })
  })

  describe('cards and section headers', () => {
    it('each block is wrapped in an lb-card', () => {
      const blocks = [makeNarrativeBlock('full'), makeActivityBlock('full')]
      const root = compose(blocks, defaultOptions)
      const cards = root.querySelectorAll('.lb-card')
      expect(cards).toHaveLength(2)
    })

    it('section headers appear inside cards with correct labels', () => {
      const blocks = [
        makeNarrativeBlock('full'),
        makeActivityBlock('full'),
      ]
      const root = compose(blocks, defaultOptions)
      const headers = root.querySelectorAll('.lb-section-header')
      expect(headers).toHaveLength(2)
      const labels = Array.from(headers).map((h) => h.textContent)
      expect(labels).toContain('SUMMARY')
      expect(labels).toContain('ACTIVITY')
    })
  })
})
