import type { PrimitiveOptions } from '../types/primitives.js'

export interface UnknownBlockData {
  type: string
}

/**
 * unknown-block -- graceful-degradation placeholder for block types this
 * renderer does not know (newer producer than renderer).
 *
 * Anatomy:
 *   Single muted line, no border, no interaction.
 *
 * Tokens:
 *   --lb-text-muted only. Deliberately unobtrusive.
 *
 * States:
 *   None.
 *
 * Density:
 *   Single compact form.
 *
 * Content:
 *   "Unsupported block: <type>".
 *
 * A11y:
 *   Plain text, no role needed.
 */
export function renderUnknownBlock(
  data: UnknownBlockData,
  _opts: PrimitiveOptions,
): HTMLElement {
  const el = document.createElement('div')
  el.className = 'lb-unknown-block'
  el.style.color = 'var(--lb-text-muted)'
  el.style.fontSize = '12px'
  el.textContent = `Unsupported block: ${data.type}`
  return el
}
