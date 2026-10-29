import { renderViewerEntryRow } from '@logmark/lblocks'
import type { EntrySummary } from '../../../types/entry.js'
import { navigate } from '../../lib/nav.js'
import { deriveTitleFromPath, escapeInlineUnderscores, isPathTitle } from '../../lib/notation.js'
import { smartTimeLabel } from '../../lib/dates.js'
import { ENTRY_TYPES } from '../lens-types.js'

/**
 * Shared entry-list rendering (design pass Q2): every cockpit list view
 * renders entries through the lblocks `viewer-entry-row` primitive's
 * cockpit variant - the SAME primitive the Ink reader uses - so the row
 * anatomy (type rim, type pill, tokenized title, mono route/time column)
 * stays converged across surfaces.
 */

function timeLabelFor(entry: EntrySummary): string {
  return smartTimeLabel(entry.createdAt || entry.updatedAt || '')
}

/**
 * Pass-3 P3: prefer the real title; derive a human one from path titles.
 * Escaped for the parseInline pass so `_` pairs cannot italicize.
 */
function rowTitle(entry: EntrySummary): string {
  const plain = isPathTitle(entry.title) ? deriveTitleFromPath(entry.title) : entry.title
  return escapeInlineUnderscores(plain)
}

/**
 * Rail responsive breakpoint (frame O / P, D1): at or above this width the
 * selected-entry rail attaches beside the list; below it a row click falls
 * back to full-page navigation (frame P's responsive-collapse note).
 */
const RAIL_MIN_WIDTH_PX = 1180

function isRailCapable(): boolean {
  if (typeof window === 'undefined') return false
  if (typeof window.matchMedia === 'function') {
    try {
      return window.matchMedia(`(min-width: ${RAIL_MIN_WIDTH_PX}px)`).matches
    } catch {
      // Environments without a real matchMedia implementation (test jsdom)
      // fall through to the innerWidth check below.
    }
  }
  return typeof window.innerWidth === 'number' && window.innerWidth >= RAIL_MIN_WIDTH_PX
}

function entryIdFromOpenUrl(url: string): string | null {
  const match = /^\/entries\/(.+)$/.exec(url)
  return match ? decodeURIComponent(match[1]) : null
}

/**
 * Toggle the active-row highlight (frame O) directly on the already-mounted
 * row DOM - no re-render, so the list's scroll position never moves when
 * the rail's selection changes. `container` is anything containing rows
 * built by `renderEntryRows` (matched via the primitive's own
 * `data-entry-id`).
 */
export function setActiveEntryRow(container: HTMLElement, id: string | null): void {
  for (const item of container.querySelectorAll<HTMLElement>('.lb-viewer-entry-row-item')) {
    item.classList.toggle('entry-row-active', id !== null && item.dataset.entryId === id)
  }
}

export interface EntryRowsOptions {
  /** Row layout (pass-2 R1): browse lists default to the scan-dense inline row. */
  layout?: 'inline' | 'stacked'
  /** Suppress the trailing route column when the route IS the page context (frame H). */
  showRoute?: boolean
  /**
   * Distill-selection mode (frame T / D7): renders a leading checkbox on
   * every row instead of the row's normal open-link affordance, and
   * routes both the checkbox and the row body to `onSelect` instead of
   * navigating. Fork retirement for search's `ck-selrow`.
   */
  selectable?: boolean
  /** Which entry ids are currently checked, when `selectable` is true. */
  selectedIds?: ReadonlySet<string>
  /** Fired when a row's checkbox (or, in selectable mode, the row itself) toggles. */
  onSelect?: (id: string, checked: boolean) => void
  /**
   * One ghost-button action appended to every row's trailing content
   * (frame T / D7), e.g. `{ label: 'Open ↗', action: 'row-open' }`.
   * The primitive fires `{ type: action, target: entryId }`; the wrapper
   * routes it to `onTrailingAction` when provided, else navigates to the
   * entry deep link (the Open case). `action` must not reuse one of the
   * primitive's built-in action names (`entry-focus`, `entry-open`,
   * `entry-select`, `type-filter`) - those are reserved for the row's own
   * URL-target open / select / type-pill handling and would swallow a
   * trailingAction of the same name.
   */
  trailingAction?: { label: string; action: string }
  /** Custom handler for `trailingAction` clicks (receives the entry id). */
  onTrailingAction?: (id: string) => void
  /**
   * Rim + pill color source (frame T / D7). 'signal' colors every row's
   * rim and type pill with `signalColor` (a `var(--lb-*)` token) instead
   * of the per-entry-type color - for lists whose rows signal state, not
   * entry type. 'type' or absent keeps the entry-type palette.
   */
  rimTone?: 'type' | 'signal'
  /** Explicit rim/pill color token applied when `rimTone` is 'signal'. */
  signalColor?: string
  /**
   * Selected-entry rail (frame O / D1): when supplied AND the viewport is
   * rail-capable (>=1180px), a row click opens the entry into the caller's
   * rail instead of navigating to the full page. Below the breakpoint, or
   * when this callback is absent, rows navigate exactly as before - the
   * frame P responsive-collapse fallback.
   */
  onOpenEntry?: (id: string) => void
}

export function renderEntryRows(entries: EntrySummary[], opts: EntryRowsOptions = {}): HTMLElement {
  const selectable = opts.selectable === true
  return renderViewerEntryRow(
    {
      variant: 'cockpit',
      layout: opts.layout ?? 'inline',
      showAttribution: false,
      selectable,
      entries: entries.map((e) => ({
        id: e.id,
        type: e.type ?? 'log',
        timestamp: e.createdAt || e.updatedAt || '',
        preview: rowTitle(e),
        tags: e.tags,
        route: opts.showRoute === false ? undefined : e.route,
        timeLabel: timeLabelFor(e),
        // Selection mode never navigates - the row toggles instead of opening.
        openUrl: selectable ? undefined : `/entries/${encodeURIComponent(e.id)}`,
        checked: selectable ? (opts.selectedIds?.has(e.id) ?? false) : undefined,
        trailingAction: opts.trailingAction,
        rimTone: opts.rimTone,
        signalColor: opts.signalColor,
      })),
    },
    {
      density: 'compact',
      onAction: (action) => {
        if (selectable && (action.type === 'entry-focus' || action.type === 'entry-select')) {
          opts.onSelect?.(action.target, !(opts.selectedIds?.has(action.target) ?? false))
          return
        }
        // Trailing ghost action (frame T / D7): the primitive fires the
        // configured action name with target = entry id. Route it before
        // the built-ins - 'entry-open' from the trailing button carries an
        // id, not a URL, so it must not fall through to the URL handler.
        if (opts.trailingAction && action.type === opts.trailingAction.action) {
          if (opts.onTrailingAction) {
            opts.onTrailingAction(action.target)
            return
          }
          navigate(`/entries/${encodeURIComponent(action.target)}`)
          return
        }
        if (action.type === 'entry-focus') {
          if (opts.onOpenEntry && isRailCapable()) {
            opts.onOpenEntry(action.target)
            return
          }
          navigate(`/entries/${encodeURIComponent(action.target)}`)
          return
        }
        if (action.type === 'entry-open') {
          if (opts.onOpenEntry && isRailCapable()) {
            const id = entryIdFromOpenUrl(action.target)
            if (id) {
              opts.onOpenEntry(id)
              return
            }
          }
          navigate(action.target)
          return
        }
        // Broadened to the full canonical type set (not just the sidebar's
        // ratify-relevant four) - a row's type-pill click routes to any of
        // the 8 entry-type lenses, matching the root at-a-glance chips.
        if (action.type === 'type-filter' && (ENTRY_TYPES as readonly string[]).includes(action.target)) {
          navigate(`/lens/${action.target}`)
        }
      },
      onTagClick: (tag) => {
        navigate(`/search?q=${encodeURIComponent('#' + tag)}`)
      },
    },
  )
}
