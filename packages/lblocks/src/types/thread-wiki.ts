// Thread-wiki types: parsed structure of a vault/logmark/_inbox/_diary/pages/<slug>.md file
// with `type: thread-wiki` frontmatter.

export type ThreadParticipant = {
  handle: string  // includes leading @, e.g., "@theo"
  url?: string    // derived; v1 = profile URL https://x.com/<handle>; F2 follow-up may add reply permalinks
}

export type ThreadSourceLink = {
  kind: 'original-tweet' | 'raw-thread' | 'image-transcript' | 'other'
  href: string    // raw markdown link target (URL or wikilink path)
  label: string   // visible label
  isWikilink: boolean
}

export type ThreadWikiSection = {
  heading: string  // "Summary", "Key insights", etc.
  body: string     // raw markdown body of this section (preserved whitespace)
}

export type ThreadWikiMeta = {
  title?: string
  slug?: string
  op?: string         // raw handle "@theo"
  opDisplay?: string  // "Theo - t3.gg"
  sourceUrl?: string  // https://x.com/<op>/status/<id>
  capturedAt?: string // raw ISO from frontmatter
  replyCount?: number
  imageCount?: number
  status?: string
  tags: string[]
  participants: ThreadParticipant[]  // OP excluded; rendered separately
  sources: ThreadSourceLink[]        // parsed from `## Sources` body block
}

export type ThreadWiki = {
  meta: ThreadWikiMeta
  sections: ThreadWikiSection[]  // body sections in document order, with `## Sources` excluded
}

// MarkdownBlock variant - carries raw markdown source so parsing happens TS-side.
export type ThreadWikiBlock = {
  kind: 'thread-wiki'
  body: string
}
