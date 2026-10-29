// LBlocksDocument: top-level markdown document model rendered by the markdown compositor.
// Distinct from ViewBlock (the existing structured-data model). Both coexist in lblocks.

import type { ChatTranscriptBlock } from './chat-transcript.js'
import type { ThreadWikiBlock } from './thread-wiki.js'

export interface LBlocksDocument {
  version: number
  blocks: MarkdownBlock[]
  metadata?: DocumentMetadata
}

export interface DocumentMetadata {
  sourcePath?: string
  sourceMtime?: number
}

export type MarkdownBlock =
  | HeadingBlock
  | ParagraphBlock
  | ListBlock
  | BlockquoteBlock
  | CodeBlock
  | TableBlock
  | ImageBlock
  | DividerBlock
  | ThreadWikiBlock
  | FrontmatterBlock
  | ChatTranscriptBlock
  | PlaintextBlock

export interface HeadingBlock {
  kind: 'heading'
  level: 1 | 2 | 3 | 4 | 5 | 6
  children: InlineNode[]
}

export interface ParagraphBlock {
  kind: 'paragraph'
  children: InlineNode[]
}

export interface ListBlock {
  kind: 'list'
  ordered: boolean
  start?: number
  items: ListItem[]
}

export interface ListItem {
  checked?: boolean
  children: MarkdownBlock[] // items can contain paragraphs, nested lists, etc.
}

export interface BlockquoteBlock {
  kind: 'blockquote'
  children: MarkdownBlock[]
}

export interface CodeBlock {
  kind: 'codeblock'
  language?: string
  text: string
}

export interface TableBlock {
  kind: 'table'
  header: TableRow
  rows: TableRow[]
  alignments: Array<'left' | 'center' | 'right' | null>
}

export interface TableRow {
  cells: TableCell[]
}

export interface TableCell {
  children: InlineNode[]
}

export interface ImageBlock {
  kind: 'image'
  src: string
  alt: string
  title?: string
}

export interface DividerBlock {
  kind: 'divider'
}

export interface FrontmatterBlock {
  kind: 'frontmatter'
  entries: Array<{ key: string; value: string }>
}

export interface PlaintextBlock {
  kind: 'plaintext'
  text: string
}

export type InlineNode = TextRun | EmphasisRun | CodeSpan | LinkInline | LineBreakNode | InlineImage

export interface TextRun {
  kind: 'text'
  text: string
}

export interface EmphasisRun {
  kind: 'emphasis'
  style: 'bold' | 'italic' | 'bold-italic' | 'strikethrough'
  children: InlineNode[]
}

export interface CodeSpan {
  kind: 'code-span'
  text: string
  // Set by the C# transform when an absolute-path code-span points at a
  // missing file or directory. Renderer dims + strikes the chip and skips
  // wiring the click handler.
  broken?: boolean
}

export interface LinkInline {
  kind: 'link'
  href: string
  title?: string
  children: InlineNode[]
}

export interface LineBreakNode {
  kind: 'line-break'
}

export interface InlineImage {
  kind: 'inline-image'
  src: string
  alt: string
  title?: string
}

export interface MarkdownRenderOptions {
  theme: 'dark' | 'light'
  onLinkClick?: (event: LinkClickEvent) => void
  onTagClick?: (tag: string) => void
  resolveAssetUrl?: (rawSrc: string) => string
}

export interface LinkClickEvent {
  href: string
  kind: 'external' | 'wikilink' | 'relative-md' | 'internal' | 'absolute-path'
  modifiers: { ctrl: boolean; shift: boolean; alt: boolean }
}

export type ComposeBlockFn = (block: MarkdownBlock) => HTMLElement
