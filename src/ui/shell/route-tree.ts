import { renderReaderSidebarTree } from '@logmark/lblocks'
import type { ReaderSidebarTreeItem, ReaderSidebarTypeChip } from '@logmark/lblocks'
import type { RouteBreakdown } from '../../types/vault.js'
import { navigate } from '../lib/nav.js'
import { normalizeRoutePath } from '../models/normalizers.js'
import { ENTRY_TYPES } from '../views/lens-types.js'

/**
 * Sidebar vault tree (design pass v4 frame Q, D2). Renders the Routes
 * group as a collapsible, depth-capped folder tree with per-folder
 * type-dot counts, replacing the flat top-5 route list when the adapter
 * exposes the full `VaultSummary.routes` breakdown. See `buildRouteTree`
 * for the model and `renderRouteTree` for the DOM (through the lblocks
 * `reader-sidebar-tree` primitive).
 */
export interface RouteTreeNode {
  label: string
  path: string
  count: number
  byType?: Record<string, number>
  children: RouteTreeNode[]
}

interface Level2Acc {
  label: string
  path: string
  count: number
  byType: Record<string, number>
  hasByType: boolean
}

interface Level1Acc {
  label: string
  path: string
  count: number
  byType: Record<string, number>
  hasByType: boolean
  childOrder: string[]
  children: Map<string, Level2Acc>
}

function mergeByType(target: Record<string, number>, source?: Record<string, number>): void {
  if (!source) return
  for (const [type, count] of Object.entries(source)) {
    target[type] = (target[type] ?? 0) + count
  }
}

function segmentsFor(path: string): string[] {
  return normalizeRoutePath(path)
    .split('/')
    .map((segment) => segment.trim())
    .filter(Boolean)
}

/**
 * Builds the sidebar vault tree from the adapter's full route breakdown.
 * Depth is capped at 2 (frame Q, D2 - a steering aid, not a file browser):
 * a route's third-and-deeper path segments fold into its level-2
 * ancestor's count/byType rather than nesting a third level. A
 * single-segment route (no `/`) becomes a childless level-1 leaf. When a
 * level-1 path also carries entries of its own (rare - routes are usually
 * either a category prefix or a leaf, not both), those entries roll into
 * the level-1 folder's total without creating a duplicate child row.
 */
export function buildRouteTree(routes: RouteBreakdown[]): RouteTreeNode[] {
  const level1Order: string[] = []
  const level1 = new Map<string, Level1Acc>()

  for (const route of routes) {
    const segments = segmentsFor(route.path)
    if (segments.length === 0) continue

    const key1 = segments[0]
    let acc1 = level1.get(key1)
    if (!acc1) {
      acc1 = { label: key1, path: key1, count: 0, byType: {}, hasByType: false, childOrder: [], children: new Map() }
      level1.set(key1, acc1)
      level1Order.push(key1)
    }
    acc1.count += route.count
    mergeByType(acc1.byType, route.by_type)
    if (route.by_type) acc1.hasByType = true

    if (segments.length === 1) {
      if (route.name) acc1.label = route.name
      continue
    }

    const key2 = segments[1]
    const path2 = `${key1}/${key2}`
    let acc2 = acc1.children.get(key2)
    if (!acc2) {
      acc2 = { label: key2, path: path2, count: 0, byType: {}, hasByType: false }
      acc1.children.set(key2, acc2)
      acc1.childOrder.push(key2)
    }
    acc2.count += route.count
    mergeByType(acc2.byType, route.by_type)
    if (route.by_type) acc2.hasByType = true
    // Depth cap: a route with exactly 2 segments names the level-2 node
    // directly; a route with 3+ segments silently folds into the same
    // level-2 accumulator (no third-level node is ever produced).
    if (segments.length === 2 && route.name) acc2.label = route.name
  }

  return level1Order.map((key1) => {
    const acc1 = level1.get(key1)!
    const children: RouteTreeNode[] = acc1.childOrder.map((key2) => {
      const acc2 = acc1.children.get(key2)!
      return {
        label: acc2.label,
        path: acc2.path,
        count: acc2.count,
        ...(acc2.hasByType ? { byType: acc2.byType } : {}),
        children: [],
      }
    })
    return {
      label: acc1.label,
      path: acc1.path,
      count: acc1.count,
      // A folder with children never shows its own type-dot row (only
      // leaves do, per frame Q) - only carry byType through when this
      // level-1 node is itself the leaf (no children).
      ...(children.length === 0 && acc1.hasByType ? { byType: acc1.byType } : {}),
      children,
    }
  })
}

const STORAGE_KEY = 'ck-tree-expansion'

function readExpansion(): Set<string> {
  const raw = sessionStorage.getItem(STORAGE_KEY)
  if (!raw) return new Set()
  try {
    const parsed = JSON.parse(raw)
    return Array.isArray(parsed) ? new Set(parsed.filter((x) => typeof x === 'string')) : new Set()
  } catch {
    return new Set()
  }
}

function writeExpansion(expanded: Set<string>): void {
  sessionStorage.setItem(STORAGE_KEY, JSON.stringify([...expanded]))
}

/**
 * `collapse all` (frame Q): resets to top-level folders only. Writes an
 * explicit empty set rather than clearing the key outright - an absent
 * key means "fresh session, auto-reveal the active leaf's ancestor";
 * `collapse all` is a deliberate user action and must not be silently
 * re-opened by that same auto-reveal on the next render.
 */
export function collapseAllRouteTree(): void {
  writeExpansion(new Set())
}

function hrefFor(path: string): string {
  return `/routes/${encodeURIComponent(path)}`
}

/** 2-3 dominant types with counts (frame Q anatomy), dot + count only - no
 * text label, matching the mock. Degraded (no byType): a single muted
 * total dot; the tree still shows folder shape. */
function typeChips(node: RouteTreeNode): ReaderSidebarTypeChip[] {
  if (!node.byType) {
    return [{ type: 'log', label: '', count: node.count, muted: true, target: node.path }]
  }
  return Object.entries(node.byType)
    .filter(([, count]) => count > 0)
    .sort((a, b) => b[1] - a[1])
    .slice(0, 3)
    .map(([type, count]) => ({ type, label: '', count, target: type }))
}

/**
 * Renders the Routes group's tree body into `container` (the group label
 * with the `collapse all` action stays in app-shell.ts). Re-invokes itself
 * on every toggle/navigation so expansion state (session-persisted) and
 * the active node stay in sync without the caller re-driving it.
 */
export function renderRouteTree(container: HTMLElement, nodes: RouteTreeNode[], pathname: string): void {
  // Only a truly fresh session (no persisted expansion at all yet) gets
  // the active leaf's ancestor auto-revealed - once the user has touched
  // expansion state (a manual toggle, or an explicit `collapse all`),
  // that persisted state is respected exactly, so `collapse all` really
  // does collapse everything rather than being silently re-opened by
  // whatever route happens to be active.
  const hasStoredState = sessionStorage.getItem(STORAGE_KEY) !== null
  const expanded = readExpansion()

  // `onPath` (accent hint on a closed folder's caret/label - "the active
  // route lives in here") is computed regardless of expansion so it still
  // surfaces after an explicit collapse.
  const onPathSet = new Set<string>()
  for (const node of nodes) {
    if (node.children.some((child) => hrefFor(child.path) === pathname)) {
      onPathSet.add(node.path)
      if (!hasStoredState) expanded.add(node.path)
    }
  }

  const items: ReaderSidebarTreeItem[] = []
  let seq = 0

  for (const node of nodes) {
    const hasChildren = node.children.length > 0
    const isOpen = hasChildren && expanded.has(node.path)
    const isActive = hrefFor(node.path) === pathname
    items.push({
      kind: 'route',
      id: `rt-${seq++}`,
      label: node.label,
      routePath: node.path,
      depth: 0,
      count: node.count,
      hasChildren,
      isCategory: hasChildren,
      expanded: isOpen,
      active: isActive,
      // Cockpit opts into the frame-Q active treatment (card-elevated +
      // amber inset). The primitive default stays the pre-option output
      // because the live Ink reader shares this primitive.
      elevatedActive: true,
      onPath: onPathSet.has(node.path),
      href: hrefFor(node.path),
    })
    if (!hasChildren && isActive) {
      items.push({ kind: 'type-chip-strip', id: `rt-${seq++}`, ownerPath: node.path, chips: typeChips(node) })
    }
    if (isOpen) {
      for (const child of node.children) {
        const childActive = hrefFor(child.path) === pathname
        items.push({
          kind: 'route',
          id: `rt-${seq++}`,
          label: child.label,
          routePath: child.path,
          depth: 1,
          count: child.count,
          hasChildren: false,
          isCategory: false,
          active: childActive,
          elevatedActive: true,
          href: hrefFor(child.path),
        })
        if (childActive) {
          items.push({ kind: 'type-chip-strip', id: `rt-${seq++}`, ownerPath: child.path, chips: typeChips(child) })
        }
      }
    }
  }

  const tree = renderReaderSidebarTree(
    { label: 'Routes', items },
    {
      density: 'compact',
      onAction: (action) => {
        if (action.type === 'route-select') {
          navigate(hrefFor(action.target))
          return
        }
        if (action.type === 'route-toggle') {
          const next = new Set(expanded)
          if (next.has(action.target)) next.delete(action.target)
          else next.add(action.target)
          writeExpansion(next)
          renderRouteTree(container, nodes, pathname)
          return
        }
        // Broadened to the full canonical type set (not just the sidebar's
        // ratify-relevant four) so a tree type-dot for `log`/`idea`/`gap`/
        // `analogy` navigates identically to the root at-a-glance chips.
        if (action.type === 'type-navigate' && (ENTRY_TYPES as readonly string[]).includes(action.target)) {
          navigate(`/lens/${action.target}`)
        }
      },
    },
  )
  container.replaceChildren(tree)
}
