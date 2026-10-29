import { describe, it, expect } from 'vitest'
import { renderLensTabs } from '../src/primitives/lens-tab.js'
import type { LensTabsData } from '../src/types/reader.js'

describe('lens-tab primitive', () => {
  it('renders a tablist with one tab per option', () => {
    const data: LensTabsData = {
      value: 'summary',
      options: ['Summary', 'Timeline', 'Highlights'],
    }
    const el = renderLensTabs(data)
    expect(el.getAttribute('role')).toBe('tablist')
    const tabs = el.querySelectorAll('.lens-tab')
    expect(tabs.length).toBe(3)
  })

  it('marks the tab matching value as active and aria-selected', () => {
    const data: LensTabsData = {
      value: 'highlights',
      options: ['Summary', 'Timeline', 'Highlights'],
    }
    const el = renderLensTabs(data)
    const tabs = el.querySelectorAll('.lens-tab')
    expect(tabs[0].classList.contains('active')).toBe(false)
    expect(tabs[1].classList.contains('active')).toBe(false)
    expect(tabs[2].classList.contains('active')).toBe(true)
    expect(tabs[2].getAttribute('aria-selected')).toBe('true')
    expect(tabs[0].getAttribute('aria-selected')).toBe('false')
  })

  it('lowercases the value for case-insensitive matching', () => {
    const data: LensTabsData = {
      value: 'TIMELINE',
      options: ['Summary', 'Timeline', 'Highlights'],
    }
    const el = renderLensTabs(data)
    const tabs = el.querySelectorAll('.lens-tab')
    expect(tabs[1].classList.contains('active')).toBe(true)
  })

  it('tabs carry both lb- and ink-facing classes', () => {
    const data: LensTabsData = { value: 'a', options: ['A'] }
    const el = renderLensTabs(data)
    const tab = el.querySelector('button')
    expect(tab?.classList.contains('lb-lens-tab')).toBe(true)
    expect(tab?.classList.contains('lens-tab')).toBe(true)
  })

  it('each tab carries its lowercased value in data-value', () => {
    const data: LensTabsData = { value: 's', options: ['Summary', 'Pages'] }
    const el = renderLensTabs(data)
    const tabs = el.querySelectorAll('.lens-tab')
    expect((tabs[0] as HTMLElement).dataset.value).toBe('summary')
    expect((tabs[1] as HTMLElement).dataset.value).toBe('pages')
  })

  it('renders the label text on each tab', () => {
    const data: LensTabsData = {
      value: 'a',
      options: ['Summary', 'Timeline'],
    }
    const el = renderLensTabs(data)
    const tabs = el.querySelectorAll('.lens-tab')
    expect(tabs[0].textContent).toBe('Summary')
    expect(tabs[1].textContent).toBe('Timeline')
  })
})
