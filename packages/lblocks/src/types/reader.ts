export interface ReaderLinkSet {
  wiki: string[]
  routes: string[]
  tags: string[]
  blockRefs: string[]
  external: string[]
}

export interface ViewerEntryCardData {
  id: string
  entryType: string
  content: string
  timestamp: string
  route: string | null
  tags: string[]
  isCompleted: boolean
  links: ReaderLinkSet
}

export interface ViewerEntryListData {
  title?: string
  entries: ViewerEntryCardData[]
  emptyMessage?: string
}

export interface ViewerEntryRowAttribution {
  handle: string
  vaultSlug?: string
  vaultTitle?: string
}

export interface ViewerEntryRowEntry {
  id: string
  type: string
  timestamp: string
  preview: string
  tags: string[]
  /**
   * Destination for navigation when the row or its Open link is clicked.
   * Wired by the host surface (typically inkEntryDeepLinkPath). When
   * undefined, the row is not interactive.
   */
  openUrl?: string
  /**
   * Author/vault attribution shown in the meta line as "@handle / vault".
   * Tag-page rows pass this so cross-vault context is visible. In-vault
   * row compositions (summary lens, timeline lens) pass `showAttribution:
   * false` on the parent data to hide the chip even when present.
   */
  attribution?: ViewerEntryRowAttribution
  /**
   * Route path shown in the trailing column (e.g. "domains/ai-llm").
   * Rendered only by the 'cockpit' variant; ignored otherwise.
   */
  route?: string
  /**
   * Task lifecycle, display-only. The cockpit views and ratifies; it
   * never completes tasks. 'done' renders a filled state glyph plus
   * struck-through muted content; 'open' renders an empty outline
   * glyph. Absent = no glyph. Rendered only by the 'cockpit' variant.
   */
  state?: 'open' | 'done'
  /**
   * Preformatted time text (e.g. "2h ago" or "20:10"). When present it
   * replaces the default long date format ("May 20, 2026") wherever the
   * row renders its time (meta line in the default variant, trailing
   * column in the 'cockpit' variant).
   */
  timeLabel?: string
  /**
   * Rim + type-pill color source (cockpit only, frame T / D7). 'type'
   * (default) resolves the color via entryTypeColor(entry.type). 'signal'
   * uses `signalColor` instead - for rows whose pill vocabulary isn't one
   * of the 8 canonical entry types (feed's concept/digest/session/
   * activity, health's changed/deleted/parse-error), so entryTypeColor's
   * unknown-type fallback ('log', muted) doesn't flatten every non-type
   * row to the same color. Ignored unless the 'cockpit' variant is set.
   */
  rimTone?: 'type' | 'signal'
  /**
   * Explicit color token (e.g. 'var(--signal-alert)' or
   * 'var(--entry-idea)') applied to the rim + type-pill when rimTone is
   * 'signal'. Ignored otherwise. Always a var(--token) reference, never
   * a raw hex.
   */
  signalColor?: string
  /**
   * One ghost-button action appended after the row's normal trailing
   * content (e.g. { label: 'Open ↗', action: 'row-open' } or
   * { label: 'Review', action: 'health-review' }). Click fires onAction
   * with { type: action, target: id, source: 'viewer-entry-row' } -
   * additive to, not a replacement for, the row body's own entry-focus
   * handler. Cockpit variant, 'inline' layout only. `action` must not
   * reuse a built-in action name this primitive already emits
   * (`entry-focus`, `entry-open`, `entry-select`, `type-filter`) - those
   * are reserved for the row's own URL-target open / select / type-pill
   * handling, and a trailingAction sharing one would be swallowed by it.
   */
  trailingAction?: { label: string; action: string }
  /**
   * Checkbox check state, meaningful only when the render-level
   * `selectable` flag (on ViewerEntryRowData) is set. Cockpit variant,
   * 'inline' layout only.
   */
  checked?: boolean
}

export interface ViewerEntryRowData {
  entries: ViewerEntryRowEntry[]
  /**
   * Whether to render the @handle / vault attribution chip in the meta
   * line. Default true. Set false inside the vault reader (kit pattern:
   * EntryRow showAttribution=false in views-reader.jsx).
   */
  showAttribution?: boolean
  /**
   * 'cockpit' renders the cockpit row shape (entry-type rim, read-only
   * state glyph, route + time trailing column) per the cockpit design
   * pass decision Q2: one shared lblocks entry-row primitive both
   * surfaces read. When absent, output is byte-identical to the Ink
   * reader default.
   */
  variant?: 'cockpit'
  /**
   * Row anatomy switch for the 'cockpit' variant (designer round-2
   * decision R1, frame G). 'inline' renders the scan-dense one-line
   * browse row: rim | time | type pill | title (single line, ellipsis)
   * | route, with no second meta line. 'stacked' or absent keeps the
   * existing two-line cockpit shape unchanged. Ignored unless `variant`
   * is 'cockpit'. Cockpit browse surfaces (timeline, lenses, recent,
   * route activity) default to 'inline'; 'stacked' stays for surfaces
   * that must show a second context line.
   */
  layout?: 'inline' | 'stacked'
  /**
   * Renders a leading checkbox on every row (frame T / D7, cockpit +
   * 'inline' layout only) - the fork retirement for search's
   * distill-selection `ck-selrow`. Checkbox click fires `entry-select`
   * (target = entry.id); per-entry `checked` supplies the visual state.
   * The row body's entry-focus/entry-open handlers are untouched -
   * selection is additive, not a mode swap on the click target.
   */
  selectable?: boolean
}

export interface TypeStripData {
  types: Array<{
    type: string
    count: number
    selected?: boolean
    target?: string
    actionType?: 'type-filter' | 'type-navigate'
    href?: string
  }>
}

export interface ReaderSidebarTypeChip {
  type: string
  label: string
  count: number
  selected?: boolean
  target: string
  href?: string
  /**
   * Additive (cockpit pass-4 D2): when true, the dot renders on
   * `--lb-text-dim` instead of the per-type color - the degraded-tier
   * "single muted total" reading for adapters that can't type entries.
   * `type` is still required but is ignored for coloring in this case.
   * Absent/false preserves the existing per-type-color output byte-for-byte.
   */
  muted?: boolean
}

export type ReaderSidebarTreeItem =
  | {
      kind: 'lens'
      id: string
      key: string
      label: string
      count: number | null
      active?: boolean
    }
  | {
      kind: 'divider'
      id: string
    }
  | {
      kind: 'vault-label'
      id: string
      label: string
    }
  | {
      kind: 'route'
      id: string
      label: string
      routePath: string
      depth: number
      count: number
      hasChildren: boolean
      isCategory: boolean
      description?: string | null
      expanded?: boolean
      active?: boolean
      onPath?: boolean
      href?: string
      /**
       * Additive (cockpit pass-4 D2): when true AND `active`, the row
       * renders the frame-Q active treatment - elevated background
       * (`--lb-bg-tertiary`) + amber inset (`inset 2px 0 0 --lb-accent`).
       * Absent/false preserves the existing active output byte-for-byte
       * (`--lb-bg-secondary`, no inset shadow) - the live Ink reader
       * consumes this primitive (ink-reader-ia.ts via ViewerNav) and must
       * not re-skin on a routine sync-lblocks:web.
       */
      elevatedActive?: boolean
    }
  | {
      kind: 'type-chip-strip'
      id: string
      ownerPath: string
      chips: ReaderSidebarTypeChip[]
    }

export interface ReaderSidebarTreeData {
  label?: string
  items: ReaderSidebarTreeItem[]
}

export interface RouteCardGridTypeCount {
  type: string
  count: number
}

export interface RouteCardGridItem {
  id: string
  label: string
  routePath: string
  count: number
  typeCounts: RouteCardGridTypeCount[]
  description?: string | null
  statsLabel?: string | null
  href?: string
}

export interface RouteCardGridData {
  routes: RouteCardGridItem[]
  emptyMessage?: string
}

export interface ReaderViewHeaderData {
  eyebrow?: string | null
  title: string
  subtitle?: string | null
  badge?: string | null
  countLabel?: string | null
}

export interface SummaryStripMetric {
  label: string
  value: string
  delta?: string | null
  deltaTone?: 'neutral' | 'positive' | 'negative' | 'warning'
}

export interface SummaryStripData {
  metrics: SummaryStripMetric[]
  emptyMessage?: string
}

export interface TypeDistributionBarData {
  segments: Array<{
    type: string
    count: number
  }>
  activeType?: string | null
}

export interface EmptyStateData {
  glyph?: string
  eyebrow?: string
  title: string
  sub?: string
  kbdHint?: string
}

export interface ViewerSectionTitleData {
  title: string
  countLabel?: string
  actionLabel?: string
}

export interface HighlightRowEntry {
  id: string
  kind: 'actionable' | 'today' | 'discovery'
  title: string
  sub: string
  actionLabel?: string
  isActive?: boolean
}

export interface HighlightRowData {
  entries: HighlightRowEntry[]
}

export interface BacklinkListData {
  backlinks: Array<{
    id: string
    label: string
    excerpt?: string | null
    sourceType: 'entry' | 'page'
  }>
}

export interface WikiLinkPillItem {
  id: string
  label: string
  target: string
  excerpt?: string | null
  backlinkCount?: number | null
}

export interface WikiLinkPillsData {
  links: WikiLinkPillItem[]
  emptyMessage?: string
}

export interface SearchResultListData {
  query: string
  results: Array<{
    id: string
    resultType: 'entry' | 'page'
    title: string
    excerpt: string
    route?: string | null
    tags?: string[]
  }>
}

export interface HeroNumberData {
  value: string | number
  label: string
  delta?: string | null
  deltaTone?: 'positive' | 'negative' | 'neutral'
  size?: 'sm' | 'md' | 'lg'
}

export interface TopicRailData {
  topic: string
  current: number
  total: number
  hasPrev: boolean
  hasNext: boolean
  prevUrl?: string | null
  nextUrl?: string | null
  exitUrl?: string | null
}

export interface AttributionChipData {
  kind: 'agent' | 'human'
  cadence?: string | null
  label?: string | null
  showKind?: boolean
}

export interface TagPageMetaSidebarVault {
  id: string
  title: string
  slug: string
  publisherHandle: string
  publisherKind: 'agent' | 'human'
  entryCount: number
  href?: string | null
}

export interface TagPageMetaSidebarCreator {
  id: string
  handle: string
  kind: 'agent' | 'human'
  entryCount: number
  href?: string | null
}

export interface TagPageMetaSidebarData {
  vaults?: TagPageMetaSidebarVault[]
  creators?: TagPageMetaSidebarCreator[]
  relatedTags?: Array<{ tag: string; coCount: number; href?: string | null }>
}

export interface TopicShelfVault {
  id: string
  title: string
  slug: string
  publisherHandle: string
  publisherKind: 'agent' | 'human'
  entryCount: number
  topTags?: string[]
  href?: string | null
}

export interface TopicShelfData {
  topic: string
  entryCount: number
  vaultCount: number
  vaults: TopicShelfVault[]
  moreHref?: string | null
}

export interface EntryNeighborRef {
  id: string
  preview: string
  type: string
  timestamp: string
  href?: string | null
}

export interface EntryTopicNeighbor {
  id: string
  preview: string
  type: string
  timestamp: string
  publisherHandle: string
  publisherKind: 'agent' | 'human'
  vaultTitle: string
  href?: string | null
}

export interface EntryNeighborsBandData {
  vaultPrev?: EntryNeighborRef | null
  vaultNext?: EntryNeighborRef | null
  topicTag?: string | null
  topicNeighbors?: EntryTopicNeighbor[]
}

export interface DlGlyphData {
  vault: string
  size?: 'sm' | 'lg'
  archiveUrl?: string | null
}

export interface LensTabsData {
  value: string
  options: string[]
}

export interface ChipStripOption {
  value: string
  label: string
}

export interface ChipStripData {
  value: string
  options: ChipStripOption[]
  role: 'sort' | 'filter'
}
