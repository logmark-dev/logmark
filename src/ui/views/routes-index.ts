import { api } from '../api.js'
import { normalizeRoutePath, normalizeVaultSummary } from '../models/normalizers.js'
import { el } from '../lib/dom.js'

/**
 * All-routes index (release review P0-7): every route navigable, with true
 * counts. `normalizeVaultSummary` prefers the daemon's full `all_routes`
 * census over the top-10-capped `top_routes` wire field when present, so
 * `s.top_routes` here already carries every route - not just the rail's
 * visible top-8.
 */
export async function renderRoutesIndex(): Promise<HTMLElement> {
  const { summary } = await api.vaultSummary()
  const s = normalizeVaultSummary(summary)
  const root = el('div', 'page-wrap')
  root.appendChild(el('div', 'hero-eyebrow', 'routes'))
  root.appendChild(el('h1', undefined, 'All routes'))
  root.appendChild(el('p', 'section-meta', `${s.top_routes.length} routes`))
  const list = el('div', 'routes-index')
  for (const r of s.top_routes) {
    const routePath = normalizeRoutePath(r.path)
    const a = el('a', 'routes-index-row')
    a.href = `/routes/${encodeURIComponent(routePath)}`
    a.dataset.route = '1'
    a.append(el('span', undefined, r.name ?? `+${routePath}`), el('span', 'mono', String(r.count ?? '')))
    list.appendChild(a)
  }
  root.appendChild(list)
  return root
}
