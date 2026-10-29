import { api } from '../api.js'
import { normalizeEntry, normalizeRoutePath, toDateLabel } from '../models/normalizers.js'
import { timeOfDay } from '../lib/dates.js'
import { loadEntryContext } from '../models/view-data.js'
import { deriveDisplayTitle, liftProvenance } from '../lib/notation.js'
import { gatedRegion, isDegradedMetadata, limitedMetadataBand } from '../lib/degraded.js'
import { el, kvRow } from '../lib/dom.js'
import { setEntryPosition } from '../lib/entry-position.js'
import { setStatusHints } from '../lib/status-hints.js'
import { renderEntryBody, renderEntryChips } from './shared/entry-rail.js'

type Neighbor = { id: string; title: string }

/**
 * Entry detail (design pass frame P, D1): the shareable full reader.
 * Header row = type pill + date/time + "back to route ↩" link; clean
 * display title; chip row (route + tags); body through the lblocks
 * markdown pipeline over the provenance-lifted text; footer = prev/next
 * preview cards; right rail = THIS ENTRY metadata card + BACKLINKS card.
 * The topbar `n / N in <route>` counter and the status bar's "open .md"
 * hint are driven through the shell's contextual slots (entry-position.ts
 * / status-hints.ts) so this view owns none of the shell chrome directly.
 */
export async function renderEntryDetail(id: string): Promise<HTMLElement> {
  const root = el('div', 'entry-page')
  const [{ entry, error }, caps] = await Promise.all([api.readEntry(id), api.capabilities()])
  if (!entry || error) {
    root.textContent = `Entry not found: ${id}`
    return root
  }
  const e = normalizeEntry(entry)
  const lifted = liftProvenance(e.body)
  const degraded = isDegradedMetadata(caps.capabilities ?? {})

  const context = await loadEntryContext(e)
  const hasContext = context.index !== -1
  const prevId = hasContext && context.index > 0 ? context.ids[context.index - 1] : undefined
  const nextId = hasContext && context.index < context.ids.length - 1 ? context.ids[context.index + 1] : undefined
  const [prevNeighbor, nextNeighbor] = await Promise.all([fetchNeighborTitle(prevId), fetchNeighborTitle(nextId)])

  // Frame P's topbar counter and status-bar file hint are shell-owned
  // contextual slots (mirrors frame O's rail hints) - this view claims or
  // releases them on every render; degraded honesty means the file hint
  // only appears when the adapter actually exposed a path (D8).
  setEntryPosition(
    hasContext
      ? {
          index: context.index,
          total: context.ids.length,
          label: context.label,
          prevHref: prevId ? `/entries/${encodeURIComponent(prevId)}` : null,
          nextHref: nextId ? `/entries/${encodeURIComponent(nextId)}` : null,
        }
      : null,
  )
  // Frame P status bar right: file hint beside the search hint - the
  // shell's hint slot joins arrays with ' · ' (frame O precedent).
  setStatusHints(e.path ? ['↗ open .md', 'Ctrl K search'] : null)

  const main = el('div')

  if (degraded) main.appendChild(limitedMetadataBand(caps.adapter))

  const headRow = el('div', 'entry-detail-head')
  const eyebrow = el('div', 'entry-eyebrow')
  const typePill = el('span', `status-pill entry-type-${e.type ?? 'log'}`, e.type ?? 'entry')
  eyebrow.append(typePill, el('span', undefined, toDateLabel(e.updatedAt)))
  const time = timeOfDay(e.updatedAt || '')
  if (time) eyebrow.append(el('span', 'crumb-sep', '·'), el('span', undefined, time))
  headRow.appendChild(eyebrow)
  if (e.route) {
    const routePath = normalizeRoutePath(e.route)
    const back = el('a', 'entry-back-link', `back to route ↩ ${routePath}`)
    back.setAttribute('href', `/routes/${encodeURIComponent(routePath)}`)
    back.dataset.route = '1'
    headRow.appendChild(back)
  }
  main.appendChild(headRow)

  main.appendChild(el('h1', 'entry-title', e.title))

  const chips = renderEntryChips(e)
  if (chips.childElementCount > 0) main.appendChild(chips)

  main.appendChild(renderEntryBody(lifted))

  if (prevNeighbor || nextNeighbor) {
    main.appendChild(renderContextNav(context.label, prevNeighbor, nextNeighbor))
  }

  const aside = el('aside', 'cp-aside')

  const metaSection = el('div', 'gutter-section')
  const metaHead = el('div', 'gutter-head')
  metaHead.appendChild(el('span', undefined, 'this entry'))
  metaSection.appendChild(metaHead)
  const frontmatter = el('div', 'frontmatter')
  frontmatter.appendChild(kvRow('id', e.id, 'fm-row'))
  if (lifted.anchors.length > 0) frontmatter.appendChild(kvRow('anchor', lifted.anchors.join(' '), 'fm-row'))
  if (lifted.syncIds.length > 0) frontmatter.appendChild(kvRow('sync', lifted.syncIds.join(' '), 'fm-row'))
  if (e.route) frontmatter.appendChild(kvRow('route', e.route, 'fm-row'))
  const sourceLabel = e.source ?? lifted.sources.join(', ')
  if (sourceLabel) frontmatter.appendChild(kvRow('source', sourceLabel, 'fm-row'))
  if (e.session) frontmatter.appendChild(kvRow('session', e.session, 'fm-row'))
  if (e.scope) frontmatter.appendChild(kvRow('scope', e.scope, 'fm-row'))
  if (e.path) frontmatter.appendChild(kvRow('path', e.path, 'fm-row'))
  metaSection.appendChild(frontmatter)
  aside.appendChild(metaSection)

  const backlinksSection = el('div', 'gutter-section')
  const backlinksHead = el('div', 'gutter-head')
  backlinksHead.append(el('span', undefined, 'backlinks'), el('span', undefined, String(e.backlinks?.length ?? 0)))
  backlinksSection.appendChild(backlinksHead)
  if ((e.backlinks ?? []).length === 0 && degraded) {
    backlinksSection.appendChild(gatedRegion('backlinks', caps.adapter))
  } else if ((e.backlinks ?? []).length === 0) {
    backlinksSection.appendChild(el('span', 'section-meta', 'No backlinks yet.'))
  } else {
    for (const b of e.backlinks ?? []) {
      const a = el('a', 'backlink')
      a.href = b.id ? `/entries/${encodeURIComponent(b.id)}` : '#'
      if (b.id) a.dataset.route = '1'
      const label = deriveDisplayTitle(b.title ?? b.id ?? 'Backlink', b.id ?? 'Backlink')
      a.append(el('span', 'mono', b.kind ?? 'entry'), el('br'), document.createTextNode(label))
      backlinksSection.appendChild(a)
    }
  }
  aside.appendChild(backlinksSection)

  root.append(main, aside)
  return root
}

/** Best-effort neighbor title fetch for the footer preview cards; a fetch failure just drops that side. */
async function fetchNeighborTitle(id: string | undefined): Promise<Neighbor | null> {
  if (!id) return null
  try {
    const { entry, error } = await api.readEntry(id)
    if (!entry || error) return null
    return { id, title: normalizeEntry(entry).title }
  } catch {
    // api.readEntry throws on non-ok responses (network/5xx); a neighbor
    // preview is decoration, never worth failing the whole page render.
    return null
  }
}

/** `<div class="entry-context-nav">` - the frame P footer: prev/next-in-context preview cards. */
function renderContextNav(label: string, prev: Neighbor | null, next: Neighbor | null): HTMLElement {
  const nav = el('div', 'entry-context-nav')
  nav.appendChild(renderContextCard('prev', `‹ prev in ${label}`, prev))
  nav.appendChild(renderContextCard('next', `next in ${label} ›`, next))
  return nav
}

function renderContextCard(dir: 'prev' | 'next', eyebrowText: string, neighbor: Neighbor | null): HTMLElement {
  if (!neighbor) {
    return el('div', `entry-context-card ${dir} empty`)
  }
  const card = el('a', `entry-context-card ${dir}`)
  card.setAttribute('href', `/entries/${encodeURIComponent(neighbor.id)}`)
  card.dataset.route = '1'
  card.append(el('div', 'entry-context-eyebrow', eyebrowText), el('div', 'entry-context-title', neighbor.title))
  return card
}
