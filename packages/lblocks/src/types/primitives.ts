import type { BlockAction } from './actions.js'
import type { LinkClickEvent } from './markdown-doc.js'

export interface PrimitiveOptions {
  onAction?: (action: BlockAction) => void
  onLinkClick?: (event: LinkClickEvent) => void
  onTagClick?: (tag: string) => void
  density: 'compact' | 'standard'
  theme?: 'dark' | 'light'
}

export type PrimitiveRenderer<T = unknown> = (
  data: T,
  options: PrimitiveOptions
) => HTMLElement

export interface BarChartBar {
  label: string
  value: number
  maxValue?: number
  suffix?: string
  action?: { type: string; target: string }
}

export interface BarChartData {
  bars: BarChartBar[]
}

export interface LabelValueItem {
  label: string
  value: number
  action?: { type: string; target: string }
}

export interface LabelValueListData {
  items: LabelValueItem[]
}

export interface NarrativeData {
  text: string
  badge?: string | null
}

export interface ComparisonData {
  text: string
  delta: number
  direction: 'up' | 'down' | 'same'
}

export interface SectionHeaderData {
  label: string
}

export interface EntryListItem {
  id: string
  title: string
  subtitle?: string
  meta?: string
  badge?: string
}

export interface EntryListData {
  items: EntryListItem[]
  emptyMessage?: string
  onItemClick?: (id: string) => void
}
