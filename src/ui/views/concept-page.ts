import { renderConceptPage, type ConceptPageData } from '@logmark/lblocks'
import { api } from '../api.js'
import { healthOrUnavailable, mapSourceFreshness } from '../models/view-data.js'
import type { SourceFreshness } from '../models/ui-models.js'
import { normalizeEntry } from '../models/normalizers.js'
import { el, kvRow } from '../lib/dom.js'
import { bodyLinkOptions } from '../lib/body-links.js'
import { navigate } from '../lib/nav.js'
import { count } from '../lib/count.js'

/** A source row whose freshness may be unknowable (health lint 501 under
 * daemon hosting) - `status: null` renders an explicit "freshness n/a"
 * instead of fabricating 'fresh'. */
type SourceRow = Pick<SourceFreshness, 'id' | 'changedAt'> & { status: SourceFreshness['status'] | null }

export async function renderConceptPageView(slug: string): Promise<HTMLElement> {
  const root = el('div', 'concept-page')
  // The health call degrades, it never gates the page (rc6): without a
  // host lint scan healthOrUnavailable never requests /api/health and
  // returns null; a 501 from a host that claimed one degrades the same
  // way. Per-source freshness is then marked unknown, never invented.
  const [{ page, error }, health] = await Promise.all([
    api.readConcept(slug),
    api.capabilities().then((record) => healthOrUnavailable(record)),
  ])
  if (!page || error) {
    root.textContent = `Concept not found: ${slug}`
    return root
  }

  // The lblocks concept-page primitive is the canonical open rendering of a
  // concept page (title, status chip, tags, body, related pills, sources
  // footer). The cockpit adds vault-specific chrome around it: frontmatter,
  // and per-source freshness.
  const data: ConceptPageData = {
    slug: page.slug,
    title: page.title,
    status: page.status,
    tags: page.tags,
    body: page.body,
    sourceCount: page.sources.length,
    relatedConcepts: page.related_concepts,
  }

  const main = el('div', 'cp-main')
  // No applyTheme here: the app.css --lb-* bridge (theme-neutral, flips
  // with :root[data-theme]) styles the primitive.
  main.appendChild(renderConceptPage(data, { theme: 'dark', ...bodyLinkOptions() }))
  // Related-concept pills are host-delegated via data-slug.
  main.addEventListener('click', (e) => {
    const pill = (e.target as HTMLElement).closest<HTMLElement>('[data-slug]')
    if (pill?.dataset.slug) {
      navigate(`/concepts/${encodeURIComponent(pill.dataset.slug)}`)
    }
  })

  const frontmatter = el('div', 'frontmatter')
  frontmatter.append(
    kvRow('slug', page.slug, 'fm-row'),
    kvRow('created', page.created, 'fm-row'),
    kvRow('last_distilled', page.last_distilled, 'fm-row'),
    kvRow('distillation_version', String(page.distillation_version), 'fm-row'),
    kvRow('status', page.status, 'fm-row'),
    kvRow('tags', `[${page.tags.join(', ')}]`, 'fm-row'),
    kvRow('sources', count(page.sources.length, 'entry', 'entries'), 'fm-row'),
  )
  main.appendChild(frontmatter)
  main.appendChild(
    el('div', 'ledger-note mono', `current version: v${page.distillation_version}. Multi-version history is not stored yet.`)
  )

  const aside = el('aside', 'cp-aside')

  const sourcesSection = el('div', 'gutter-section')
  const sourcesHead = el('div', 'gutter-head')
  sourcesHead.append(el('span', undefined, 'sources'), el('span', undefined, String(page.sources.length)))
  sourcesSection.appendChild(sourcesHead)
  const freshness: SourceRow[] = health
    ? mapSourceFreshness(page.slug, page.sources, health)
    : page.sources.map((source) => ({ id: source.id, status: null }))
  // Title labels keyed by entry id (release fix) - the async fill loop
  // below patches these directly rather than re-querying the DOM by id,
  // which sidesteps needing an attribute-selector escape for ids with
  // characters that aren't CSS-selector-safe.
  const titleLabelById = new Map<string, HTMLElement>()
  for (const s of freshness) {
    const a = el('a', 'source')
    a.href = `/entries/${encodeURIComponent(s.id)}`
    a.dataset.route = '1'
    const cell = el('span')
    const titleLabel = el('span', undefined, s.id)
    titleLabel.dataset.entryId = s.id
    titleLabelById.set(s.id, titleLabel)
    cell.append(
      titleLabel,
      el('br'),
      el('span', 'section-meta mono', `${s.id} · ${s.status ?? 'freshness n/a'}${s.changedAt ? ` | ${s.changedAt}` : ''}`)
    )
    // Unknown freshness: a bare pulse dot (inherits the row's muted ink)
    // instead of borrowing the green 'fresh' status color.
    a.append(el('span', s.status ? `${s.status} pulse` : 'pulse'), cell)
    sourcesSection.appendChild(a)
  }
  aside.appendChild(sourcesSection)

  // Async title fill (release fix): first paint no longer blocks on up to
  // 40 `api.readEntry` calls - rows render immediately with the id
  // fallback above, and each row's label is patched in place when the
  // title resolves. Same 40 cap, same per-row try/catch fallback as
  // before; skips the DOM write if root is no longer connected (SPA
  // navigated away before the fetch resolved).
  void Promise.all(
    freshness.slice(0, 40).map(async (s) => {
      try {
        const { entry } = await api.readEntry(s.id)
        if (!entry || !root.isConnected) return
        const label = titleLabelById.get(s.id)
        if (label) label.textContent = normalizeEntry(entry).title
      } catch {
        // A missing source keeps its id fallback.
      }
    }),
  )

  root.append(main, aside)
  return root
}
