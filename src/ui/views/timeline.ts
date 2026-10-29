import type { EntrySummary } from '../../types/entry.js'
import { api } from '../api.js'
import { isDegradedMetadata, limitedMetadataBand } from '../lib/degraded.js'
import { localDay } from '../lib/dates.js'
import { renderEntryRows, setActiveEntryRow } from './shared/entry-card.js'
import { attachEntryRail } from './shared/entry-rail.js'

const DAYS_PER_PAGE = 14
const FETCH_LIMIT = 300

export function groupByDay(
  entries: EntrySummary[]
): Array<{ day: string; entries: EntrySummary[] }> {
  const byDay = new Map<string, EntrySummary[]>()
  for (const e of entries) {
    const day = localDay(e.createdAt)
    const list = byDay.get(day) ?? []
    list.push(e)
    byDay.set(day, list)
  }
  return Array.from(byDay.entries())
    .sort(([a], [b]) => b.localeCompare(a))
    .map(([day, list]) => ({
      day,
      entries: [...list].sort((a, b) => b.createdAt.localeCompare(a.createdAt)),
    }))
}

export async function renderTimeline(): Promise<HTMLElement> {
  // Over-fetch by one so a vault with exactly FETCH_LIMIT entries can be
  // told apart from one that has more: `entries.length > FETCH_LIMIT` only
  // trips when there really is an (N+1)th entry beyond the display window.
  const [{ entries: fetched }, caps] = await Promise.all([
    api.listEntries(`limit=${FETCH_LIMIT + 1}`),
    api.capabilities(),
  ])
  const capped = fetched.length > FETCH_LIMIT
  const entries = capped ? fetched.slice(0, FETCH_LIMIT) : fetched
  const groups = groupByDay(entries)

  const root = document.createElement('div')
  root.className = 'page-wrap timeline-page'

  if (isDegradedMetadata(caps.capabilities ?? {})) {
    root.appendChild(limitedMetadataBand(caps.adapter))
  }

  const header = document.createElement('section')
  const eyebrow = document.createElement('div')
  eyebrow.className = 'hero-eyebrow'
  eyebrow.textContent = 'Timeline'
  const h = document.createElement('h1')
  h.textContent = 'Day-by-day vault activity'
  const count = document.createElement('p')
  count.className = 'section-meta'
  count.textContent = capped
    ? `the ${entries.length} most recent entries across ${groups.length} days`
    : `${entries.length} ${entries.length === 1 ? 'entry' : 'entries'} across ${groups.length} ${groups.length === 1 ? 'day' : 'days'}`
  header.append(eyebrow, h, count)
  root.appendChild(header)

  if (groups.length === 0) {
    const empty = document.createElement('p')
    empty.className = 'empty-state'
    empty.textContent = 'No entries yet.'
    root.appendChild(empty)
    return root
  }

  const list = document.createElement('div')
  let shown = 0

  const more = document.createElement('button')
  more.className = 'btn secondary'
  more.type = 'button'
  more.textContent = 'Show earlier days'
  more.dataset.action = 'timeline-more'

  // The rail context grows as "Show earlier days" reveals more rows -
  // `attachEntryRail` reads `ctx.ids` live, so pushing onto this same
  // array keeps up/down navigation in sync with what's rendered.
  const ctx = { ids: [] as string[], label: 'vault' }
  const { layout, rail } = attachEntryRail(list, ctx, setActiveEntryRow)

  const showMore = (): void => {
    for (const g of groups.slice(shown, shown + DAYS_PER_PAGE)) {
      const dayHeader = document.createElement('div')
      dayHeader.className = 'timeline-day-header'
      dayHeader.textContent = g.day
      list.appendChild(dayHeader)
      list.appendChild(renderEntryRows(g.entries, { onOpenEntry: (id) => void rail.open(id) }))
      ctx.ids.push(...g.entries.map((e) => e.id))
    }
    shown = Math.min(shown + DAYS_PER_PAGE, groups.length)
    more.hidden = shown >= groups.length
  }
  more.addEventListener('click', showMore)
  showMore()

  root.append(layout, more)

  if (capped) {
    const band = document.createElement('p')
    band.className = 'section-meta window-note'
    band.textContent = 'older entries are not loaded here - reach them via search or a route page'
    root.appendChild(band)
  }

  return root
}
