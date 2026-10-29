import type { LogMarkMcpClient } from './client.js'
import type { Entry, EntrySummary, EntryFilter, SearchOpts } from '../../types/entry.js'
import type { VaultSummary, Route } from '../../types/vault.js'
import type { FeedItem, Decision } from '../../types/feed.js'

type EntryListResult = {
  entries?: unknown[]
  Entries?: unknown[]
}

type EntryType = NonNullable<EntrySummary['type']>

const entryTypes = new Set<EntryType>(['task', 'idea', 'block', 'decision', 'log', 'timetrack', 'question', 'gap', 'analogy'])

export async function mcpListEntries(
  client: LogMarkMcpClient,
  filter?: EntryFilter
): Promise<EntrySummary[]> {
  const args: Record<string, unknown> = {}
  if (filter?.type) args.type = filter.type
  if (filter?.route) args.route = filter.route
  if (filter?.limit) args.limit = filter.limit
  const result = await client.callTool<EntryListResult>('list_recent', args)
  return entryList(result).map(normalizeEntrySummary)
}

export async function mcpReadEntry(
  client: LogMarkMcpClient,
  id: string
): Promise<Entry | null> {
  try {
    const result = await client.callTool<Entry>('read_entry', { id })
    return result ?? null
  } catch {
    return null
  }
}

export async function mcpSearchEntries(
  client: LogMarkMcpClient,
  query: string,
  opts?: SearchOpts
): Promise<EntrySummary[]> {
  const result = await client.callTool<EntryListResult>('search_entries', {
    query,
    semantic: opts?.semantic ?? false,
    limit: opts?.limit ?? 50,
  })
  return entryList(result).map(normalizeEntrySummary)
}

export async function mcpVaultSummary(client: LogMarkMcpClient): Promise<VaultSummary> {
  return client.callTool<VaultSummary>('vault_summary', {})
}

export async function mcpGetRoutes(client: LogMarkMcpClient): Promise<Route[]> {
  const result = await client.callTool<{ routes?: Route[] }>('get_routes', {})
  return result.routes ?? []
}

export async function mcpGetFeedItems(client: LogMarkMcpClient): Promise<FeedItem[]> {
  const result = await client.callTool<{ items?: FeedItem[] }>('get_feed_items', {})
  return result.items ?? []
}

export async function mcpGetDecisions(
  client: LogMarkMcpClient,
  filter?: { scope?: string; status?: string }
): Promise<Decision[]> {
  const args: Record<string, unknown> = {}
  if (filter?.scope) args.scope = filter.scope
  if (filter?.status) args.status = filter.status
  const result = await client.callTool<{ decisions?: Decision[] }>('get_decisions', args)
  return result.decisions ?? []
}

export async function mcpExecuteBehavior(
  client: LogMarkMcpClient,
  behaviorId: string,
  input: unknown
): Promise<unknown> {
  return client.callTool('execute_behavior', { behavior_id: behaviorId, input })
}

function entryList(result: EntryListResult): unknown[] {
  return result.entries ?? result.Entries ?? []
}

function normalizeEntrySummary(input: unknown): EntrySummary {
  const source = record(input)
  const body = stringValue(source.body ?? source.Body ?? source.content ?? source.Content ?? source.contentPreview ?? source.ContentPreview)
  const id = stringValue(source.id ?? source.entry_id ?? source.EntryId ?? source.shortId ?? source.ShortId)
  const timestamp = stringValue(source.updatedAt ?? source.updated_at ?? source.UpdatedAt ?? source.createdAt ?? source.created_at ?? source.CreatedAt ?? source.Timestamp)

  return {
    id,
    title: stringValue(source.title ?? source.Title, titleFromBody(body, id || 'Untitled')),
    type: typeValue(source.type ?? source.entry_type ?? source.EntryType),
    ...(optionalString(source.route ?? source.Route) ? { route: optionalString(source.route ?? source.Route) } : {}),
    tags: stringArray(source.tags ?? source.Tags),
    createdAt: stringValue(source.createdAt ?? source.created_at ?? source.CreatedAt ?? source.Timestamp, timestamp),
    updatedAt: stringValue(source.updatedAt ?? source.updated_at ?? source.UpdatedAt ?? source.Timestamp, timestamp),
  }
}

function record(value: unknown): Record<string, unknown> {
  return value !== null && typeof value === 'object' && !Array.isArray(value) ? value as Record<string, unknown> : {}
}

function stringValue(value: unknown, fallback = ''): string {
  return typeof value === 'string' ? value : fallback
}

function optionalString(value: unknown): string | undefined {
  return typeof value === 'string' ? value : undefined
}

function stringArray(value: unknown): string[] {
  return Array.isArray(value) ? value.filter((item): item is string => typeof item === 'string') : []
}

function titleFromBody(body: string, fallback: string): string {
  const heading = body.split(/\r?\n/).find((line) => line.startsWith('# '))
  return heading ? heading.replace(/^#\s+/, '').trim() || fallback : fallback
}

function typeValue(value: unknown): EntryType {
  const normalized = stringValue(value).toLowerCase()
  return entryTypes.has(normalized as EntryType) ? normalized as EntryType : 'log'
}
