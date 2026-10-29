import type { ConceptPageSummary } from '../../types/concept-page.js'
import type { Entry, EntrySummary } from '../../types/entry.js'
import type { Route, VaultSummary } from '../../types/vault.js'
import { deriveDisplayTitle } from '../lib/notation.js'
import { localDay } from '../lib/dates.js'

type EntryType = NonNullable<EntrySummary['type']>
type ConceptStatus = ConceptPageSummary['status']

const entryTypes = new Set<EntryType>(['task', 'idea', 'block', 'decision', 'log', 'timetrack', 'question', 'gap', 'analogy'])
const conceptStatuses = new Set<ConceptStatus>(['draft', 'mature', 'archived'])
const routeScopes = new Set<Route['scope']>(['project', 'domain', 'client', 'area', 'reference', 'other'])

export function record(value: unknown): Record<string, unknown> {
  return value !== null && typeof value === 'object' && !Array.isArray(value) ? value as Record<string, unknown> : {}
}

export function stringValue(value: unknown, fallback = ''): string {
  return typeof value === 'string' ? value : fallback
}

export function numberValue(value: unknown, fallback = 0): number {
  return typeof value === 'number' && Number.isFinite(value) ? value : fallback
}

export function optionalString(value: unknown): string | undefined {
  return typeof value === 'string' ? value : undefined
}

export function optionalNumber(value: unknown): number | undefined {
  return typeof value === 'number' && Number.isFinite(value) ? value : undefined
}

export function optionalBoolean(value: unknown): boolean | undefined {
  return typeof value === 'boolean' ? value : undefined
}

export function stringArray(value: unknown): string[] {
  return Array.isArray(value) ? value.filter((item): item is string => typeof item === 'string') : []
}

export function titleFromBody(body: string, fallback: string): string {
  const heading = body.split(/\r?\n/).find((line) => line.startsWith('# '))
  return heading ? heading.replace(/^#\s+/, '').trim() || fallback : fallback
}

export function typeValue(value: unknown): EntryType {
  const normalized = stringValue(value).toLowerCase()
  return entryTypes.has(normalized as EntryType) ? normalized as EntryType : 'log'
}

export function toDateLabel(value: string): string {
  if (!value) {
    return ''
  }

  return localDay(value)
}

export function normalizeRoutePath(route: string): string {
  return route.trim().replace(/^\+/, '').replace(/^\/+/, '')
}

export function normalizeRoute(input: unknown): Route {
  const source = record(input)
  const path = stringValue(source.path ?? source.Path ?? source.name ?? source.Name)
  const name = optionalString(source.name ?? source.Name)
  const rawScope = stringValue(source.scope ?? source.Scope ?? source.type ?? source.Type).toLowerCase()
  const scope = routeScopes.has(rawScope as Route['scope']) ? rawScope as Route['scope'] : 'other'

  return {
    path,
    ...(name ? { name } : {}),
    ...(optionalString(source.description ?? source.Description) ? { description: optionalString(source.description ?? source.Description) } : {}),
    ...(optionalString(source.status ?? source.Status) ? { status: optionalString(source.status ?? source.Status) } : {}),
    scope,
    entry_count: numberValue(source.entry_count ?? source.entryCount ?? source.EntryCount ?? source.count ?? source.Count),
  }
}

export function normalizeVaultSummary(input: unknown): VaultSummary {
  const source = record(input)
  const byTypeInput = record(source.by_type ?? source.byType ?? source.CountsByType)
  const by_type = Object.fromEntries(
    Object.entries(byTypeInput).map(([key, value]) => [key.toLowerCase(), numberValue(value)]),
  )
  // Prefer the full route census (`all_routes`) when the adapter exposes
  // it (daemon P0-7 fix): `top_routes` on the wire stays a top-10
  // compatibility cap, but the cockpit's normalized `top_routes` is the
  // full list whenever it's available, so nothing downstream (rail,
  // route landing, link graph, the /routes index) is silently truncated.
  const routesInput = source.all_routes ?? source.allRoutes ?? source.AllRoutes
    ?? source.top_routes ?? source.topRoutes ?? source.Routes
  const top_routes = (Array.isArray(routesInput) ? routesInput : []).map((item) => {
    const route = normalizeRoute(item)
    return {
      path: route.path,
      count: route.entry_count,
      ...(route.name ? { name: route.name } : {}),
      ...(route.description ? { description: route.description } : {}),
    }
  })
  const topTagsInput = source.top_tags ?? source.topTags ?? source.TopTags
  const top_tags = (Array.isArray(topTagsInput) ? topTagsInput : []).map((item) => {
    const tag = record(item)
    return {
      tag: stringValue(tag.tag ?? tag.Name),
      count: numberValue(tag.count ?? tag.Count),
    }
  })
  const recentInput = source.recent_entries ?? source.recentEntries ?? source.RecentEntries
  const recent_entries = (Array.isArray(recentInput) ? recentInput : []).map((item) => {
    const entry = record(item)
    return {
      id: stringValue(entry.id ?? entry.EntryId),
      title: stringValue(entry.title ?? entry.Title),
      updatedAt: stringValue(entry.updatedAt ?? entry.UpdatedAt ?? entry.Timestamp),
      ...(optionalString(entry.route ?? entry.Route) ? { route: optionalString(entry.route ?? entry.Route) } : {}),
      ...(optionalString(entry.type ?? entry.EntryType) ? { type: typeValue(entry.type ?? entry.EntryType) } : {}),
    }
  })
  const recentActivity = record(source.recent_activity ?? source.recentActivity ?? source.RecentActivity)

  // Full route list with optional per-route type counts (pass-4 frame Q).
  // Deliberately reads only lowercase `routes` - PascalCase `Routes` is the
  // desktop's top-N list and already claimed by the top_routes normalizer.
  const fullRoutesInput = source.routes
  const routes = (Array.isArray(fullRoutesInput) ? fullRoutesInput : []).map((item) => {
    const route = normalizeRoute(item)
    const byTypeRaw = record(record(item).by_type ?? record(item).byType)
    const routeByType = Object.fromEntries(
      Object.entries(byTypeRaw).map(([key, value]) => [key.toLowerCase(), numberValue(value)]),
    )
    return {
      path: route.path,
      count: route.entry_count,
      ...(route.name ? { name: route.name } : {}),
      ...(Object.keys(routeByType).length > 0 ? { by_type: routeByType } : {}),
    }
  })

  return {
    total_entries: numberValue(source.total_entries ?? source.totalEntries ?? source.TotalEntries),
    by_type,
    recent_entries,
    top_routes,
    top_tags,
    total_concept_pages: numberValue(source.total_concept_pages ?? source.totalConceptPages ?? source.TotalConceptPages),
    route_count: optionalNumber(source.route_count ?? source.routeCount ?? source.RouteCount) ?? top_routes.length,
    ...(routes.length > 0 ? { routes } : {}),
    ...(optionalNumber(source.recent_activity_total ?? source.recentActivityTotal ?? recentActivity.TotalEntries) !== undefined
      ? { recent_activity_total: optionalNumber(source.recent_activity_total ?? source.recentActivityTotal ?? recentActivity.TotalEntries) }
      : {}),
  }
}

export function normalizeEntry(input: unknown): Entry {
  const root = record(input)
  const source = record(root.entry ?? root.Entry ?? input)
  const body = stringValue(source.body ?? source.Body ?? source.Content)
  const id = stringValue(source.id ?? source.entry_id ?? source.EntryId)
  const fallbackTitle = id || 'Untitled'
  // Display titles never carry raw notation (design pass Q3): wikilinks
  // collapse to labels; tags, anchors, and provenance clauses lift out.
  const title = deriveDisplayTitle(
    stringValue(source.title ?? source.Title, titleFromBody(body, fallbackTitle)),
    fallbackTitle,
  )
  const timestamp = stringValue(source.updatedAt ?? source.updated_at ?? source.createdAt ?? source.created_at ?? source.Timestamp)
  const backlinksInput = source.backlinks ?? source.Backlinks
  const linksInput = source.links ?? source.Links

  return {
    id,
    title,
    type: typeValue(source.type ?? source.entry_type ?? source.EntryType),
    ...(optionalString(source.route ?? source.Route) ? { route: optionalString(source.route ?? source.Route) } : {}),
    tags: stringArray(source.tags ?? source.Tags),
    createdAt: stringValue(source.createdAt ?? source.created_at ?? source.CreatedAt ?? source.Timestamp, timestamp),
    updatedAt: stringValue(source.updatedAt ?? source.updated_at ?? source.UpdatedAt ?? source.Timestamp, timestamp),
    body,
    frontmatter: record(source.frontmatter ?? source.Frontmatter),
    ...(optionalString(source.path ?? source.Path) ? { path: optionalString(source.path ?? source.Path) } : {}),
    ...(optionalString(source.scope ?? source.Scope) ? { scope: optionalString(source.scope ?? source.Scope) } : {}),
    ...(optionalString(source.source ?? source.Source) ? { source: optionalString(source.source ?? source.Source) } : {}),
    ...(optionalString(source.session ?? source.Session) ? { session: optionalString(source.session ?? source.Session) } : {}),
    ...(Array.isArray(backlinksInput) ? { backlinks: backlinksInput.map(normalizeBacklink) } : {}),
    ...(Array.isArray(linksInput) ? { links: linksInput.map(normalizeLink) } : {}),
  }
}

export function normalizeConceptSummary(input: unknown): ConceptPageSummary {
  const source = record(input)
  const status = stringValue(source.status ?? source.Status).toLowerCase()

  return {
    slug: stringValue(source.slug ?? source.Slug),
    title: stringValue(source.title ?? source.Title),
    status: conceptStatuses.has(status as ConceptStatus) ? status as ConceptStatus : 'draft',
    tags: stringArray(source.tags ?? source.Tags),
    last_distilled: stringValue(source.last_distilled ?? source.lastDistilled ?? source.LastDistilled),
    source_count: numberValue(source.source_count ?? source.sourceCount ?? source.SourceCount),
    ...(stringArray(source.related_concepts ?? source.relatedConcepts ?? source.RelatedConcepts).length > 0
      ? { related_concepts: stringArray(source.related_concepts ?? source.relatedConcepts ?? source.RelatedConcepts) }
      : {}),
    ...(optionalNumber(source.distillation_version ?? source.distillationVersion ?? source.DistillationVersion) !== undefined
      ? { distillation_version: optionalNumber(source.distillation_version ?? source.distillationVersion ?? source.DistillationVersion) }
      : {}),
    ...(optionalNumber(source.density ?? source.Density) !== undefined ? { density: optionalNumber(source.density ?? source.Density) } : {}),
    ...(optionalBoolean(source.draft ?? source.Draft) !== undefined ? { draft: optionalBoolean(source.draft ?? source.Draft) } : {}),
    ...(optionalNumber(source.stale_count ?? source.staleCount ?? source.StaleCount) !== undefined
      ? { stale_count: optionalNumber(source.stale_count ?? source.staleCount ?? source.StaleCount) }
      : {}),
    ...(optionalNumber(source.related_count ?? source.relatedCount ?? source.RelatedCount) !== undefined
      ? { related_count: optionalNumber(source.related_count ?? source.relatedCount ?? source.RelatedCount) }
      : {}),
    ...(optionalNumber(source.backlink_count ?? source.backlinkCount ?? source.BacklinkCount) !== undefined
      ? { backlink_count: optionalNumber(source.backlink_count ?? source.backlinkCount ?? source.BacklinkCount) }
      : {}),
  }
}

function normalizeBacklink(input: unknown): NonNullable<Entry['backlinks']>[number] {
  const source = record(input)
  const preview = optionalString(source.preview ?? source.contentPreview ?? source.content_preview ?? source.ContentPreview)

  return {
    ...(optionalString(source.id ?? source.entry_id ?? source.EntryId) ? { id: optionalString(source.id ?? source.entry_id ?? source.EntryId) } : {}),
    ...(optionalString(source.title ?? source.Title ?? preview) ? { title: optionalString(source.title ?? source.Title ?? preview) } : {}),
    ...(optionalString(source.kind ?? source.entry_type ?? source.EntryType) ? { kind: typeValue(source.kind ?? source.entry_type ?? source.EntryType) } : {}),
    ...(preview ? { preview } : {}),
    ...(optionalString(source.sourcePath ?? source.source_path ?? source.SourcePath) ? { sourcePath: optionalString(source.sourcePath ?? source.source_path ?? source.SourcePath) } : {}),
  }
}

function normalizeLink(input: unknown): NonNullable<Entry['links']>[number] {
  const source = record(input)
  return {
    label: stringValue(source.label ?? source.Label),
    ...(optionalString(source.linkType ?? source.link_type ?? source.LinkType) ? { linkType: optionalString(source.linkType ?? source.link_type ?? source.LinkType) } : {}),
    ...(optionalString(source.targetShortId ?? source.target_short_id ?? source.TargetShortId) ? { targetShortId: optionalString(source.targetShortId ?? source.target_short_id ?? source.TargetShortId) } : {}),
    ...(optionalString(source.targetPath ?? source.target_path ?? source.TargetPath) ? { targetPath: optionalString(source.targetPath ?? source.target_path ?? source.TargetPath) } : {}),
    ...(optionalBoolean(source.isResolved ?? source.is_resolved ?? source.IsResolved) !== undefined ? { isResolved: optionalBoolean(source.isResolved ?? source.is_resolved ?? source.IsResolved) } : {}),
  }
}
