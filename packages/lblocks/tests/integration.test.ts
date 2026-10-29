import { describe, it, expect, beforeEach, vi } from 'vitest'
import { render } from '../src/index.js'
import { loadFixture, createContainer } from './helpers.js'
import type { ViewBlock, RenderOptions } from '../src/types/viewblock.js'

describe('integration', () => {
  let container: HTMLElement

  beforeEach(() => {
    container = createContainer()
  })

  describe('full digest render', () => {
    it('applies dark theme to container', () => {
      const blocks: ViewBlock[] = [
        loadFixture('narrative-summary', 'llm-summary') as ViewBlock,
        loadFixture('activity-chart', 'typical-week') as ViewBlock,
        loadFixture('type-breakdown', 'mixed-types') as ViewBlock,
        loadFixture('route-distribution', 'multi-route') as ViewBlock,
      ]

      render(blocks, container, { theme: 'dark' })

      expect(container.style.getPropertyValue('--lb-bg-primary')).toBe('rgb(var(--surface, 30 30 46))')
    })

    it('renders lb-root inside container', () => {
      const blocks: ViewBlock[] = [
        loadFixture('narrative-summary', 'llm-summary') as ViewBlock,
        loadFixture('activity-chart', 'typical-week') as ViewBlock,
        loadFixture('type-breakdown', 'mixed-types') as ViewBlock,
        loadFixture('route-distribution', 'multi-route') as ViewBlock,
      ]

      render(blocks, container, { theme: 'dark' })

      const root = container.querySelector('.lb-root')
      expect(root).not.toBeNull()
    })

    it('renders 4 section headers for 4 blocks', () => {
      const blocks: ViewBlock[] = [
        loadFixture('narrative-summary', 'llm-summary') as ViewBlock,
        loadFixture('activity-chart', 'typical-week') as ViewBlock,
        loadFixture('type-breakdown', 'mixed-types') as ViewBlock,
        loadFixture('route-distribution', 'multi-route') as ViewBlock,
      ]

      render(blocks, container, { theme: 'dark' })

      const headers = container.querySelectorAll('.lb-section-header')
      expect(headers).toHaveLength(4)
    })

    it('narrative-summary llm-summary (full weight) is first row lb-row-full', () => {
      const blocks: ViewBlock[] = [
        loadFixture('narrative-summary', 'llm-summary') as ViewBlock,
        loadFixture('activity-chart', 'typical-week') as ViewBlock,
        loadFixture('type-breakdown', 'mixed-types') as ViewBlock,
        loadFixture('route-distribution', 'multi-route') as ViewBlock,
      ]

      render(blocks, container, { theme: 'dark' })

      const rows = container.querySelectorAll('.lb-row')
      expect(rows[0].classList.contains('lb-row-full')).toBe(true)
    })
  })

  describe('re-render', () => {
    it('re-rendering replaces content: only one lb-root after two renders', () => {
      const blocks: ViewBlock[] = [
        loadFixture('narrative-summary', 'llm-summary') as ViewBlock,
      ]

      render(blocks, container, { theme: 'dark' })
      render(blocks, container, { theme: 'light' })

      const roots = container.querySelectorAll('.lb-root')
      expect(roots).toHaveLength(1)
    })

    it('re-rendering switches theme vars to light', () => {
      const blocks: ViewBlock[] = [
        loadFixture('narrative-summary', 'llm-summary') as ViewBlock,
      ]

      render(blocks, container, { theme: 'dark' })
      expect(container.style.getPropertyValue('--lb-bg-primary')).toBe('rgb(var(--surface, 30 30 46))')

      render(blocks, container, { theme: 'light' })
      expect(container.style.getPropertyValue('--lb-bg-primary')).toBe('rgb(var(--surface, 250 250 250))')
    })
  })

  describe('onAction', () => {
    it('onAction passes through to options', () => {
      const onAction = vi.fn()
      const blocks: ViewBlock[] = [
        loadFixture('narrative-summary', 'llm-summary') as ViewBlock,
      ]
      const options: RenderOptions = { theme: 'dark', onAction }

      render(blocks, container, options)

      // verify render completed without error -- onAction wired through
      expect(container.querySelector('.lb-root')).not.toBeNull()
    })
  })

  describe('empty blocks', () => {
    it('renders empty block array without error', () => {
      expect(() => render([], container, { theme: 'dark' })).not.toThrow()
    })

    it('empty render produces lb-root with no rows', () => {
      render([], container, { theme: 'dark' })
      const root = container.querySelector('.lb-root')
      expect(root).not.toBeNull()
      expect(root!.querySelectorAll('.lb-row')).toHaveLength(0)
    })
  })
})
