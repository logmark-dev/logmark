export const VERSION = '0.5.0'

// Engine
export { applyTheme, DARK_TOKENS, LIGHT_TOKENS } from './engine/theme.js'
export { resolve, BLOCK_PRIMITIVE_MAP } from './engine/resolver.js'
export { compose, composePrimitive } from './engine/compositor.js'
export { composeMarkdown } from './engine/markdown-compositor.js'
export { parseMarkdown, parseInline } from './engine/markdown-parser.js'

// Primitives
export { renderSectionHeader } from './primitives/section-header.js'
export { renderBarChart } from './primitives/bar-chart.js'
export { renderLabelValueList } from './primitives/label-value-list.js'
export { renderNarrative } from './primitives/narrative.js'
export { renderComparison } from './primitives/comparison.js'
export { renderEntryList } from './primitives/entry-list.js'
export { renderConceptPage } from './primitives/concept-page.js'
export { renderChatTranscript, parseChatTranscript } from './primitives/chat-transcript.js'
export { renderThreadWiki, parseThreadWiki } from './primitives/thread-wiki.js'
export { renderPlaintext } from './primitives/markdown-plaintext.js'
export { renderViewerEntryCard } from './primitives/viewer-entry-card.js'
export { renderViewerEntryList } from './primitives/viewer-entry-list.js'
export { renderViewerEntryRow } from './primitives/viewer-entry-row.js'
export { renderTypeStrip } from './primitives/type-strip.js'
export { renderReaderSidebarTree } from './primitives/reader-sidebar-tree.js'
export { renderRouteCardGrid } from './primitives/route-card-grid.js'
export { renderReaderViewHeader } from './primitives/reader-view-header.js'
export { renderSummaryStrip } from './primitives/summary-strip.js'
export { renderHeroNumber } from './primitives/hero-number.js'
export { renderTopicRail } from './primitives/topic-rail.js'
export { renderAttributionChip } from './primitives/attribution-chip.js'
export { renderTagPageMetaSidebar } from './primitives/tag-page-meta-sidebar.js'
export { renderTopicShelf } from './primitives/topic-shelf.js'
export { renderEntryNeighborsBand } from './primitives/entry-neighbors-band.js'
export { renderTypeDistributionBar } from './primitives/type-distribution-bar.js'
export { renderEmptyState } from './primitives/empty-state.js'
export { renderViewerSectionTitle } from './primitives/viewer-section-title.js'
export { renderHighlightRow } from './primitives/highlight-row.js'
export { renderBacklinkList } from './primitives/backlink-list.js'
export { renderWikiLinkPills } from './primitives/wiki-link-pills.js'
export { renderSearchResultList } from './primitives/search-result-list.js'
export { renderUnknownBlock } from './primitives/unknown-block.js'

// v0.5.0 -- Ink renovation primitives
export { renderDlGlyph } from './primitives/dl-glyph.js'
export { renderLensTabs } from './primitives/lens-tab.js'
export { renderChipStrip } from './primitives/chip-strip.js'

// Util
export { ENTRY_TYPE_KEYS, entryTypeColor, getEntryTypeToken } from './util/entry-type-tokens.js'

// Render function imports
import { applyTheme } from './engine/theme.js'
import { compose, composePrimitive } from './engine/compositor.js'
import { composeMarkdown } from './engine/markdown-compositor.js'
import type { ViewBlock, RenderOptions } from './types/viewblock.js'
import type { LBlocksDocument, MarkdownRenderOptions } from './types/markdown-doc.js'

// Types
export type {
  ViewBlock,
  BlockType,
  RenderOptions,
  ViewBlockMeta,
  RenderHints,
  ActivityChartData,
  TypeBreakdownData,
  RouteDistributionData,
  NarrativeSummaryData,
  ConceptPageData,
} from './types/viewblock.js'

export type { BlockAction } from './types/actions.js'

export type {
  PrimitiveRenderer,
  PrimitiveOptions,
  BarChartBar,
  BarChartData,
  LabelValueListData,
  LabelValueItem,
  NarrativeData,
  ComparisonData,
  SectionHeaderData,
  EntryListItem,
  EntryListData,
} from './types/primitives.js'

export type {
  ReaderLinkSet,
  ViewerEntryCardData,
  ViewerEntryListData,
  ViewerEntryRowData,
  ViewerEntryRowEntry,
  TypeStripData,
  ReaderSidebarTreeData,
  ReaderSidebarTreeItem,
  ReaderSidebarTypeChip,
  RouteCardGridData,
  RouteCardGridItem,
  RouteCardGridTypeCount,
  ReaderViewHeaderData,
  SummaryStripData,
  SummaryStripMetric,
  TypeDistributionBarData,
  EmptyStateData,
  ViewerSectionTitleData,
  HighlightRowEntry,
  HighlightRowData,
  BacklinkListData,
  WikiLinkPillItem,
  WikiLinkPillsData,
  SearchResultListData,
  HeroNumberData,
  TopicRailData,
  AttributionChipData,
  TagPageMetaSidebarData,
  TagPageMetaSidebarVault,
  TagPageMetaSidebarCreator,
  TopicShelfData,
  TopicShelfVault,
  EntryNeighborsBandData,
  EntryNeighborRef,
  EntryTopicNeighbor,
  DlGlyphData,
  LensTabsData,
  ChipStripData,
  ChipStripOption,
} from './types/reader.js'

export type {
  LBlocksDocument,
  MarkdownBlock,
  HeadingBlock,
  ParagraphBlock,
  ListBlock,
  ListItem,
  BlockquoteBlock,
  CodeBlock,
  TableBlock,
  TableRow,
  TableCell,
  ImageBlock,
  DividerBlock,
  FrontmatterBlock,
  PlaintextBlock,
  InlineNode,
  TextRun,
  EmphasisRun,
  CodeSpan,
  LinkInline,
  LineBreakNode,
  InlineImage,
  MarkdownRenderOptions,
  LinkClickEvent,
  DocumentMetadata,
  ComposeBlockFn,
} from './types/markdown-doc.js'

export type {
  ChatTranscriptMeta,
  ChatTurnKind,
  ChatTurn,
  ChatTranscript,
  ChatTranscriptBlock,
} from './types/chat-transcript.js'

export type {
  ThreadWikiMeta,
  ThreadParticipant,
  ThreadSourceLink,
  ThreadWikiSection,
  ThreadWiki,
  ThreadWikiBlock,
} from './types/thread-wiki.js'

export function render(
  blocks: ViewBlock[],
  container: HTMLElement,
  options: RenderOptions
): void {
  applyTheme(container, options.theme)
  const root = compose(blocks, options)
  container.replaceChildren(root)
}

export function renderPrimitive(
  block: ViewBlock,
  container: HTMLElement,
  options: RenderOptions
): void {
  applyTheme(container, options.theme)
  const root = composePrimitive(block, options)
  container.replaceChildren(root)
}

export function renderMarkdown(
  doc: LBlocksDocument,
  container: HTMLElement,
  options: MarkdownRenderOptions
): void {
  applyTheme(container, options.theme)
  const root = composeMarkdown(doc, options)
  container.replaceChildren(root)
}
