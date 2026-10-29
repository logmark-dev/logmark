export interface Route {
  path: string
  scope: 'project' | 'domain' | 'client' | 'area' | 'reference' | 'other'
  entry_count: number
  name?: string
  description?: string
  status?: string
}

/**
 * Route-scoped breakdown for the vault tree (pass-4 frame Q / D2).
 * Additive and optional across the adapter seam: degraded adapters that
 * cannot type entries omit `by_type`; adapters that cannot enumerate all
 * routes omit the whole `routes` list (consumers fall back to top_routes).
 */
export interface RouteBreakdown {
  path: string
  count: number
  name?: string
  by_type?: Record<string, number>
}

export interface VaultSummary {
  total_entries: number
  by_type: Record<string, number>
  recent_entries: Array<{ id: string; title: string; updatedAt: string; route?: string; type?: string }>
  /**
   * Route census (release review P0-7): the raw wire field of this name is
   * a top-10-capped compatibility view, but `normalizeVaultSummary` prefers
   * the daemon's full `all_routes` census when present, so this normalized
   * array carries every route with an entry, not just the top-N. Rail
   * display capping and "all routes" affordances live downstream (shell
   * model / app-shell), not here.
   */
  top_routes: Array<{
    path: string
    count: number
    name?: string
    /**
     * The route's declared description (routes.md), when the host's
     * summary carries one (the v0 daemon sends it from routes.md); the
     * route landing shows it in place of its generic line when present.
     */
    description?: string
  }>
  top_tags: Array<{ tag: string; count: number }>
  total_concept_pages: number
  route_count?: number
  recent_activity_total?: number
  /** FULL route list incl. per-route type counts; top_routes stays top-N. */
  routes?: RouteBreakdown[]
  /**
   * Wire-shape full route census (v0 contract, umbrella
   * docs/reference/daemon-http-api-spec.md): one entry per route with at
   * least one entry, ordered by count descending then path ascending
   * (ordinal) tie-break. `normalizeVaultSummary` prefers this over
   * `top_routes` when present. `route_count` is this array's length.
   */
  all_routes?: Array<{ path: string; count: number; name: string }>
}
