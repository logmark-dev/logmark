import type { ConceptPageSource } from '../../types/concept-page.js'
import type { EntrySummary } from '../../types/entry.js'
import type { LintScanResult, StaleItem } from '../../types/lint.js'
import { isHostInfo, type CapabilitiesResponse } from '../../types/host.js'
import { api, ApiError } from '../api.js'
import { hostFeatures } from '../lib/host.js'
import { buildRouteTree } from '../shell/route-tree.js'
import type { DashboardModel, EntryDetailModel, HealthTriageModel, RouteLandingModel, SettingsStatusModel, ShellModel, SourceFreshness } from './ui-models.js'
import { normalizeConceptSummary, normalizeEntry, normalizeRoutePath, normalizeVaultSummary, toDateLabel } from './normalizers.js'
import { bucketWeek } from '../views/shared/at-a-glance.js'

type HealthDisplayItem = DashboardModel['healthItems'][number]
type BaseData = Awaited<ReturnType<typeof loadBaseData>>
type HealthTriageKind = HealthTriageModel['queue'][number]['kind']
type RecentEntryInput = {
  id: string
  title: string
  updatedAt: string
  createdAt?: string
  route?: string
  type?: string
}

const statusByKind: Record<StaleItem['kind'], SourceFreshness['status']> = {
  'source-changed': 'changed',
  'source-deleted': 'deleted',
  'parse-error': 'parse-error',
}

const displayKindByLintKind: Record<StaleItem['kind'], HealthTriageKind> = {
  'source-changed': 'changed',
  'source-deleted': 'deleted',
  'parse-error': 'parse-error',
}

export function mapSourceFreshness(
  slug: string,
  sources: ConceptPageSource[],
  health: LintScanResult,
): SourceFreshness[] {
  return sources.map((source) => {
    const item = health.items.find((candidate) => candidate.concept_slug === slug && candidate.source?.id === source.id)
    if (!item) {
      return { id: source.id, status: 'fresh' }
    }

    return {
      id: source.id,
      status: statusByKind[item.kind],
      changedAt: toDateLabel(item.changed_at || health.scanned_at),
    }
  })
}

export async function loadShellModel(): Promise<ShellModel> {
  return buildShellModel(await loadBaseData())
}

export async function loadDashboardModel(): Promise<DashboardModel> {
  const base = await loadBaseData()
  const shell = buildShellModel(base)
  const healthItems = base.health?.items ?? []
  const staleCounts = countHealthItemsBySlug(healthItems)

  return {
    ...shell,
    lastCaptureLabel: toDateLabel(mostRecentUpdatedAt(base.summary.recent_entries)),
    recentEntryCount: base.summary.recent_entries.length || base.summary.recent_activity_total || 0,
    recentEntries: base.recentEntries.map(toRecentEntrySummary),
    concepts: base.concepts.map((concept) => ({
      ...concept,
      stale_count: staleCounts.get(concept.slug) ?? concept.stale_count ?? 0,
    })),
    healthItems: healthItems.slice(0, 8).map(toHealthDisplayItem),
  }
}

export async function loadRouteLandingModel(route: string): Promise<RouteLandingModel> {
  const base = await loadBaseData()
  const shell = buildShellModel(base)
  const routePath = normalizeRoutePath(route)
  const routeSummary = base.summary.top_routes.find((candidate) => normalizeRoutePath(candidate.path) === routePath)
  const routeEntriesResponse = await loadRouteEntries(routeSummary?.path ?? routePath, routePath)
  // The + route-operator is notation, not display copy (pass-2 R4).
  const routeLabel = (routeSummary?.name ?? routePath).replace(/^\+/, '')
  const staleCounts = countHealthItemsBySlug(base.health?.items ?? [])
  const routeEntries = routeEntriesResponse.entries
    .filter((entry) => normalizeRoutePath(entry.route ?? '') === routePath)
    .sort((a, b) => entryTime(b) - entryTime(a))
  // ONE route-scoped entry pool feeds everything downstream - the head's
  // "types" cell, the filter-chip counts (frame S/O, Task 7), the
  // sparkline, and the filterable list itself - so a chip's advertised
  // count always equals the rows its click yields. The view paginates
  // rendering client-side (timeline's incremental pattern); no model slice.
  const routeSummaries = routeEntries.map(toRecentEntrySummary)
  const concepts = base.concepts
    .filter((concept) => (concept.tags ?? []).some((tag) => normalizeRoutePath(tag) === routePath))
    .map((concept) => ({
      ...concept,
      href: `/concepts/${encodeURIComponent(concept.slug)}`,
      stale_count: staleCounts.get(concept.slug) ?? concept.stale_count ?? 0,
    }))

  return {
    ...shell,
    routePath,
    routeLabel,
    // The route's own declared description when the host's summary carries
    // one (lmk-jsnz item 2); otherwise the generic line, which names concept
    // pages only where a concept producer writes them (lmk-hqgm).
    description: routeSummary?.description
      ?? (hostFeatures(shell.capabilities).conceptProducer
        ? `${routeLabel} collects entries and concept pages tagged for ${routePath}.`
        : `${routeLabel} collects entries tagged for ${routePath}.`),
    entryCount: routeSummary?.count ?? routeEntries.length,
    conceptCount: concepts.length,
    staleCount: concepts.reduce((total, concept) => total + concept.stale_count, 0),
    lastActivityLabel: toDateLabel(mostRecentUpdatedAt(routeEntries)),
    routeByType: countByType(routeSummaries),
    week: bucketWeek(routeSummaries, new Date()),
    entries: routeSummaries,
    concepts,
  }
}

/** Entry-type tally over an already-typed entry list (frame S "types" cell + filter chips). */
function countByType(entries: EntrySummary[]): Record<string, number> {
  const counts: Record<string, number> = {}
  for (const entry of entries) {
    const type = entry.type ?? 'log'
    counts[type] = (counts[type] ?? 0) + 1
  }
  return counts
}

const PROVIDER_LABELS: Record<string, string> = {
  'claude-cli': 'Claude CLI',
  ollama: 'Ollama local',
  'lm-studio': 'LM Studio',
}

export async function loadSettingsStatusModel(): Promise<SettingsStatusModel> {
  const [base, settingsRes] = await Promise.all([loadBaseData(), api.settings()])
  const active = settingsRes.inference.active
  return {
    ...buildShellModel(base),
    capabilities: base.caps.capabilities,
    settings: settingsRes.settings,
    activeProvider: active,
    providers: settingsRes.settings.providerOrder.map((id) => ({
      id,
      label: PROVIDER_LABELS[id] ?? id,
      status: id === active ? ('ready' as const) : ('unknown' as const),
      detail: id === active ? 'Active provider.' : 'Not active; probed when the order is applied.',
    })),
  }
}

async function loadRouteEntries(routeToken: string, routePath: string): Promise<{ entries: RecentEntryInput[] }> {
  const primary = await api.listEntries(`route=${encodeURIComponent(routeToken)}&limit=2000`)
  if (primary.entries.some((entry) => normalizeRoutePath(entry.route ?? '') === routePath)) {
    return primary
  }

  const fallbackToken = `+${routePath}`
  if (routeToken === fallbackToken) {
    return primary
  }

  const fallback = await api.listEntries(`route=${encodeURIComponent(fallbackToken)}&limit=2000`)
  return fallback.entries.length > 0 ? fallback : primary
}

export async function loadHealthTriageModel(): Promise<HealthTriageModel> {
  const { result } = await api.health()

  return {
    scannedAt: result.scanned_at,
    staleCount: result.stale_count,
    conceptCount: result.concept_count,
    queue: result.items.map((item, index) => ({
      id: healthItemId(item, index),
      conceptSlug: item.concept_slug,
      title: titleFromSlug(item.concept_slug),
      kind: lintKindLabel(item.kind),
      since: toDateLabel(item.changed_at ?? result.scanned_at),
      sourceId: item.source?.id,
      actionLabel: item.kind === 'source-changed' ? 'Re-distill' : 'Open',
    })),
  }
}

export async function loadEntryDetailModel(id: string): Promise<EntryDetailModel> {
  const { entry: rawEntry, error } = await api.readEntry(id)
  if (!rawEntry || error) {
    throw new Error('Entry not found: ' + id)
  }

  const entry = normalizeEntry(rawEntry)
  const context = await loadEntryContext(entry)
  return {
    entry,
    formattedDate: toDateLabel(entry.updatedAt || entry.createdAt),
    backlinks: entry.backlinks ?? [],
    context,
  }
}

const ENTRY_CONTEXT_FETCH_LIMIT = 300

/**
 * Frame P (D1): the entry's prev/next-in-context, in capture order. Routed
 * entries walk their route's full entry list (createdAt desc); unrouted
 * entries fall back to the recent-timeline window (same fetch pattern/limit
 * as `renderTimeline`). Direct URL loads resolve this from the entry alone -
 * no SPA navigation state required. `index` is -1 when the entry falls
 * outside the fallback window (degrades to no counter/prev-next, not a
 * crash - the window is a bounded convenience, not a promise).
 */
export async function loadEntryContext(entry: EntrySummary): Promise<{ ids: string[]; label: string; index: number }> {
  const routePath = entry.route ? normalizeRoutePath(entry.route) : ''
  if (routePath) {
    const routed = await loadRouteEntries(entry.route!, routePath)
    const ids = routed.entries
      .filter((candidate) => normalizeRoutePath(candidate.route ?? '') === routePath)
      .sort((a, b) => entryTime(b) - entryTime(a))
      .map((candidate) => candidate.id)
    return { ids, label: routePath, index: ids.indexOf(entry.id) }
  }

  const recent = await api.listEntries(`limit=${ENTRY_CONTEXT_FETCH_LIMIT}`)
  const ids = [...recent.entries]
    .sort((a, b) => entryTime(b) - entryTime(a))
    .map((candidate) => candidate.id)
  return { ids, label: 'vault', index: ids.indexOf(entry.id) }
}

function countHealthItemsBySlug(items: StaleItem[]): Map<string, number> {
  const counts = new Map<string, number>()
  for (const item of items) {
    counts.set(item.concept_slug, (counts.get(item.concept_slug) ?? 0) + 1)
  }
  return counts
}

/**
 * The health lint scan (GET /api/health) runs only on hosts that set
 * `hasLintScan` - the Node cockpit server. Without the flag this never
 * fetches: the daemon answers 501 there, and the cockpit used to request
 * it on every navigation (rc5 QA: 35+ console 501s, permanent
 * "health n/a"). null means staleness is UNKNOWN, never an empty-zero
 * scan. A 501 from a host that claimed the flag still degrades to null;
 * every other failure propagates. Pass `caps` when the caller already has
 * the record so it is fetched once.
 */
export async function healthOrUnavailable(caps?: CapabilitiesResponse): Promise<LintScanResult | null> {
  const record = caps ?? (await api.capabilities())
  if (!hostFeatures(record?.capabilities).lintScan) return null
  try {
    return (await api.health()).result
  } catch (err) {
    if (err instanceof ApiError && err.status === 501) {
      return null
    }
    throw err
  }
}

/**
 * Liveness of the host serving the vault: GET /api/v0/health, the daemon's
 * real health route (rc6, design 4.2). False on any failure, so a host
 * mid-restart reads "health n/a" instead of breaking the shell.
 */
export async function hostHealthOk(): Promise<boolean> {
  try {
    return (await api.hostHealth()).status === 'ok'
  } catch {
    return false
  }
}

async function loadBaseData() {
  const caps = await api.capabilities()
  const lintScan = hostFeatures(caps?.capabilities).lintScan
  const [vault, recentEntries, concepts, health, hostOk] = await Promise.all([
    api.vaultSummary(),
    api.listEntries('limit=6'),
    api.listConcepts(),
    healthOrUnavailable(caps),
    // With a lint scan the stale count drives the pill; otherwise the
    // host's own health route does.
    lintScan ? Promise.resolve(null) : hostHealthOk(),
  ])
  const summary = normalizeVaultSummary(vault.summary)

  return {
    caps,
    summary,
    recentEntries: summary.recent_entries.length > 0 ? summary.recent_entries : recentEntries.entries,
    concepts: concepts.pages.map(normalizeConceptSummary),
    health,
    hostOk,
  }
}

/** Coerce a recent-entry input (summary shape or full EntrySummary) into EntrySummary. */
function toRecentEntrySummary(input: RecentEntryInput & { tags?: string[] }): EntrySummary {
  return {
    id: input.id,
    title: input.title,
    type: input.type as EntrySummary['type'],
    route: input.route,
    tags: input.tags ?? [],
    createdAt: input.createdAt ?? input.updatedAt,
    updatedAt: input.updatedAt,
  }
}

function buildShellModel(base: BaseData): ShellModel {
  return {
    adapter: base.caps.adapter,
    vaultPath: base.caps.vaultPath ?? '',
    totalEntries: base.summary.total_entries,
    totalConcepts: base.concepts.length || base.summary.total_concept_pages,
    // null = the health lint never ran here (daemon-hosted 501) - the
    // shell renders "health n/a", never an affirmative "0 stale".
    staleCount: base.health ? base.health.items.length : null,
    hostOk: base.hostOk,
    ...(isHostInfo(base.caps.host) ? { host: base.caps.host } : {}),
    capabilities: base.caps.capabilities ?? {},
    byType: base.summary.by_type ?? {},
    // Flat fallback (frame Q degraded note): stays the sidebar's source
    // when the adapter doesn't expose the full `routes` breakdown. Carries
    // the FULL route census (normalizeVaultSummary prefers `all_routes`
    // when present) - app-shell.ts caps the rail's visible rows and links
    // out to /routes for the rest (P0-7 route census fix).
    routes: base.summary.top_routes.map((route) => ({
      label: route.name ?? route.path,
      href: `/routes/${encodeURIComponent(normalizeRoutePath(route.path))}`,
      count: route.count,
    })),
    ...(base.summary.routes && base.summary.routes.length > 0
      ? { routeTree: buildRouteTree(base.summary.routes) }
      : {}),
    recentConcepts: base.concepts.slice(0, 5).map((concept) => ({
      label: concept.title,
      href: `/concepts/${encodeURIComponent(concept.slug)}`,
      count: concept.source_count,
      isWarning: (concept.stale_count ?? 0) > 0,
    })),
  }
}

function healthItemId(item: StaleItem, index = 0): string {
  const discriminator = item.source?.id ?? `${index}:${item.error ?? item.changed_at ?? 'unscoped'}`
  return [item.concept_slug, item.kind, discriminator].filter(Boolean).join(':')
}

function lintKindLabel(kind: StaleItem['kind']): HealthTriageKind {
  return displayKindByLintKind[kind]
}

function titleFromSlug(slug: string): string {
  return slug.split('-').map((part) => part[0]?.toUpperCase() + part.slice(1)).join(' ')
}

function entryTime(entry: RecentEntryInput): number {
  const time = Date.parse(entry.updatedAt || entry.createdAt || '')
  return Number.isNaN(time) ? 0 : time
}

function mostRecentUpdatedAt(entries: RecentEntryInput[]): string {
  let latest = ''
  let latestTime = Number.NEGATIVE_INFINITY
  for (const entry of entries) {
    const value = entry.updatedAt || entry.createdAt || ''
    const time = Date.parse(value)
    if (!Number.isNaN(time) && time > latestTime) {
      latest = value
      latestTime = time
    }
  }
  return latest || entries[0]?.updatedAt || entries[0]?.createdAt || ''
}

function toHealthDisplayItem(item: StaleItem): HealthDisplayItem {
  return {
    slug: item.concept_slug,
    title: `${item.concept_slug} needs ${lintKindLabel(item.kind)} review`,
    kind: lintKindLabel(item.kind),
    count: 1,
    since: item.changed_at ?? '',
  }
}
