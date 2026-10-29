import { describe, expect, it, vi } from 'vitest'
import { renderSearchResultList } from '../../src/primitives/search-result-list.js'

describe('renderSearchResultList', () => {
  it('renders search results with route and tags', () => {
    const el = renderSearchResultList({
      query: 'mobile',
      results: [
        {
          id: 'entry-1',
          resultType: 'entry',
          title: 'task',
          excerpt: 'Check mobile layout',
          route: '+mobile',
          tags: ['phone'],
        },
      ],
    }, { density: 'standard' })

    expect(el.className).toContain('lb-search-result-list')
    expect(el.textContent).toContain('mobile')
    expect(el.textContent).toContain('Check mobile layout')
    expect(el.textContent).toContain('+mobile')
    expect(el.textContent).toContain('#phone')
  })

  it('emits search-result-open action when a result is clicked', () => {
    const onAction = vi.fn()
    const el = renderSearchResultList({
      query: 'mobile',
      results: [
        { id: 'entry-1', resultType: 'entry', title: 'task', excerpt: 'Check mobile layout' },
      ],
    }, { density: 'standard', onAction })

    const row = el.querySelector('[data-result-id="entry-1"]') as HTMLElement
    row.click()
    expect(onAction).toHaveBeenCalledWith({
      type: 'search-result-open',
      target: 'entry-1',
      source: 'search-result-list',
    })
  })

  it('emits search-result-open action from keyboard activation', () => {
    const onAction = vi.fn()
    const el = renderSearchResultList({
      query: 'mobile',
      results: [
        { id: 'entry-1', resultType: 'entry', title: 'task', excerpt: 'Check mobile layout' },
      ],
    }, { density: 'standard', onAction })

    const row = el.querySelector('[data-result-id="entry-1"]') as HTMLElement
    row.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }))

    expect(row.getAttribute('role')).toBe('button')
    expect(row.getAttribute('tabindex')).toBe('0')
    expect(onAction).toHaveBeenCalledWith({
      type: 'search-result-open',
      target: 'entry-1',
      source: 'search-result-list',
    })
  })

  it('emits tag click actions without opening the result row', () => {
    const onAction = vi.fn()
    const onTagClick = vi.fn()
    const el = renderSearchResultList({
      query: 'mobile',
      results: [
        {
          id: 'entry-1',
          resultType: 'entry',
          title: 'task',
          excerpt: 'Check mobile layout',
          tags: ['phone'],
        },
      ],
    }, { density: 'standard', onAction, onTagClick })

    const tag = el.querySelector('.lb-search-result-list-tag') as HTMLButtonElement
    tag.click()

    expect(tag.tagName).toBe('BUTTON')
    expect(tag.getAttribute('aria-label')).toBe('Filter by #phone')
    expect(onTagClick).toHaveBeenCalledWith('phone')
    expect(onAction).not.toHaveBeenCalled()
  })

  it('renders empty search state', () => {
    const el = renderSearchResultList({ query: 'none', results: [] }, { density: 'standard' })
    expect(el.textContent).toContain('No results')
  })
})
