export type BlockType =
  | 'activity-chart'
  | 'type-breakdown'
  | 'route-distribution'
  | 'narrative-summary'
  | 'tag-cloud'
  | 'theme-clusters'
  | 'delta-table'
  | 'comparison-text'
  | 'entry-list'
  | 'workflow-report'
  | 'vault-summary'
  | 'rendered-content'
  | 'feed-item'
  | 'observatory-card'
  | 'action-bar'
  | 'viewer-entry-card'
  | 'viewer-entry-list'
  | 'type-strip'
  | 'backlink-list'
  | 'search-result-list'
  | 'viewer-entry-row'
  | 'reader-sidebar-tree'
  | 'route-card-grid'
  | 'reader-view-header'
  | 'summary-strip'
  | 'type-distribution-bar'
  | 'empty-state'
  | 'viewer-section-title'
  | 'highlight-row'
  | 'wiki-link-pills'
  | 'hero-number'
  | 'topic-rail'
  | 'attribution-chip'
  | 'tag-page-meta-sidebar'
  | 'topic-shelf'
  | 'entry-neighbors-band'
  | 'dl-glyph'
  | 'lens-tabs'
  | 'chip-strip'
  | 'concept-page'
  | 'grouped-list'
  | 'section-header'
  | 'board'

/**
 * Envelope spec version (distinct from per-block `version`). Producers may
 * stamp it; renderers ignore values they do not know and degrade per-block.
 */
export const VIEWBLOCK_SPEC_VERSION = 1

export interface RenderHints {
  weight?: 'half' | 'full'
  density?: 'compact' | 'standard'
}

export interface ViewBlockMeta {
  source?: string
  period?: { start: string; end: string }
  generatedAt?: string
  inference?: 'L0' | 'L1' | 'L2' | 'L3' | null
  renderHints?: RenderHints
}

export interface ViewBlock<T = unknown> {
  type: BlockType
  version: number
  specVersion?: number
  data: T
  meta?: ViewBlockMeta
}

export interface ActivityChartData {
  days: Array<{
    label: 'Mon' | 'Tue' | 'Wed' | 'Thu' | 'Fri' | 'Sat' | 'Sun'
    count: number
  }>
}

export interface TypeBreakdownData {
  types: Array<{ type: string; count: number }>
}

export interface RouteDistributionData {
  routes: Array<{
    name: string
    count: number
    pct: number
    /** Most-frequent entry type in the route (camelCase name, e.g. "task"); renderers may tint by it. */
    dominantType?: string | null
  }>
  total: number
}

export interface NarrativeSummaryData {
  text: string
  inference?: 'L0' | 'L1' | 'L2' | 'L3' | null
}

export interface ConceptPageData {
  slug: string
  title: string
  status: 'draft' | 'mature' | 'archived'
  tags: string[]
  /** Markdown source of the page body. */
  body: string
  /** Number of vault entries this page was distilled from; integer >= 0. */
  sourceCount: number
  /** Slugs of other concept pages this one relates to. */
  relatedConcepts: string[]
}

export interface SectionHeaderData {
  /** Section name; may be empty for unnamed single-section views. */
  name: string
  /** Scope context rendered beside the name, e.g. "last 7 days". */
  scopeText: string
  /** Count of matched entries in the section; integer >= 0. */
  count: number
  /**
   * Transform-slot advisory: parse complaints and the truncation warning.
   * Absent when the section is well-formed (the common case). A warning, not
   * an error - the section beneath it still rendered.
   */
  notice?: string
}

export interface RenderOptions {
  theme: 'dark' | 'light'
  onAction?: (action: import('./actions.js').BlockAction) => void
  onLinkClick?: (event: import('./markdown-doc.js').LinkClickEvent) => void
  onTagClick?: (tag: string) => void
}
