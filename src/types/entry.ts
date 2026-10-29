export interface EntrySummary {
  id: string                    // ShortId (LogMark) or relative path (Vanilla)
  title: string                 // first line / heading
  type?: 'task' | 'idea' | 'block' | 'decision' | 'log' | 'timetrack' | 'question' | 'gap' | 'analogy'
  route?: string                // e.g. "projects/foo"
  tags: string[]
  createdAt: string             // ISO 8601
  updatedAt: string             // ISO 8601
}

export interface Entry extends EntrySummary {
  body: string
  frontmatter: Record<string, unknown>
  path?: string
  scope?: string
  source?: string
  session?: string
  backlinks?: Array<{
    id?: string
    title?: string
    kind?: string
    preview?: string
    sourcePath?: string
  }>
  links?: Array<{
    label: string
    linkType?: string
    targetShortId?: string
    targetPath?: string
    isResolved?: boolean
  }>
}

export interface EntryFilter {
  type?: EntrySummary['type']
  route?: string
  tag?: string
  since?: string                // ISO 8601 lower bound on updatedAt
  limit?: number
}

export interface SearchOpts {
  semantic?: boolean
  limit?: number
}
