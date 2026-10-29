import type { PrimitiveOptions } from '../types/primitives.js'
import type { LensTabsData } from '../types/reader.js'

/**
 * lens-tab -- The V2 active-state primitive. Vault-reader lens tabs.
 *
 * Anatomy:
 *   Horizontal row of tab buttons. Active tab carries a 2px amber rail
 *   indicator at the bottom edge with a soft amber glow (polish rule 2
 *   exemption: rail is bounded to 2px-3px).
 *
 * Tokens:
 *   Inactive: --text-secondary (Ink palette).
 *   Active: --text-primary.
 *   Indicator: 2px --accent rail with soft amber glow.
 *
 * States:
 *   inactive (default), hover (--text-primary), active (rail visible),
 *   focus (--glow-focus ring), disabled (opacity 0.5, pointer-events none).
 *
 * Density:
 *   Single height. Padding 10px 14px. No compact variant.
 *
 * Content:
 *   Labels are single words: Summary, Timeline, Highlights, Tags, Pages.
 *   Sentence case.
 *
 * A11y:
 *   role="tablist" on container, role="tab" on each, aria-selected on
 *   active. Arrow keys move focus left/right (delegated to host code).
 *
 * Motion:
 *   200ms ease-out on the rail indicator transition between tabs.
 *
 * Responsiveness:
 *   Horizontal scroll within the container if needed. Tabs never wrap.
 *
 * Composition:
 *   Vault reader. Embed reader inherits.
 *
 * Variants:
 *   None. Amber rail is the V2 codified default (3px-accent exemption
 *   from polish rule 2). Neutral and pill variants from the kit are
 *   stripped per master design decision 3.
 *
 * Data:
 *   { value: string, options: string[] }
 *
 * Boundaries:
 *   Don't use for navigation between unrelated surfaces -- that's the
 *   topbar. Lens tabs change WHAT inside a vault is shown.
 *
 * Anti-patterns:
 *   Never exceed 2px on the amber rail. Never have more than 7 tabs.
 *   Never use as primary site navigation.
 */

export function renderLensTabs(
  data: LensTabsData,
  _options: Partial<PrimitiveOptions> = {}
): HTMLElement {
  const root = document.createElement('div')
  root.className = 'lb-lens-tabs lens-tabs'
  root.setAttribute('role', 'tablist')
  root.style.display = 'flex'
  root.style.gap = '0'
  root.style.alignItems = 'flex-end'
  root.style.borderBottom = '1px solid var(--border, var(--lb-border))'
  for (const label of data.options) {
    const value = label.toLowerCase()
    const active = value === data.value.toLowerCase()
    const tab = document.createElement('button')
    tab.type = 'button'
    tab.className = active ? 'lb-lens-tab lens-tab active' : 'lb-lens-tab lens-tab'
    tab.dataset.value = value
    tab.setAttribute('role', 'tab')
    tab.setAttribute('aria-selected', active ? 'true' : 'false')
    tab.textContent = label
    tab.style.position = 'relative'
    tab.style.padding = '10px 14px'
    tab.style.background = 'transparent'
    tab.style.border = '0'
    tab.style.color = active
      ? 'var(--text-primary, var(--lb-text-primary))'
      : 'var(--text-secondary, var(--lb-text-secondary))'
    tab.style.fontSize = 'var(--fs-meta, var(--lb-font-size-small))'
    tab.style.fontWeight = '500'
    tab.style.cursor = 'pointer'
    tab.style.font = 'inherit'
    root.appendChild(tab)
  }
  return root
}
