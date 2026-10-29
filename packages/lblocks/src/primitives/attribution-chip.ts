import type { PrimitiveOptions } from '../types/primitives.js'
import type { AttributionChipData } from '../types/reader.js'

/**
 * attribution-chip -- Provenance line for a publisher (and optional vault).
 *
 * Anatomy:
 *   Handle (amber link) - separator slash - vault title (secondary link) -
 *   optional download glyph - optional AGENTIC kind pill - optional cadence.
 *   At the lblocks runtime level this primitive renders the kind pill +
 *   cadence; the handle/vault/separator/dl-glyph are composed by host
 *   surfaces (which use renderDlGlyph next to it).
 *
 * Tokens:
 *   Kind pill: --lb-accent-tinted-bg background, --lb-accent foreground,
 *   soft amber border. Cadence chip: neutral --lb-text-secondary, no tint.
 *
 * States:
 *   Static. No hover/active on the chip itself; both handle and vault
 *   are real links rendered by the host surface.
 *
 * Density:
 *   One size. Mono pill is 10px caps; cadence is 10px medium. No sm/lg
 *   variants -- if it doesn't fit, the surface is wrong.
 *
 * Content:
 *   Pill label defaults to "AGENTIC" for kind === 'agent' when showKind.
 *   The HUMAN default label is dropped per HANDOFF; humans render no
 *   kind pill unless explicit data.label is provided.
 *
 * A11y:
 *   role="img" on the wrapper with an aria-label describing the publisher
 *   kind. Pill text is decorative once the aria-label conveys the same.
 *
 * Motion:
 *   None. The chip itself does not animate.
 *
 * Responsiveness:
 *   Flex-inline, wraps. The chip is small enough to fit alongside the
 *   handle and vault links it sits between.
 *
 * Composition:
 *   Entry deep-link header, page deep-link header, vault reader header
 *   (with showDownload=false -- the header carries the large dl-glyph),
 *   topic-mini foot, publisher profile, publishers card, entry card meta,
 *   OG card footer.
 *
 * Variants:
 *   showKind: false (default) -- no kind pill, just provenance.
 *   showKind: true -- AGENTIC pill emerges for kind === 'agent' (only).
 *   HUMAN pill is dropped; humans get no pill ever (the absence is the
 *   signal). Explicit data.label overrides the default text when shown.
 *
 * Data:
 *   { kind: 'agent' | 'human', cadence?: string | null,
 *     label?: string | null, showKind?: boolean }
 *
 * Boundaries:
 *   When the surface is about the publisher (creator profile header,
 *   publishers card), use the larger avatar + handle composition; this
 *   chip is for inline use beside content.
 *
 * Anti-patterns:
 *   No background fill on the wrapper; it's text. No bold weight for
 *   emphasis. Never show the AGENTIC pill on entry rows -- it's noise
 *   at row level (V13 single-affordance polish rule).
 */

const KIND_BG: Record<'agent', string> = {
  agent: 'color-mix(in srgb, var(--lb-accent) 12%, transparent)',
}

const KIND_BORDER: Record<'agent', string> = {
  agent: 'color-mix(in srgb, var(--lb-accent) 30%, transparent)',
}

const KIND_COLOR: Record<'agent', string> = {
  agent: 'var(--lb-accent)',
}

export function renderAttributionChip(
  data: AttributionChipData,
  _options: Partial<PrimitiveOptions> = {}
): HTMLElement {
  const root = document.createElement('span')
  root.className = `lb-attribution-chip lb-attribution-chip-${data.kind}`
  root.dataset.kind = data.kind
  root.setAttribute('role', 'img')
  const showAgenticPill = data.showKind === true && data.kind === 'agent'
  const pillLabel = data.label ?? (showAgenticPill ? 'AGENTIC' : null)
  root.setAttribute(
    'aria-label',
    pillLabel ? `${pillLabel} publisher` : `${data.kind} publisher`
  )
  root.style.display = 'inline-flex'
  root.style.alignItems = 'center'
  root.style.gap = '4px'

  // Kind pill -- only renders for showKind && kind === 'agent', OR when an
  // explicit label is provided (allowing host surfaces to surface custom
  // tone). HUMAN default pill is dropped per HANDOFF.
  if ((showAgenticPill || data.label != null) && data.kind === 'agent') {
    const pill = document.createElement('span')
    pill.className = 'lb-attribution-chip-pill'
    pill.style.display = 'inline-flex'
    pill.style.alignItems = 'center'
    pill.style.padding = '1px 6px'
    pill.style.borderRadius = '9999px'
    pill.style.border = `1px solid ${KIND_BORDER.agent}`
    pill.style.background = KIND_BG.agent

    const label = document.createElement('span')
    label.className = 'lb-attribution-chip-label'
    label.textContent = data.label ?? 'AGENTIC'
    label.style.fontSize = '10px'
    label.style.fontWeight = '700'
    label.style.letterSpacing = '0.06em'
    label.style.color = KIND_COLOR.agent
    pill.appendChild(label)
    root.appendChild(pill)
  }

  // Cadence chip -- chip-taxonomy section 7: neutral only, no tint.
  if (data.cadence != null) {
    const cadence = document.createElement('span')
    cadence.className = 'lb-attribution-chip-cadence'
    cadence.textContent = data.cadence
    cadence.style.fontSize = '10px'
    cadence.style.fontWeight = '500'
    cadence.style.letterSpacing = '0.02em'
    cadence.style.color = 'var(--lb-text-secondary)'
    cadence.style.padding = '1px 5px'
    cadence.style.border = '1px solid var(--lb-border)'
    cadence.style.borderRadius = 'var(--lb-radius)'
    cadence.style.background = 'transparent'
    root.appendChild(cadence)
  }

  return root
}
