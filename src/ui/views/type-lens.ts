import { api } from '../api.js'
import { windowNote } from '../lib/dom.js'
import { normalizeVaultSummary } from '../models/normalizers.js'
import { renderEntryRows, setActiveEntryRow } from './shared/entry-card.js'
import { attachEntryRail } from './shared/entry-rail.js'
import { ENTRY_TYPE_LABELS, ENTRY_TYPES, LENS_TYPES } from './lens-types.js'
import type { EntryLensType } from './lens-types.js'

export { LENS_TYPES }

/**
 * Renders any of the 8 canonical entry types (`ENTRY_TYPES`), not just the
 * sidebar's ratify-relevant four - a type chip or tree dot for `log`,
 * `idea`, `gap`, or `analogy` lands here exactly like `task`/`block`/
 * `decision`/`question` do. Unknown strings keep the graceful empty state.
 */
export async function renderTypeLens(type: string): Promise<HTMLElement> {
  const root = document.createElement('div')
  root.className = 'page-wrap lens-page'

  if (!(ENTRY_TYPES as readonly string[]).includes(type)) {
    const unknown = document.createElement('p')
    unknown.className = 'empty-state'
    unknown.textContent = `Unknown lens: ${type}`
    root.appendChild(unknown)
    return root
  }

  const lens = type as EntryLensType
  const [{ entries }, summaryResult] = await Promise.all([
    api.listEntries(`type=${encodeURIComponent(lens)}&limit=2000`),
    api.vaultSummary().catch(() => null),
  ])
  // The vault summary is a purely-cosmetic enrichment (drives the window
  // note only) - its failure must never fail the whole lens page, so a
  // null result here just means no window note.
  const typeTotal = summaryResult ? normalizeVaultSummary(summaryResult.summary).by_type[lens] : undefined

  const header = document.createElement('section')
  const eyebrow = document.createElement('div')
  eyebrow.className = 'hero-eyebrow'
  eyebrow.textContent = 'Lens'
  const h = document.createElement('h1')
  h.textContent = ENTRY_TYPE_LABELS[lens]
  const count = document.createElement('p')
  count.className = 'lens-count'
  count.textContent = `${entries.length} ${entries.length === 1 ? 'entry' : 'entries'}`
  header.append(eyebrow, h, count)
  const note = windowNote(entries.length, typeTotal, `${lens} entries`)
  if (note) header.appendChild(note)
  root.appendChild(header)

  const list = document.createElement('div')
  if (entries.length === 0) {
    const empty = document.createElement('p')
    empty.className = 'empty-state'
    empty.textContent = `No ${lens} entries in this vault.`
    list.appendChild(empty)
    root.appendChild(list)
  } else {
    const ctx = { ids: entries.map((e) => e.id), label: ENTRY_TYPE_LABELS[lens] }
    const { layout, rail } = attachEntryRail(list, ctx, setActiveEntryRow)
    list.appendChild(renderEntryRows(entries, { onOpenEntry: (id) => void rail.open(id) }))
    root.appendChild(layout)
  }
  return root
}
