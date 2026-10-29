import type { PrimitiveOptions } from '../types/primitives.js'
import type { ChipStripData } from '../types/reader.js'

/**
 * chip-strip -- Sort and filter chip cluster.
 *
 * Anatomy:
 *   Rounded pill container holding 2-5 chip buttons. Active chip has
 *   amber background tint + amber text.
 *
 * Tokens:
 *   Container: --workspace-2 background, --border-subtle border, fully
 *   rounded.
 *   Inactive chip: --text-secondary.
 *   Active chip: --accent-tinted-bg bg, --accent foreground, medium weight.
 *
 * States:
 *   inactive hover -> --text-primary. Active stays active. Pressed: no
 *   shrink. Focus: --glow-focus ring.
 *
 * Density:
 *   Single size. Padding inside chips: 6px 14px.
 *
 * Content:
 *   Each chip is one or two words. Sentence case.
 *
 * A11y:
 *   role="tablist" for sort (single-select), role="group" for filter
 *   (binary/multi). Each chip is a <button> with aria-pressed.
 *
 * Motion:
 *   Background transition 150ms ease-out.
 *
 * Responsiveness:
 *   Wraps to a second row at narrow viewports.
 *
 * Composition:
 *   Tag index sort, publishers sort/filter, pages-lens sort, timeline
 *   type filter.
 *
 * Variants:
 *   role: 'sort' (single-select) | 'filter' (binary or multi). Behavior
 *   enforced by host; primitive only signals the chrome variant via role.
 *
 * Data:
 *   { value: string, options: {value: string, label: string}[], role: 'sort' | 'filter' }
 *
 * Boundaries:
 *   Only one chip-strip per logical action per surface. Never combine
 *   sort + filter into one strip.
 *
 * Anti-patterns:
 *   Never use non-amber for active state -- amber tint is the contract
 *   per chip-taxonomy section 5. Never exceed 5 chips; switch to a
 *   select.
 */

export function renderChipStrip(
  data: ChipStripData,
  _options: Partial<PrimitiveOptions> = {}
): HTMLElement {
  const root = document.createElement('div')
  root.className = `lb-chip-strip chip-strip lb-chip-strip-${data.role}`
  root.setAttribute('role', data.role === 'sort' ? 'tablist' : 'group')
  root.style.display = 'inline-flex'
  root.style.flexWrap = 'wrap'
  root.style.gap = '4px'
  root.style.padding = '4px'
  root.style.background = 'var(--workspace-2, var(--lb-bg-secondary))'
  root.style.border = '1px solid var(--border-subtle, var(--lb-border-subtle))'
  root.style.borderRadius = '9999px'
  for (const opt of data.options) {
    const active = opt.value === data.value
    const chip = document.createElement('button')
    chip.type = 'button'
    chip.className = active ? 'lb-chip-strip-chip chip active' : 'lb-chip-strip-chip chip'
    chip.dataset.value = opt.value
    chip.setAttribute('role', data.role === 'sort' ? 'tab' : 'button')
    chip.setAttribute('aria-pressed', active ? 'true' : 'false')
    chip.textContent = opt.label
    chip.style.padding = '6px 14px'
    chip.style.background = active
      ? 'var(--accent-tinted-bg, color-mix(in srgb, var(--lb-accent) 12%, transparent))'
      : 'transparent'
    chip.style.color = active
      ? 'var(--accent, var(--lb-accent))'
      : 'var(--text-secondary, var(--lb-text-secondary))'
    chip.style.border = '0'
    chip.style.borderRadius = '9999px'
    chip.style.fontSize = 'var(--fs-meta, var(--lb-font-size-small))'
    chip.style.fontWeight = active ? '500' : '400'
    chip.style.font = 'inherit'
    chip.style.cursor = 'pointer'
    chip.style.transition = 'background 150ms ease-out, color 150ms ease-out'
    root.appendChild(chip)
  }
  return root
}
