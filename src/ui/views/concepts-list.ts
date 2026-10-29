import { api } from '../api.js'
import type { ConceptPageSummary } from '../../types/concept-page.js'
import type { VaultSummary } from '../../types/vault.js'
import { normalizeVaultSummary } from '../models/normalizers.js'
import { ckSelect } from '../lib/controls.js'
import { hostFeatures } from '../lib/host.js'
import { redirectTo, type RouteRedirect } from '../router.js'
import { localDay } from '../lib/dates.js'

type ConceptFilter = 'all' | 'mature' | 'draft' | 'stale'
type ConceptSort = 'recent' | 'maturity' | 'source health'

const FILTERS: ConceptFilter[] = ['all', 'mature', 'draft', 'stale']
const SORTS = ['recent', 'maturity', 'source health'] as const

const MATURITY_RANK: Record<ConceptPageSummary['status'], number> = {
  mature: 0,
  draft: 1,
  archived: 2,
}

function sortPages(pages: ConceptPageSummary[], sort: ConceptSort): ConceptPageSummary[] {
  const byRecent = (a: ConceptPageSummary, b: ConceptPageSummary) =>
    (b.last_distilled || '').localeCompare(a.last_distilled || '')
  const sorted = [...pages]
  if (sort === 'maturity') {
    sorted.sort((a, b) => MATURITY_RANK[a.status] - MATURITY_RANK[b.status] || byRecent(a, b))
  } else if (sort === 'source health') {
    sorted.sort((a, b) => (b.stale_count ?? 0) - (a.stale_count ?? 0) || byRecent(a, b))
  } else {
    sorted.sort(byRecent)
  }
  return sorted
}

/**
 * The /concepts route (lmk-hqgm): the index exists only where a producer
 * writes concept pages. A host that reports `hasConceptProducer: false`
 * (the open daemon) sends a deep link to the vault root instead of an
 * always-empty index; absent or true renders the page.
 */
export async function renderConceptsIndexRoute(): Promise<HTMLElement | RouteRedirect> {
  const caps = await api.capabilities()
  if (!hostFeatures(caps?.capabilities).conceptProducer) return redirectTo('/')
  return renderConceptsList()
}

export async function renderConceptsList(): Promise<HTMLElement> {
  const root = document.createElement('div')
  root.className = 'concepts-index-page page-wrap'

  const [{ pages }, vault] = await Promise.all([api.listConcepts(), api.vaultSummary()])
  const summary = normalizeVaultSummary(vault.summary)
  let activeFilter: ConceptFilter = 'all'
  let activeSort: ConceptSort = 'recent'

  const hero = document.createElement('header')
  hero.className = 'concepts-index-hero'

  // Compact head (frame W, D9): "hero reserved for root" - the old
  // explanatory paragraph is removed; the metrics strip alongside it
  // already carries the meta (total / mature / stale).
  const heroCopy = document.createElement('div')
  const eyebrow = document.createElement('div')
  eyebrow.className = 'hero-eyebrow'
  eyebrow.textContent = 'concept pages'
  const h = document.createElement('h1')
  h.textContent = 'Concepts'
  heroCopy.append(eyebrow, h)

  const metrics = document.createElement('div')
  metrics.className = 'concepts-index-metrics'
  metrics.append(
    metric('total', String(pages.length)),
    metric('mature', String(pages.filter((page) => page.status === 'mature').length)),
    metric('stale', String(pages.filter((page) => (page.stale_count ?? 0) > 0).length)),
  )

  hero.append(heroCopy, metrics)
  root.appendChild(hero)

  const toolbar = document.createElement('div')
  toolbar.className = 'concepts-index-toolbar'

  const tabs = document.createElement('div')
  tabs.className = 'concepts-index-tabs'

  const grid = document.createElement('div')
  grid.className = 'concepts-index-grid'

  const renderTiles = () => {
    grid.replaceChildren()
    const filtered = filterPages(pages, activeFilter)
    if (filtered.length === 0) {
      if (pages.length === 0 && activeFilter === 'all') {
        grid.appendChild(firstRunEmptyState(summary))
        return
      }
      const empty = document.createElement('div')
      empty.className = 'empty-state concepts-index-empty'
      empty.textContent = emptyMessage(pages.length, activeFilter)
      grid.appendChild(empty)
      return
    }

    for (const page of sortPages(filtered, activeSort)) {
      grid.appendChild(conceptTile(page))
    }
  }

  for (const filter of FILTERS) {
    const button = document.createElement('button')
    button.type = 'button'
    button.className = 'concepts-index-tab'
    button.textContent = filter
    button.setAttribute('aria-pressed', filter === activeFilter ? 'true' : 'false')
    button.addEventListener('click', () => {
      activeFilter = filter
      for (const tab of tabs.querySelectorAll<HTMLButtonElement>('.concepts-index-tab')) {
        tab.setAttribute('aria-pressed', tab.textContent === activeFilter ? 'true' : 'false')
      }
      renderTiles()
    })
    tabs.appendChild(button)
  }

  // Live control-kit Select (pass-2 R6 / frame J) - summaries carry
  // last_distilled, status, and stale_count on every adapter, so the
  // sort works client-side everywhere and never needs the gated state.
  const sort = ckSelect<ConceptSort>('sort', SORTS, activeSort, (value) => {
    activeSort = value
    renderTiles()
  })
  sort.classList.add('concepts-index-sort')

  toolbar.append(tabs, sort)
  root.append(toolbar, grid)
  renderTiles()

  return root
}

/**
 * Designed first-run empty state (design pass frame E): icon card and a
 * one-line explanation grounded in the vault's actual entry count. The
 * distill CTA, explainer, and nearest-route hint were cut with distill
 * review (lmk-oss-cockpit-cut-empty-producer-views-7qcb), so the copy
 * promises nothing beyond pages already saved in the vault.
 */
function firstRunEmptyState(summary: VaultSummary): HTMLElement {
  const card = document.createElement('div')
  card.className = 'concepts-empty-card'

  const icon = document.createElement('div')
  icon.className = 'concepts-empty-icon'
  icon.setAttribute('aria-hidden', 'true')
  icon.textContent = '◇'

  const h = document.createElement('h2')
  h.textContent = 'No concept pages yet'

  const p = document.createElement('p')
  const total = summary.total_entries.toLocaleString()
  p.textContent = `Concept pages are the synthesis layer above your ${total} captures. Pages saved in this vault's concepts folder appear here.`

  card.append(icon, h, p)

  const wrap = document.createElement('div')
  wrap.className = 'concepts-empty-wrap'
  wrap.appendChild(card)
  return wrap
}

function filterPages(pages: ConceptPageSummary[], filter: ConceptFilter): ConceptPageSummary[] {
  if (filter === 'mature') return pages.filter((page) => page.status === 'mature')
  if (filter === 'draft') return pages.filter((page) => page.status === 'draft')
  if (filter === 'stale') return pages.filter((page) => (page.stale_count ?? 0) > 0)
  return pages
}

function emptyMessage(totalPages: number, filter: ConceptFilter): string {
  if (totalPages === 0) return 'No concept pages yet.'
  if (filter === 'all') return 'No concept pages match this filter.'
  return `No ${filter} concept pages match this filter.`
}

function conceptTile(page: ConceptPageSummary): HTMLAnchorElement {
  const tile = document.createElement('a')
  tile.className = 'concepts-index-tile'
  tile.href = `/concepts/${encodeURIComponent(page.slug)}`
  tile.dataset.route = '1'

  const top = document.createElement('div')
  top.className = 'concepts-index-tile-top'
  const status = document.createElement('span')
  status.className = `status-pill ${page.status}`
  status.textContent = page.status
  const version = document.createElement('span')
  version.className = 'mono'
  version.textContent = `v${page.distillation_version ?? 1}`
  top.append(status, version)

  const title = document.createElement('h2')
  title.textContent = page.title

  const facts = document.createElement('div')
  facts.className = 'concepts-index-facts'
  facts.append(
    fact(`${page.source_count} ${page.source_count === 1 ? 'source' : 'sources'}`),
    fact(`${page.stale_count ?? 0} stale`),
    fact(formatDate(page.last_distilled)),
  )

  const tags = document.createElement('div')
  tags.className = 'tag-list'
  for (const tag of page.tags ?? []) {
    const el = document.createElement('span')
    el.className = 'tag'
    el.textContent = `#${tag}`
    tags.appendChild(el)
  }
  if (tags.childElementCount === 0) {
    const el = document.createElement('span')
    el.className = 'tag'
    el.textContent = '#untagged'
    tags.appendChild(el)
  }

  tile.append(top, title, facts, tags)
  return tile
}

function metric(label: string, value: string): HTMLElement {
  const el = document.createElement('div')
  el.className = 'concepts-index-metric'
  const k = document.createElement('span')
  k.className = 'k'
  k.textContent = label
  const v = document.createElement('span')
  v.className = 'v'
  v.textContent = value
  el.append(k, v)
  return el
}

function fact(text: string): HTMLElement {
  const el = document.createElement('span')
  el.textContent = text
  return el
}

function formatDate(value: string): string {
  if (!value) return 'not distilled'
  return localDay(value)
}
