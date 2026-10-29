import type { EntrySummary } from '../../../types/entry.js'
import { el } from '../../lib/dom.js'
import { navigate } from '../../lib/nav.js'

/**
 * At-a-glance head (design pass v4 frame R/S, D3): statstrip + optional
 * week-sparkline cell + optional type-pill row, shared by the vault root
 * (Task 6) and the route landing head (Task 7). The caller owns the
 * eyebrow/h1/lede copy - this only renders the data-driven strip below it.
 */
export interface AtAGlanceModel {
  /** Stat cells in display order, e.g. entries / concepts / routes. */
  cells: Array<{ k: string; v: string }>
  /** 7 daily counts, oldest first (index 6 = today). Omit to hide the
   * sparkline cell entirely. */
  week?: number[]
  /** Entry-type counts. Omit (or pass an all-zero map) to hide the
   * type-pill row - the degraded-adapter case where the vault summary
   * doesn't expose per-type counts. */
  byType?: Record<string, number>
}

const DAY_MS = 24 * 60 * 60 * 1000

/**
 * Pure 7-day bucketing for the sparkline cell: oldest-first, index 6 is
 * `now`'s UTC day. Entries outside the trailing 7-day window are dropped.
 * UTC day boundaries match `timeline.ts`'s `groupByDay` (createdAt sliced
 * to its first 10 chars), so an entry lands on the same day in both views.
 */
export function bucketWeek(entries: EntrySummary[], now: Date): number[] {
  const buckets: number[] = new Array(7).fill(0)
  const todayKey = Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate())
  for (const entry of entries) {
    const time = Date.parse(entry.createdAt || '')
    if (Number.isNaN(time)) continue
    const created = new Date(time)
    const entryKey = Date.UTC(created.getUTCFullYear(), created.getUTCMonth(), created.getUTCDate())
    const dayDiff = Math.round((todayKey - entryKey) / DAY_MS)
    if (dayDiff < 0 || dayDiff > 6) continue
    buckets[6 - dayDiff] += 1
  }
  return buckets
}

export function renderAtAGlance(model: AtAGlanceModel): HTMLElement {
  // display:contents (not a real box) so the statstrip and type-pill row
  // can occupy independent cells of the caller's grid (statstrip beside
  // the hero copy, type row spanning full width beneath both) while this
  // function still returns a single element per its interface contract.
  const wrap = el('div', 'ck-glance')
  wrap.appendChild(renderStatstrip(model))
  if (model.byType && Object.values(model.byType).some((count) => count > 0)) {
    wrap.appendChild(renderTypeRow(model.byType))
  }
  return wrap
}

function renderStatstrip(model: AtAGlanceModel): HTMLElement {
  const strip = el('div', 'ck-statstrip')
  for (const cell of model.cells) {
    const c = el('div', 'cell')
    c.append(el('div', 'k', cell.k), el('div', 'v', cell.v))
    strip.appendChild(c)
  }
  if (model.week && model.week.length > 0) {
    strip.appendChild(renderSparkCell(model.week))
  }
  return strip
}

/**
 * Polish rule #5 (sparkline peak, not "today"): the max-count bucket
 * carries the accent bar, not the current-day bucket.
 */
function renderSparkCell(week: number[]): HTMLElement {
  const cell = el('div', 'ck-sparkcell')
  cell.appendChild(el('div', 'k', 'this week'))
  const spark = el('div', 'ck-spark')
  const max = Math.max(1, ...week)
  const peak = Math.max(...week)
  const peakIndex = week.indexOf(peak)
  week.forEach((count, index) => {
    const bar = el('span', index === peakIndex && peak > 0 ? 'bar hi' : 'bar')
    bar.style.height = `${Math.max(3, Math.round((count / max) * 32))}px`
    spark.appendChild(bar)
  })
  cell.appendChild(spark)
  return cell
}

function renderTypeRow(byType: Record<string, number>): HTMLElement {
  const row = el('div', 'ck-typerow')
  const entries = Object.entries(byType)
    .filter(([, count]) => count > 0)
    .sort((a, b) => b[1] - a[1])
  for (const [type, count] of entries) {
    const chip = el('button', 'ck-typechip')
    chip.type = 'button'
    chip.style.setProperty('--lp-type-color', `var(--entry-${type}, var(--entry-log))`)
    chip.appendChild(el('span', 'd'))
    chip.appendChild(document.createTextNode(type))
    chip.appendChild(el('span', 'n', String(count)))
    chip.addEventListener('click', () => navigate(`/lens/${encodeURIComponent(type)}`))
    row.appendChild(chip)
  }
  return row
}
