import { api } from '../api.js'
import { normalizeRoutePath, normalizeVaultSummary } from '../models/normalizers.js'
import type { EntrySummary } from '../../types/entry.js'
import { renderEntryRows, setActiveEntryRow } from './shared/entry-card.js'
import { attachEntryRail } from './shared/entry-rail.js'
import { count } from '../lib/count.js'

type SearchHit = EntrySummary

/**
 * Search (pass-2 R3 / frame I): a prompt state instead of a blank pane and
 * results as plain inline entry-rows that open into the selected-entry
 * rail. `?route=<path>` scopes results to one route.
 *
 * Deviation from frames T / V (lmk-oss-cockpit-cut-empty-producer-views-7qcb):
 * the distill-selection mode, its naming panel and the `?distill=1`
 * pre-seed are cut with distill review - /api/distill answers 501 on both
 * the open daemon and the desktop, so the mode led nowhere on any host. A
 * stale `?distill=1` link is ignored and renders plain search.
 */

function emptyState(message: string): HTMLElement {
  const div = document.createElement('div')
  div.className = 'search-empty'
  div.textContent = message
  return div
}

export async function renderSearch(initialQuery: string): Promise<HTMLElement> {
  const params = new URLSearchParams(location.search)
  const route = params.get('route')
  const routePath = route ? normalizeRoutePath(route) : ''
  let currentHits: SearchHit[] = []
  let searchRequestId = 0
  let totalEntries = 0

  const root = document.createElement('div')
  root.className = 'search-page page-wrap'

  // Compact head (frame W, D9): search is an inner view.
  const hero = document.createElement('section')
  hero.className = 'search-hero'
  const eyebrow = document.createElement('div')
  eyebrow.className = 'hero-eyebrow'
  eyebrow.textContent = routePath ? `search | +${routePath}` : 'search'
  const h = document.createElement('h1')
  h.textContent = 'Find the captures'
  hero.append(eyebrow, h)
  root.appendChild(hero)

  const layout = document.createElement('div')
  layout.className = 'search-layout'

  const main = document.createElement('div')
  main.className = 'search-main'

  const field = document.createElement('div')
  field.className = 'search-field'
  const glyph = document.createElement('span')
  glyph.className = 'search-field-glyph'
  glyph.textContent = '⌕'
  const input = document.createElement('input')
  input.className = 'search-box'
  input.placeholder = 'Search vault...'
  input.value = initialQuery
  const fieldMeta = document.createElement('span')
  fieldMeta.className = 'search-field-meta mono'
  field.append(glyph, input, fieldMeta)
  main.appendChild(field)

  const strip = document.createElement('div')
  strip.className = 'search-strip'
  main.appendChild(strip)

  const results = document.createElement('div')
  results.className = 'search-results'
  const railCtx = { ids: [] as string[], label: 'results' }
  const { layout: resultsLayout, rail } = attachEntryRail(results, railCtx, setActiveEntryRow)
  main.appendChild(resultsLayout)

  layout.append(main)
  root.appendChild(layout)

  function renderFieldMeta(): void {
    const scope = totalEntries > 0 ? count(totalEntries, 'entry', 'entries') : 'vault'
    fieldMeta.textContent = currentHits.length > 0 ? `${count(currentHits.length, 'result')} · ${scope}` : scope
  }

  function renderStrip(): void {
    strip.replaceChildren()
    if (currentHits.length === 0) return
    const label = document.createElement('span')
    label.className = 'hero-eyebrow'
    label.textContent = 'Results'
    strip.appendChild(label)
  }

  function renderResults(message?: string): void {
    railCtx.ids = currentHits.map((hit) => hit.id)
    if (message) {
      results.replaceChildren(emptyState(message))
    } else if (currentHits.length === 0) {
      results.replaceChildren(emptyState('No captures matched this query.'))
    } else {
      results.replaceChildren(renderEntryRows(currentHits, { onOpenEntry: (id) => void rail.open(id) }))
    }
    renderStrip()
    renderFieldMeta()
  }

  async function runSearch(q: string): Promise<void> {
    // A new query invalidates whatever the rail was showing.
    rail.close()
    const requestId = ++searchRequestId
    const query = q.trim()
    if (!query) {
      currentHits = []
      renderResults(promptMessage())
      return
    }

    const { results: allHits } = await api.search(query)
    if (requestId !== searchRequestId) return
    currentHits = routePath
      ? allHits.filter((hit) => normalizeRoutePath(hit.route ?? '') === routePath)
      : allHits
    renderResults()
  }

  function promptMessage(): string {
    const scope = totalEntries > 0 ? count(totalEntries, 'entry', 'entries') : 'the vault'
    return routePath ? `Search ${scope} in +${routePath}...` : `Search ${scope}...`
  }

  let searchDebounce: ReturnType<typeof setTimeout> | null = null
  input.addEventListener('input', () => {
    if (searchDebounce) clearTimeout(searchDebounce)
    searchDebounce = setTimeout(() => {
      searchDebounce = null
      // The SPA may have navigated away before this timer fired (router.ts
      // has no view-unmount hook to clear it) - self-heal like entry-rail's
      // onKeydown instead of firing a search into a detached view.
      if (!root.isConnected) return
      void runSearch(input.value)
    }, 150)
  })

  // Prompt state fills in the real entry count once the summary arrives.
  void api.vaultSummary().then(({ summary }) => {
    totalEntries = normalizeVaultSummary(summary).total_entries
    renderFieldMeta()
    if (currentHits.length === 0 && !input.value.trim()) renderResults(promptMessage())
  }).catch(() => {})

  renderResults(promptMessage())
  if (initialQuery.trim()) await runSearch(initialQuery)

  input.dataset.searchInput = '1'
  requestAnimationFrame(() => input.focus())

  return root
}
