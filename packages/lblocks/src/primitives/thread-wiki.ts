import type {
  ThreadWiki,
  ThreadWikiMeta,
  ThreadWikiBlock,
  ThreadWikiSection,
  ThreadParticipant,
  ThreadSourceLink,
} from '../types/thread-wiki.js'
import type { MarkdownBlock, MarkdownRenderOptions } from '../types/markdown-doc.js'
import { parseMarkdown } from '../engine/markdown-parser.js'
import { renderHeading } from './markdown-heading.js'
import { renderParagraph } from './markdown-paragraph.js'
import { renderList } from './markdown-list.js'
import { renderCodeblock } from './markdown-codeblock.js'
import { renderBlockquote } from './markdown-blockquote.js'
import { renderTable } from './markdown-table.js'
import { renderImage } from './markdown-image.js'
import { renderDivider } from './markdown-divider.js'

const COLLAPSED_PILL_LIMIT = 12

export function parseThreadWiki(raw: string): ThreadWiki {
  const lines = raw.replace(/\r\n/g, '\n').split('\n')

  const meta: ThreadWikiMeta = {
    tags: [],
    participants: [],
    sources: [],
  }

  let i = 0
  // Frontmatter block
  if (i < lines.length && lines[i] === '---') {
    i++
    while (i < lines.length && lines[i] !== '---') {
      const line = lines[i]
      const scalarMatch = line.match(/^([a-z_]+):\s*(.*)$/i)
      if (scalarMatch) {
        const key = scalarMatch[1]
        const value = stripQuotes(scalarMatch[2].trim())
        i++
        // Check whether the next lines are a list (`- item` indented)
        if (value === '' && i < lines.length && /^\s*-\s+/.test(lines[i])) {
          const listItems: string[] = []
          while (i < lines.length && /^\s*-\s+/.test(lines[i])) {
            listItems.push(stripQuotes(lines[i].replace(/^\s*-\s+/, '').trim()))
            i++
          }
          assignMetaList(meta, key, listItems)
          continue
        }
        assignMetaScalar(meta, key, value)
        continue
      }
      i++ // skip unrecognized line
    }
    if (i < lines.length) i++ // skip closing ---
  }

  // Body
  const bodyLines = lines.slice(i)
  // Extract `# Title` (first h1) and strip from body
  let bodyTitle: string | undefined
  for (let j = 0; j < bodyLines.length; j++) {
    const m = bodyLines[j].match(/^#\s+(.+?)\s*#*\s*$/)
    if (m) {
      bodyTitle = m[1].trim()
      bodyLines.splice(j, 1)
      break
    }
  }
  if (bodyTitle) meta.title = bodyTitle
  if (!meta.title) meta.title = '(untitled thread)'

  // Split into sections by `## ` headings
  const sections: ThreadWikiSection[] = []
  let currentHeading: string | null = null
  let currentBody: string[] = []
  const flushSection = () => {
    if (currentHeading !== null) {
      sections.push({ heading: currentHeading, body: currentBody.join('\n').trim() })
    }
    currentHeading = null
    currentBody = []
  }
  for (const line of bodyLines) {
    const h2 = line.match(/^##\s+(.+?)\s*#*\s*$/)
    if (h2) {
      flushSection()
      currentHeading = h2[1].trim()
      continue
    }
    if (currentHeading !== null) {
      currentBody.push(line)
    }
  }
  flushSection()

  // Strip `## Sources` from sections + parse it into meta.sources
  const sourcesIdx = sections.findIndex(s => s.heading === 'Sources')
  if (sourcesIdx >= 0) {
    const sourcesSection = sections.splice(sourcesIdx, 1)[0]
    meta.sources = parseSourceLinks(sourcesSection.body)
  }

  // Enrich participants: exclude OP, derive URL
  meta.participants = meta.participants
    .filter(p => p.handle !== meta.op)
    .map(p => ({
      handle: p.handle,
      url: p.url ?? `https://x.com/${p.handle.replace(/^@/, '')}`,
    }))

  return { meta, sections }
}

function stripQuotes(s: string): string {
  if ((s.startsWith("'") && s.endsWith("'")) || (s.startsWith('"') && s.endsWith('"'))) {
    return s.slice(1, -1)
  }
  return s
}

function assignMetaScalar(meta: ThreadWikiMeta, key: string, value: string): void {
  switch (key) {
    case 'title': meta.title = value; break
    case 'slug': meta.slug = value; break
    case 'op': meta.op = value; break
    case 'op_display': meta.opDisplay = value; break
    case 'source_url': meta.sourceUrl = value; break
    case 'created':
    case 'last_distilled': meta.capturedAt = value; break
    case 'reply_count': meta.replyCount = parseInt(value, 10) || undefined; break
    case 'images': meta.imageCount = parseInt(value, 10) || undefined; break
    case 'status': meta.status = value; break
    // type, distillation_version, etc. - ignored
  }
}

function assignMetaList(meta: ThreadWikiMeta, key: string, items: string[]): void {
  switch (key) {
    case 'tags': meta.tags = items; break
    case 'participants':
      meta.participants = items.map<ThreadParticipant>(handle => ({ handle }))
      break
    // sources: nested list-of-objects shape; parser is intentionally minimal here.
    // The body's `## Sources` section is the canonical link source for v1.
  }
}

function parseSourceLinks(body: string): ThreadSourceLink[] {
  const result: ThreadSourceLink[] = []
  const lines = body.split('\n')
  for (const line of lines) {
    // Match `- [label](url)` (standard markdown link in a bullet)
    const stdMatch = line.match(/^\s*-\s*\[([^\]]+)\]\(([^)]+)\)\s*$/)
    if (stdMatch) {
      const label = stdMatch[1]
      const href = stdMatch[2]
      result.push({
        kind: classifyStdLink(label, href),
        href,
        label,
        isWikilink: false,
      })
      continue
    }
    // Match `- [[path|label]]` or `- [[path]]` (wikilink in a bullet)
    const wikiMatch = line.match(/^\s*-\s*\[\[([^\]|]+)(?:\|([^\]]+))?\]\]\s*$/)
    if (wikiMatch) {
      const href = wikiMatch[1]
      const label = wikiMatch[2] ?? href
      result.push({
        kind: classifyWikilink(href, label),
        href,
        label,
        isWikilink: true,
      })
    }
  }
  return result
}

function classifyStdLink(label: string, _href: string): ThreadSourceLink['kind'] {
  if (/^Original tweet$/i.test(label)) return 'original-tweet'
  return 'other'
}

function classifyWikilink(href: string, label: string): ThreadSourceLink['kind'] {
  if (/images\/.+\.transcript$/i.test(href) || /Image transcript/i.test(label)) return 'image-transcript'
  if (/source$/i.test(href) || /Raw thread/i.test(label)) return 'raw-thread'
  return 'other'
}

export function renderThreadWiki(
  block: ThreadWikiBlock,
  options: MarkdownRenderOptions
): HTMLElement {
  const wiki = parseThreadWiki(block.body)
  const root = document.createElement('div')
  root.className = 'lb-thread-wiki'
  root.style.fontFamily = 'var(--lb-font-family)'
  root.style.color = 'var(--lb-text-primary)'
  root.style.padding = '20px 24px'
  root.style.maxWidth = '760px'
  root.style.margin = '0 auto'

  root.appendChild(renderHeader(wiki.meta, options))
  root.appendChild(renderParticipantsBlock(wiki.meta, options))
  root.appendChild(makeDivider())
  root.appendChild(renderBody(wiki.sections, options))
  if (wiki.meta.sources.length > 0) {
    root.appendChild(makeDivider())
    root.appendChild(renderSourcesFooter(wiki.meta.sources, options))
  }
  return root
}

// Intercept clicks on <a> elements so the WebView host doesn't navigate to
// the raw href (which causes ERR_FILE_NOT_FOUND for relative wikilink targets
// and unwanted in-WebView navigation for external URLs). Dispatches via
// options.onLinkClick when available; falls back to silent preventDefault.
function attachLinkHandler(
  a: HTMLAnchorElement,
  href: string,
  linkKind: 'external' | 'wikilink',
  options: MarkdownRenderOptions
): void {
  a.addEventListener('click', (ev) => {
    ev.preventDefault()
    if (!options.onLinkClick) return
    options.onLinkClick({
      href,
      kind: linkKind,
      modifiers: { ctrl: ev.ctrlKey, shift: ev.shiftKey, alt: ev.altKey },
    })
  })
}

function renderHeader(meta: ThreadWikiMeta, options: MarkdownRenderOptions): HTMLElement {
  const header = document.createElement('header')
  header.className = 'lb-thread-header'

  const title = document.createElement('h1')
  title.className = 'lb-thread-title'
  title.style.fontSize = '24px'
  title.style.fontWeight = '600'
  title.style.margin = '0 0 8px 0'
  title.textContent = meta.title ?? '(untitled thread)'
  header.appendChild(title)

  // OP row
  if (meta.op) {
    const opRow = document.createElement('div')
    opRow.className = 'lb-thread-op-row'
    opRow.style.display = 'flex'
    opRow.style.alignItems = 'center'
    opRow.style.gap = '8px'
    opRow.style.marginBottom = '4px'

    const opPill = document.createElement('a')
    const opHref = meta.sourceUrl ?? `https://x.com/${meta.op.replace(/^@/, '')}`
    opPill.className = 'lb-thread-pill lb-thread-pill--op'
    opPill.href = opHref
    opPill.target = '_blank'
    opPill.rel = 'noopener'
    opPill.textContent = meta.op
    attachLinkHandler(opPill, opHref, 'external', options)
    opPill.style.background = 'var(--lb-bg-secondary)'
    opPill.style.color = 'var(--lb-text-secondary)'
    opPill.style.border = '1px solid var(--lb-border)'
    opPill.style.padding = '4px 12px'
    opPill.style.borderRadius = '12px'
    opPill.style.fontSize = '13px'
    opPill.style.fontWeight = '600'
    opPill.style.textDecoration = 'none'
    opRow.appendChild(opPill)

    if (meta.opDisplay) {
      const name = document.createElement('span')
      name.className = 'lb-thread-op-name'
      name.style.color = 'var(--lb-text-secondary)'
      name.style.fontSize = '13px'
      name.textContent = meta.opDisplay
      opRow.appendChild(name)
    }
    header.appendChild(opRow)
  }

  // Meta strip
  const metaStrip = document.createElement('div')
  metaStrip.className = 'lb-thread-meta'
  metaStrip.style.fontSize = '12px'
  metaStrip.style.color = 'var(--lb-text-muted)'
  metaStrip.style.marginBottom = '12px'
  const parts: string[] = []
  if (meta.replyCount !== undefined) parts.push(`${meta.replyCount} replies`)
  if (meta.imageCount !== undefined) parts.push(`${meta.imageCount} images`)
  if (meta.capturedAt) parts.push(`captured ${formatDate(meta.capturedAt)}`)
  metaStrip.textContent = parts.join(' · ')  // middle dot
  if (meta.sourceUrl) {
    metaStrip.appendChild(document.createTextNode(' · '))
    const a = document.createElement('a')
    a.href = meta.sourceUrl
    a.target = '_blank'
    a.rel = 'noopener'
    a.textContent = 'source'
    a.style.color = 'var(--lb-accent-thread)'
    attachLinkHandler(a, meta.sourceUrl, 'external', options)
    metaStrip.appendChild(a)
  }
  header.appendChild(metaStrip)

  // Tags
  if (meta.tags.length > 0) {
    const tagsRow = document.createElement('div')
    tagsRow.className = 'lb-thread-tags'
    tagsRow.style.display = 'flex'
    tagsRow.style.gap = '6px'
    tagsRow.style.flexWrap = 'wrap'
    tagsRow.style.fontSize = '11px'
    tagsRow.style.color = 'var(--lb-text-muted)'
    tagsRow.style.marginBottom = '16px'
    for (const tag of meta.tags) {
      const chip = document.createElement('span')
      chip.className = 'lb-thread-tag'
      chip.textContent = `#${tag}`
      tagsRow.appendChild(chip)
    }
    header.appendChild(tagsRow)
  }

  return header
}

function renderParticipantsBlock(meta: ThreadWikiMeta, options: MarkdownRenderOptions): HTMLElement {
  const section = document.createElement('section')
  section.className = 'lb-thread-participants'
  section.style.margin = '16px 0 24px 0'

  const label = document.createElement('div')
  label.className = 'lb-thread-participants-label'
  label.style.fontSize = '12px'
  label.style.color = 'var(--lb-text-muted)'
  label.style.display = 'flex'
  label.style.justifyContent = 'space-between'
  label.style.alignItems = 'baseline'
  label.style.marginBottom = '8px'

  const labelText = document.createElement('span')
  labelText.textContent = `Participants (${meta.participants.length})`
  label.appendChild(labelText)

  const pills = document.createElement('div')
  pills.className = 'lb-thread-pills'
  pills.style.display = 'flex'
  pills.style.flexWrap = 'wrap'
  pills.style.gap = '6px'

  if (meta.participants.length > COLLAPSED_PILL_LIMIT) {
    pills.setAttribute('data-collapsed', 'true')
    const toggle = document.createElement('button')
    toggle.className = 'lb-thread-participants-toggle'
    toggle.textContent = `Show all ${meta.participants.length} v`
    toggle.style.background = 'none'
    toggle.style.border = 'none'
    toggle.style.color = 'var(--lb-accent-thread)'
    toggle.style.fontSize = '11px'
    toggle.style.cursor = 'pointer'
    toggle.style.padding = '0'
    toggle.addEventListener('click', () => {
      const collapsed = pills.getAttribute('data-collapsed') === 'true'
      pills.setAttribute('data-collapsed', collapsed ? 'false' : 'true')
      // Toggle visibility on overflow pills
      const all = pills.querySelectorAll('.lb-thread-pill')
      all.forEach((el, idx) => {
        if (idx >= COLLAPSED_PILL_LIMIT) {
          (el as HTMLElement).style.display = collapsed ? 'inline-block' : 'none'
        }
      })
      toggle.textContent = collapsed ? 'Show fewer ^' : `Show all ${meta.participants.length} v`
    })
    label.appendChild(toggle)
  }

  meta.participants.forEach((p, idx) => {
    const pill = document.createElement('a')
    const pillHref = p.url ?? `https://x.com/${p.handle.replace(/^@/, '')}`
    pill.className = 'lb-thread-pill'
    pill.href = pillHref
    pill.target = '_blank'
    pill.rel = 'noopener'
    pill.textContent = p.handle
    attachLinkHandler(pill, pillHref, 'external', options)
    pill.style.padding = '2px 10px'
    pill.style.borderRadius = '12px'
    pill.style.fontSize = '12px'
    pill.style.color = 'var(--lb-text-secondary)'
    pill.style.border = '1px solid var(--lb-border)'
    pill.style.textDecoration = 'none'
    pill.style.display = 'inline-block'
    if (meta.participants.length > COLLAPSED_PILL_LIMIT && idx >= COLLAPSED_PILL_LIMIT) {
      pill.style.display = 'none'
    }
    pills.appendChild(pill)
  })

  section.appendChild(label)
  section.appendChild(pills)
  return section
}

function renderBody(sections: ThreadWikiSection[], options: MarkdownRenderOptions): HTMLElement {
  const main = document.createElement('main')
  main.className = 'lb-thread-body'
  for (const s of sections) {
    const sec = document.createElement('section')
    const h = document.createElement('h2')
    h.textContent = s.heading
    h.style.fontSize = '16px'
    h.style.fontWeight = '600'
    h.style.margin = '16px 0 8px 0'
    sec.appendChild(h)
    const blocks = parseMarkdown(s.body)
    for (const b of blocks) {
      sec.appendChild(dispatchBlock(b, options))
    }
    main.appendChild(sec)
  }
  return main
}

function renderSourcesFooter(sources: ThreadSourceLink[], options: MarkdownRenderOptions): HTMLElement {
  const footer = document.createElement('footer')
  footer.className = 'lb-thread-sources'
  footer.style.marginTop = '24px'
  footer.style.paddingTop = '16px'
  footer.style.borderTop = '1px solid var(--lb-border)'

  const h = document.createElement('h2')
  h.textContent = 'Sources'
  h.style.fontSize = '16px'
  h.style.fontWeight = '600'
  h.style.margin = '0 0 8px 0'
  footer.appendChild(h)

  const ul = document.createElement('ul')
  ul.style.listStyle = 'none'
  ul.style.padding = '0'
  ul.style.margin = '0'
  for (const s of sources) {
    const li = document.createElement('li')
    li.setAttribute('data-kind', s.kind)
    li.style.padding = '4px 0'
    const icon = document.createElement('span')
    icon.style.color = 'var(--lb-text-muted)'
    icon.style.marginRight = '8px'
    icon.style.fontFamily = 'var(--lb-font-mono, monospace)'
    icon.textContent = iconFor(s.kind)
    const a = document.createElement('a')
    a.href = s.href
    a.textContent = s.label
    a.target = s.isWikilink ? '_self' : '_blank'
    a.rel = 'noopener'
    a.style.color = 'var(--lb-accent-thread)'
    attachLinkHandler(a, s.href, s.isWikilink ? 'wikilink' : 'external', options)
    li.appendChild(icon)
    li.appendChild(a)
    ul.appendChild(li)
  }
  footer.appendChild(ul)
  return footer
}

function iconFor(kind: ThreadSourceLink['kind']): string {
  switch (kind) {
    case 'original-tweet': return '>>'
    case 'raw-thread': return '[]'
    case 'image-transcript': return '[img]'
    default: return '--'
  }
}

function makeDivider(): HTMLElement {
  const hr = document.createElement('hr')
  hr.className = 'lb-thread-divider'
  hr.style.border = 'none'
  hr.style.borderTop = '1px solid var(--lb-border)'
  hr.style.margin = '16px 0'
  return hr
}

function formatDate(iso: string): string {
  // 2026-05-05T08:20:32+00:00 -> 2026-05-05
  const m = iso.match(/^(\d{4}-\d{2}-\d{2})/)
  return m ? m[1] : iso
}

function dispatchBlock(block: MarkdownBlock, options: MarkdownRenderOptions): HTMLElement {
  switch (block.kind) {
    case 'heading': return renderHeading(block, options)
    case 'paragraph': return renderParagraph(block, options)
    case 'list': return renderList(block, options, (b) => dispatchBlock(b, options))
    case 'codeblock': return renderCodeblock(block, options)
    case 'blockquote': return renderBlockquote(block, options, (b) => dispatchBlock(b, options))
    case 'table': return renderTable(block, options)
    case 'image': return renderImage(block, options)
    case 'divider': return renderDivider(block, options)
    default: {
      const span = document.createElement('span')
      span.textContent = `(unsupported block: ${(block as { kind: string }).kind})`
      return span
    }
  }
}
