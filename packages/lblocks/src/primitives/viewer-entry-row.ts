import type { PrimitiveOptions } from '../types/primitives.js'
import type { ViewerEntryRowData, ViewerEntryRowEntry } from '../types/reader.js'
import type { InlineNode } from '../types/markdown-doc.js'
import { parseInline } from '../engine/markdown-parser.js'
import { entryTypeColor } from '../util/entry-type-tokens.js'

const INLINE_TAG_RE = /(^|[^\w/])#([A-Za-z0-9][A-Za-z0-9_-]*)/g

function formatDate(timestamp: string): string {
  const date = new Date(timestamp)
  if (Number.isNaN(date.getTime())) return timestamp
  return date.toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' })
}

function classifyLink(href: string): 'external' | 'internal' | 'relative-md' | 'wikilink' {
  if (/^https?:\/\//i.test(href)) return 'external'
  if (href.startsWith('#')) return 'internal'
  if (/\.md(?:#|$)/i.test(href)) return 'relative-md'
  if (!/^[a-z]+:/i.test(href) && !/\.[a-z0-9]+(?:#|$)/i.test(href)) return 'wikilink'
  return 'internal'
}

function appendTaggedText(root: HTMLElement, text: string, options: Partial<PrimitiveOptions>): void {
  INLINE_TAG_RE.lastIndex = 0
  let cursor = 0
  let match: RegExpExecArray | null
  while ((match = INLINE_TAG_RE.exec(text)) !== null) {
    const prefix = match[1] ?? ''
    const tag = match[2]
    const tagStart = match.index + prefix.length
    if (tagStart > cursor) {
      root.append(document.createTextNode(text.slice(cursor, tagStart)))
    }
    const tagEl = options.onTagClick ? document.createElement('button') : document.createElement('span')
    tagEl.className = options.onTagClick
      ? 'lb-viewer-entry-row-inline-tag lb-viewer-entry-row-inline-tag-button'
      : 'lb-viewer-entry-row-inline-tag'
    tagEl.textContent = `#${tag}`
    tagEl.style.color = 'var(--lb-accent)'
    tagEl.style.font = 'inherit'
    if (tagEl instanceof HTMLButtonElement) {
      tagEl.type = 'button'
      tagEl.setAttribute('aria-label', `Filter by #${tag}`)
      tagEl.title = `Filter by #${tag}`
      tagEl.style.border = '0'
      tagEl.style.background = 'transparent'
      tagEl.style.padding = '0'
      tagEl.style.cursor = 'pointer'
      tagEl.addEventListener('click', (event) => {
        event.preventDefault()
        event.stopPropagation()
        options.onTagClick?.(tag)
      })
    }
    root.append(tagEl)
    cursor = tagStart + tag.length + 1
  }
  if (cursor < text.length) {
    root.append(document.createTextNode(text.slice(cursor)))
  }
}

function appendInlineNodes(root: HTMLElement, nodes: InlineNode[], options: Partial<PrimitiveOptions>): void {
  for (const node of nodes) {
    if (node.kind === 'text') {
      appendTaggedText(root, node.text, options)
      continue
    }
    if (node.kind === 'emphasis') {
      const emphasis = document.createElement(
        node.style === 'bold' ? 'strong' : node.style === 'strikethrough' ? 'del' : 'em'
      )
      appendInlineNodes(emphasis, node.children, options)
      root.appendChild(emphasis)
      continue
    }
    if (node.kind === 'code-span') {
      const code = document.createElement('code')
      code.className = 'lb-viewer-entry-row-code'
      code.textContent = node.text
      code.style.fontFamily = 'var(--lb-font-mono)'
      root.appendChild(code)
      continue
    }
    if (node.kind === 'link') {
      const link = document.createElement('a')
      link.className = 'lb-viewer-entry-row-link-inline'
      link.href = node.href
      if (node.title) link.title = node.title
      link.style.color = 'var(--lb-accent)'
      link.style.textDecoration = 'none'
      appendInlineNodes(link, node.children, options)
      const linkKind = classifyLink(node.href)
      link.addEventListener('click', (event) => {
        if (!options.onLinkClick) {
          // Host didn't supply a resolver. External links navigate
          // normally; wikilinks / relative-md / internal anchors would
          // otherwise resolve against the current URL and break (e.g.
          // `[[foo]]` from `/ink/tags/power` becomes
          // `/ink/tags/foo`). Block the broken nav and let the row's
          // entry-focus click bubble so the host routes to the source
          // entry deep-link instead.
          if (linkKind !== 'external') event.preventDefault()
          return
        }
        event.stopPropagation()
        event.preventDefault()
        options.onLinkClick({
          href: node.href,
          kind: linkKind,
          modifiers: {
            ctrl: event.ctrlKey,
            shift: event.shiftKey,
            alt: event.altKey,
          },
        })
      })
      root.appendChild(link)
      continue
    }
    if (node.kind === 'line-break') {
      root.append(document.createTextNode(' '))
      continue
    }
    const link = document.createElement('a')
    link.className = 'lb-viewer-entry-row-link-inline'
    link.href = node.src
    link.textContent = node.alt || 'image'
    link.style.color = 'var(--lb-accent)'
    link.style.textDecoration = 'none'
    root.appendChild(link)
  }
}

// Rim + type-pill color source. 'signal' (frame T / D7) lets rows whose
// pill vocabulary isn't one of the 8 canonical entry types (feed's
// concept/digest/session/activity, health's changed/deleted/parse-error)
// carry an explicit color instead of falling back to entryTypeColor's
// unknown-type 'log' default. 'type' or absent is the existing behavior,
// byte-identical to before this option existed.
function resolveRowColor(entry: ViewerEntryRowEntry): string {
  if (entry.rimTone === 'signal' && entry.signalColor) return entry.signalColor
  return entryTypeColor(entry.type)
}

// Leading selection checkbox (frame T / D7, cockpit 'inline' layout
// only): the fork retirement for search's distill-selection `ck-selrow`.
// A real <input type=checkbox> for a11y; click/change both stop
// propagation so the row's own entry-focus handler doesn't also fire.
// The host owns selection state (fires `entry-select`, re-renders with
// the updated `checked` prop) - this element never manages its own
// checked state beyond the native toggle.
function renderCheckbox(entry: ViewerEntryRowEntry, options: Partial<PrimitiveOptions>): HTMLInputElement {
  const checkbox = document.createElement('input')
  checkbox.type = 'checkbox'
  checkbox.className = 'lb-viewer-entry-row-checkbox'
  checkbox.checked = entry.checked === true
  checkbox.style.width = '15px'
  checkbox.style.height = '15px'
  checkbox.style.margin = '0'
  checkbox.style.accentColor = 'var(--lb-accent)'
  checkbox.style.cursor = 'pointer'
  checkbox.setAttribute('aria-label', `Select entry: ${entry.preview.slice(0, 60)}`)
  checkbox.addEventListener('click', (event) => {
    event.stopPropagation()
  })
  checkbox.addEventListener('change', () => {
    options.onAction?.({
      type: 'entry-select',
      target: entry.id,
      source: 'viewer-entry-row',
    })
  })
  return checkbox
}

// Trailing ghost-button action (frame T / D7, cockpit 'inline' layout
// only): the fork retirement for feed's `queue-row` Open link and
// health's `queue-row` Review link. Neutral-ghost per chip-taxonomy §9
// (Action) - these are housekeeping verbs (Open, Review), not the
// feature's earned amber accent (polish rule #2).
function renderTrailingActionButton(
  entry: ViewerEntryRowEntry,
  action: { label: string; action: string },
  options: Partial<PrimitiveOptions>
): HTMLButtonElement {
  const button = document.createElement('button')
  button.type = 'button'
  button.className = 'lb-viewer-entry-row-trailing-action'
  button.textContent = action.label
  button.style.border = '1px solid var(--lb-border-subtle)'
  button.style.borderRadius = 'var(--lb-radius-sm)'
  button.style.background = 'transparent'
  button.style.color = 'var(--lb-text-muted)'
  button.style.font = 'inherit'
  button.style.fontSize = 'var(--lb-font-size-meta, var(--lb-font-size-small))'
  button.style.padding = '4px 10px'
  button.style.whiteSpace = 'nowrap'
  button.style.cursor = options.onAction ? 'pointer' : 'default'
  button.addEventListener('click', (event) => {
    event.preventDefault()
    event.stopPropagation()
    options.onAction?.({
      type: action.action,
      target: entry.id,
      source: 'viewer-entry-row',
    })
  })
  return button
}

function renderArrow(): SVGSVGElement {
  const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg')
  svg.setAttribute('width', '11')
  svg.setAttribute('height', '11')
  svg.setAttribute('viewBox', '0 0 24 24')
  svg.setAttribute('fill', 'none')
  svg.setAttribute('stroke', 'currentColor')
  svg.setAttribute('stroke-width', '2.5')
  svg.setAttribute('stroke-linecap', 'round')
  svg.setAttribute('stroke-linejoin', 'round')
  svg.setAttribute('aria-hidden', 'true')
  const arrow = document.createElementNS('http://www.w3.org/2000/svg', 'path')
  arrow.setAttribute('d', 'M7 17 17 7')
  svg.appendChild(arrow)
  const cap = document.createElementNS('http://www.w3.org/2000/svg', 'path')
  cap.setAttribute('d', 'M7 7h10v10')
  svg.appendChild(cap)
  svg.style.verticalAlign = '-1px'
  svg.style.marginLeft = '4px'
  return svg
}

function isModifierClick(event: Event): boolean {
  const mouseEvent = event as Partial<MouseEvent>
  if (mouseEvent.ctrlKey || mouseEvent.metaKey || mouseEvent.shiftKey || mouseEvent.altKey) return true
  if (mouseEvent.button !== undefined && mouseEvent.button !== 0) return true
  return false
}

// Type chip is always a <button>: in vaults it filters the list by type
// (chip-taxonomy §5 Filter -- list-scoped); on tag-page feeds where the
// host doesn't pass onAction, the click is a harmless no-op. Visual
// treatment matches the prior <span> exactly (entry-type token color,
// mono caps, 10px). Polish rule #4: entry-type discrimination uses the
// entry-type palette via entryTypeColor().
function renderTypePill(
  entry: ViewerEntryRowEntry,
  color: string,
  options: Partial<PrimitiveOptions>
): HTMLButtonElement {
  const type = document.createElement('button')
  type.type = 'button'
  type.className = 'lb-viewer-entry-row-type ve-type lb-viewer-entry-row-type-button'
  type.textContent = entry.type
  type.style.color = color
  type.style.fontFamily = 'var(--lb-font-mono)'
  type.style.fontSize = '10px'
  type.style.fontWeight = '600'
  type.style.letterSpacing = '0.08em'
  type.style.textTransform = 'uppercase'
  type.style.border = '0'
  type.style.background = 'transparent'
  type.style.padding = '0'
  type.style.cursor = 'pointer'
  type.setAttribute('aria-label', `Filter by ${entry.type} entries`)
  type.title = `Filter by ${entry.type} entries`
  type.addEventListener('click', (event) => {
    event.preventDefault()
    event.stopPropagation()
    options.onAction?.({
      type: 'type-filter',
      target: entry.type,
      source: 'viewer-entry-row',
    })
  })
  return type
}

// READ-ONLY state glyph: the cockpit views and ratifies, it does not
// complete tasks. A span, never a checkbox input or button. Done =
// small muted filled square; open = empty square outline. Entries
// with no `state` get no glyph. inline-block keeps the glyph exempt
// from the line-through applied to done content. `withMargin` is true
// in the stacked layout where the glyph is prepended into the content
// flow; the inline layout gives the glyph its own grid cell and lets
// the grid gap carry the spacing.
function renderStateGlyph(state: 'open' | 'done', withMargin: boolean): HTMLSpanElement {
  const glyph = document.createElement('span')
  glyph.className = `lb-viewer-entry-row-state lb-viewer-entry-row-state-${state}`
  glyph.setAttribute('aria-hidden', 'true')
  glyph.style.display = 'inline-block'
  glyph.style.width = '9px'
  glyph.style.height = '9px'
  glyph.style.borderRadius = '2px'
  if (withMargin) glyph.style.marginRight = '8px'
  glyph.style.border = '1px solid var(--lb-text-dim)'
  glyph.style.background = state === 'done' ? 'var(--lb-text-dim)' : 'transparent'
  return glyph
}

function renderRow(
  entry: ViewerEntryRowEntry,
  showAttribution: boolean,
  options: Partial<PrimitiveOptions>,
  cockpit: boolean
): HTMLElement {
  const color = resolveRowColor(entry)
  const item = document.createElement('li')
  item.className = 'lb-viewer-entry-row-item'
  item.dataset.entryId = entry.id
  item.dataset.entryType = entry.type
  item.style.listStyle = 'none'
  item.style.setProperty('--lb-entry-row-color', color)

  const article = document.createElement('article')
  article.className = 'lb-viewer-entry-row ve-row ink-reader-entry-row'
  article.style.display = 'grid'
  article.style.gridTemplateColumns = '1fr auto'
  article.style.alignItems = 'start'
  article.style.gap = '12px 18px'
  article.style.padding = options.density === 'compact' ? '12px 0' : '18px 0'
  article.style.borderBottom = '1px solid var(--lb-border-subtle)'
  article.style.position = 'relative'
  article.style.cursor = entry.openUrl ? 'pointer' : 'default'
  article.style.transition = 'background 150ms ease-out'

  if (cockpit) {
    // Cockpit rim: entry-type discrimination on the row's left edge via
    // the per-type token already resolved into --lb-entry-row-color
    // (polish rule #4: entry-type palette, never signal colors).
    article.classList.add('lb-viewer-entry-row-cockpit')
    article.style.borderLeft = '2px solid var(--lb-entry-row-color)'
    article.style.paddingLeft = '14px'
  }

  // Row body click -> entry-focus (target = entry.id). Hosts decide whether
  // to focus a preview panel (in-vault reader) or navigate to the deep link
  // (tag-page feed). Open link click -> entry-open (target = openUrl) which
  // always means "navigate to the entry deep link." Modifier-clicks bail
  // out so the browser can open in a new tab via the anchor.
  const focusRow = (event: Event): void => {
    if (isModifierClick(event)) return
    if (!options.onAction) return
    event.preventDefault()
    event.stopPropagation()
    options.onAction({
      type: 'entry-focus',
      target: entry.id,
      source: 'viewer-entry-row',
    })
  }

  const openLink = (event: Event): void => {
    if (!entry.openUrl) return
    if (isModifierClick(event)) return
    if (!options.onAction) return
    event.preventDefault()
    event.stopPropagation()
    options.onAction({
      type: 'entry-open',
      target: entry.openUrl,
      source: 'viewer-entry-row',
    })
  }

  article.addEventListener('click', focusRow)
  article.tabIndex = 0
  article.setAttribute('role', 'button')
  article.setAttribute('aria-label', `Preview entry from ${formatDate(entry.timestamp)}`)
  article.addEventListener('keydown', (event) => {
    if (event.key !== 'Enter' && event.key !== ' ') return
    focusRow(event)
  })

  const body = document.createElement('div')
  body.className = 'lb-viewer-entry-row-body'
  body.style.minWidth = '0'

  const content = document.createElement('div')
  content.className = 'lb-viewer-entry-row-content ve-content ink-reader-entry-content'
  content.style.fontSize = 'var(--lb-font-size-lead, var(--lb-font-size-body))'
  content.style.lineHeight = '1.45'
  content.style.color = 'var(--lb-text-primary)'
  // Clamp the row preview to 2 lines. The list is for scanning; the side
  // preview panel carries the full content. Mirrors the desktop EntryCard
  // density (~2 lines/row) and the front-door supporting-row clamp pattern
  // in `apps/web/app/surfaces/ink/ink.css`.
  content.style.display = '-webkit-box'
  content.style.webkitLineClamp = '2'
  content.style.webkitBoxOrient = 'vertical'
  content.style.overflow = 'hidden'
  appendInlineNodes(content, parseInline(entry.preview), options)

  if (cockpit && entry.state) {
    content.prepend(renderStateGlyph(entry.state, true))
    if (entry.state === 'done') {
      content.style.textDecoration = 'line-through'
      content.style.color = 'var(--lb-text-muted)'
    }
  }

  body.appendChild(content)

  const meta = document.createElement('div')
  meta.className = 'lb-viewer-entry-row-meta ve-meta ink-reader-entry-meta'
  meta.style.display = 'flex'
  meta.style.flexWrap = 'wrap'
  meta.style.alignItems = 'center'
  meta.style.gap = '12px'
  meta.style.marginTop = '8px'
  meta.style.fontSize = 'var(--lb-font-size-meta, var(--lb-font-size-small))'
  meta.style.color = 'var(--lb-text-muted)'

  meta.appendChild(renderTypePill(entry, color, options))

  if (showAttribution && entry.attribution) {
    const sep = document.createElement('span')
    sep.setAttribute('aria-hidden', 'true')
    sep.textContent = '·'
    sep.style.color = 'var(--lb-text-dim)'
    meta.appendChild(sep)

    const attr = document.createElement('span')
    attr.className = 'lb-viewer-entry-row-attribution'
    attr.style.display = 'inline-flex'
    attr.style.alignItems = 'center'
    attr.style.gap = '6px'
    const handle = document.createElement('span')
    handle.className = 'lb-viewer-entry-row-handle'
    handle.textContent = `@${entry.attribution.handle.replace(/^@/, '')}`
    handle.style.color = 'var(--lb-accent)'
    attr.appendChild(handle)
    if (entry.attribution.vaultTitle) {
      const slash = document.createElement('span')
      slash.textContent = '/'
      slash.setAttribute('aria-hidden', 'true')
      slash.style.color = 'var(--lb-text-dim)'
      attr.appendChild(slash)
      const vault = document.createElement('span')
      vault.className = 'lb-viewer-entry-row-vault'
      vault.textContent = entry.attribution.vaultTitle
      vault.style.color = 'var(--lb-text-muted)'
      attr.appendChild(vault)
    }
    meta.appendChild(attr)
  }

  // Cockpit moves the time into the trailing route · time column, so the
  // meta line keeps only the type pill (+ attribution) and tag chips.
  if (!cockpit) {
    const dateSep = document.createElement('span')
    dateSep.setAttribute('aria-hidden', 'true')
    dateSep.textContent = '·'
    dateSep.style.color = 'var(--lb-text-dim)'
    meta.appendChild(dateSep)

    const time = document.createElement('time')
    time.className = 'lb-viewer-entry-row-time'
    time.dateTime = entry.timestamp
    time.textContent = entry.timeLabel ?? formatDate(entry.timestamp)
    meta.appendChild(time)
  }

  for (const tag of entry.tags.slice(0, 3)) {
    const chip = options.onTagClick ? document.createElement('button') : document.createElement('span')
    chip.className = options.onTagClick
      ? 'lb-viewer-entry-row-tag lb-viewer-entry-row-tag-button'
      : 'lb-viewer-entry-row-tag'
    chip.textContent = `#${tag}`
    chip.style.color = 'var(--lb-accent)'
    chip.style.font = 'inherit'
    if (chip instanceof HTMLButtonElement) {
      chip.type = 'button'
      chip.setAttribute('aria-label', `Filter by #${tag}`)
      chip.style.border = '0'
      chip.style.background = 'transparent'
      chip.style.padding = '0'
      chip.style.cursor = 'pointer'
      chip.addEventListener('click', (event) => {
        event.preventDefault()
        event.stopPropagation()
        options.onTagClick?.(tag)
      })
    }
    meta.appendChild(chip)
  }

  body.appendChild(meta)
  article.appendChild(body)

  if (cockpit) {
    // Trailing column replaces the Open arrow link: "route · time" on one
    // line, right-aligned, mono. When openUrl is present the column is the
    // anchor carrying entry-open; the row body keeps entry-focus.
    const trailing = entry.openUrl ? document.createElement('a') : document.createElement('span')
    trailing.className = 'lb-viewer-entry-row-trailing ve-trailing'
    trailing.style.fontFamily = 'var(--lb-font-mono)'
    trailing.style.fontSize = 'var(--lb-font-size-meta, var(--lb-font-size-small))'
    trailing.style.whiteSpace = 'nowrap'
    trailing.style.textAlign = 'right'
    trailing.style.alignSelf = 'start'
    trailing.style.paddingTop = '2px'
    if (trailing instanceof HTMLAnchorElement && entry.openUrl) {
      trailing.href = entry.openUrl
      trailing.style.textDecoration = 'none'
      trailing.addEventListener('click', openLink)
    }
    if (entry.route) {
      const route = document.createElement('span')
      route.className = 'lb-viewer-entry-row-route'
      route.textContent = entry.route
      route.style.color = 'var(--lb-text-muted)'
      trailing.appendChild(route)
      const sep = document.createElement('span')
      sep.setAttribute('aria-hidden', 'true')
      sep.textContent = '·'
      sep.style.color = 'var(--lb-text-dim)'
      sep.style.margin = '0 6px'
      trailing.appendChild(sep)
    }
    const time = document.createElement('time')
    time.className = 'lb-viewer-entry-row-time'
    time.dateTime = entry.timestamp
    time.textContent = entry.timeLabel ?? formatDate(entry.timestamp)
    time.style.color = 'var(--lb-text-dim)'
    trailing.appendChild(time)
    article.appendChild(trailing)
  } else if (entry.openUrl) {
    const link = document.createElement('a')
    link.className = 'lb-viewer-entry-row-open ve-link'
    link.href = entry.openUrl
    link.style.color = 'var(--lb-text-muted)'
    link.style.fontSize = 'var(--lb-font-size-meta, var(--lb-font-size-small))'
    link.style.whiteSpace = 'nowrap'
    link.style.textDecoration = 'none'
    link.style.alignSelf = 'start'
    link.style.paddingTop = '2px'
    link.textContent = 'Open '
    link.appendChild(renderArrow())
    link.addEventListener('click', openLink)
    article.appendChild(link)
  }

  item.appendChild(article)
  return item
}

// Inline cockpit row (designer round-2 decision R1, frame G): the
// scan-dense one-line browse row. One row = one line maximizes scan
// throughput on the cockpit's core lists (timeline, lenses, recent,
// route activity); the type rim + pill already carry type at a glance,
// so there is no second meta line - no date row, no tag-chip row (tags
// appear only inline in the title text via parseInline).
//
// `selectable` (render-level, frame T / D7) inserts a leading checkbox
// column right after the rim - search's distill-selection fork onto this
// same anatomy instead of the retired `ck-selrow`.
function renderInlineRow(
  entry: ViewerEntryRowEntry,
  options: Partial<PrimitiveOptions>,
  selectable: boolean
): HTMLElement {
  const color = resolveRowColor(entry)
  const item = document.createElement('li')
  item.className = 'lb-viewer-entry-row-item'
  item.dataset.entryId = entry.id
  item.dataset.entryType = entry.type
  item.style.listStyle = 'none'
  item.style.setProperty('--lb-entry-row-color', color)

  const article = document.createElement('article')
  article.className =
    'lb-viewer-entry-row ve-row ink-reader-entry-row lb-viewer-entry-row-cockpit lb-viewer-entry-row-inline'
  article.style.display = 'grid'
  // Columns: rim | [checkbox] | time | pill | [state glyph] | title (1fr) | [route] | [trailingAction].
  const columns = ['3px']
  if (selectable) columns.push('16px')
  columns.push('auto', 'auto')
  if (entry.state) columns.push('auto')
  columns.push('1fr')
  if (entry.route) columns.push('auto')
  if (entry.trailingAction) columns.push('auto')
  article.style.gridTemplateColumns = columns.join(' ')
  article.style.alignItems = 'center'
  article.style.gap = '14px'
  article.style.padding = options.density === 'compact' ? '9px 0' : '13px 0'
  article.style.borderBottom = '1px solid var(--lb-border-subtle)'
  article.style.position = 'relative'
  article.style.cursor = entry.openUrl ? 'pointer' : 'default'
  article.style.transition = 'background 150ms ease-out'
  // Selected-row tint (frame T checkbox variant): whole-row highlight,
  // matching the retired `.ck-selrow.sel` treatment. Only meaningful
  // when selectable; entry.checked is otherwise unset.
  if (selectable && entry.checked) {
    article.style.background = 'color-mix(in srgb, var(--lb-accent) 10%, transparent)'
    article.style.borderRadius = 'var(--lb-radius)'
  }

  // Row body keeps entry-focus + keyboard behavior, same as the stacked
  // shapes. The title anchor (below) carries entry-open.
  const focusRow = (event: Event): void => {
    if (isModifierClick(event)) return
    if (!options.onAction) return
    event.preventDefault()
    event.stopPropagation()
    options.onAction({
      type: 'entry-focus',
      target: entry.id,
      source: 'viewer-entry-row',
    })
  }

  article.addEventListener('click', focusRow)
  article.tabIndex = 0
  article.setAttribute('role', 'button')
  article.setAttribute('aria-label', `Preview entry from ${formatDate(entry.timestamp)}`)
  article.addEventListener('keydown', (event) => {
    if (event.key !== 'Enter' && event.key !== ' ') return
    focusRow(event)
  })

  // Rim: entry-type discrimination as a 3px grid column on the row's
  // left edge (polish rule #4: entry-type palette via entryTypeColor,
  // resolved into --lb-entry-row-color; never signal colors).
  const rim = document.createElement('span')
  rim.className = 'lb-viewer-entry-row-rim'
  rim.setAttribute('aria-hidden', 'true')
  rim.style.alignSelf = 'stretch'
  rim.style.width = '3px'
  rim.style.borderRadius = '0 3px 3px 0'
  rim.style.background = 'var(--lb-entry-row-color)'
  article.appendChild(rim)

  if (selectable) {
    article.appendChild(renderCheckbox(entry, options))
  }

  // Time sits in the left column (moved out of the trailing column,
  // which keeps only the route in this layout).
  const time = document.createElement('time')
  time.className = 'lb-viewer-entry-row-time'
  time.dateTime = entry.timestamp
  time.textContent = entry.timeLabel ?? formatDate(entry.timestamp)
  time.style.fontFamily = 'var(--lb-font-mono)'
  time.style.fontSize = 'var(--lb-font-size-meta, var(--lb-font-size-small))'
  time.style.color = 'var(--lb-text-dim)'
  time.style.whiteSpace = 'nowrap'
  article.appendChild(time)

  article.appendChild(renderTypePill(entry, color, options))

  if (entry.state) {
    article.appendChild(renderStateGlyph(entry.state, false))
  }

  // Title: single line, ellipsis. Inline markdown (tags, links,
  // emphasis) still renders through parseInline within the line.
  const title = document.createElement('span')
  title.className = 'lb-viewer-entry-row-title ve-title'
  title.style.display = 'block'
  title.style.minWidth = '0'
  title.style.whiteSpace = 'nowrap'
  title.style.overflow = 'hidden'
  title.style.textOverflow = 'ellipsis'
  title.style.fontSize = 'var(--lb-font-size-body)'
  title.style.lineHeight = '1.45'
  title.style.color = 'var(--lb-text-primary)'
  if (entry.state === 'done') {
    title.style.textDecoration = 'line-through'
    title.style.color = 'var(--lb-text-muted)'
  }
  appendInlineNodes(title, parseInline(entry.preview), options)

  if (entry.openUrl) {
    // The title anchor carries entry-open (plain click preventDefault +
    // stopPropagation; modifier-clicks fall through to the browser for
    // new-tab), mirroring the stacked trailing-anchor semantics, which
    // this layout drops.
    const openLink = (event: Event): void => {
      if (!entry.openUrl) return
      if (isModifierClick(event)) return
      if (!options.onAction) return
      event.preventDefault()
      event.stopPropagation()
      options.onAction({
        type: 'entry-open',
        target: entry.openUrl,
        source: 'viewer-entry-row',
      })
    }
    const link = document.createElement('a')
    link.className = 'lb-viewer-entry-row-title-link'
    link.href = entry.openUrl
    link.style.textDecoration = 'none'
    link.style.color = 'inherit'
    link.style.minWidth = '0'
    link.style.overflow = 'hidden'
    link.appendChild(title)
    link.addEventListener('click', openLink)
    article.appendChild(link)
  } else {
    article.appendChild(title)
  }

  // Trailing column: route only (mono, right-aligned). No time here -
  // it moved to the left column - and no anchor: entry-open lives on
  // the title in this layout.
  if (entry.route) {
    const route = document.createElement('span')
    route.className = 'lb-viewer-entry-row-trailing lb-viewer-entry-row-route ve-trailing'
    route.textContent = entry.route
    route.style.fontFamily = 'var(--lb-font-mono)'
    route.style.fontSize = 'var(--lb-font-size-meta, var(--lb-font-size-small))'
    route.style.color = 'var(--lb-text-muted)'
    route.style.whiteSpace = 'nowrap'
    route.style.textAlign = 'right'
    route.style.justifySelf = 'end'
    article.appendChild(route)
  }

  if (entry.trailingAction) {
    article.appendChild(renderTrailingActionButton(entry, entry.trailingAction, options))
  }

  item.appendChild(article)
  return item
}

/**
 * viewer-entry-row -- A single entry rendered as a row in a feed.
 *
 * Kit parity: mirrors `EntryRow` in
 * `DesignSystem/ui_kits/ink/shell.jsx:136-157` with the dual-action
 * semantics the kit's wired but didn't fully implement. The row emits
 * TWO distinct actions and lets the host decide what each one means:
 *   `entry-focus` (target = entry.id) -- fired on row body click. In a
 *     vault reader, the host updates a side preview panel (kit's
 *     `<aside class="reader-aside">`). On a tag-page feed where there's
 *     no panel, the host treats it as navigation.
 *   `entry-open`  (target = entry.openUrl) -- fired on the explicit
 *     `Open ↗` link. Always means "navigate to the entry deep link"
 *     (`/@user/vault/e/<entryId>`).
 * Modifier-clicks (cmd/ctrl/shift/alt) and non-primary buttons bail out
 * of both handlers so the browser can open the underlying anchor in a
 * new tab.
 *
 * Anatomy:
 *   Two-column grid: content body (1fr) | Open link (auto). Body stacks
 *   content + meta. Meta line: TYPE · @handle / vault · date · #tags
 *   (attribution + vault optional). Border-bottom separator.
 *   Cockpit variant (stacked layout, the default): 2px entry-type rim
 *   on the left edge (--lb-entry-row-color), optional read-only state
 *   glyph before the content, and the trailing column becomes
 *   "route · time" (mono, right-aligned) instead of the Open arrow
 *   link; the meta date moves into that column.
 *   Cockpit variant + `layout: 'inline'` (designer round-2 decision R1,
 *   frame G): scan-dense one-line browse row on a single grid:
 *   rim (3px column, entry-type color) | time (mono, --lb-text-dim) |
 *   type pill | [state glyph] | title (1fr, single line with ellipsis;
 *   inline tags/links/emphasis via parseInline) | route (mono,
 *   --lb-text-muted, right-aligned). No second meta line: no date row,
 *   no tag-chip row. When openUrl is present the TITLE is wrapped in
 *   the entry-open anchor (the trailing column drops its anchor role
 *   and its time).
 *
 * Variants:
 *   `showAttribution` (data, default true): hide the @handle / vault
 *   chip when rendering inside a vault context (kit pattern:
 *   `showAttribution=false` in views-reader.jsx vault lenses).
 *   `density: 'compact'` tightens padding to 12px 0 (9px 0 in the
 *   inline layout).
 *   `variant: 'cockpit'` (data): cockpit row shape per the cockpit
 *   design pass decision Q2 (one shared lblocks entry-row primitive
 *   both surfaces read). Adds the entry-type rim, the route + time
 *   trailing column (anchor to openUrl when present, carrying
 *   entry-open; row body keeps entry-focus), and per-entry `state`
 *   rendering: 'done' = small muted filled square glyph + line-through
 *   muted content, 'open' = empty square outline glyph, absent = no
 *   glyph. The glyph is a span (aria-hidden), never a checkbox or
 *   button - the cockpit views and ratifies, it does not complete
 *   tasks. When `variant` is absent, output is byte-identical to the
 *   Ink reader default.
 *   `layout: 'inline' | 'stacked'` (data, cockpit-only): row anatomy
 *   switch per designer round-2 decision R1. 'inline' is the one-line
 *   browse row above - cockpit browse surfaces (timeline, lenses,
 *   recent, route activity) default to it. 'stacked' or absent keeps
 *   the existing two-line cockpit shape unchanged, for surfaces that
 *   must show a second context line. Ignored unless `variant` is
 *   'cockpit'.
 *
 * Tokens:
 *   Border-bottom --lb-border-subtle. Content --lb-text-primary at
 *   --lb-font-size-lead. Type label uses per-entry-type token via
 *   entryTypeColor() in mono caps. Meta --lb-text-muted at
 *   --lb-font-size-meta. Tags --lb-accent. Cockpit: rim
 *   --lb-entry-row-color (per-type via entryTypeColor), route
 *   --lb-text-muted, time --lb-text-dim, state glyph --lb-text-dim,
 *   all mono in the trailing column. Inline layout: same token roles;
 *   title --lb-text-primary at --lb-font-size-body.
 *
 * Data:
 *   { entries: ViewerEntryRowEntry[], showAttribution?: boolean,
 *   variant?: 'cockpit', layout?: 'inline' | 'stacked', selectable?:
 *   boolean }
 *   Each entry: { id, type, timestamp, preview, tags, openUrl?,
 *   attribution?, route?, state?: 'open' | 'done', timeLabel?,
 *   rimTone?: 'type' | 'signal', signalColor?: string, trailingAction?:
 *   { label: string; action: string }, checked?: boolean }
 *   `timeLabel` (preformatted, e.g. "2h ago" or "20:10") replaces the
 *   default long date format wherever the row renders its time, in
 *   both variants.
 *
 * A11y:
 *   The article has role=button, tabindex=0, and responds to Enter/Space
 *   (fires entry-focus). The Open anchor is the keyboard / screen-reader
 *   path to navigation (the title anchor in the inline layout).
 *
 * Composition:
 *   Tag-page feed (`/ink/tags/<tag>`) with `showAttribution=true`. Vault
 *   reader summary / timeline / tags lenses with
 *   `showAttribution=false`. Same primitive everywhere -- kit pattern.
 *
 * Boundaries:
 *   When showing only a reference excerpt (backlink list, neighbor band),
 *   use the compact `viewer-entry-card` or a host composition instead.
 *
 * Anti-patterns:
 *   Never show the AGENTIC pill on rows (V13). Never give the row a
 *   second navigation destination inside the content area -- one row
 *   focus action, one Open link, both rooted to the same entry id.
 *   Never render the cockpit state glyph as an interactive checkbox or
 *   button - task completion belongs to the capture surfaces, not the
 *   cockpit. (The `selectable` checkbox below is a distinct affordance -
 *   multi-select for distillation, never task completion.)
 *
 * Row-primitive variants (frame T, D7): four additive props on this ONE
 * primitive - never new components - so a fix lands everywhere at once.
 *   `selectable` (data, cockpit + 'inline' layout only): leading
 *   checkbox on every row, fork retirement for search's
 *   distill-selection `ck-selrow`. Checkbox click fires `entry-select`
 *   (target = entry.id); per-entry `checked` supplies the visual state
 *   and tints the whole row when true. Row body entry-focus/entry-open
 *   handlers are unaffected.
 *   `trailingAction` (per-entry, cockpit + 'inline' layout only): one
 *   ghost-button action appended after the row's normal trailing
 *   content (e.g. Open / Review), fork retirement for feed's and
 *   health's `queue-row` link. Fires `{ type: action, target: id,
 *   source: 'viewer-entry-row' }` on click.
 *   `rimTone: 'type' | 'signal'` + `signalColor` (per-entry): color
 *   source for the rim + type-pill. 'signal' (with `signalColor`) lets
 *   rows whose pill vocabulary isn't a canonical entry type (feed's
 *   concept/digest/session/activity, health's changed/deleted/
 *   parse-error) carry an explicit color instead of entryTypeColor's
 *   unknown-type fallback. 'type' or absent is unchanged behavior.
 */
export function renderViewerEntryRow(
  data: ViewerEntryRowData,
  options: Partial<PrimitiveOptions> = {}
): HTMLElement {
  const root = document.createElement('ul')
  root.className = 'lb-viewer-entry-row-list'
  root.style.display = 'flex'
  root.style.flexDirection = 'column'
  root.style.margin = '0'
  root.style.padding = '0'
  const showAttribution = data.showAttribution !== false
  const cockpit = data.variant === 'cockpit'
  // `layout` only has effect on the cockpit variant; 'stacked' or
  // absent is the exact existing cockpit rendering.
  const inline = cockpit && data.layout === 'inline'
  // `selectable` only has effect on the cockpit 'inline' layout.
  const selectable = inline && data.selectable === true
  if (cockpit) root.dataset.variant = 'cockpit'
  if (inline) root.dataset.layout = 'inline'
  for (const entry of data.entries) {
    root.appendChild(
      inline
        ? renderInlineRow(entry, options, selectable)
        : renderRow(entry, showAttribution, options, cockpit)
    )
  }
  return root
}
