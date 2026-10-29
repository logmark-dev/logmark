import type { ShellModel } from '../models/ui-models.js'
import type { EntryPositionInfo } from '../lib/entry-position.js'
import { breadcrumbsForPath } from './breadcrumbs.js'
import { collapseAllRouteTree, renderRouteTree } from './route-tree.js'
import { brandFor, hostFeatures, type Brand } from '../lib/host.js'
import { getStoredTheme, toggleTheme } from './theme.js'

/** Sidebar wordmark (rc6, design 4.2). textContent only: product strings come from the server. */
export function renderBrand(container: HTMLElement, brand: Brand): void {
  const dot = document.createElement('span')
  dot.className = 'brand-dot'
  const name = document.createElement('span')
  name.className = 'brand-name'
  const bold = document.createElement('b')
  bold.textContent = brand.name
  name.appendChild(bold)
  if (brand.suffix) {
    name.appendChild(document.createTextNode(' '))
    const suffix = document.createElement('span')
    suffix.className = 'brand-suffix'
    suffix.textContent = brand.suffix
    name.appendChild(suffix)
  }
  container.replaceChildren(dot, name)
}

export interface AppShell {
  root: HTMLElement
  content: HTMLElement
  update(pathname: string, model: ShellModel): void
  /** Reflect SSE connection state in the status bar live dot. */
  setLive(live: boolean): void
  /**
   * Contextual status-bar hints (frame O footer: "↑↓ move · ↵ open full ·
   * esc close"), replacing the default Ctrl K search hint while a
   * selected-entry rail is open. Pass null to restore the default hint.
   */
  setContextHints(hints: string[] | null): void
  /**
   * Contextual topbar position widget (frame P: "n / N in <route>" counter
   * + prev/next arrows). Pass null to hide it.
   */
  setEntryPosition(position: EntryPositionInfo | null): void
}

/** Lens entries shown in the sidebar - the four ratify-relevant types. */
const LENSES: Array<{ type: string; label: string }> = [
  { type: 'task', label: 'Tasks' },
  { type: 'block', label: 'Blocks' },
  { type: 'decision', label: 'Decisions' },
  { type: 'question', label: 'Questions' },
]

/**
 * Capability flags surfaced in the diagnostics popover, in display order.
 * No `feed` row (lmk-oss-cockpit-cut-empty-producer-views-7qcb): no host
 * serves a feed, so the row only ever read "off".
 */
const STATUS_CAPS: Array<{ key: string; label: string }> = [
  { key: 'hasNotation', label: 'notation' },
  { key: 'hasSessions', label: 'sessions' },
]

function link(href: string, label: string, count?: number, warn = false, dotType?: string): HTMLAnchorElement {
  const a = document.createElement('a')
  a.href = href
  a.dataset.route = '1'
  a.className = 'nav-item'
  const labelSpan = document.createElement('span')
  labelSpan.className = 'nav-item-label'
  if (dotType) {
    const dot = document.createElement('span')
    dot.className = 'nav-dot'
    dot.style.background = `var(--entry-${dotType}, var(--entry-log))`
    labelSpan.appendChild(dot)
  }
  labelSpan.appendChild(document.createTextNode(label))
  a.appendChild(labelSpan)
  if (count !== undefined) {
    const countSpan = document.createElement('span')
    countSpan.className = warn ? 'count warn' : 'count'
    countSpan.textContent = String(count)
    a.appendChild(countSpan)
  }
  return a
}

function navLabel(label: string): HTMLDivElement {
  const div = document.createElement('div')
  div.className = 'nav-label'
  div.textContent = label
  return div
}

/** Routes group label with the tree's `collapse all` action (frame Q). */
function navLabelWithAction(label: string, actionLabel: string, onAction: () => void): HTMLDivElement {
  const div = document.createElement('div')
  div.className = 'nav-label nav-label-with-action'
  div.appendChild(document.createTextNode(label))
  const action = document.createElement('button')
  action.type = 'button'
  action.className = 'nav-label-action'
  action.textContent = actionLabel
  action.addEventListener('click', onAction)
  div.appendChild(action)
  return div
}

function renderLinks(
  container: HTMLElement,
  label: string,
  links: Array<{ label: string; href: string; count?: number; isWarning?: boolean }>,
): void {
  container.replaceChildren(navLabel(label), ...links.map((x) => link(x.href, x.label, x.count, x.isWarning)))
}

function historyButton(dir: 'back' | 'forward'): HTMLButtonElement {
  const b = document.createElement('button')
  b.type = 'button'
  b.className = 'nav-history'
  b.textContent = dir === 'back' ? '‹' : '›'
  b.title = dir === 'back' ? 'Back' : 'Forward'
  b.addEventListener('click', () => {
    if (dir === 'back') history.back()
    else history.forward()
  })
  return b
}

/** `<span class="topbar-position">` - frame P's "‹ n / N in <route> ›" widget. */
function positionWidget(position: EntryPositionInfo): HTMLElement {
  const wrap = document.createElement('span')
  wrap.className = 'topbar-position'

  const prev = document.createElement(position.prevHref ? 'a' : 'span')
  prev.className = 'topbar-position-btn'
  prev.textContent = '‹'
  if (position.prevHref) {
    ;(prev as HTMLAnchorElement).href = position.prevHref
    prev.dataset.route = '1'
    prev.title = 'Previous in ' + position.label
  } else {
    prev.setAttribute('aria-disabled', 'true')
  }

  const label = document.createElement('span')
  label.className = 'topbar-position-label'
  label.textContent = `${position.index + 1} / ${position.total} in ${position.label}`

  const next = document.createElement(position.nextHref ? 'a' : 'span')
  next.className = 'topbar-position-btn'
  next.textContent = '›'
  if (position.nextHref) {
    ;(next as HTMLAnchorElement).href = position.nextHref
    next.dataset.route = '1'
    next.title = 'Next in ' + position.label
  } else {
    next.setAttribute('aria-disabled', 'true')
  }

  wrap.append(prev, label, next)
  return wrap
}

/**
 * Topbar health pill (rc6, design 4.2). With a lint scan it counts stale
 * concept sources (amber when any, green "0 stale" after a clean scan).
 * Without one (daemon hosting) it reflects GET /api/v0/health: green
 * "health ok" when the host answered ok, the muted "health n/a" when it
 * did not or was never asked. Never the amber state without a scan.
 */
export function healthPill(model: Pick<ShellModel, 'staleCount' | 'hostOk'>): { className: string; text: string } {
  if (model.staleCount !== null) {
    return model.staleCount > 0
      ? { className: 'health-pill', text: `${model.staleCount} stale` }
      : { className: 'health-pill ok', text: '0 stale' }
  }
  return model.hostOk === true
    ? { className: 'health-pill ok', text: 'health ok' }
    : { className: 'health-pill na', text: 'health n/a' }
}

function renderTopbar(topbar: HTMLElement, pathname: string, model: ShellModel, position: EntryPositionInfo | null): void {
  const crumbsEl = document.createElement('div')
  crumbsEl.className = 'crumbs'
  const crumbs = breadcrumbsForPath(pathname)
  crumbs.forEach((crumb, index) => {
    if (index > 0) {
      const sep = document.createElement('span')
      sep.className = 'crumb-sep'
      sep.textContent = '›'
      crumbsEl.appendChild(sep)
    }

    if (crumb.href) {
      const a = document.createElement('a')
      a.href = crumb.href
      a.dataset.route = '1'
      a.textContent = crumb.label
      crumbsEl.appendChild(a)
      return
    }

    const span = document.createElement('span')
    span.textContent = crumb.label
    crumbsEl.appendChild(span)
  })

  const spacer = document.createElement('div')
  spacer.className = 'topbar-spacer'

  const health = document.createElement('a')
  const pill = healthPill(model)
  health.className = pill.className
  health.href = '/health'
  health.dataset.route = '1'
  const pulse = document.createElement('span')
  pulse.className = 'pulse'
  health.append(pulse, document.createTextNode(pill.text))

  const theme = document.createElement('button')
  theme.className = 'theme-toggle'
  theme.type = 'button'
  theme.title = 'Toggle theme'
  theme.textContent = getStoredTheme() === 'dark' ? 'Light' : 'Dark'
  theme.addEventListener('click', () => {
    const nextTheme = toggleTheme()
    theme.textContent = nextTheme === 'dark' ? 'Light' : 'Dark'
  })

  const children: HTMLElement[] = [historyButton('back'), historyButton('forward'), crumbsEl, spacer]
  if (position) children.push(positionWidget(position))
  children.push(health, theme)
  topbar.replaceChildren(...children)
}

/** Vault display name derived from its filesystem path (last path segment).
 * Shared with the root at-a-glance hero (frame R, D3/D9). */
export function vaultNameFrom(vaultPath: string): string {
  const parts = vaultPath.split(/[\\/]/).filter(Boolean)
  return parts[parts.length - 1] ?? vaultPath
}

function statusSep(): HTMLSpanElement {
  const sep = document.createElement('span')
  sep.className = 'status-sep'
  sep.textContent = '|'
  sep.setAttribute('aria-hidden', 'true')
  return sep
}

/**
 * Diagnostics popover (frame W, D9/Q6): the honest home for adapter +
 * capability detail now that the bar itself only carries `vault · live`.
 * Truthful per-adapter on/off, same three flags the bar used to spell
 * out permanently - just moved behind a hover/click instead of shown at
 * all times.
 */
function renderDiagRow(key: string, value: string, on: boolean | null): HTMLElement {
  const row = document.createElement('div')
  row.className = 'dr'
  const k = document.createElement('span')
  k.className = 'dk'
  k.textContent = key
  row.appendChild(k)
  if (on !== null) {
    const dot = document.createElement('span')
    dot.className = on ? 'dot-on' : 'dot-off'
    row.appendChild(dot)
  }
  row.appendChild(document.createTextNode(value))
  return row
}

function renderDiagPopover(model: ShellModel): HTMLElement {
  const pop = document.createElement('div')
  pop.className = 'ck-diag'
  pop.id = 'status-diag'
  // Non-modal disclosure, not a dialog: no role (the trigger's
  // aria-expanded + aria-controls carry the relationship).
  pop.setAttribute('role', 'group')
  pop.hidden = true

  const heading = document.createElement('div')
  heading.className = 'dh'
  heading.textContent = 'diagnostics'
  pop.appendChild(heading)

  pop.appendChild(renderDiagRow('adapter', model.adapter, null))
  for (const cap of STATUS_CAPS) {
    const on = model.capabilities[cap.key] === true
    pop.appendChild(renderDiagRow(cap.label, on ? 'on' : 'off', on))
  }

  const tail = document.createElement('div')
  tail.className = 'tail'
  pop.appendChild(tail)
  return pop
}

/** Mutable handle onto whichever diag trigger/popover is currently in the
 * DOM - `createAppShell` owns exactly one outside-click listener for the
 * shell's lifetime and reads through this rather than each re-render
 * attaching (and leaking) its own document-level listener. `open`/`locked`
 * survive statusbar rebuilds (setLive / hint updates), so a click-locked
 * popover doesn't silently close mid-read. */
interface DiagHandle {
  locked: boolean
  open: boolean
  liveEl: HTMLButtonElement | null
  diag: HTMLElement | null
  close(): void
}

/** Grace window for the pointer to cross the gap between the live dot and
 * the popover (the popover floats 10px above the trigger) without a
 * mouseleave flicker-closing it. */
const DIAG_HOVER_CLOSE_DELAY_MS = 150

/**
 * Status bar (frame W, D9): caps to `vault · live` - adapter + capability
 * flags move into the diagnostics popover (`renderDiagPopover`), which
 * opens on hover OR click of the live dot (click for touch/keyboard).
 * The right-side contextual hints (Task 4) are untouched.
 */
function renderStatusbar(
  statusbar: HTMLElement,
  model: ShellModel,
  live: boolean,
  contextHints: string[] | null,
  handle: DiagHandle,
): void {
  const left = document.createElement('div')
  left.className = 'status-left'

  const vault = document.createElement('span')
  vault.className = 'status-vault'
  vault.append('vault ')
  const vaultName = document.createElement('b')
  vaultName.textContent = vaultNameFrom(model.vaultPath)
  vaultName.title = model.vaultPath
  vault.appendChild(vaultName)

  const diagWrap = document.createElement('div')
  diagWrap.className = 'status-diag-wrap'

  const liveEl = document.createElement('button')
  liveEl.type = 'button'
  liveEl.className = live ? 'status-live on' : 'status-live'
  liveEl.setAttribute('aria-expanded', 'false')
  liveEl.setAttribute('aria-controls', 'status-diag')
  const liveDot = document.createElement('span')
  liveDot.className = 'status-live-dot'
  liveEl.append(liveDot, document.createTextNode(live ? 'live' : 'offline'))

  const diag = renderDiagPopover(model)

  // Open/locked state carries over from the previous render of this bar
  // (setLive / setContextHints / update all rebuild it) - a popover the
  // user click-locked open must survive a live-dot flip mid-read.
  const wasOpen = handle.open
  const wasLocked = handle.locked

  handle.liveEl = liveEl
  handle.diag = diag

  // Escape must work however the popover was opened (hover never focuses
  // the trigger), so the listener is document-level and exists only while
  // the popover is open. The router has no view-unmount hook - if the
  // whole bar was replaced while open, the handler self-heals off
  // `isConnected` like entry-rail.ts / search.ts's keydown handlers.
  let escHandler: ((e: KeyboardEvent) => void) | null = null
  const unbindEsc = (): void => {
    if (!escHandler) return
    document.removeEventListener('keydown', escHandler)
    escHandler = null
  }
  const bindEsc = (): void => {
    if (escHandler) return
    escHandler = (e) => {
      if (!diag.isConnected) {
        unbindEsc()
        return
      }
      if (e.key === 'Escape') {
        e.preventDefault()
        handle.close()
      }
    }
    document.addEventListener('keydown', escHandler)
  }

  // Hover-close goes through a short grace timer so the pointer can cross
  // the gap between the dot and the popover without flicker-closing it;
  // re-entering either element cancels the pending close.
  let closeTimer: number | null = null
  const cancelCloseTimer = (): void => {
    if (closeTimer === null) return
    clearTimeout(closeTimer)
    closeTimer = null
  }

  const openDiag = (): void => {
    cancelCloseTimer()
    diag.hidden = false
    liveEl.setAttribute('aria-expanded', 'true')
    handle.open = true
    bindEsc()
  }
  handle.close = (): void => {
    cancelCloseTimer()
    diag.hidden = true
    liveEl.setAttribute('aria-expanded', 'false')
    handle.open = false
    handle.locked = false
    unbindEsc()
  }
  const scheduleClose = (): void => {
    if (handle.locked) return
    cancelCloseTimer()
    closeTimer = window.setTimeout(() => {
      closeTimer = null
      handle.close()
    }, DIAG_HOVER_CLOSE_DELAY_MS)
  }

  liveEl.addEventListener('mouseenter', openDiag)
  liveEl.addEventListener('mouseleave', scheduleClose)
  diag.addEventListener('mouseenter', openDiag)
  diag.addEventListener('mouseleave', scheduleClose)
  liveEl.addEventListener('click', () => {
    if (handle.locked) {
      handle.close()
    } else {
      handle.locked = true
      openDiag()
    }
  })

  if (wasOpen) {
    openDiag()
    handle.locked = wasLocked
  }

  diagWrap.append(liveEl, diag)
  left.append(vault, statusSep(), diagWrap)

  const right = document.createElement('div')
  right.className = 'status-right'
  const hint = document.createElement('span')
  hint.className = 'status-hint'
  if (contextHints && contextHints.length > 0) {
    hint.textContent = contextHints.join(' · ')
  } else {
    const kbd = document.createElement('span')
    kbd.className = 'kbd'
    kbd.textContent = 'Ctrl K'
    hint.append(kbd, document.createTextNode(' search'))
  }
  right.appendChild(hint)

  statusbar.replaceChildren(left, right)
}

export function createAppShell(model: ShellModel): AppShell {
  const root = document.createElement('div')
  root.className = 'shell'

  const topbar = document.createElement('div')
  topbar.className = 'topbar'

  const body = document.createElement('div')
  body.className = 'shell-body'

  const sidebar = document.createElement('aside')
  sidebar.className = 'sidebar'
  sidebar.innerHTML = `
    <div class="brand" data-brand></div>
    <a class="search-trigger" href="/search" data-route="1"><span>search vault...</span><span class="kbd">Ctrl K</span></a>
    <nav class="nav-section" data-primary></nav>
    <nav class="nav-section" data-lenses></nav>
    <nav class="nav-section" data-routes></nav>
    <nav class="nav-section" data-concepts></nav>
  `

  const main = document.createElement('main')
  main.className = 'main'
  const content = document.createElement('div')
  content.className = 'content-slot'
  main.append(content)

  const statusbar = document.createElement('div')
  statusbar.className = 'statusbar'

  renderBrand(sidebar.querySelector<HTMLElement>('[data-brand]')!, brandFor(model.host))

  body.append(sidebar, main)
  root.append(topbar, body, statusbar)

  let currentModel = model
  let liveState = false
  let contextHints: string[] | null = null
  let entryPosition: EntryPositionInfo | null = null
  // Each contextual slot tracks which pathname last claimed it. A route
  // change clears a slot only when the NEW view didn't already re-claim it
  // itself during its own render (frame P's entry-detail view sets both
  // slots synchronously before this update() runs for the same pathname) -
  // otherwise a page that owns only one of the two slots would inherit a
  // stale value left behind in the other (e.g. frame O's rail hints
  // surviving a navigation into a hint-less, position-owning frame P page).
  let contextHintsOwner: string | null = null
  let entryPositionOwner: string | null = null

  // One outside-click listener for the shell's lifetime (not one per
  // render) - `renderStatusbar` keeps this handle pointed at whichever
  // trigger/popover pair is currently live in the DOM.
  const diagHandle: DiagHandle = { locked: false, open: false, liveEl: null, diag: null, close: () => {} }
  document.addEventListener('click', (e) => {
    if (!diagHandle.locked || !diagHandle.liveEl || !diagHandle.diag) return
    const target = e.target as Node
    if (target !== diagHandle.liveEl && !diagHandle.liveEl.contains(target) && !diagHandle.diag.contains(target)) {
      diagHandle.close()
    }
  })

  const update = (pathname: string, next: ShellModel): void => {
    currentModel = next
    // Tab title from the host (rc6, design D1): "LogMark Engine",
    // "LogMark", or "LogMark dev" for the Knowledge dev server.
    const brand = brandFor(next.host)
    document.title = brand.title
    renderBrand(sidebar.querySelector<HTMLElement>('[data-brand]')!, brand)
    const primary = sidebar.querySelector<HTMLElement>('[data-primary]')!
    const lenses = sidebar.querySelector<HTMLElement>('[data-lenses]')!
    const routes = sidebar.querySelector<HTMLElement>('[data-routes]')!
    const concepts = sidebar.querySelector<HTMLElement>('[data-concepts]')!

    // Concept surfaces only where a producer writes concept pages
    // (lmk-hqgm): an explicit hasConceptProducer: false drops the Concepts
    // nav item, its count, and the Recent concepts section.
    const conceptProducer = hostFeatures(next.capabilities).conceptProducer
    primary.replaceChildren(
      navLabel('Navigate'),
      link('/', 'Vault', next.totalEntries),
      link('/timeline', 'Timeline'),
      ...(conceptProducer ? [link('/concepts', 'Concepts', next.totalConcepts)] : []),
      link('/constitution', 'Constitution'),
      // No count badge when the lint never ran (null) - a "0" would
      // assert a clean vault that was never scanned.
      link('/health', 'Health', next.staleCount ?? undefined, (next.staleCount ?? 0) > 0),
      link('/settings', 'Settings'),
    )

    lenses.replaceChildren(
      navLabel('Lenses'),
      ...LENSES.map((lens) => {
        const count = next.byType[lens.type]
        return link(`/lens/${lens.type}`, lens.label, count !== undefined && count > 0 ? count : undefined, false, lens.type)
      }),
    )

    if (next.routeTree && next.routeTree.length > 0) {
      const tree = next.routeTree
      const body = document.createElement('div')
      body.className = 'route-tree'
      const label = navLabelWithAction('Routes', 'collapse all', () => {
        collapseAllRouteTree()
        renderRouteTree(body, tree, pathname)
      })
      routes.replaceChildren(label, body)
      renderRouteTree(body, tree, pathname)
    } else {
      renderLinks(routes, 'Routes', next.routes.slice(0, 8))
      if (next.routes.length > 8) {
        const all = document.createElement('a')
        all.className = 'nav-item nav-item-more'
        all.textContent = `all routes → ${next.routes.length}`
        all.href = '/routes'
        all.dataset.route = '1'
        routes.appendChild(all)
      }
    }
    if (conceptProducer) {
      concepts.hidden = false
      renderLinks(concepts, 'Recent concepts', next.recentConcepts)
    } else {
      concepts.hidden = true
      concepts.replaceChildren()
    }

    for (const a of sidebar.querySelectorAll<HTMLAnchorElement>('a.nav-item')) {
      const href = a.getAttribute('href') ?? '/'
      const hrefPath = new URL(href, location.origin).pathname
      const active = hrefPath === pathname || (pathname !== '/' && hrefPath !== '/' && pathname.startsWith(hrefPath))
      a.classList.toggle('active', active)
    }

    // A route change always leaves any open rail behind (frame O rails are
    // per-view) - the context hints/position go with it, unless the new
    // view already reclaimed its slot for this same pathname (frame P).
    if (contextHintsOwner !== pathname) contextHints = null
    if (entryPositionOwner !== pathname) entryPosition = null
    renderTopbar(topbar, pathname, next, entryPosition)
    renderStatusbar(statusbar, next, liveState, contextHints, diagHandle)
  }

  const setContextHints = (hints: string[] | null): void => {
    contextHints = hints
    contextHintsOwner = location.pathname
    renderStatusbar(statusbar, currentModel, liveState, contextHints, diagHandle)
  }

  const setEntryPosition = (position: EntryPositionInfo | null): void => {
    entryPosition = position
    entryPositionOwner = location.pathname
    renderTopbar(topbar, location.pathname, currentModel, entryPosition)
  }

  const setLive = (live: boolean): void => {
    liveState = live
    renderStatusbar(statusbar, currentModel, liveState, contextHints, diagHandle)
  }

  update(location.pathname, model)
  return { root, content, update, setLive, setContextHints, setEntryPosition }
}
