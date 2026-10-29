import { listEntriesFs, readEntryFs } from './entry-reader.js'
import type { EntrySummary, SearchOpts } from '../../types/entry.js'

export async function searchEntriesFs(
  vault: string,
  query: string,
  opts: SearchOpts = {}
): Promise<EntrySummary[]> {
  const needle = query.trim().toLowerCase()
  if (needle.length === 0) return []
  const summaries = await listEntriesFs(vault)
  const out: EntrySummary[] = []
  for (const s of summaries) {
    const titleHit = s.title.toLowerCase().includes(needle)
    if (titleHit) {
      out.push(s)
      if (opts.limit && out.length >= opts.limit) break
      continue
    }
    const entry = await readEntryFs(vault, s.id)
    if (entry && entry.body.toLowerCase().includes(needle)) {
      out.push(s)
    }
    if (opts.limit && out.length >= opts.limit) break
  }
  return out
}
