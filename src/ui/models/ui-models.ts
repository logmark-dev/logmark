import type { KnowledgeSettings } from '../../types/settings.js'
import type { ConceptPageSummary } from '../../types/concept-page.js'
import type { Entry, EntrySummary } from '../../types/entry.js'
import type { RouteTreeNode } from '../shell/route-tree.js'
import type { HostInfo } from '../../types/host.js'

export interface ShellRouteItem {
  label: string
  href: string
  count?: number
  isWarning?: boolean
}

export interface ShellModel {
  adapter: string
  vaultPath: string
  totalEntries: number
  totalConcepts: number
  /** Open health-lint signal count. `null` means the lint never ran
   * (daemon-hosted 501: staleness is UNKNOWN, not zero) - surfaces render
   * an explicit n/a state instead of an affirmative clean count. */
  staleCount: number | null
  /**
   * GET /api/v0/health result (rc6) when the host has no lint scan - the
   * topbar pill shows it. null or undefined when the lint scan drives the
   * pill instead.
   */
  hostOk?: boolean | null
  /** Host identity from `/api/capabilities` (rc6); undefined for the Knowledge dev server or an older host. */
  host?: HostInfo
  /** Adapter capability flags (hasNotation, hasSessions, ...) - surfaced in
   * the status bar's diagnostics popover (frame W, D9) and the settings
   * Capabilities panel. */
  capabilities: Record<string, boolean>
  /** Entry counts per type - drives the sidebar lens counts. */
  byType: Record<string, number>
  /** Flat top-N route list - the sidebar's fallback when `routeTree` is
   * absent (adapter doesn't expose the full route breakdown), and the
   * source for the root dashboard's route-count stat. */
  routes: ShellRouteItem[]
  /**
   * Depth-capped vault tree (design pass v4 frame Q, D2), built from the
   * adapter's full `VaultSummary.routes` breakdown. Undefined when the
   * adapter only exposes `top_routes` - the sidebar falls back to the
   * flat `routes` list per frame Q's degraded note.
   */
  routeTree?: RouteTreeNode[]
  recentConcepts: ShellRouteItem[]
}

export interface SettingsStatusModel extends ShellModel {
  capabilities: Record<string, boolean>
  providers: Array<{
    id: string
    label: string
    status: 'ready' | 'offline' | 'unknown'
    detail: string
  }>
  settings: KnowledgeSettings
  activeProvider: string
}

export interface DashboardModel extends ShellModel {
  lastCaptureLabel: string
  recentEntryCount: number
  /** Typed recent entries for the Recent list (rendered via the shared entry-row primitive). */
  recentEntries: EntrySummary[]
  concepts: Array<ConceptPageSummary & { stale_count: number }>
  healthItems: Array<{
    slug: string
    title: string
    kind: string
    count: number
    since: string
  }>
}

export type RouteLandingConcept = ConceptPageSummary & { href: string; stale_count: number }

export interface RouteLandingModel extends ShellModel {
  routePath: string
  routeLabel: string
  description: string
  entryCount: number
  conceptCount: number
  staleCount: number
  lastActivityLabel: string
  /**
   * Route-scoped entry-type counts (frame S at-a-glance head, Task 7) -
   * distinct from the inherited `ShellModel.byType`, which stays
   * vault-wide (sidebar Lens counts). Drives the "types" statstrip cell
   * and the type filter-chip row; counted over the SAME `entries` pool
   * the filter chips narrow, so a chip's count always equals the number
   * of rows the click yields.
   */
  routeByType: Record<string, number>
  /** Route-scoped 7-day sparkline bucketing (oldest first, index 6 = today). */
  week: number[]
  /**
   * FULL route-scoped entry pool, newest first (frame O: the rail's n/N
   * position reads against the whole route, not a display cap). The view
   * paginates rendering client-side; no 10-row model slice.
   */
  entries: EntrySummary[]
  concepts: RouteLandingConcept[]
}

export interface HealthTriageModel {
  scannedAt: string
  staleCount: number
  conceptCount: number
  queue: Array<{
    id: string
    conceptSlug: string
    title: string
    kind: 'changed' | 'deleted' | 'parse-error'
    since: string
    sourceId?: string
    actionLabel: string
  }>
}

export interface SourceFreshness {
  id: string
  status: 'fresh' | 'changed' | 'deleted' | 'parse-error'
  changedAt?: string
}

export interface EntryDetailModel {
  entry: Entry
  formattedDate: string
  backlinks: NonNullable<Entry['backlinks']>
  /** Frame P (D1): prev/next-in-context - route order when routed, else the recent-timeline window. */
  context: { ids: string[]; label: string; index: number }
}
