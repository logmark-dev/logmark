import type { PrimitiveOptions } from '../types/primitives.js'
import type { RouteCardGridData, RouteCardGridItem } from '../types/reader.js'
import { entryTypeColor } from '../util/entry-type-tokens.js'

const SOURCE = 'route-card-grid'

function formatEntryCount(count: number): string {
  return `${count} ${count === 1 ? 'entry' : 'entries'}`
}

function emit(options: Partial<PrimitiveOptions>, routePath: string): void {
  options.onAction?.({ type: 'route-select', target: routePath, source: SOURCE })
}

function renderTypeCounts(route: RouteCardGridItem): HTMLElement {
  const row = document.createElement('div')
  row.className = 'lb-route-card-grid-type-counts'
  row.style.display = 'flex'
  row.style.flexWrap = 'wrap'
  row.style.gap = '5px'
  row.style.minHeight = route.typeCounts.length > 0 ? '22px' : '0'
  for (const item of route.typeCounts) {
    const color = entryTypeColor(item.type)
    const chip = document.createElement('span')
    chip.className = 'lb-route-card-grid-type-count'
    chip.dataset.type = item.type
    chip.style.display = 'inline-flex'
    chip.style.alignItems = 'center'
    chip.style.gap = '5px'
    chip.style.minHeight = '20px'
    chip.style.padding = '2px 6px'
    chip.style.border = '1px solid var(--lb-border-subtle)'
    chip.style.borderRadius = 'var(--lb-radius-sm)'
    chip.style.background = 'var(--lb-bg-secondary)'
    chip.style.color = 'var(--lb-text-secondary)'
    chip.style.fontSize = 'var(--lb-font-size-label)'
    const dot = document.createElement('span')
    dot.className = 'lb-route-card-grid-type-dot'
    dot.setAttribute('aria-hidden', 'true')
    dot.style.width = '6px'
    dot.style.height = '6px'
    dot.style.borderRadius = 'var(--lb-radius-full)'
    dot.style.background = color
    chip.appendChild(dot)
    const label = document.createElement('span')
    label.className = 'lb-route-card-grid-type-label'
    label.textContent = item.type
    chip.appendChild(label)
    const count = document.createElement('span')
    count.className = 'lb-route-card-grid-type-count-value'
    count.textContent = String(item.count)
    count.style.color = 'var(--lb-text-muted)'
    count.style.fontFamily = 'var(--lb-font-mono)'
    chip.appendChild(count)
    row.appendChild(chip)
  }
  return row
}

function renderRouteCard(route: RouteCardGridItem, options: Partial<PrimitiveOptions>): HTMLElement {
  const item = document.createElement('div')
  item.className = 'lb-route-card-grid-item'
  item.setAttribute('role', 'listitem')
  const button: HTMLAnchorElement | HTMLButtonElement = route.href
    ? document.createElement('a')
    : document.createElement('button')
  if (button instanceof HTMLAnchorElement && route.href) {
    button.href = route.href
    button.style.textDecoration = 'none'
    button.setAttribute('role', 'button')
  } else if (button instanceof HTMLButtonElement) {
    button.type = 'button'
  }
  button.className = 'lb-route-card-grid-card'
  button.dataset.routePath = route.routePath
  button.setAttribute('aria-label', `Open ${route.label}, ${formatEntryCount(route.count)}`)
  button.style.display = 'flex'
  button.style.flexDirection = 'column'
  button.style.alignItems = 'stretch'
  button.style.gap = options.density === 'compact' ? '8px' : '10px'
  button.style.width = '100%'
  button.style.height = '100%'
  button.style.minHeight = options.density === 'compact' ? '116px' : '136px'
  button.style.padding = options.density === 'compact' ? '11px' : '14px'
  button.style.border = '1px solid var(--lb-border)'
  button.style.borderRadius = 'var(--lb-radius)'
  button.style.background = 'var(--lb-bg-tertiary)'
  button.style.color = 'var(--lb-text-primary)'
  button.style.font = 'inherit'
  button.style.textAlign = 'left'
  button.style.cursor = 'pointer'
  button.style.outline = 'none'

  const head = document.createElement('div')
  head.className = 'lb-route-card-grid-head'
  head.style.display = 'grid'
  head.style.gridTemplateColumns = '4px minmax(0, 1fr)'
  head.style.alignItems = 'center'
  head.style.gap = '9px'
  const rail = document.createElement('span')
  rail.className = 'lb-route-card-grid-rail'
  rail.setAttribute('aria-hidden', 'true')
  rail.style.width = '4px'
  rail.style.height = '22px'
  rail.style.borderRadius = 'var(--lb-radius-full)'
  rail.style.background = 'var(--lb-accent)'
  head.appendChild(rail)
  const title = document.createElement('span')
  title.className = 'lb-route-card-grid-title'
  title.textContent = route.label
  title.style.minWidth = '0'
  title.style.overflow = 'hidden'
  title.style.textOverflow = 'ellipsis'
  title.style.whiteSpace = 'nowrap'
  title.style.fontSize = 'var(--lb-font-size-body)'
  title.style.fontWeight = '600'
  title.style.lineHeight = '1.25'
  head.appendChild(title)
  button.appendChild(head)

  if (route.description) {
    const description = document.createElement('span')
    description.className = 'lb-route-card-grid-description'
    description.textContent = route.description
    description.style.display = '-webkit-box'
    description.style.overflow = 'hidden'
    description.style.setProperty('-webkit-line-clamp', '2')
    description.style.setProperty('-webkit-box-orient', 'vertical')
    description.style.color = 'var(--lb-text-muted)'
    description.style.fontSize = 'var(--lb-font-size-label)'
    description.style.lineHeight = '1.35'
    button.appendChild(description)
  }
  button.appendChild(renderTypeCounts(route))

  const foot = document.createElement('div')
  foot.className = 'lb-route-card-grid-foot'
  foot.style.display = 'grid'
  foot.style.gridTemplateColumns = 'minmax(0, 1fr) auto'
  foot.style.alignItems = 'center'
  foot.style.gap = '12px'
  foot.style.marginTop = 'auto'
  foot.style.paddingTop = '10px'
  foot.style.borderTop = '1px dashed var(--lb-border-subtle)'
  const stats = document.createElement('span')
  stats.className = 'lb-route-card-grid-stats'
  stats.textContent = route.statsLabel ?? formatEntryCount(route.count)
  stats.style.minWidth = '0'
  stats.style.color = 'var(--lb-text-muted)'
  stats.style.fontFamily = 'var(--lb-font-mono)'
  stats.style.fontSize = 'var(--lb-font-size-label)'
  stats.style.whiteSpace = 'normal'
  foot.appendChild(stats)
  const action = document.createElement('span')
  action.className = 'lb-route-card-grid-action'
  action.textContent = 'Open >'
  action.style.color = 'var(--lb-accent)'
  action.style.fontSize = 'var(--lb-font-size-label)'
  action.style.fontWeight = '600'
  action.style.whiteSpace = 'nowrap'
  foot.appendChild(action)
  button.appendChild(foot)

  button.addEventListener('click', ((event: MouseEvent) => {
    if (
      route.href &&
      (event.metaKey || event.ctrlKey || event.shiftKey || event.altKey || event.button !== 0)
    ) {
      return
    }
    event.preventDefault()
    emit(options, route.routePath)
  }) as EventListener)
  button.addEventListener('keydown', ((event: KeyboardEvent) => {
    if (event.key !== 'Enter' && event.key !== ' ') return
    event.preventDefault()
    emit(options, route.routePath)
  }) as EventListener)
  item.appendChild(button)
  return item
}

export function renderRouteCardGrid(
  data: RouteCardGridData,
  options: Partial<PrimitiveOptions> = {}
): HTMLElement {
  const root = document.createElement('div')
  root.className = 'lb-route-card-grid'
  if (data.routes.length === 0) {
    const empty = document.createElement('p')
    empty.className = 'lb-route-card-grid-empty'
    empty.textContent = data.emptyMessage ?? 'No routes available.'
    empty.style.margin = '0'
    empty.style.color = 'var(--lb-text-muted)'
    empty.style.fontSize = 'var(--lb-font-size-body)'
    root.appendChild(empty)
    return root
  }
  const grid = document.createElement('div')
  grid.className = 'lb-route-card-grid-list'
  grid.setAttribute('role', 'list')
  grid.style.display = 'grid'
  grid.style.gridTemplateColumns = 'repeat(auto-fit, minmax(220px, 1fr))'
  grid.style.gap = '10px'
  grid.style.minWidth = '0'
  data.routes.forEach((route) => grid.appendChild(renderRouteCard(route, options)))
  root.appendChild(grid)
  return root
}
