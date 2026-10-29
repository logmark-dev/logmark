import type { ViewBlock, RenderOptions } from '../types/viewblock.js'
import type { PrimitiveOptions } from '../types/primitives.js'
import { resolve } from './resolver.js'
import { renderSectionHeader } from '../primitives/section-header.js'
import { renderBarChart } from '../primitives/bar-chart.js'
import { renderLabelValueList } from '../primitives/label-value-list.js'
import { renderNarrative } from '../primitives/narrative.js'
import { renderComparison } from '../primitives/comparison.js'
import { renderViewerEntryCard } from '../primitives/viewer-entry-card.js'
import { renderViewerEntryList } from '../primitives/viewer-entry-list.js'
import { renderViewerEntryRow } from '../primitives/viewer-entry-row.js'
import { renderTypeStrip } from '../primitives/type-strip.js'
import { renderReaderSidebarTree } from '../primitives/reader-sidebar-tree.js'
import { renderRouteCardGrid } from '../primitives/route-card-grid.js'
import { renderReaderViewHeader } from '../primitives/reader-view-header.js'
import { renderSummaryStrip } from '../primitives/summary-strip.js'
import { renderTypeDistributionBar } from '../primitives/type-distribution-bar.js'
import { renderEmptyState } from '../primitives/empty-state.js'
import { renderViewerSectionTitle } from '../primitives/viewer-section-title.js'
import { renderHighlightRow } from '../primitives/highlight-row.js'
import { renderBacklinkList } from '../primitives/backlink-list.js'
import { renderWikiLinkPills } from '../primitives/wiki-link-pills.js'
import { renderSearchResultList } from '../primitives/search-result-list.js'
import { renderConceptPage } from '../primitives/concept-page.js'
import { renderUnknownBlock } from '../primitives/unknown-block.js'

type AnyRenderer = (data: unknown, opts: PrimitiveOptions) => HTMLElement

const PRIMITIVE_RENDERERS: Record<string, AnyRenderer> = {
  'bar-chart': renderBarChart as AnyRenderer,
  'label-value-list': renderLabelValueList as AnyRenderer,
  'narrative': renderNarrative as AnyRenderer,
  'comparison': renderComparison as AnyRenderer,
  'viewer-entry-card': renderViewerEntryCard as AnyRenderer,
  'viewer-entry-list': renderViewerEntryList as AnyRenderer,
  'viewer-entry-row': renderViewerEntryRow as AnyRenderer,
  'type-strip': renderTypeStrip as AnyRenderer,
  'reader-sidebar-tree': renderReaderSidebarTree as AnyRenderer,
  'route-card-grid': renderRouteCardGrid as AnyRenderer,
  'reader-view-header': renderReaderViewHeader as AnyRenderer,
  'summary-strip': renderSummaryStrip as AnyRenderer,
  'type-distribution-bar': renderTypeDistributionBar as AnyRenderer,
  'empty-state': renderEmptyState as AnyRenderer,
  'viewer-section-title': renderViewerSectionTitle as AnyRenderer,
  'highlight-row': renderHighlightRow as AnyRenderer,
  'backlink-list': renderBacklinkList as AnyRenderer,
  'wiki-link-pills': renderWikiLinkPills as AnyRenderer,
  'search-result-list': renderSearchResultList as AnyRenderer,
  'concept-page': renderConceptPage as AnyRenderer,
  'unknown-block': renderUnknownBlock as AnyRenderer,
}

function getWeight(block: ViewBlock): 'full' | 'half' {
  return block.meta?.renderHints?.weight ?? 'full'
}

function getDensity(block: ViewBlock): 'compact' | 'standard' {
  return block.meta?.renderHints?.density ?? 'standard'
}

function buildCard(block: ViewBlock, options: RenderOptions): HTMLElement {
  const resolved = resolve(block)

  const card = document.createElement('div')
  card.className = 'lb-card'
  card.style.backgroundColor = 'var(--lb-bg-tertiary)'
  card.style.borderRadius = 'var(--lb-radius)'
  card.style.padding = '12px'

  if (resolved.sectionLabel !== '') {
    const header = renderSectionHeader({ label: resolved.sectionLabel })
    card.appendChild(header)
  }

  const primitiveOpts: PrimitiveOptions = {
    density: getDensity(block),
    onAction: options.onAction,
    onLinkClick: options.onLinkClick,
    onTagClick: options.onTagClick,
    theme: options.theme,
  }

  const renderer = PRIMITIVE_RENDERERS[resolved.primitive]
  if (renderer) {
    const content = renderer(resolved.data, primitiveOpts)
    card.appendChild(content)
  }

  return card
}

export function composePrimitive(block: ViewBlock, options: RenderOptions): HTMLElement {
  const resolved = resolve(block)
  const primitiveOpts: PrimitiveOptions = {
    density: getDensity(block),
    onAction: options.onAction,
    onLinkClick: options.onLinkClick,
    onTagClick: options.onTagClick,
    theme: options.theme,
  }
  const renderer = PRIMITIVE_RENDERERS[resolved.primitive]
  if (!renderer) {
    throw new Error(`Unknown primitive: ${resolved.primitive}`)
  }
  return renderer(resolved.data, primitiveOpts)
}

export function compose(blocks: ViewBlock[], options: RenderOptions): HTMLElement {
  const root = document.createElement('div')
  root.className = 'lb-root'
  root.style.maxWidth = blocks.some(block => block.meta?.renderHints?.weight === 'full')
    ? '100%'
    : '620px'
  root.style.display = 'flex'
  root.style.flexDirection = 'column'
  root.style.gap = '12px'

  let i = 0
  while (i < blocks.length) {
    const block = blocks[i]
    const weight = getWeight(block)

    if (weight === 'full') {
      const row = document.createElement('div')
      row.className = 'lb-row lb-row-full'
      row.appendChild(buildCard(block, options))
      root.appendChild(row)
      i++
    } else {
      const next = blocks[i + 1]
      const nextWeight = next ? getWeight(next) : null

      const row = document.createElement('div')
      row.className = 'lb-row lb-row-half'
      row.style.display = 'grid'
      row.style.gridTemplateColumns = '1fr 1fr'
      row.style.gap = '12px'

      row.appendChild(buildCard(block, options))

      if (nextWeight === 'half') {
        row.appendChild(buildCard(next!, options))
        i += 2
      } else {
        i++
      }

      root.appendChild(row)
    }
  }

  return root
}
