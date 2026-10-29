import { api } from '../../api.js'
import { markdownToLBlocksDocument } from '../../../lib/markdown-to-lblocks.js'
import type { Entry } from '../../../types/entry.js'
import { normalizeEntry, toDateLabel } from '../../models/normalizers.js'
import { timeOfDay } from '../../lib/dates.js'
import { liftProvenance } from '../../lib/notation.js'
import type { LiftedProvenance } from '../../lib/notation.js'
import { renderMarkdownInto } from '../../lib/lblocks-render.js'
import { isDegradedMetadata, limitedMetadataBand } from '../../lib/degraded.js'
import { el, kvRow } from '../../lib/dom.js'
import { navigate } from '../../lib/nav.js'
import { setStatusHints } from '../../lib/status-hints.js'

/**
 * Selected-entry rail (design pass v4 frame O, D1): click a row on a list
 * surface and read the entry in a 380px right rail without leaving the
 * list. The list stays put - the rail is a sibling column, not a route
 * change. `/entries/:id` (frame P) remains the shareable full-page deep
 * link; the rail's footer links straight to it.
 *
 * Reuses the frame-B body pipeline (markdownToLBlocksDocument +
 * renderMarkdownInto) and the chip-row construction from entry-detail.ts -
 * both extracted here as `renderEntryChips` / `renderEntryBody` so neither
 * view duplicates the notation-lifting + lblocks rendering logic.
 */

export interface RailContext {
  /**
   * The list the rail navigates within, in display order.
   *
   * LIVE-MUTATION CONTRACT: `createEntryRail` reads `ctx.ids` at
   * interaction time, never snapshotting it at construction. Callers rely
   * on this to keep the rail's ↑↓ context and n/N position in sync with a
   * changing list - timeline.ts pushes onto the array as "Show earlier
   * days" reveals rows; search.ts reassigns `ctx.ids` when a new query
   * lands; route-landing.ts reassigns it when a type filter chip narrows
   * the pool. Do not refactor the rail to copy or freeze this array
   * without migrating those three call sites.
   */
  ids: string[]
  /** Human label for the context, used in nav-button aria-labels. */
  label: string
}

export interface EntryRail {
  open(id: string): Promise<void>
  close(): void
  readonly openId: string | null
}

/** `<div class="entry-chips">` - route chip (Provenance, chip-taxonomy §6) + tag chips (Tag, §3). Shared with entry-detail.ts. */
export function renderEntryChips(e: Entry): HTMLElement {
  const chips = el('div', 'entry-chips')
  if (e.route) {
    const routeChip = el('a', 'chip chip-route', e.route)
    routeChip.setAttribute('href', `/routes/${encodeURIComponent(e.route)}`)
    routeChip.dataset.route = '1'
    chips.appendChild(routeChip)
  }
  for (const t of e.tags) {
    const tagChip = el('a', 'chip chip-tag', `#${t}`)
    tagChip.setAttribute('href', `/search?q=${encodeURIComponent('#' + t)}`)
    tagChip.dataset.route = '1'
    chips.appendChild(tagChip)
  }
  return chips
}

/** `<div class="entry-body">` - the frame-B lblocks pipeline over provenance-lifted body text. Shared with entry-detail.ts. */
export function renderEntryBody(lifted: LiftedProvenance): HTMLElement {
  const body = el('div', 'entry-body')
  renderMarkdownInto(body, markdownToLBlocksDocument(lifted.body))
  return body
}

/** `<div class="frontmatter">` metacard subset (id/route/source only - frame O carries no backlinks in the rail). */
function renderRailMetacard(e: Entry, lifted: LiftedProvenance): HTMLElement {
  const frontmatter = el('div', 'frontmatter')
  frontmatter.appendChild(kvRow('id', e.id, 'fm-row'))
  if (e.route) frontmatter.appendChild(kvRow('route', e.route, 'fm-row'))
  const sourceLabel = e.source ?? lifted.sources.join(', ')
  if (sourceLabel) frontmatter.appendChild(kvRow('source', sourceLabel, 'fm-row'))
  return frontmatter
}

function navButton(glyph: string, label: string, disabled: boolean, onClick: () => void): HTMLButtonElement {
  const button = el('button', 'entry-rail-nav-btn', glyph)
  button.type = 'button'
  button.setAttribute('aria-label', label)
  button.title = label
  button.disabled = disabled
  button.addEventListener('click', onClick)
  return button
}

function renderLoading(): HTMLElement {
  return el('div', 'entry-rail-loading', 'Loading...')
}

function renderNotFound(id: string): HTMLElement {
  return el('div', 'entry-rail-empty', `Entry not found: ${id}`)
}

/**
 * `createEntryRail(host, ctx)` - the host owns nothing but the DOM node;
 * all state (open id, keyboard binding) lives in this closure. Views wire
 * `EntryRowsOptions.onOpenEntry` to `rail.open` and read the
 * `entry-rail-open` / `entry-rail-close` events the rail dispatches on
 * `host` to sync the list's active-row highlight (via
 * `setActiveEntryRow` in entry-card.ts).
 *
 * Keyboard scope (ArrowUp/Down/Enter/Esc) binds only while a rail entry is
 * open, ignores keys typed into input/textarea/select (the search box),
 * and self-heals if the host is ever detached by an SPA navigation the
 * rail didn't initiate itself (router.ts has no view-unmount hook - see
 * the task report's Gaps section).
 */
export function createEntryRail(host: HTMLElement, ctx: RailContext): EntryRail {
  let openId: string | null = null
  let requestToken = 0
  let keydownHandler: ((e: KeyboardEvent) => void) | null = null

  function currentIndex(): number {
    return openId ? ctx.ids.indexOf(openId) : -1
  }

  function shouldIgnoreKey(target: EventTarget | null): boolean {
    const node = target as HTMLElement | null
    if (!node) return false
    if (node.isContentEditable) return true
    return node.tagName === 'INPUT' || node.tagName === 'TEXTAREA' || node.tagName === 'SELECT'
  }

  function onKeydown(e: KeyboardEvent): void {
    if (!host.isConnected) {
      // The SPA navigated away without going through close(); this rail
      // is orphaned - stop listening rather than act on a dead panel.
      // Hint ownership stays with whichever rail is live: an orphan must
      // NOT clear the status slot, or its lazy self-heal would wipe the
      // hints a newer rail set on the next view (the shell's route-change
      // update() already cleared this orphan's own hints at navigation).
      unbindKeys({ clearHints: false })
      return
    }
    if (shouldIgnoreKey(e.target)) return
    if (e.key === 'Escape') {
      e.preventDefault()
      close()
      return
    }
    if (e.key === 'ArrowDown') {
      e.preventDefault()
      void step(1)
      return
    }
    if (e.key === 'ArrowUp') {
      e.preventDefault()
      void step(-1)
      return
    }
    if (e.key === 'Enter') {
      if (!openId) return
      e.preventDefault()
      const id = openId
      close()
      navigate(`/entries/${encodeURIComponent(id)}`)
    }
  }

  function bindKeys(): void {
    if (keydownHandler) return
    keydownHandler = onKeydown
    addEventListener('keydown', keydownHandler)
    setStatusHints(['↑↓ move', '↵ open full', 'esc close'])
  }

  function unbindKeys(opts: { clearHints: boolean } = { clearHints: true }): void {
    if (!keydownHandler) return
    removeEventListener('keydown', keydownHandler)
    keydownHandler = null
    if (opts.clearHints) setStatusHints(null)
  }

  async function step(delta: number): Promise<void> {
    const idx = currentIndex()
    if (idx === -1) return
    const nextIdx = idx + delta
    if (nextIdx < 0 || nextIdx >= ctx.ids.length) return
    await open(ctx.ids[nextIdx])
  }

  function renderContent(entry: Entry, idx: number, degraded: boolean, adapter: string): HTMLElement {
    const e = normalizeEntry(entry)
    const lifted = liftProvenance(e.body)

    const head = el('div', 'entry-rail-head')
    head.appendChild(el('span', `status-pill entry-type-${e.type ?? 'log'}`, e.type ?? 'entry'))
    head.appendChild(
      el('span', 'entry-rail-position', idx >= 0 ? `${idx + 1}/${ctx.ids.length}` : `1/${ctx.ids.length || 1}`),
    )
    const nav = el('div', 'entry-rail-nav')
    nav.append(
      navButton('↑', `Previous entry in ${ctx.label}`, idx <= 0, () => void step(-1)),
      navButton('↓', `Next entry in ${ctx.label}`, idx === -1 || idx >= ctx.ids.length - 1, () => void step(1)),
      navButton('✕', 'Close', false, close),
    )
    head.appendChild(nav)

    const body = el('div', 'entry-rail-body')
    if (degraded) body.appendChild(limitedMetadataBand(adapter))

    const stamp = el('div')
    const time = timeOfDay(e.updatedAt || '')
    stamp.appendChild(
      el('div', 'entry-eyebrow', time ? `${toDateLabel(e.updatedAt)} · ${time}` : toDateLabel(e.updatedAt)),
    )
    stamp.appendChild(el('h2', 'entry-rail-title', e.title))
    body.appendChild(stamp)

    const chips = renderEntryChips(e)
    if (chips.childElementCount > 0) body.appendChild(chips)

    body.appendChild(renderEntryBody(lifted))
    // Metacard only - frame O shows no backlinks in the rail (gated or
    // otherwise); backlinks belong to the full page (frame P).
    body.appendChild(renderRailMetacard(e, lifted))

    const openFull = el('a', 'entry-rail-open')
    openFull.href = `/entries/${encodeURIComponent(e.id)}`
    openFull.dataset.route = '1'
    openFull.append(document.createTextNode('Open full ↗ '), el('span', 'dim', `- shareable /entries/${e.id}`))
    body.appendChild(openFull)

    const root = el('div')
    root.append(head, body)
    return root
  }

  async function open(id: string): Promise<void> {
    openId = id
    requestToken += 1
    const token = requestToken
    host.hidden = false
    host.replaceChildren(renderLoading())
    bindKeys()
    host.dispatchEvent(new CustomEvent('entry-rail-open', { detail: { id } }))

    const [{ entry, error }, caps] = await Promise.all([api.readEntry(id), api.capabilities()])
    if (token !== requestToken || openId !== id) return // superseded by a later open()/close()
    if (!entry || error) {
      host.replaceChildren(renderNotFound(id))
      return
    }
    const degraded = isDegradedMetadata(caps.capabilities ?? {})
    const idx = ctx.ids.indexOf(id)
    host.replaceChildren(renderContent(entry, idx, degraded, caps.adapter))
  }

  function close(): void {
    openId = null
    requestToken += 1
    host.replaceChildren()
    host.hidden = true
    host.dispatchEvent(new CustomEvent('entry-rail-close'))
    unbindKeys()
  }

  return {
    open,
    close,
    get openId() {
      return openId
    },
  }
}

/**
 * Wraps `list` (the already-rendered `renderEntryRows` output) into the
 * frame O two-column layout with a fresh rail host, and syncs the list's
 * active-row highlight to the rail's open/close lifecycle. Views own
 * `RailContext.ids` and the `onOpenEntry` wiring on `renderEntryRows`;
 * this only saves the four call sites from re-deriving the same
 * layout+event plumbing.
 */
export function attachEntryRail(
  list: HTMLElement,
  ctx: RailContext,
  setActiveRow: (container: HTMLElement, id: string | null) => void,
): { layout: HTMLElement; rail: EntryRail } {
  const layout = el('div', 'entry-rail-layout')
  const railHost = el('aside', 'entry-rail')
  railHost.hidden = true
  layout.append(list, railHost)

  const rail = createEntryRail(railHost, ctx)
  railHost.addEventListener('entry-rail-open', (e) => {
    setActiveRow(list, (e as CustomEvent<{ id: string }>).detail.id)
  })
  railHost.addEventListener('entry-rail-close', () => {
    setActiveRow(list, null)
  })

  return { layout, rail }
}
