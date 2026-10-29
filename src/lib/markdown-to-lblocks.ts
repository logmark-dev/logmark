import { unified } from 'unified'
import remarkParse from 'remark-parse'
import type {
  LBlocksDocument,
  MarkdownBlock,
  InlineNode,
} from '@logmark/lblocks'

interface MdastNode {
  type: string
  value?: string
  depth?: number
  ordered?: boolean
  lang?: string
  url?: string
  alt?: string
  title?: string
  children?: MdastNode[]
}

/**
 * Leading YAML front matter (rc6, S3 finding 6 / I3 finding 2). remark-parse
 * has no front matter support, so `---` / `key: value` / `---` parsed as a
 * thematic break plus a setext heading and rendered as a giant heading.
 * Only a block whose every line looks like YAML counts (`key: value`, an
 * indented continuation, a `- ` list item, a `#` comment), so a body that
 * opens with a thematic break followed by prose renders exactly as before.
 */
const FRONTMATTER_RE =
  /^---[ \t]*\r?\n(?:(?:[A-Za-z_][\w.-]*[ \t]*:[^\r\n]*|[ \t]+[^\r\n]*|-[ \t][^\r\n]*|#[^\r\n]*)\r?\n)*(?:---|\.\.\.)[ \t]*(?:\r?\n|$)/

/** Drops a leading BOM and a leading YAML front matter block. Rendering only - files are never rewritten. */
export function stripFrontmatter(markdown: string): string {
  const text = markdown.charCodeAt(0) === 0xfeff ? markdown.slice(1) : markdown
  return text.replace(FRONTMATTER_RE, '')
}

export function markdownToLBlocksDocument(markdown: string): LBlocksDocument {
  const tree = unified().use(remarkParse).parse(stripFrontmatter(markdown)) as MdastNode
  const blocks: MarkdownBlock[] = []
  for (const child of tree.children ?? []) {
    const block = mdastNodeToBlock(child)
    if (block) blocks.push(block)
  }
  return { version: 1, blocks }
}

function mdastNodeToBlock(node: MdastNode): MarkdownBlock | null {
  switch (node.type) {
    case 'heading':
      return {
        kind: 'heading',
        level: clampLevel(node.depth ?? 1),
        children: inlineChildren(node),
      } as MarkdownBlock
    case 'paragraph':
      return { kind: 'paragraph', children: inlineChildren(node) } as MarkdownBlock
    case 'list':
      return {
        kind: 'list',
        ordered: !!node.ordered,
        items: (node.children ?? []).map((li) => ({
          children: (li.children ?? [])
            .map(mdastNodeToBlock)
            .filter((b): b is MarkdownBlock => b !== null),
        })),
      } as MarkdownBlock
    case 'blockquote':
      return {
        kind: 'blockquote',
        children: (node.children ?? [])
          .map(mdastNodeToBlock)
          .filter((b): b is MarkdownBlock => b !== null),
      } as MarkdownBlock
    case 'code':
      return {
        kind: 'codeblock',
        language: node.lang ?? undefined,
        text: node.value ?? '',
      } as MarkdownBlock
    case 'thematicBreak':
      return { kind: 'divider' } as MarkdownBlock
    case 'image':
      return {
        kind: 'image',
        src: node.url ?? '',
        alt: node.alt ?? '',
        title: node.title ?? undefined,
      } as MarkdownBlock
    case 'table':
      return mdastTableToBlock(node)
    default:
      return null
  }
}

function mdastTableToBlock(node: MdastNode): MarkdownBlock {
  const rows = (node.children ?? []).map((row) => ({
    cells: (row.children ?? []).map((cell) => ({ children: inlineChildren(cell) })),
  }))
  const [header, ...body] = rows
  return {
    kind: 'table',
    header: header ?? { cells: [] },
    rows: body,
    alignments: [],
  } as MarkdownBlock
}

function inlineChildren(node: MdastNode): InlineNode[] {
  const out: InlineNode[] = []
  for (const c of node.children ?? []) {
    out.push(...mdastInline(c))
  }
  return out
}

function mdastInline(node: MdastNode): InlineNode[] {
  switch (node.type) {
    case 'text':
      return textWithWikilinks(node.value ?? '')
    case 'emphasis':
      // Emit with style: 'italic' to match lblocks EmphasisRun type.
      return [
        { kind: 'emphasis', style: 'italic', children: inlineChildren(node) },
      ] as InlineNode[]
    case 'strong':
      // Emit with style: 'bold' to match lblocks EmphasisRun type.
      return [
        { kind: 'emphasis', style: 'bold', children: inlineChildren(node) },
      ] as InlineNode[]
    case 'inlineCode':
      return [{ kind: 'code-span', text: node.value ?? '' }] as InlineNode[]
    case 'link':
      return [
        {
          kind: 'link',
          href: node.url ?? '',
          title: node.title ?? undefined,
          children: inlineChildren(node),
        },
      ] as InlineNode[]
    case 'break':
      return [{ kind: 'text', text: '\n' }] as InlineNode[]
    default:
      if (node.children) return inlineChildren(node)
      if (node.value) return [{ kind: 'text', text: node.value }] as InlineNode[]
      return []
  }
}

function clampLevel(n: number): 1 | 2 | 3 | 4 | 5 | 6 {
  return (Math.max(1, Math.min(6, n)) as 1 | 2 | 3 | 4 | 5 | 6)
}

const WIKILINK_RE = /\[\[([^\]|]+)(?:\|([^\]]+))?\]\]/g

/**
 * Split a text run on [[target]] / [[target|label]] wikilinks (release
 * review P0-3). Emitted as plain link nodes; the lblocks renderer
 * classifies a schemeless, extensionless href as kind 'wikilink' and
 * routes it through onLinkClick.
 */
function textWithWikilinks(text: string): InlineNode[] {
  const out: InlineNode[] = []
  let last = 0
  for (const m of text.matchAll(WIKILINK_RE)) {
    const idx = m.index ?? 0
    if (idx > last) out.push({ kind: 'text', text: text.slice(last, idx) } as InlineNode)
    const target = m[1].trim()
    const rawLabel = (m[2] ?? m[1]).trim()
    // A piped label that trims to empty (`[[target| ]]`) must never emit
    // an empty-text link child (polish sweep, lmk-r8q4 item 6) - fall
    // back to the target, matching the bare-`[[target]]` behavior below.
    const label = rawLabel.length > 0 ? rawLabel : target
    out.push({
      kind: 'link',
      href: target,
      children: [{ kind: 'text', text: label }],
    } as InlineNode)
    last = idx + m[0].length
  }
  if (last < text.length) out.push({ kind: 'text', text: text.slice(last) } as InlineNode)
  if (out.length === 0) out.push({ kind: 'text', text } as InlineNode)
  return out
}
