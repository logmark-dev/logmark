import { readFile, readdir, stat } from 'node:fs/promises'
import { extname, join, relative, sep } from 'node:path'
import { parseFrontmatter } from '../../lib/frontmatter.js'
import type { Entry, EntrySummary } from '../../types/entry.js'
import { resolveVaultContentRoot } from './vault-root.js'

// `_concepts` is the canonical concept-page dir (ConceptPageStore); `concepts`
// is the legacy fallback (both dirs are content, not entries).
const EXCLUDED_DIRS = new Set(['concepts', '_concepts', '.git', 'node_modules', '_system'])
const EXCLUDED_FILES = new Set(['constitution.md', 'vault.md', 'README.md'])

async function walkMarkdown(root: string, current: string): Promise<string[]> {
  const out: string[] = []
  const entries = await readdir(current, { withFileTypes: true })
  for (const e of entries) {
    if (e.isDirectory()) {
      if (EXCLUDED_DIRS.has(e.name)) continue
      out.push(...(await walkMarkdown(root, join(current, e.name))))
    } else if (e.isFile() && extname(e.name) === '.md') {
      const rel = relative(root, join(current, e.name)).split(sep).join('/')
      if (EXCLUDED_FILES.has(rel)) continue
      out.push(rel)
    }
  }
  return out
}

function idFromPath(relPath: string): string {
  // Path-qualified id: basenames like `2026.md` recur across every route
  // directory, so a basename id collapses distinct files onto one entry
  // (the audit's /entries/2026 defect). The full relative path (minus
  // extension) is unique and readable.
  return relPath.replace(/\.md$/, '')
}

function inferType(relPath: string): EntrySummary['type'] {
  const marker = relPath.split('/').find((part) => part.startsWith('_')) ?? relPath.split('/')[0]
  switch (marker) {
    case '_tasks': return 'task'
    case '_ideas': return 'idea'
    case '_blocks': return 'block'
    case '_decisions': return 'decision'
    case '_questions': return 'question'
    case '_gaps': return 'gap'
    case '_analogies': return 'analogy'
    default: return 'log'
  }
}

function inferRoute(relPath: string): string | undefined {
  const parts = relPath.split('/')
  if (parts[0] === 'projects' && parts.length >= 2) return `projects/${parts[1]}`
  if (parts[0] === 'domains' && parts.length >= 2) return `domains/${parts[1]}`
  return undefined
}

function extractTitle(body: string, fallback: string): string {
  const match = body.match(/^#\s+(.+)$/m)
  return match ? match[1].trim() : fallback
}

// gray-matter parses unquoted YAML datetime values as JS Date objects,
// not strings. Normalize to ISO 8601 strings at the boundary.
function toIsoString(value: unknown, fallback: string): string {
  if (value instanceof Date) return value.toISOString()
  if (typeof value === 'string' && value.length > 0) return value
  return fallback
}

export async function listEntriesFs(vault: string): Promise<EntrySummary[]> {
  const root = await resolveVaultContentRoot(vault)
  const paths = await walkMarkdown(root, root)
  const out: EntrySummary[] = []
  for (const rel of paths) {
    const full = join(root, rel)
    const id = idFromPath(rel)
    try {
      const src = await readFile(full, 'utf8')
      const { data, body } = parseFrontmatter(src)
      const stats = await stat(full)
      out.push({
        id,
        title: extractTitle(body, id),
        type: ((data.type as EntrySummary['type']) ?? inferType(rel)),
        route: (data.route as string | undefined) ?? inferRoute(rel),
        tags: Array.isArray(data.tags) ? (data.tags as string[]) : [],
        createdAt: toIsoString(data.created, stats.birthtime.toISOString()),
        updatedAt: toIsoString(data.updated, stats.mtime.toISOString()),
      })
    } catch (err) {
      console.warn(`[vault] skipping ${rel}: ${(err as Error).message}`)
    }
  }
  return out
}

export async function readEntryFs(vault: string, id: string): Promise<Entry | null> {
  const root = await resolveVaultContentRoot(vault)
  const paths = await walkMarkdown(root, root)
  let match = paths.find((p) => idFromPath(p) === id)
  if (!match) {
    // Legacy-basename fallback: pre-path-qualified links carried only the
    // file stem. Honor them when unambiguous; ambiguous stems stay null
    // rather than guessing.
    const stemMatches = paths.filter((p) => idFromPath(p).split('/').pop() === id)
    if (stemMatches.length === 1) match = stemMatches[0]
  }
  if (!match) return null
  const full = join(root, match)
  try {
    const src = await readFile(full, 'utf8')
    const { data, body } = parseFrontmatter(src)
    const stats = await stat(full)
    const canonicalId = idFromPath(match)
    return {
      id: canonicalId,
      title: extractTitle(body, canonicalId),
      type: ((data.type as Entry['type']) ?? inferType(match)),
      route: (data.route as string | undefined) ?? inferRoute(match),
      tags: Array.isArray(data.tags) ? (data.tags as string[]) : [],
      createdAt: toIsoString(data.created, stats.birthtime.toISOString()),
      updatedAt: toIsoString(data.updated, stats.mtime.toISOString()),
      body,
      frontmatter: data,
    }
  } catch (err) {
    console.warn(`[vault] read failed for ${id}: ${(err as Error).message}`)
    return null
  }
}
