import type {
  InlineNode,
  MarkdownRenderOptions,
  LinkClickEvent,
  InlineImage,
} from '../types/markdown-doc.js'

const INLINE_TAG_RE = /(^|[^\w/])#([A-Za-z0-9][A-Za-z0-9_-]*)/g

export function renderInline(
  nodes: InlineNode[],
  options: MarkdownRenderOptions
): DocumentFragment {
  const frag = document.createDocumentFragment()
  for (const node of nodes) {
    frag.appendChild(renderInlineNode(node, options))
  }
  return frag
}

function renderInlineNode(node: InlineNode, options: MarkdownRenderOptions): Node {
  switch (node.kind) {
    case 'text':
      return renderText(node.text, options)
    case 'emphasis':
      return renderEmphasis(node.style, node.children, options)
    case 'code-span':
      return renderCodeSpan(node.text, options, node.broken === true)
    case 'link':
      return renderLink(node.href, node.title, node.children, options)
    case 'line-break':
      return document.createElement('br')
    case 'inline-image':
      return renderInlineImage(node, options)
  }
}

function renderText(text: string, options: MarkdownRenderOptions): Node {
  if (!options.onTagClick) return document.createTextNode(text)

  INLINE_TAG_RE.lastIndex = 0
  const frag = document.createDocumentFragment()
  let cursor = 0
  let match: RegExpExecArray | null

  while ((match = INLINE_TAG_RE.exec(text)) !== null) {
    const prefix = match[1] ?? ''
    const tag = match[2]
    const tagStart = match.index + prefix.length

    if (tagStart > cursor) {
      frag.appendChild(document.createTextNode(text.slice(cursor, tagStart)))
    }

    const button = document.createElement('button')
    button.type = 'button'
    button.className = 'lb-md-tag-link'
    button.setAttribute('aria-label', `Filter by #${tag}`)
    button.title = `Filter by #${tag}`
    button.textContent = `#${tag}`
    button.style.border = '0'
    button.style.background = 'transparent'
    button.style.color = 'var(--lb-signal-positive)'
    button.style.cursor = 'pointer'
    button.style.font = 'inherit'
    button.style.fontWeight = '600'
    button.style.padding = '0'

    button.addEventListener('click', (ev) => {
      ev.preventDefault()
      ev.stopPropagation()
      options.onTagClick?.(tag)
    })

    frag.appendChild(button)
    cursor = tagStart + tag.length + 1
  }

  if (cursor < text.length) {
    frag.appendChild(document.createTextNode(text.slice(cursor)))
  }

  return frag.childNodes.length > 0 ? frag : document.createTextNode(text)
}

function renderEmphasis(
  style: 'bold' | 'italic' | 'bold-italic' | 'strikethrough',
  children: InlineNode[],
  options: MarkdownRenderOptions
): HTMLElement {
  if (style === 'strikethrough') {
    const del = document.createElement('del')
    del.style.textDecoration = 'line-through'
    del.appendChild(renderInline(children, options))
    return del
  }
  if (style === 'bold-italic') {
    const strong = document.createElement('strong')
    const em = document.createElement('em')
    em.appendChild(renderInline(children, options))
    strong.appendChild(em)
    return strong
  }
  const tag = style === 'bold' ? 'strong' : 'em'
  const el = document.createElement(tag)
  el.appendChild(renderInline(children, options))
  return el
}

// Windows absolute path: drive letter + colon + slash or backslash. Caught
// inside backtick code spans only -- prose-text auto-link is intentionally
// out of scope to avoid false positives on paths embedded in sentences.
const ABSOLUTE_PATH_RE = /^[A-Za-z]:[\\/]/

function renderCodeSpan(text: string, options: MarkdownRenderOptions, broken: boolean): HTMLElement {
  if (ABSOLUTE_PATH_RE.test(text)) {
    return broken ? renderBrokenPathCodeSpan(text) : renderPathCodeSpan(text, options)
  }
  const code = document.createElement('code')
  code.textContent = text
  code.style.fontFamily = 'var(--lb-font-mono)'
  code.style.fontSize = 'var(--lb-font-size-small)'
  code.style.backgroundColor = 'var(--lb-code-bg)'
  code.style.padding = '1px 4px'
  code.style.borderRadius = '3px'
  return code
}

function renderBrokenPathCodeSpan(text: string): HTMLElement {
  // Filesystem check upstream determined the target doesn't exist. Show the
  // path so the user can still read it (and copy it manually if needed) but
  // make it visually unmistakable: dimmed, strikethrough, no click affordance.
  const el = document.createElement('span')
  el.textContent = text
  el.setAttribute('data-broken', 'true')
  el.setAttribute('title', 'Path not found on disk')
  el.style.fontFamily = 'var(--lb-font-mono)'
  el.style.fontSize = 'var(--lb-font-size-small)'
  el.style.backgroundColor = 'var(--lb-code-bg)'
  el.style.padding = '1px 4px'
  el.style.borderRadius = '3px'
  el.style.color = 'var(--lb-text-muted, rgb(136 136 136))'
  el.style.textDecoration = 'line-through'
  el.style.opacity = '0.7'
  el.style.cursor = 'not-allowed'
  el.style.wordBreak = 'break-all'
  return el
}

function renderPathCodeSpan(text: string, options: MarkdownRenderOptions): HTMLElement {
  // Render as <span> rather than <a>: a real <a href="C:\...\"> in a WebView2
  // host gets normalised to file:///C:/... and the OS shell will pick the
  // navigation up before our click handler can preventDefault, breaking the
  // page render and dumping the user into a file:// folder listing. A span
  // styled like a link sidesteps the browser's anchor machinery entirely.
  const el = document.createElement('span')
  el.textContent = text
  el.setAttribute('role', 'link')
  el.setAttribute('data-path', text)
  el.style.fontFamily = 'var(--lb-font-mono)'
  el.style.fontSize = 'var(--lb-font-size-small)'
  el.style.backgroundColor = 'var(--lb-code-bg)'
  el.style.padding = '1px 4px'
  el.style.borderRadius = '3px'
  el.style.color = 'var(--lb-accent)'
  el.style.cursor = 'pointer'
  el.style.wordBreak = 'break-all'

  el.addEventListener('click', (ev) => {
    ev.preventDefault()
    ev.stopPropagation()
    if (!options.onLinkClick) return
    const event: LinkClickEvent = {
      href: text,
      kind: 'absolute-path',
      modifiers: { ctrl: ev.ctrlKey, shift: ev.shiftKey, alt: ev.altKey },
    }
    options.onLinkClick(event)
  })

  return el
}

function renderLink(
  href: string,
  title: string | undefined,
  children: InlineNode[],
  options: MarkdownRenderOptions
): HTMLElement {
  const a = document.createElement('a')
  a.setAttribute('href', href)
  if (title) a.setAttribute('title', title)
  a.style.color = 'var(--lb-accent)'
  a.style.textDecoration = 'underline'
  a.appendChild(renderInline(children, options))

  a.addEventListener('click', (ev) => {
    if (!options.onLinkClick) return
    ev.preventDefault()
    const event: LinkClickEvent = {
      href,
      kind: classifyLink(href),
      modifiers: { ctrl: ev.ctrlKey, shift: ev.shiftKey, alt: ev.altKey },
    }
    options.onLinkClick(event)
  })

  return a
}

function classifyLink(href: string): LinkClickEvent['kind'] {
  if (/^https?:\/\//i.test(href)) return 'external'
  if (href.startsWith('#')) return 'internal'
  if (/\.md(?:#|$)/i.test(href)) return 'relative-md'
  // Wikilinks: parseInline emits LinkInline { href: <wikilink path> } for [[...]]
  // syntax. Heuristic: extension-less, protocol-less hrefs that aren't anchors
  // are wikilink targets (vault page slugs or asset paths like
  // `.assets/<slug>/source`). Hrefs with file extensions other than .md are
  // treated as 'internal' relative paths -- they could be standard markdown
  // links to non-md assets, which is an unusual but valid pattern.
  if (!/^[a-z]+:/i.test(href) && !/\.[a-z0-9]+(?:#|$)/i.test(href)) return 'wikilink'
  return 'internal'
}

function renderInlineImage(node: InlineImage, options: MarkdownRenderOptions): HTMLElement {
  const img = document.createElement('img')
  const resolved = options.resolveAssetUrl ? options.resolveAssetUrl(node.src) : node.src
  img.setAttribute('src', resolved)
  img.alt = node.alt
  if (node.title) img.title = node.title
  img.style.maxHeight = '1.6em'
  img.style.verticalAlign = 'middle'
  img.style.display = 'inline-block'
  return img
}
