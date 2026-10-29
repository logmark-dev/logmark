/**
 * LogMark-notation display helpers (design pass Q3).
 *
 * Reading content renders richly; plumbing lifts into the metadata rail.
 * A display title NEVER shows raw notation: wikilinks collapse to their
 * labels, tags/anchors/provenance clauses are stripped.
 */

const WIKILINK_RE = /\[\[([^\]|]+)(?:\|([^\]]+))?\]\]/g
const TAG_RE = /(^|\s)#[A-Za-z0-9][\w/-]*/g
const ANCHOR_RE = /(^|\s)\^[0-9a-f]{6,12}\b/gi
const SYNC_ID_RE = /(^|\s)_id:\s*[0-9a-f]{6,12}\b/gi
const SOURCE_CLAUSE_RE = /(^|\s)source:\s*[\w./-]+/gi

function wikilinkLabel(target: string, label?: string): string {
  if (label && label.trim()) return label.trim()
  const segments = target.split('/')
  return (segments[segments.length - 1] ?? target).trim()
}

/** Collapse notation in a raw title line to plain reading text. */
export function deriveDisplayTitle(raw: string, fallback = 'Untitled'): string {
  let text = raw
  text = text.replace(WIKILINK_RE, (_m, target: string, label?: string) => wikilinkLabel(target, label))
  text = text.replace(SYNC_ID_RE, ' ')
  text = text.replace(SOURCE_CLAUSE_RE, ' ')
  text = text.replace(ANCHOR_RE, ' ')
  text = text.replace(TAG_RE, ' ')
  text = text.replace(/\*\*([^*]+)\*\*/g, '$1').replace(/\*([^*]+)\*/g, '$1')
  text = text.replace(/\s+/g, ' ').replace(/\s+([.,;:!?])/g, '$1').trim()
  text = text.replace(/^[-|:;,.\s]+/, '').replace(/[-|:;,\s]+$/, '')
  return text || fallback
}

export interface LiftedProvenance {
  body: string
  anchors: string[]
  sources: string[]
  syncIds: string[]
}

/**
 * Lift standalone provenance tokens (^anchors, _id: sync ids, trailing
 * source: clauses) out of a notation body so the prose reads clean and
 * the metadata rail can display them. Conservative: only token-shaped
 * matches move; anything ambiguous stays in the body.
 */
export function liftProvenance(body: string): LiftedProvenance {
  const anchors: string[] = []
  const sources: string[] = []
  const syncIds: string[] = []

  let out = body.replace(/(^|\s)(\^[0-9a-f]{6,12})\b/gi, (_m, pre: string, token: string) => {
    anchors.push(token)
    return pre
  })
  out = out.replace(SYNC_ID_RE, (m, pre: string) => {
    const token = m.match(/[0-9a-f]{6,12}/i)?.[0]
    if (token) syncIds.push(token)
    return pre
  })
  out = out.replace(SOURCE_CLAUSE_RE, (m, pre: string) => {
    const token = m.replace(/^\s*source:\s*/i, '').trim()
    if (token && !sources.includes(token)) sources.push(token)
    return pre
  })
  out = out
    .split('\n')
    .map((line) => line.replace(/[ \t]+$/, ''))
    .join('\n')

  return { body: out, anchors: dedupe(anchors), sources: dedupe(sources), syncIds: dedupe(syncIds) }
}

function dedupe(values: string[]): string[] {
  return [...new Set(values)]
}

/** True when a title is really a vault path (the vanilla degraded tier). */
export function isPathTitle(title: string): boolean {
  return title.includes('/') && !/\s/.test(title.trim())
}

/**
 * Derive a human title from a vault path (pass-3 P3 / frame N): strip the
 * `+` route-operator, resolve `_type` folders into words, and never leave
 * raw underscores for a markdown pass to italicize. Real notation titles
 * (MCP/daemon) should be preferred by callers - this is the path fallback.
 */
export function deriveTitleFromPath(path: string): string {
  const clean = path.replace(/^\+/, '').replace(/\.md$/i, '')
  const segments = clean.split('/').filter(Boolean)
  if (segments.length === 0) return path

  const typeIndex = segments.findIndex((seg) => seg.startsWith('_'))
  if (typeIndex === -1) {
    return clean
  }

  const type = segments[typeIndex].replace(/^_+/, '')
  const parent = typeIndex > 0 ? segments[typeIndex - 1] : undefined
  const tail = segments.slice(typeIndex + 1).join('/')

  let label: string
  if (parent) {
    label = `${parent} - ${type}`
  } else {
    const word = type.replace(/s$/, '')
    label = word.charAt(0).toUpperCase() + word.slice(1)
  }
  if (tail) label += ` · ${tail}`
  return label
}

/**
 * Neutralize `_` pairs so parseInline cannot render emphasis out of path
 * segments (the italic `_diary` / `_tasks` artifact from the audit).
 */
export function escapeInlineUnderscores(text: string): string {
  return text.replace(/_/g, '\\_')
}
