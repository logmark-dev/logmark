import { describe, it, expect, vi } from 'vitest'
import { renderInline } from '../src/primitives/markdown-inline.js'
import type { InlineNode, MarkdownRenderOptions } from '../src/types/markdown-doc.js'

const opts: MarkdownRenderOptions = { theme: 'dark' }

function asText(frag: DocumentFragment): string {
  const div = document.createElement('div')
  div.appendChild(frag.cloneNode(true))
  return div.textContent ?? ''
}

describe('markdown-inline primitive', () => {
  it('renders plain text', () => {
    const frag = renderInline([{ kind: 'text', text: 'hello' }], opts)
    expect(asText(frag)).toBe('hello')
  })

  it('renders bold emphasis as <strong>', () => {
    const frag = renderInline(
      [{ kind: 'emphasis', style: 'bold', children: [{ kind: 'text', text: 'b' }] }],
      opts
    )
    const div = document.createElement('div')
    div.appendChild(frag)
    expect(div.querySelector('strong')?.textContent).toBe('b')
  })

  it('renders italic emphasis as <em>', () => {
    const frag = renderInline(
      [{ kind: 'emphasis', style: 'italic', children: [{ kind: 'text', text: 'i' }] }],
      opts
    )
    const div = document.createElement('div')
    div.appendChild(frag)
    expect(div.querySelector('em')?.textContent).toBe('i')
  })

  it('renders bold-italic as nested <strong><em>', () => {
    const frag = renderInline(
      [{ kind: 'emphasis', style: 'bold-italic', children: [{ kind: 'text', text: 'bi' }] }],
      opts
    )
    const div = document.createElement('div')
    div.appendChild(frag)
    expect(div.querySelector('strong em')?.textContent).toBe('bi')
  })

  it('renders code span as <code> with mono font', () => {
    const frag = renderInline([{ kind: 'code-span', text: 'x()' }], opts)
    const div = document.createElement('div')
    div.appendChild(frag)
    const code = div.querySelector('code')
    expect(code?.textContent).toBe('x()')
    expect(code?.style.fontFamily).toBe('var(--lb-font-mono)')
    expect(code?.style.backgroundColor).toBe('var(--lb-code-bg)')
  })

  it('renders code-span containing Windows absolute path as clickable role-link span', () => {
    const onLinkClick = vi.fn()
    const frag = renderInline(
      [{ kind: 'code-span', text: 'C:\\foo\\bar.md' }],
      { ...opts, onLinkClick }
    )
    const div = document.createElement('div')
    div.appendChild(frag)
    const span = div.querySelector('span[role="link"]')
    expect(span, 'expected role=link span for path code-span').not.toBeNull()
    expect(span?.textContent).toBe('C:\\foo\\bar.md')
    expect((span as HTMLElement).style.fontFamily).toBe('var(--lb-font-mono)')
    expect(span?.getAttribute('data-path')).toBe('C:\\foo\\bar.md')
    // Critical: NOT an anchor -- prevents WebView2 from navigating to file:///
    expect(div.querySelector('a')).toBeNull()
    ;(span as HTMLElement).click()
    expect(onLinkClick).toHaveBeenCalledWith(
      expect.objectContaining({ href: 'C:\\foo\\bar.md', kind: 'absolute-path' })
    )
  })

  it('detects forward-slash variant of Windows absolute path in code-span', () => {
    const onLinkClick = vi.fn()
    const frag = renderInline(
      [{ kind: 'code-span', text: 'C:/projects/note.md' }],
      { ...opts, onLinkClick }
    )
    const div = document.createElement('div')
    div.appendChild(frag)
    const span = div.querySelector('span[role="link"]') as HTMLElement | null
    expect(span).not.toBeNull()
    span!.click()
    expect(onLinkClick).toHaveBeenCalledWith(
      expect.objectContaining({ kind: 'absolute-path' })
    )
  })

  it('does not treat ordinary code-span text as a path', () => {
    const onLinkClick = vi.fn()
    const frag = renderInline(
      [{ kind: 'code-span', text: 'Path.Combine(a, b)' }],
      { ...opts, onLinkClick }
    )
    const div = document.createElement('div')
    div.appendChild(frag)
    expect(div.querySelector('span[role="link"]')).toBeNull()
    expect(div.querySelector('code')?.textContent).toBe('Path.Combine(a, b)')
  })

  it('renders broken path code-span as non-clickable struck-through chip', () => {
    const onLinkClick = vi.fn()
    const frag = renderInline(
      [{ kind: 'code-span', text: 'C:\\missing\\ghost.md', broken: true }],
      { ...opts, onLinkClick }
    )
    const div = document.createElement('div')
    div.appendChild(frag)
    // No role=link on broken paths -- not interactive.
    expect(div.querySelector('span[role="link"]')).toBeNull()
    const span = div.querySelector('span[data-broken="true"]') as HTMLElement | null
    expect(span, 'expected broken-path span').not.toBeNull()
    expect(span!.textContent).toBe('C:\\missing\\ghost.md')
    expect(span!.style.textDecoration).toContain('line-through')
    span!.click()
    expect(onLinkClick).not.toHaveBeenCalled()
  })

  it('renders link as <a> with href', () => {
    const frag = renderInline(
      [{ kind: 'link', href: 'https://logmark.dev', children: [{ kind: 'text', text: 'site' }] }],
      opts
    )
    const div = document.createElement('div')
    div.appendChild(frag)
    const a = div.querySelector('a')
    expect(a?.getAttribute('href')).toBe('https://logmark.dev')
    expect(a?.textContent).toBe('site')
    expect(a?.style.color).toBe('var(--lb-accent)')
  })

  it('classifies external link by https prefix', () => {
    const onLinkClick = vi.fn()
    const frag = renderInline(
      [{ kind: 'link', href: 'https://x', children: [{ kind: 'text', text: 'x' }] }],
      { ...opts, onLinkClick }
    )
    const div = document.createElement('div')
    div.appendChild(frag)
    div.querySelector('a')!.click()
    expect(onLinkClick).toHaveBeenCalledWith(
      expect.objectContaining({ href: 'https://x', kind: 'external' })
    )
  })

  it('classifies relative md link', () => {
    const onLinkClick = vi.fn()
    const frag = renderInline(
      [{ kind: 'link', href: 'notes.md', children: [{ kind: 'text', text: 'n' }] }],
      { ...opts, onLinkClick }
    )
    const div = document.createElement('div')
    div.appendChild(frag)
    div.querySelector('a')!.click()
    expect(onLinkClick).toHaveBeenCalledWith(
      expect.objectContaining({ href: 'notes.md', kind: 'relative-md' })
    )
  })

  it('classifies anchor link as internal', () => {
    const onLinkClick = vi.fn()
    const frag = renderInline(
      [{ kind: 'link', href: '#section', children: [{ kind: 'text', text: 's' }] }],
      { ...opts, onLinkClick }
    )
    const div = document.createElement('div')
    div.appendChild(frag)
    div.querySelector('a')!.click()
    expect(onLinkClick).toHaveBeenCalledWith(
      expect.objectContaining({ href: '#section', kind: 'internal' })
    )
  })

  it('classifies wikilink (extension-less, protocol-less path) as wikilink', () => {
    const onLinkClick = vi.fn()
    const frag = renderInline(
      [{ kind: 'link', href: 'my-page', children: [{ kind: 'text', text: 'My Page' }] }],
      { ...opts, onLinkClick }
    )
    const div = document.createElement('div')
    div.appendChild(frag)
    div.querySelector('a')!.click()
    expect(onLinkClick).toHaveBeenCalledWith(
      expect.objectContaining({ href: 'my-page', kind: 'wikilink' })
    )
  })

  it('classifies nested wikilink path as wikilink', () => {
    const onLinkClick = vi.fn()
    const frag = renderInline(
      [{ kind: 'link', href: '.assets/foo/source', children: [{ kind: 'text', text: 'Raw thread' }] }],
      { ...opts, onLinkClick }
    )
    const div = document.createElement('div')
    div.appendChild(frag)
    div.querySelector('a')!.click()
    expect(onLinkClick).toHaveBeenCalledWith(
      expect.objectContaining({ kind: 'wikilink' })
    )
  })

  it('classifies relative path with non-md extension as internal (not wikilink)', () => {
    const onLinkClick = vi.fn()
    const frag = renderInline(
      [{ kind: 'link', href: 'images/foo.png', children: [{ kind: 'text', text: 'img' }] }],
      { ...opts, onLinkClick }
    )
    const div = document.createElement('div')
    div.appendChild(frag)
    div.querySelector('a')!.click()
    expect(onLinkClick).toHaveBeenCalledWith(
      expect.objectContaining({ kind: 'internal' })
    )
  })

  it('passes modifier keys', () => {
    const onLinkClick = vi.fn()
    const frag = renderInline(
      [{ kind: 'link', href: 'x', children: [{ kind: 'text', text: 'x' }] }],
      { ...opts, onLinkClick }
    )
    const div = document.createElement('div')
    div.appendChild(frag)
    const a = div.querySelector('a')!
    const ev = new MouseEvent('click', { bubbles: true, ctrlKey: true, shiftKey: false, altKey: true, cancelable: true })
    a.dispatchEvent(ev)
    expect(onLinkClick).toHaveBeenCalledWith(
      expect.objectContaining({ modifiers: { ctrl: true, shift: false, alt: true } })
    )
  })

  it('preserves prevent-default-then-emit ordering for clicks', () => {
    const onLinkClick = vi.fn()
    const frag = renderInline(
      [{ kind: 'link', href: 'https://x', children: [{ kind: 'text', text: 'x' }] }],
      { ...opts, onLinkClick }
    )
    const div = document.createElement('div')
    div.appendChild(frag)
    const a = div.querySelector('a')!
    const ev = new MouseEvent('click', { bubbles: true, cancelable: true })
    a.dispatchEvent(ev)
    expect(ev.defaultPrevented).toBe(true)
    expect(onLinkClick).toHaveBeenCalled()
  })

  it('handles nested emphasis', () => {
    const frag = renderInline(
      [
        {
          kind: 'emphasis',
          style: 'bold',
          children: [
            { kind: 'text', text: 'b ' },
            { kind: 'emphasis', style: 'italic', children: [{ kind: 'text', text: 'i' }] },
          ],
        },
      ],
      opts
    )
    const div = document.createElement('div')
    div.appendChild(frag)
    expect(div.querySelector('strong em')?.textContent).toBe('i')
  })
})
