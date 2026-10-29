import { describe, expect, it, vi } from 'vitest'
import { renderReaderSidebarTree } from '../../src/primitives/reader-sidebar-tree.js'
import type { BlockAction } from '../../src/types/actions.js'

const baseRouteItem = {
  kind: 'route' as const,
  id: 'r1',
  label: 'Test Route',
  routePath: '/test',
  depth: 0,
  count: 5,
  hasChildren: false,
  isCategory: false,
  expanded: false,
  active: false,
  onPath: false,
}

describe('renderReaderSidebarTree', () => {
  it('renders route treeitem with title attribute when description is provided', () => {
    const root = renderReaderSidebarTree({
      items: [{ ...baseRouteItem, description: 'Grid stress and power' }],
    })
    const treeitem = root.querySelector('[role="treeitem"]')
    expect(treeitem).toBeTruthy()
    expect(treeitem!.getAttribute('title')).toBe('Grid stress and power')
  })

  it('renders route treeitem with no title attribute when description is absent', () => {
    const root = renderReaderSidebarTree({
      items: [{ ...baseRouteItem }],
    })
    const treeitem = root.querySelector('[role="treeitem"]')
    expect(treeitem).toBeTruthy()
    expect(treeitem!.hasAttribute('title')).toBe(false)
  })

  // Cockpit pass-4 D2: `elevatedActive` opts an active row into the
  // frame-Q treatment. The DEFAULT active output must stay byte-identical
  // to the pre-option rendering - the live Ink reader (ink-reader-ia.ts
  // via ViewerNav) consumes this primitive with `active` set and no new
  // options, and must not re-skin on a routine sync-lblocks:web.
  describe('active-node treatment (frame Q, D2, opt-in)', () => {
    it('active + elevatedActive gets the elevated background + amber inset shadow', () => {
      const root = renderReaderSidebarTree({
        items: [{ ...baseRouteItem, active: true, elevatedActive: true }],
      })
      const treeitem = root.querySelector<HTMLElement>('[role="treeitem"]')!
      expect(treeitem.style.background).toBe('var(--lb-bg-tertiary)')
      expect(treeitem.style.boxShadow).toBe('inset 2px 0 0 var(--lb-accent)')
    })

    it('active without the option keeps the pre-option treatment (secondary bg, no shadow)', () => {
      const root = renderReaderSidebarTree({ items: [{ ...baseRouteItem, active: true }] })
      const treeitem = root.querySelector<HTMLElement>('[role="treeitem"]')!
      expect(treeitem.style.background).toBe('var(--lb-bg-secondary)')
      expect(treeitem.style.boxShadow).toBe('')
    })

    it('elevatedActive on an inactive row does nothing', () => {
      const root = renderReaderSidebarTree({
        items: [{ ...baseRouteItem, active: false, elevatedActive: true }],
      })
      const treeitem = root.querySelector<HTMLElement>('[role="treeitem"]')!
      expect(treeitem.style.background).toBe('transparent')
      expect(treeitem.style.boxShadow).toBe('')
    })

    // Byte-identity guards, captured from the pre-change implementation
    // (commit f759711) with exactly the item data below. Any diff here is
    // an Ink-facing regression, not a snapshot to refresh casually. Leaf
    // items (hasChildren: false) are used so the guards pin the
    // active-node treatment specifically - the caret cursor/listener
    // addition on folder rows is a separately reviewed additive change.
    it('active leaf with no new options renders byte-identical to the pre-change output', () => {
      const BASELINE_ACTIVE_LEAF =
        '<div class="lb-reader-sidebar-tree" role="tree" aria-label="Vault navigation" style="display: flex; flex-direction: column; gap: 4px; min-width: 0;"><button type="button" class="lb-reader-sidebar-route active" style="width: 100%; border: 0px; background: var(--lb-bg-secondary); color: var(--lb-text-secondary); text-align: left; cursor: pointer; border-radius: var(--lb-radius-sm); outline: none; display: flex; align-items: center; gap: 7px; min-height: 28px; padding: 6px 8px 6px 4px;" data-route-path="/test" data-category="false" data-on-path="false" role="treeitem" aria-level="1" aria-current="page"><span class="lb-reader-sidebar-route-chevron" aria-hidden="true" style="width: 10px; color: var(--lb-text-dim); transform: none; transition: transform 120ms ease;"></span><span class="lb-reader-sidebar-route-label" style="min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; color: var(--lb-text-primary);">Test Route</span><span class="lb-reader-sidebar-count" style="margin-left: auto; color: var(--lb-text-muted); font-family: var(--lb-font-mono); font-size: var(--lb-font-size-label);">5</span></button></div>'
      const root = renderReaderSidebarTree({ items: [{ ...baseRouteItem, active: true }] })
      expect(root.outerHTML).toBe(BASELINE_ACTIVE_LEAF)
    })

    it('inactive leaf with no new options renders byte-identical to the pre-change output', () => {
      const BASELINE_INACTIVE_LEAF =
        '<div class="lb-reader-sidebar-tree" role="tree" aria-label="Vault navigation" style="display: flex; flex-direction: column; gap: 4px; min-width: 0;"><button type="button" class="lb-reader-sidebar-route" style="width: 100%; border: 0px; color: var(--lb-text-secondary); text-align: left; cursor: pointer; border-radius: var(--lb-radius-sm); outline: none; display: flex; align-items: center; gap: 7px; min-height: 28px; padding: 6px 8px 6px 4px; background: transparent;" data-route-path="/test" data-category="false" data-on-path="false" role="treeitem" aria-level="1" aria-current="false"><span class="lb-reader-sidebar-route-chevron" aria-hidden="true" style="width: 10px; color: var(--lb-text-dim); transform: none; transition: transform 120ms ease;"></span><span class="lb-reader-sidebar-route-label" style="min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; color: var(--lb-text-secondary);">Test Route</span><span class="lb-reader-sidebar-count" style="margin-left: auto; color: var(--lb-text-muted); font-family: var(--lb-font-mono); font-size: var(--lb-font-size-label);">5</span></button></div>'
      const root = renderReaderSidebarTree({ items: [{ ...baseRouteItem }] })
      expect(root.outerHTML).toBe(BASELINE_INACTIVE_LEAF)
    })
  })

  // Cockpit pass-4 D2: the caret toggles expand/collapse independently of
  // the row's navigate-on-click behavior, so a folder can be browsed
  // without leaving the current route.
  describe('caret toggle (frame Q, D2)', () => {
    it('clicking the caret on a folder emits route-toggle, not route-select', () => {
      const actions: BlockAction[] = []
      const root = renderReaderSidebarTree(
        { items: [{ ...baseRouteItem, hasChildren: true, isCategory: true }] },
        { onAction: (a) => actions.push(a) }
      )
      const caret = root.querySelector<HTMLElement>('.lb-reader-sidebar-route-chevron')!
      caret.dispatchEvent(new MouseEvent('click', { bubbles: true }))
      expect(actions).toEqual([{ type: 'route-toggle', target: '/test', source: 'reader-sidebar-tree' }])
    })

    it('clicking the row body still navigates (route-select)', () => {
      const actions: BlockAction[] = []
      const root = renderReaderSidebarTree(
        { items: [{ ...baseRouteItem, hasChildren: true, isCategory: true }] },
        { onAction: (a) => actions.push(a) }
      )
      const treeitem = root.querySelector<HTMLElement>('[role="treeitem"]')!
      treeitem.dispatchEvent(new MouseEvent('click', { bubbles: true }))
      expect(actions).toEqual([{ type: 'route-select', target: '/test', source: 'reader-sidebar-tree' }])
    })

    it('a leaf (no children) has no clickable caret target', () => {
      const root = renderReaderSidebarTree({ items: [{ ...baseRouteItem, hasChildren: false }] })
      const caret = root.querySelector<HTMLElement>('.lb-reader-sidebar-route-chevron')!
      expect(caret.style.cursor).not.toBe('pointer')
    })
  })

  // Cockpit pass-4 D2: `muted` chips are the degraded-tier single-total
  // reading when a route's byType breakdown is unavailable.
  describe('type-chip-strip muted dot (frame Q, D2)', () => {
    it('muted chip dots render on the dim-text token regardless of type', () => {
      const root = renderReaderSidebarTree({
        items: [
          {
            kind: 'type-chip-strip',
            id: 'strip1',
            ownerPath: '/test',
            chips: [{ type: 'task', label: '', count: 12, muted: true, target: '/test' }],
          },
        ],
      })
      const dot = root.querySelector<HTMLElement>('.lb-reader-sidebar-type-chip-dot')!
      expect(dot.style.background).toBe('var(--lb-text-dim)')
    })

    it('non-muted chip dots keep the per-type color (absent-option baseline)', () => {
      const root = renderReaderSidebarTree({
        items: [
          {
            kind: 'type-chip-strip',
            id: 'strip1',
            ownerPath: '/test',
            chips: [{ type: 'task', label: '', count: 12, target: 'task' }],
          },
        ],
      })
      const dot = root.querySelector<HTMLElement>('.lb-reader-sidebar-type-chip-dot')!
      expect(dot.style.background).toBe('var(--lb-entry-task)')
    })
  })
})
