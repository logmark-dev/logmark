import { api } from '../api.js'
import { loadDashboardModel } from '../models/view-data.js'
import type { DashboardModel } from '../models/ui-models.js'
import type { EntrySummary } from '../../types/entry.js'
import { el } from '../lib/dom.js'
import { isDegradedMetadata, limitedMetadataBand } from '../lib/degraded.js'
import { hostFeatures } from '../lib/host.js'
import { renderEntryRows } from './shared/entry-card.js'
import { renderAtAGlance, bucketWeek } from './shared/at-a-glance.js'
import type { AtAGlanceModel } from './shared/at-a-glance.js'
import { navigate } from '../lib/nav.js'
import { localDay } from '../lib/dates.js'
import { vaultNameFrom } from '../shell/app-shell.js'

/**
 * Vault root (design pass v4 frame R, D3/D8/D9): activity-first, at-a-glance
 * head. Recent activity leads; the concept refinery drops to a second band
 * and its prose becomes the band's caption, not the page thesis.
 *
 * Deviation from frame R (lmk-oss-cockpit-cut-empty-producer-views-7qcb):
 * the review band is cut. It was built in the browser from the same
 * newest entries as Recent activity, with no review producer behind it on
 * any host. The refinery band, its caption, the `concepts` glance cell and
 * the lede's concept sentence render only where a concept producer exists
 * (lmk-hqgm: hidden on an explicit `hasConceptProducer: false`).
 */

// Sparkline bucketing wants a wider window than the dashboard's own
// recent-entries fetch (limit=6) - same fetch pattern/limit as
// timeline.ts's day-by-day browse.
const SPARKLINE_FETCH_LIMIT = 300
const ACTIVITY_BAND_CAP = 5
const CONCEPT_BAND_CAP = 6
const DAY_MS = 24 * 60 * 60 * 1000

export async function renderRootSummary(): Promise<HTMLElement> {
  const [m, weekEntries] = await Promise.all([
    loadDashboardModel(),
    api.listEntries(`limit=${SPARKLINE_FETCH_LIMIT}`),
  ])
  const root = el('div', 'root-wrap')
  const concepts = hostFeatures(m.capabilities).conceptProducer

  if (isDegradedMetadata(m.capabilities)) {
    root.appendChild(limitedMetadataBand(m.adapter))
  }

  root.appendChild(renderGlanceHead(m, weekEntries.entries, concepts))
  root.appendChild(renderActivityBand(m))
  if (concepts) root.appendChild(renderRefineryBand(m))

  return root
}

function renderGlanceHead(m: DashboardModel, weekWindowEntries: EntrySummary[], concepts: boolean): HTMLElement {
  const grid = el('div', 'glance-grid')

  const copy = el('div', 'glance-copy')
  copy.appendChild(el('div', 'hero-eyebrow', 'vault root'))
  copy.appendChild(el('h1', undefined, vaultNameFrom(m.vaultPath)))
  copy.appendChild(el('p', undefined, glanceLede(m, concepts)))
  grid.appendChild(copy)

  const model: AtAGlanceModel = {
    cells: [
      { k: 'entries', v: m.totalEntries.toLocaleString() },
      ...(concepts ? [{ k: 'concepts', v: String(m.totalConcepts) }] : []),
      { k: 'routes', v: String(m.routes.length) },
    ],
    week: bucketWeek(weekWindowEntries, new Date()),
    byType: m.byType,
  }
  grid.appendChild(renderAtAGlance(model))

  return grid
}

function glanceLede(m: DashboardModel, concepts: boolean): string {
  const now = new Date()
  const entryNoun = m.totalEntries === 1 ? 'entry' : 'entries'
  const routeNoun = m.routes.length === 1 ? 'route' : 'routes'
  const census = `${m.totalEntries.toLocaleString()} ${entryNoun} across ${m.routes.length} ${routeNoun}.`
  // No concept producer on this host: the census alone, promising nothing.
  if (!concepts) return census
  const conceptsThisWeek = m.concepts.filter((c) => withinPastWeek(c.last_distilled, now)).length
  const conceptSentence = conceptsThisWeek > 0
    ? `${conceptsThisWeek} concept${conceptsThisWeek === 1 ? '' : 's'} distilled this week.`
    : `The refinery is warming up.`
  return `${census} ${conceptSentence}`
}

function withinPastWeek(iso: string, now: Date): boolean {
  const time = Date.parse(iso || '')
  if (Number.isNaN(time)) return false
  const diffDays = (now.getTime() - time) / DAY_MS
  return diffDays >= 0 && diffDays < 7
}

function relativeDayLabel(iso: string): string {
  if (!iso) return ''
  // Routed through localDay (release fix) - a raw `.toISOString().slice(0,10)`
  // compares UTC calendar days, which drifts a local-midnight-adjacent
  // capture onto the wrong side of "today"/"yesterday" for any user not
  // on UTC. localDay derives the calendar day from local Date getters,
  // matching every other date label in the cockpit (src/ui/lib/dates.ts).
  const day = localDay(iso)
  const now = new Date()
  const today = localDay(now.toISOString())
  if (day === today) return 'today'
  const yesterday = localDay(new Date(now.getTime() - DAY_MS).toISOString())
  if (day === yesterday) return 'yesterday'
  return day
}

/**
 * Band 1 (D8): recent activity leads. Rows fold onto the shared entry-row
 * primitive with a trailing `Open ↗` ghost (frame T / D7) - no rail on
 * root (frame R shows none), so the row body's own click keeps its normal
 * full-page navigation too. `row-open` is a distinct action name so the
 * trailing button's id-shaped target never collides with the row body's
 * own `entry-open` (URL-shaped target).
 */
function renderActivityBand(m: DashboardModel): HTMLElement {
  const band = el('div', 'ck-band')
  band.appendChild(bandHead('Recent activity', relativeDayLabel(m.lastCaptureLabel), 'timeline →', '/timeline'))

  const entries = m.recentEntries.slice(0, ACTIVITY_BAND_CAP)
  if (entries.length === 0) {
    band.appendChild(el('p', 'empty-state', 'No entries yet.'))
    return band
  }

  band.appendChild(
    renderEntryRows(entries, {
      trailingAction: { label: 'Open ↗', action: 'row-open' },
      onTrailingAction: (id) => navigate(`/entries/${encodeURIComponent(id)}`),
    }),
  )
  return band
}

/**
 * Band 2 (D8): the concept refinery drops to a second band; its prose
 * becomes the band's caption instead of the page's hero thesis.
 */
function renderRefineryBand(m: DashboardModel): HTMLElement {
  const band = el('div', 'ck-band')
  band.appendChild(bandHead('Concept refinery', String(m.totalConcepts), 'concepts →', '/concepts'))

  if (m.concepts.length === 0) {
    band.appendChild(el('p', 'empty-state', 'No concept pages yet.'))
    return band
  }

  const grid = el('div', 'concept-mini-grid')
  for (const c of m.concepts.slice(0, CONCEPT_BAND_CAP)) {
    const card = el('a', 'ck-concept-mini')
    card.href = `/concepts/${encodeURIComponent(c.slug)}`
    card.dataset.route = '1'
    card.appendChild(el('span', 'ct', c.title))
    const sourceNoun = c.source_count === 1 ? 'source' : 'sources'
    card.appendChild(el('span', 'cm', `${c.source_count} ${sourceNoun} · ${c.stale_count > 0 ? 'drift' : 'fresh'}`))
    grid.appendChild(card)
  }
  band.appendChild(grid)
  band.appendChild(
    el(
      'p',
      'refinery-caption',
      'The refinery is where enough vault material accumulates to become synthesis.',
    ),
  )

  return band
}

function bandHead(title: string, count: string, moreLabel: string, moreHref: string): HTMLElement {
  const head = el('div', 'ck-bandhead')
  head.appendChild(el('span', 'ct', title))
  head.appendChild(el('span', 'n', `· ${count}`))
  const more = el('a', 'more', moreLabel)
  more.href = moreHref
  more.dataset.route = '1'
  head.appendChild(more)
  return head
}
