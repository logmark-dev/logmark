import type { PrimitiveOptions } from '../types/primitives.js'
import type { DlGlyphData } from '../types/reader.js'

/**
 * dl-glyph -- Vault archive download.
 *
 * Anatomy:
 *   sm: 20x20 button with 12x12 download arrow glyph.
 *   lg: 32-tall pill with arrow + label "Download Vault".
 *
 * Tokens:
 *   Resting: --lb-text-muted.
 *   Hover bg: --lb-accent-tinted-bg, color: --lb-accent, border: soft amber.
 *   Large: inherits the ghost-button outline.
 *
 * States:
 *   hover (full color + tinted pill), focus (--lb-glow-focus outer ring),
 *   pressed (opacity 0.85, no shrink).
 *
 * Density:
 *   Two sizes only: sm (default, inline beside vault names) and lg
 *   (vault reader header). No intermediate sizes.
 *
 * Content:
 *   Tooltip: "Download vault (.logmark) -- open in the desktop
 *   app to subscribe." Same copy in both variants. lg also renders the
 *   label "Download Vault" inline.
 *
 * A11y:
 *   Both variants render as <button> with explicit aria-label keyed to
 *   the vault name. Tooltip via title attribute. lg has visible label.
 *
 * Motion:
 *   150ms ease-out on hover. No motion on rest.
 *
 * Responsiveness:
 *   sm stays inline with vault name on all viewports.
 *   lg drops below the tag row on narrow viewports (handled by parent).
 *
 * Composition:
 *   sm rendered inside attribution-chip when archive is available.
 *   lg used standalone on vault reader header.
 *
 * Variants:
 *   size: 'sm' (default) | 'lg'.
 *
 * Data:
 *   { vault: string, size?: 'sm' | 'lg', archiveUrl?: string | null }
 *
 * Boundaries:
 *   Never duplicate sm + lg on the same surface. Reader header carries
 *   lg; its attribution-chip omits the embedded glyph.
 *
 * Anti-patterns:
 *   No color encoding by vault size or recency. Same affordance always.
 *   Never point the arrow up. Never style as a primary call-to-action --
 *   this is a discoverable secondary affordance, not the hero CTA.
 */

const TOOLTIP =
  'Download vault (.logmark) -- open in the desktop app to subscribe.'

const ARROW_SVG =
  '<svg viewBox="0 0 12 12" width="12" height="12" aria-hidden="true" focusable="false">' +
  '<path d="M6 1v8m0 0L2 5m4 4l4-4M1 11h10" fill="none" stroke="currentColor" stroke-width="1.5"/>' +
  '</svg>'

export function renderDlGlyph(
  data: DlGlyphData,
  _options: Partial<PrimitiveOptions> = {}
): HTMLElement {
  const size = data.size ?? 'sm'
  const button = document.createElement('button')
  button.type = 'button'
  button.className = `lb-dl-glyph lb-dl-glyph-${size}`
  button.dataset.vault = data.vault
  if (data.archiveUrl) {
    button.dataset.archiveUrl = data.archiveUrl
  }
  button.setAttribute('aria-label', `Download ${data.vault} as LogMark vault`)
  button.title = TOOLTIP
  button.style.display = 'inline-flex'
  button.style.alignItems = 'center'
  button.style.justifyContent = 'center'
  button.style.gap = size === 'lg' ? '8px' : '0'
  button.style.background = 'transparent'
  button.style.color = 'var(--lb-text-muted)'
  button.style.border = '1px solid transparent'
  button.style.borderRadius = 'var(--lb-radius-sm)'
  button.style.cursor = 'pointer'
  button.style.font = 'inherit'
  button.style.transition =
    'color 150ms ease-out, background 150ms ease-out, border-color 150ms ease-out'
  if (size === 'sm') {
    button.style.width = '20px'
    button.style.height = '20px'
    button.style.padding = '4px'
    button.innerHTML = ARROW_SVG
  } else {
    button.style.height = '32px'
    button.style.padding = '0 14px'
    button.style.fontSize = 'var(--lb-font-size-small)'
    button.style.fontWeight = '500'
    button.innerHTML = `${ARROW_SVG}<span class="lb-dl-glyph-label">Download Vault</span>`
  }
  return button
}
