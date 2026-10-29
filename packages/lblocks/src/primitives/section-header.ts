import type { SectionHeaderData } from '../types/primitives.js'

/**
 * section-header (Tier 2) -- Eyebrow + title pair with optional right-side link.
 *
 * Role: Open a logical region inside a surface (front-door shelves,
 *   tag-page Related sidebar, profile Publications).
 * When to use: Above any grouped band on a surface where the surrounding
 *   context already provides the page-level h1.
 * Anti-pattern: Never as the page-level h1 -- the page header is its
 *   own composition.
 */
export function renderSectionHeader(data: SectionHeaderData): HTMLElement {
  const el = document.createElement('div')
  el.className = 'lb-section-header'
  el.textContent = data.label
  el.style.fontSize = 'var(--lb-font-size-label)'
  el.style.color = 'var(--lb-accent)'
  el.style.textTransform = 'uppercase'
  el.style.letterSpacing = '0.02em'
  el.style.marginBottom = '8px'
  return el
}
