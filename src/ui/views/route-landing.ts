import { loadRouteLandingModel } from '../models/view-data.js'
import type { RouteLandingModel } from '../models/ui-models.js'
import type { EntrySummary } from '../../types/entry.js'
import { el, sectionHead, windowNote } from '../lib/dom.js'
import { hostFeatures } from '../lib/host.js'
import { renderEntryRows, setActiveEntryRow } from './shared/entry-card.js'
import { attachEntryRail } from './shared/entry-rail.js'
import type { RailContext } from './shared/entry-rail.js'
import { renderAtAGlance } from './shared/at-a-glance.js'
import type { AtAGlanceModel } from './shared/at-a-glance.js'

/**
 * Route landing (design pass v4 frame S + O, D3/D4/D9): a compact head -
 * eyebrow + h1 + short lede + at-a-glance statstrip - no tall hero (that's
 * reserved for the vault root, frame R). Sits above a type filter-chip row
 * (frame O's `all · N` pattern) that narrows the rail-driving entries band
 * in place. The Concept coverage band renders only on a host with a
 * concept producer (lmk-hqgm).
 */

function emptyState(message: string): HTMLElement {
  return el('div', 'feed-item', message)
}

export async function renderRouteLanding(route: string): Promise<HTMLElement> {
  const m = await loadRouteLandingModel(route)
  const root = el('div', 'root-wrap')

  root.appendChild(renderGlanceHead(m))
  root.appendChild(renderEntriesBand(m))
  // Concept coverage only where a producer writes concept pages (lmk-hqgm).
  if (hostFeatures(m.capabilities).conceptProducer) root.appendChild(renderConceptCoverage(m))

  return root
}

function renderConceptCoverage(m: RouteLandingModel): DocumentFragment {
  const section = document.createDocumentFragment()
  section.appendChild(
    sectionHead('Concept coverage', `${m.concepts.length} concept${m.concepts.length === 1 ? '' : 's'}`)
  )

  const concepts = el('div', 'concept-grid')
  if (m.concepts.length === 0) {
    concepts.appendChild(emptyState('No concept pages tag this route yet.'))
  } else {
    for (const concept of m.concepts) {
      const a = el('a', 'concept-card')
      a.href = concept.href
      a.dataset.route = '1'
      a.appendChild(
        el(
          'div',
          'section-meta',
          `${concept.status} | ${concept.source_count} source${concept.source_count === 1 ? '' : 's'}${concept.stale_count ? ` | ${concept.stale_count} stale` : ''}`
        )
      )
      a.appendChild(el('h3', undefined, concept.title))
      const tags = el('div', 'tag-list')
      for (const tag of (concept.tags ?? []).slice(0, 4)) tags.appendChild(el('span', 'tag', tag))
      a.appendChild(tags)
      concepts.appendChild(a)
    }
  }
  section.appendChild(concepts)
  return section
}

/**
 * Compact head (frame S, D9): eyebrow "route" + h1 route path + short lede
 * beside the shared at-a-glance statstrip (entries / types / this-week
 * sparkline - the byType breakdown moves to the interactive filter row
 * below instead of the read-only type-pill row root uses). The toolbar
 * carries "Search this route" only: the route distill action was cut with
 * distill review (lmk-oss-cockpit-cut-empty-producer-views-7qcb).
 */
function renderGlanceHead(m: RouteLandingModel): HTMLElement {
  const grid = el('div', 'glance-grid')

  const copy = el('div', 'glance-copy route-head')
  copy.appendChild(el('div', 'hero-eyebrow', 'route'))
  copy.appendChild(el('h1', undefined, m.routeLabel))
  copy.appendChild(el('p', undefined, m.description))

  const toolbar = el('div', 'route-actions')
  const searchLink = el('a', 'btn secondary', 'Search this route')
  searchLink.href = `/search?route=${encodeURIComponent(m.routePath)}`
  searchLink.dataset.route = '1'
  toolbar.append(searchLink)
  copy.appendChild(toolbar)
  grid.appendChild(copy)

  const typeCount = Object.values(m.routeByType).filter((count) => count > 0).length
  const glanceModel: AtAGlanceModel = {
    cells: [
      { k: 'entries', v: m.entryCount.toLocaleString() },
      { k: 'types', v: String(typeCount) },
    ],
    week: m.week,
  }
  grid.appendChild(renderAtAGlance(glanceModel))

  return grid
}

/**
 * Incremental render page (timeline.ts's "Show earlier days" pattern):
 * render the first page of the filtered pool, then a secondary Show-more
 * button appends the next page. The rail context always carries the FULL
 * filtered id list, not just the rendered page - frame O's rail position
 * reads n/N against the whole route-scoped set, and `rail.open` fetches
 * by id so it never needs the target row's DOM to exist.
 */
const ENTRIES_PER_PAGE = 50

/**
 * Entries band (frame S bandhead + frame O filter chips + rail): the chip
 * row narrows the FULL route-scoped `m.entries` pool client-side - the
 * same pool `routeByType` was tallied over, so a chip's advertised count
 * always equals the rows its click yields. The bandhead count tracks the
 * current filter; rendering paginates via Show more.
 */
function renderEntriesBand(m: RouteLandingModel): HTMLElement {
  const band = el('div', 'ck-band')
  const countLabel = el('span', 'n', `· ${m.entries.length}`)
  const head = el('div', 'ck-bandhead')
  head.append(el('span', 'ct', 'Entries'), countLabel, el('span', 'hint', 'click a row → rail'))
  band.appendChild(head)

  const note = windowNote(m.entries.length, m.entryCount, 'entries')
  if (note) band.appendChild(note)

  if (m.entries.length === 0) {
    band.appendChild(emptyState('No entries found for this route.'))
    return band
  }

  const railCtx: RailContext = { ids: m.entries.map((e) => e.id), label: m.routeLabel }
  const listHost = el('div')
  const { layout, rail } = attachEntryRail(listHost, railCtx, setActiveEntryRow)

  const more = el('button', 'btn secondary', 'Show more')
  more.type = 'button'
  more.dataset.action = 'route-entries-more'

  let pool = m.entries
  let shown = 0

  const renderPage = (): void => {
    const page = pool.slice(shown, shown + ENTRIES_PER_PAGE)
    if (page.length > 0) {
      // Inline entry-rows; the route IS the page context, so the trailing
      // route column is suppressed (pass-2 R4 / frame H).
      listHost.appendChild(
        renderEntryRows(page, {
          showRoute: false,
          onOpenEntry: (id) => void rail.open(id),
        })
      )
    }
    shown = Math.min(shown + ENTRIES_PER_PAGE, pool.length)
    more.hidden = shown >= pool.length
  }
  more.addEventListener('click', renderPage)

  const applyPool = (filtered: EntrySummary[]): void => {
    pool = filtered
    shown = 0
    listHost.replaceChildren()
    railCtx.ids = filtered.map((e) => e.id)
    if (rail.openId && !railCtx.ids.includes(rail.openId)) rail.close()
    countLabel.textContent = `· ${filtered.length}`
    renderPage()
  }

  band.appendChild(renderFilterRow(m, applyPool))
  band.append(layout, more)
  renderPage()
  return band
}

/**
 * Type filter-chip row (frame O: `all · 53`, `log · 40`, `decision · 9`).
 * Chip counts come from the route-scoped `routeByType` tally over the SAME
 * `m.entries` pool the click narrows, so count and yield can never
 * disagree. Selection hands the filtered pool to `applyPool`, which owns
 * the list re-render, the bandhead count, and the rail-context re-point.
 */
function renderFilterRow(m: RouteLandingModel, applyPool: (filtered: EntrySummary[]) => void): HTMLElement {
  const row = el('div', 'ck-filterrow')
  const chipByType = new Map<string | null, HTMLButtonElement>()

  function applyFilter(type: string | null): void {
    for (const [chipType, chip] of chipByType) chip.classList.toggle('active', chipType === type)
    applyPool(type === null ? m.entries : m.entries.filter((e) => (e.type ?? 'log') === type))
  }

  function makeChip(type: string | null, count: number): HTMLButtonElement {
    const chip = el('button', 'ck-filterchip')
    chip.type = 'button'
    if (type) {
      chip.style.setProperty('--lp-type-color', `var(--entry-${type}, var(--entry-log))`)
      chip.appendChild(el('span', 'd'))
    }
    chip.appendChild(document.createTextNode(`${type ?? 'all'} · ${count}`))
    chip.addEventListener('click', () => applyFilter(type))
    chipByType.set(type, chip)
    row.appendChild(chip)
    return chip
  }

  makeChip(null, m.entries.length)
  const sortedTypes = Object.entries(m.routeByType)
    .filter(([, count]) => count > 0)
    .sort((a, b) => b[1] - a[1])
  for (const [type, count] of sortedTypes) makeChip(type, count)

  chipByType.get(null)?.classList.add('active')
  return row
}
