import { api } from '../api.js'
import { markdownToLBlocksDocument } from '../../lib/markdown-to-lblocks.js'
import { renderMarkdownInto } from '../lib/lblocks-render.js'
import { el } from '../lib/dom.js'
import { pageLookupPaths } from '../lib/page-href.js'

/**
 * Read-only subpage viewer (release review P0-3b): the destination for
 * body wikilinks. Renders any vault-relative markdown page through the
 * same lblocks pipeline as entry bodies; wikilinks inside the page keep
 * working via the shared body-link handlers.
 *
 * Root carries `page-view` (release fix), not `entry-page` - the latter
 * drags in the entry-detail two-column grid, which squeezes this
 * single-column subpage into a narrow right column.
 */
export async function renderPageView(path: string): Promise<HTMLElement> {
  const root = el('div', 'page-wrap page-view')

  const renderNotFound = () => {
    const eyebrow = el('div', 'hero-eyebrow', 'page')
    const h = el('h1', undefined, 'Page not found')
    const detail = el('p', 'route-error-detail mono', path || 'no path given')
    root.append(eyebrow, h, detail)
    return root
  }

  // Not-found is every candidate failing to fetch or reporting `error`
  // (404). A present path with an empty string body is a genuinely empty
  // 0-byte page: it renders the normal frame with an empty-state line,
  // not the not-found state.
  const res = await readFirstPage(pageLookupPaths(path))
  if (!res) {
    return renderNotFound()
  }

  const name = res.path.split('/').pop() ?? path
  const title = name.replace(/\.md$/, '').replace(/^\d{4}-\d{2}-\d{2}-/, '').replace(/-/g, ' ')
  const headRow = el('div', 'entry-detail-head')
  const eyebrow = el('div', 'hero-eyebrow', 'page')
  const back = el('button', 'entry-back-link', 'back ↩') as HTMLButtonElement
  back.type = 'button'
  back.addEventListener('click', () => {
    history.back()
  })
  headRow.append(eyebrow, back)
  const h = el('h1', undefined, title)
  const meta = el('p', 'section-meta mono', res.path)
  const body = el('div', 'entry-body')
  if (res.body) {
    renderMarkdownInto(body, markdownToLBlocksDocument(res.body))
  } else {
    body.append(el('p', 'empty-state', 'This page is empty.'))
  }
  root.append(headRow, h, meta, body)
  return root
}

/** First candidate the host serves, or null. Candidate order comes from pageLookupPaths. */
async function readFirstPage(candidates: string[]): Promise<{ path: string; body?: string } | null> {
  for (const candidate of candidates) {
    try {
      const res = await api.readPage(candidate)
      if (!res.error && res.path) return { path: res.path, body: res.body }
    } catch {
      // Unreachable or rejected candidate - try the next one.
    }
  }
  return null
}
