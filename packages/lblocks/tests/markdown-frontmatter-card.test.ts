import { describe, it, expect } from 'vitest'
import { renderFrontmatterCard } from '../src/primitives/markdown-frontmatter-card.js'
import type { FrontmatterBlock, MarkdownRenderOptions } from '../src/types/markdown-doc.js'

const opts: MarkdownRenderOptions = { theme: 'dark' }

describe('markdown-frontmatter-card primitive', () => {
  it('renders an empty card when entries empty', () => {
    const block: FrontmatterBlock = { kind: 'frontmatter', entries: [] }
    const el = renderFrontmatterCard(block, opts)
    expect(el.classList.contains('lb-md-frontmatter')).toBe(true)
    expect(el.querySelector('dl')?.children.length ?? 0).toBe(0)
  })

  it('renders entries as <dt>/<dd> pairs', () => {
    const block: FrontmatterBlock = {
      kind: 'frontmatter',
      entries: [
        { key: 'title', value: 'Hello' },
        { key: 'date', value: '2026-04-28' },
      ],
    }
    const el = renderFrontmatterCard(block, opts)
    const dts = el.querySelectorAll('dt')
    const dds = el.querySelectorAll('dd')
    expect(dts).toHaveLength(2)
    expect(dds).toHaveLength(2)
    expect(dts[0].textContent).toBe('title')
    expect(dds[0].textContent).toBe('Hello')
    expect(dts[1].textContent).toBe('date')
    expect(dds[1].textContent).toBe('2026-04-28')
  })

  it('renders each value as text (no HTML interpretation)', () => {
    const block: FrontmatterBlock = {
      kind: 'frontmatter',
      entries: [{ key: 'x', value: '<b>bold</b>' }],
    }
    const el = renderFrontmatterCard(block, opts)
    expect(el.querySelector('dd')?.textContent).toBe('<b>bold</b>')
    expect(el.querySelector('dd b')).toBeNull()
  })

  it('uses subdued styling', () => {
    const block: FrontmatterBlock = {
      kind: 'frontmatter',
      entries: [{ key: 'k', value: 'v' }],
    }
    const el = renderFrontmatterCard(block, opts)
    expect(el.style.backgroundColor).toBe('var(--lb-bg-secondary)')
    expect(el.style.fontSize).toBe('var(--lb-font-size-small)')
  })
})
