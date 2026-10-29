import { listEntriesFs } from './entry-reader.js'
import { listConceptPagesFs } from './concept-page-store.js'
import type { VaultSummary } from '../../types/vault.js'

export async function vaultSummaryFs(vault: string): Promise<VaultSummary> {
  const entries = await listEntriesFs(vault)
  const concepts = await listConceptPagesFs(vault)
  const by_type: Record<string, number> = {}
  const route_counts: Record<string, number> = {}
  const route_types: Record<string, Record<string, number>> = {}
  const tag_counts: Record<string, number> = {}
  for (const e of entries) {
    const t = e.type ?? 'log'
    by_type[t] = (by_type[t] ?? 0) + 1
    if (e.route) {
      route_counts[e.route] = (route_counts[e.route] ?? 0) + 1
      const types = (route_types[e.route] ??= {})
      types[t] = (types[t] ?? 0) + 1
    }
    for (const tag of e.tags) tag_counts[tag] = (tag_counts[tag] ?? 0) + 1
  }
  const recent_entries = [...entries]
    .sort((a, b) => b.updatedAt.localeCompare(a.updatedAt))
    .slice(0, 10)
    .map((e) => ({ id: e.id, title: e.title, updatedAt: e.updatedAt }))
  const top_routes = Object.entries(route_counts)
    .map(([path, count]) => ({ path, count }))
    .sort((a, b) => b.count - a.count)
    .slice(0, 10)
  const top_tags = Object.entries(tag_counts)
    .map(([tag, count]) => ({ tag, count }))
    .sort((a, b) => b.count - a.count)
    .slice(0, 20)
  // Full route list with per-route type counts (pass-4 frame Q / D2).
  const routes = Object.entries(route_counts)
    .map(([path, count]) => ({ path, count, by_type: route_types[path] }))
    .sort((a, b) => b.count - a.count)
  // Full route census, wire-shape `{path,count,name}` (v0 contract): count
  // desc then path ascending (ordinal) tie-break, mirrors BuildSummary()'s
  // allRoutes in V0Routes.cs. route_count is its length (not capped at 10
  // like top_routes).
  const all_routes = Object.entries(route_counts)
    .map(([path, count]) => ({ path, count, name: '+' + path }))
    .sort((a, b) => b.count - a.count || (a.path < b.path ? -1 : a.path > b.path ? 1 : 0))
  return {
    total_entries: entries.length,
    by_type,
    recent_entries,
    top_routes,
    top_tags,
    total_concept_pages: concepts.length,
    routes,
    all_routes,
    route_count: all_routes.length,
  }
}
