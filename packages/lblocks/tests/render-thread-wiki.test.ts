import { describe, expect, test, beforeEach } from 'vitest'
import { JSDOM } from 'jsdom'
import { readFileSync } from 'fs'
import { resolve } from 'path'
import { renderThreadWiki } from '../src/primitives/thread-wiki.js'

const fixturesDir = resolve(__dirname, 'fixtures/thread-wiki')

describe('renderThreadWiki', () => {
  beforeEach(() => {
    const dom = new JSDOM('<!DOCTYPE html><html><body></body></html>')
    ;(globalThis as any).document = dom.window.document
    ;(globalThis as any).HTMLElement = dom.window.HTMLElement
  })

  test('renders title, OP pill, meta strip, tags, participants, sections, sources', () => {
    const md = readFileSync(resolve(fixturesDir, 'qwen.md'), 'utf8')
    const block = { kind: 'thread-wiki' as const, body: md }
    const root = renderThreadWiki(block, { theme: 'dark' } as any)

    // Title
    expect(root.querySelector('.lb-thread-title')?.textContent).toBe('Qwen 3.6 Configuration Optimization')

    // OP pill
    const opPill = root.querySelector('.lb-thread-pill--op')
    expect(opPill).not.toBeNull()
    expect(opPill?.textContent).toBe('@Michaelzsguo')
    expect(opPill?.getAttribute('href')).toMatch(/x\.com\/Michaelzsguo\/status/)

    // Meta strip contains reply count + image count
    const meta = root.querySelector('.lb-thread-meta')?.textContent ?? ''
    expect(meta).toContain('43')
    expect(meta).toContain('3')

    // Participants pills
    const pills = root.querySelectorAll('.lb-thread-pill:not(.lb-thread-pill--op)')
    expect(pills.length).toBeGreaterThan(20)
    // Each pill is an <a> with href
    const firstPill = pills[0] as HTMLAnchorElement
    expect(firstPill.tagName).toBe('A')
    expect(firstPill.href).toMatch(/^https:\/\/x\.com\//)

    // Toggle button visible (because participants > 12)
    expect(root.querySelector('.lb-thread-participants-toggle')).not.toBeNull()
    expect(root.querySelector('.lb-thread-pills')?.getAttribute('data-collapsed')).toBe('true')

    // Body sections.
    // NOTE: qwen.md uses plain-text "Summary:" labels in the body rather than
    // proper `## Summary` headings (synthesis-prompt inconsistency tracked
    // separately; theo.md uses proper headings). The parser correctly produces
    // 0 sections here -- the body collapses into the implicit area before any
    // `##`. We still assert the body container exists and that the synthetic
    // body-list-rendering test below validates section dispatch via a fixture
    // with proper headings.
    expect(root.querySelector('.lb-thread-body')).not.toBeNull()

    // Sources footer
    const sources = root.querySelectorAll('.lb-thread-sources li')
    expect(sources.length).toBeGreaterThan(0)
  })

  test('participants <= 12 hides toggle', () => {
    const md = `---
type: thread-wiki
op: '@a'
participants:
- '@a'
- '@b'
- '@c'
---

# T

## Summary
body
`
    const block = { kind: 'thread-wiki' as const, body: md }
    const root = renderThreadWiki(block, { theme: 'dark' } as any)
    expect(root.querySelector('.lb-thread-participants-toggle')).toBeNull()
  })

  test('empty participants -> "Participants (0)" + no toggle + empty pill row', () => {
    const md = `---\ntype: thread-wiki\nop: '@a'\n---\n\n# T\n`
    const block = { kind: 'thread-wiki' as const, body: md }
    const root = renderThreadWiki(block, { theme: 'dark' } as any)
    expect(root.querySelector('.lb-thread-participants-label')?.textContent).toContain('(0)')
    expect(root.querySelector('.lb-thread-participants-toggle')).toBeNull()
    expect(root.querySelectorAll('.lb-thread-pill:not(.lb-thread-pill--op)')).toHaveLength(0)
  })

  test('empty sources -> footer not rendered', () => {
    const md = `---\ntype: thread-wiki\nop: '@a'\n---\n\n# T\n\n## Summary\nbody\n`
    const block = { kind: 'thread-wiki' as const, body: md }
    const root = renderThreadWiki(block, { theme: 'dark' } as any)
    expect(root.querySelector('.lb-thread-sources')).toBeNull()
  })

  test('source links rendered with kind-specific icons', () => {
    const md = readFileSync(resolve(fixturesDir, 'qwen.md'), 'utf8')
    const block = { kind: 'thread-wiki' as const, body: md }
    const root = renderThreadWiki(block, { theme: 'dark' } as any)
    const items = root.querySelectorAll('.lb-thread-sources li')
    // At least one Original tweet link, one raw-thread, one image-transcript
    const kinds = Array.from(items).map(li => li.getAttribute('data-kind'))
    expect(kinds).toContain('original-tweet')
    expect(kinds).toContain('raw-thread')
    expect(kinds).toContain('image-transcript')
  })

  test('body sections render via markdown primitives (lists become <ul>)', () => {
    const md = `---\ntype: thread-wiki\nop: '@a'\n---

# T

## Key insights
- one
- two
- three
`
    const block = { kind: 'thread-wiki' as const, body: md }
    const root = renderThreadWiki(block, { theme: 'dark' } as any)
    const ul = root.querySelector('.lb-thread-body ul')
    expect(ul).not.toBeNull()
    expect(ul?.querySelectorAll('li')).toHaveLength(3)
  })
})
