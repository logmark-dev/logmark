import type { PrimitiveOptions } from '../types/primitives.js'
import type {
  ReaderSidebarTreeData,
  ReaderSidebarTreeItem,
  ReaderSidebarTypeChip,
} from '../types/reader.js'
import { entryTypeColor } from '../util/entry-type-tokens.js'

const SOURCE = 'reader-sidebar-tree'

function emit(options: Partial<PrimitiveOptions>, type: string, target: string): void {
  options.onAction?.({ type, target, source: SOURCE })
}

function styleBaseControl(control: HTMLElement): void {
  control.style.width = '100%'
  control.style.border = '0'
  control.style.background = 'transparent'
  control.style.color = 'var(--lb-text-secondary)'
  control.style.font = 'inherit'
  control.style.textAlign = 'left'
  control.style.cursor = 'pointer'
  control.style.borderRadius = 'var(--lb-radius-sm)'
  control.style.outline = 'none'
}

function baseButton(className: string): HTMLButtonElement {
  const button = document.createElement('button')
  button.type = 'button'
  button.className = className
  styleBaseControl(button)
  return button
}

function baseAnchor(className: string, href: string): HTMLAnchorElement {
  const anchor = document.createElement('a')
  anchor.href = href
  anchor.className = className
  anchor.style.textDecoration = 'none'
  styleBaseControl(anchor)
  return anchor
}

function appendCount(root: HTMLElement, count: number | null): void {
  if (count === null) return
  const countEl = document.createElement('span')
  countEl.className = 'lb-reader-sidebar-count'
  countEl.textContent = String(count)
  countEl.style.marginLeft = 'auto'
  countEl.style.color = 'var(--lb-text-muted)'
  countEl.style.fontFamily = 'var(--lb-font-mono)'
  countEl.style.fontSize = 'var(--lb-font-size-label)'
  root.appendChild(countEl)
}

type LensItem = Extract<ReaderSidebarTreeItem, { kind: 'lens' }>
type DividerItem = Extract<ReaderSidebarTreeItem, { kind: 'divider' }>
type VaultLabelItem = Extract<ReaderSidebarTreeItem, { kind: 'vault-label' }>
type RouteItem = Extract<ReaderSidebarTreeItem, { kind: 'route' }>
type TypeStripItem = Extract<ReaderSidebarTreeItem, { kind: 'type-chip-strip' }>

function renderLens(item: LensItem, options: Partial<PrimitiveOptions>): HTMLElement {
  const button = baseButton(item.active ? 'lb-reader-sidebar-lens active' : 'lb-reader-sidebar-lens')
  button.dataset.lensKey = item.key
  button.setAttribute('aria-current', item.active ? 'page' : 'false')
  button.style.display = 'flex'
  button.style.alignItems = 'center'
  button.style.gap = '8px'
  button.style.padding = options.density === 'compact' ? '5px 8px' : '7px 10px'
  button.style.background = item.active ? 'var(--lb-bg-secondary)' : 'transparent'
  const label = document.createElement('span')
  label.className = 'lb-reader-sidebar-lens-label'
  label.textContent = item.label
  label.style.color = item.active ? 'var(--lb-text-primary)' : 'var(--lb-text-secondary)'
  button.appendChild(label)
  appendCount(button, item.count)
  button.addEventListener('click', () => emit(options, 'lens-select', item.key))
  return button
}

function renderDivider(item: DividerItem): HTMLElement {
  const divider = document.createElement('div')
  divider.id = item.id
  divider.className = 'lb-reader-sidebar-divider'
  divider.style.height = '1px'
  divider.style.margin = '8px 4px'
  divider.style.background = 'var(--lb-border-subtle)'
  return divider
}

function renderVaultLabel(item: VaultLabelItem): HTMLElement {
  const label = document.createElement('div')
  label.id = item.id
  label.className = 'lb-reader-sidebar-vault-label'
  label.textContent = item.label
  label.style.padding = '4px 8px'
  label.style.color = 'var(--lb-text-muted)'
  label.style.fontFamily = 'var(--lb-font-mono)'
  label.style.fontSize = 'var(--lb-font-size-label)'
  label.style.textTransform = 'uppercase'
  label.style.letterSpacing = '0.02em'
  return label
}

function renderRoute(item: RouteItem, options: Partial<PrimitiveOptions>): HTMLElement {
  const button: HTMLAnchorElement | HTMLButtonElement = item.href
    ? baseAnchor(item.active ? 'lb-reader-sidebar-route active' : 'lb-reader-sidebar-route', item.href)
    : baseButton(item.active ? 'lb-reader-sidebar-route active' : 'lb-reader-sidebar-route')
  button.dataset.routePath = item.routePath
  button.dataset.category = String(item.isCategory)
  button.dataset.onPath = String(Boolean(item.onPath))
  button.setAttribute('role', 'treeitem')
  button.setAttribute('aria-level', String(item.depth + 1))
  button.setAttribute('aria-current', item.active ? 'page' : 'false')
  if (item.description) button.title = item.description
  if (item.hasChildren) button.setAttribute('aria-expanded', item.expanded ? 'true' : 'false')
  button.style.display = 'flex'
  button.style.alignItems = 'center'
  button.style.gap = '7px'
  button.style.minHeight = options.density === 'compact' ? '24px' : '28px'
  button.style.padding = `${options.density === 'compact' ? 4 : 6}px 8px`
  button.style.paddingLeft = `${4 + item.depth * 16}px`
  // Additive (cockpit pass-4 D2): `elevatedActive` opts an active row into
  // the frame-Q treatment - elevated background + amber inset ("card-
  // elevated + amber inset, same as flat nav items"). The DEFAULT active
  // output stays byte-identical to the pre-option rendering (secondary
  // bg, no shadow): the live Ink reader consumes this primitive
  // (ink-reader-ia.ts via ViewerNav) and must not re-skin on a routine
  // sync-lblocks:web.
  if (item.active && item.elevatedActive) {
    button.style.background = 'var(--lb-bg-tertiary)'
    button.style.boxShadow = 'inset 2px 0 0 var(--lb-accent)'
  } else {
    button.style.background = item.active ? 'var(--lb-bg-secondary)' : 'transparent'
  }
  const chevron = document.createElement('span')
  chevron.className = 'lb-reader-sidebar-route-chevron'
  chevron.textContent = item.hasChildren ? '>' : ''
  chevron.setAttribute('aria-hidden', 'true')
  chevron.style.width = '10px'
  chevron.style.color = item.onPath ? 'var(--lb-accent)' : 'var(--lb-text-dim)'
  chevron.style.transform = item.expanded ? 'rotate(90deg)' : 'none'
  chevron.style.transition = 'transform 120ms ease'
  if (item.hasChildren) {
    // Additive (cockpit pass-4 D2): the caret toggles expand/collapse
    // independently of the row's navigate-on-click behavior below, so a
    // tree can be browsed without leaving the current route. Leaf rows
    // (no children, empty chevron text) get no listener - unchanged
    // click-to-navigate behavior.
    chevron.style.cursor = 'pointer'
    chevron.addEventListener('click', ((event: MouseEvent) => {
      event.preventDefault()
      event.stopPropagation()
      emit(options, 'route-toggle', item.routePath)
    }) as EventListener)
  }
  button.appendChild(chevron)
  const label = document.createElement('span')
  label.className = 'lb-reader-sidebar-route-label'
  label.textContent = item.label
  label.style.minWidth = '0'
  label.style.overflow = 'hidden'
  label.style.textOverflow = 'ellipsis'
  label.style.whiteSpace = 'nowrap'
  label.style.color =
    item.active || item.onPath ? 'var(--lb-text-primary)' : 'var(--lb-text-secondary)'
  button.appendChild(label)
  appendCount(button, item.count)
  button.addEventListener('click', ((event: MouseEvent) => {
    if (
      item.href &&
      (event.metaKey || event.ctrlKey || event.shiftKey || event.altKey || event.button !== 0)
    ) {
      return
    }
    event.preventDefault()
    emit(options, 'route-select', item.routePath)
  }) as EventListener)
  button.addEventListener('keydown', ((event: KeyboardEvent) => {
    if ((event.key === 'ArrowRight' || event.key === 'ArrowLeft') && item.hasChildren) {
      event.preventDefault()
      emit(options, 'route-toggle', item.routePath)
      return
    }
    if (event.key !== 'Enter' && event.key !== ' ') return
    event.preventDefault()
    emit(options, 'route-select', item.routePath)
  }) as EventListener)
  return button
}

function renderChip(chip: ReaderSidebarTypeChip, options: Partial<PrimitiveOptions>): HTMLElement {
  // Additive (cockpit pass-4 D2): `muted` forces the dot to the dim-text
  // token regardless of `type` - the degraded single-total reading.
  // Absent/false keeps the existing per-type-color output.
  const color = chip.muted ? 'var(--lb-text-dim)' : entryTypeColor(chip.type)
  const button: HTMLAnchorElement | HTMLButtonElement = chip.href
    ? document.createElement('a')
    : document.createElement('button')
  if (button instanceof HTMLAnchorElement && chip.href) {
    button.href = chip.href
    button.style.textDecoration = 'none'
    button.setAttribute('role', 'button')
  } else if (button instanceof HTMLButtonElement) {
    button.type = 'button'
  }
  button.className = chip.selected ? 'lb-reader-sidebar-type-chip selected' : 'lb-reader-sidebar-type-chip'
  button.dataset.type = chip.type
  button.setAttribute('aria-pressed', chip.selected ? 'true' : 'false')
  button.style.display = 'inline-flex'
  button.style.alignItems = 'center'
  button.style.gap = '5px'
  button.style.minHeight = '20px'
  button.style.padding = '2px 6px'
  button.style.border = chip.selected ? `1px solid ${color}` : '1px solid var(--lb-border-subtle)'
  button.style.borderRadius = 'var(--lb-radius-sm)'
  button.style.background = chip.selected
    ? `color-mix(in srgb, ${color} 16%, var(--lb-bg-secondary))`
    : 'var(--lb-bg-secondary)'
  button.style.color = chip.selected ? 'var(--lb-text-primary)' : 'var(--lb-text-secondary)'
  button.style.font = 'inherit'
  button.style.cursor = 'pointer'
  const dot = document.createElement('span')
  dot.className = 'lb-reader-sidebar-type-chip-dot'
  dot.setAttribute('aria-hidden', 'true')
  dot.style.width = '6px'
  dot.style.height = '6px'
  dot.style.borderRadius = 'var(--lb-radius-full)'
  dot.style.background = color
  button.appendChild(dot)
  const label = document.createElement('span')
  label.className = 'lb-reader-sidebar-type-chip-label'
  label.textContent = chip.label
  button.appendChild(label)
  const count = document.createElement('span')
  count.className = 'lb-reader-sidebar-type-chip-count'
  count.textContent = String(chip.count)
  count.style.color = 'var(--lb-text-muted)'
  count.style.fontFamily = 'var(--lb-font-mono)'
  button.appendChild(count)
  button.addEventListener('click', ((event: MouseEvent) => {
    if (
      chip.href &&
      (event.metaKey || event.ctrlKey || event.shiftKey || event.altKey || event.button !== 0)
    ) {
      return
    }
    event.preventDefault()
    event.stopPropagation()
    emit(options, 'type-navigate', chip.target)
  }) as EventListener)
  return button
}

function renderTypeStrip(item: TypeStripItem, options: Partial<PrimitiveOptions>): HTMLElement {
  const strip = document.createElement('div')
  strip.id = item.id
  strip.className = 'lb-reader-sidebar-type-strip'
  strip.dataset.ownerPath = item.ownerPath
  strip.setAttribute('role', 'group')
  strip.setAttribute('aria-label', `Entry types for ${item.ownerPath}`)
  strip.style.display = 'flex'
  strip.style.flexWrap = 'wrap'
  strip.style.gap = '4px'
  strip.style.padding = '3px 8px 6px 24px'
  item.chips.forEach((chip) => strip.appendChild(renderChip(chip, options)))
  return strip
}

export function renderReaderSidebarTree(
  data: ReaderSidebarTreeData,
  options: Partial<PrimitiveOptions> = {}
): HTMLElement {
  const root = document.createElement('div')
  root.className = 'lb-reader-sidebar-tree'
  root.setAttribute('role', 'tree')
  root.setAttribute('aria-label', data.label ?? 'Vault navigation')
  root.style.display = 'flex'
  root.style.flexDirection = 'column'
  root.style.gap = options.density === 'compact' ? '2px' : '4px'
  root.style.minWidth = '0'
  for (const item of data.items) {
    if (item.kind === 'lens') {
      root.appendChild(renderLens(item, options))
    } else if (item.kind === 'divider') {
      root.appendChild(renderDivider(item))
    } else if (item.kind === 'vault-label') {
      root.appendChild(renderVaultLabel(item))
    } else if (item.kind === 'route') {
      root.appendChild(renderRoute(item, options))
    } else {
      root.appendChild(renderTypeStrip(item, options))
    }
  }
  return root
}
