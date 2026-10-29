import type { ViewBlock } from '../types/viewblock.js'
import type { BarChartData, LabelValueListData, NarrativeData } from '../types/primitives.js'

export interface ResolvedBlock {
  primitive: string
  data: unknown
  sectionLabel: string
}

interface BlockMapping {
  primitive: string
  dataMap: (data: unknown) => unknown
  sectionLabel: string
}

type ActivityChartInput = { days: Array<{ label: string; count: number }> }
type TypeBreakdownInput = { types: Array<{ type: string; count: number }> }
type RouteDistributionInput = { routes: Array<{ name: string; count: number; pct: number }> }
type NarrativeSummaryInput = { text: string; inference?: string | null }

export const BLOCK_PRIMITIVE_MAP: Record<string, BlockMapping> = {
  'activity-chart': {
    primitive: 'bar-chart',
    sectionLabel: 'ACTIVITY',
    dataMap: (d: unknown): BarChartData => {
      const data = d as ActivityChartInput
      return {
        bars: data.days.map((day) => ({ label: day.label, value: day.count })),
      }
    },
  },
  'type-breakdown': {
    primitive: 'label-value-list',
    sectionLabel: 'ENTRY TYPES',
    dataMap: (d: unknown): LabelValueListData => {
      const data = d as TypeBreakdownInput
      return {
        items: data.types.map((t) => ({ label: t.type, value: t.count })),
      }
    },
  },
  'route-distribution': {
    primitive: 'bar-chart',
    sectionLabel: 'ROUTES',
    dataMap: (d: unknown): BarChartData => {
      const data = d as RouteDistributionInput
      return {
        bars: data.routes.map((r) => ({
          label: `+${r.name}`,
          value: r.count,
          suffix: `(${r.pct}%)`,
        })),
      }
    },
  },
  'narrative-summary': {
    primitive: 'narrative',
    sectionLabel: 'SUMMARY',
    dataMap: (d: unknown): NarrativeData => {
      const data = d as NarrativeSummaryInput
      return {
        text: data.text,
        badge: data.inference ?? null,
      }
    },
  },
  'viewer-entry-card': {
    primitive: 'viewer-entry-card',
    sectionLabel: 'ENTRY',
    dataMap: (d: unknown): unknown => d,
  },
  'viewer-entry-list': {
    primitive: 'viewer-entry-list',
    sectionLabel: 'ENTRIES',
    dataMap: (d: unknown): unknown => d,
  },
  'type-strip': {
    primitive: 'type-strip',
    sectionLabel: 'TYPES',
    dataMap: (d: unknown): unknown => d,
  },
  'backlink-list': {
    primitive: 'backlink-list',
    sectionLabel: 'BACKLINKS',
    dataMap: (d: unknown): unknown => d,
  },
  'search-result-list': {
    primitive: 'search-result-list',
    sectionLabel: 'SEARCH',
    dataMap: (d: unknown): unknown => d,
  },
  'viewer-entry-row': {
    primitive: 'viewer-entry-row',
    sectionLabel: 'ENTRIES',
    dataMap: (d: unknown): unknown => d,
  },
  'reader-sidebar-tree': {
    primitive: 'reader-sidebar-tree',
    sectionLabel: 'NAVIGATION',
    dataMap: (d: unknown): unknown => d,
  },
  'route-card-grid': {
    primitive: 'route-card-grid',
    sectionLabel: 'ROUTES',
    dataMap: (d: unknown): unknown => d,
  },
  'reader-view-header': {
    primitive: 'reader-view-header',
    sectionLabel: 'HEADER',
    dataMap: (d: unknown): unknown => d,
  },
  'summary-strip': {
    primitive: 'summary-strip',
    sectionLabel: 'SUMMARY',
    dataMap: (d: unknown): unknown => d,
  },
  'type-distribution-bar': {
    primitive: 'type-distribution-bar',
    sectionLabel: 'ENTRY TYPES',
    dataMap: (d: unknown): unknown => d,
  },
  'empty-state': {
    primitive: 'empty-state',
    sectionLabel: 'EMPTY',
    dataMap: (d: unknown): unknown => d,
  },
  'viewer-section-title': {
    primitive: 'viewer-section-title',
    sectionLabel: 'SECTION',
    dataMap: (d: unknown): unknown => d,
  },
  'highlight-row': {
    primitive: 'highlight-row',
    sectionLabel: 'HIGHLIGHTS',
    dataMap: (d: unknown): unknown => d,
  },
  'wiki-link-pills': {
    primitive: 'wiki-link-pills',
    sectionLabel: 'PAGES',
    dataMap: (d: unknown): unknown => d,
  },
  'hero-number': {
    primitive: 'hero-number',
    sectionLabel: 'STAT',
    dataMap: (d: unknown): unknown => d,
  },
  'topic-rail': {
    primitive: 'topic-rail',
    sectionLabel: 'TOPIC',
    dataMap: (d: unknown): unknown => d,
  },
  'attribution-chip': {
    primitive: 'attribution-chip',
    sectionLabel: 'ATTRIBUTION',
    dataMap: (d: unknown): unknown => d,
  },
  'tag-page-meta-sidebar': {
    primitive: 'tag-page-meta-sidebar',
    sectionLabel: 'META',
    dataMap: (d: unknown): unknown => d,
  },
  'topic-shelf': {
    primitive: 'topic-shelf',
    sectionLabel: 'TOPIC SHELF',
    dataMap: (d: unknown): unknown => d,
  },
  'entry-neighbors-band': {
    primitive: 'entry-neighbors-band',
    sectionLabel: 'NEIGHBORS',
    dataMap: (d: unknown): unknown => d,
  },
  'concept-page': {
    primitive: 'concept-page',
    sectionLabel: 'CONCEPT',
    dataMap: (d: unknown): unknown => d,
  },
}

/**
 * Total: unknown block types resolve to the 'unknown-block' placeholder
 * (closed producers may ship newer blocks than this renderer knows;
 * see docs/reference/viewblock-envelope-spec.md).
 */
export function resolve(block: ViewBlock): ResolvedBlock {
  const mapping = BLOCK_PRIMITIVE_MAP[block.type]
  if (!mapping) {
    return {
      primitive: 'unknown-block',
      data: { type: block.type },
      sectionLabel: '',
    }
  }
  return {
    primitive: mapping.primitive,
    data: mapping.dataMap(block.data),
    sectionLabel: mapping.sectionLabel,
  }
}
