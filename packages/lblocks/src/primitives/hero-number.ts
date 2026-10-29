import type { PrimitiveOptions } from '../types/primitives.js'
import type { HeroNumberData } from '../types/reader.js'

const SIZE_VALUE_FONT: Record<NonNullable<HeroNumberData['size']>, string> = {
  sm: 'var(--lb-font-size-body)',
  md: '28px',
  lg: '40px',
}

const SIZE_LABEL_FONT: Record<NonNullable<HeroNumberData['size']>, string> = {
  sm: 'var(--lb-font-size-small)',
  md: 'var(--lb-font-size-small)',
  lg: 'var(--lb-font-size-body)',
}

const DELTA_TONE_COLOR: Record<NonNullable<HeroNumberData['deltaTone']>, string> = {
  positive: 'var(--lb-signal-positive)',
  negative: 'var(--lb-signal-alert)',
  neutral: 'var(--lb-text-secondary)',
}

/**
 * hero-number -- A single page-level numeric. The headline of a stat cluster.
 *
 * Anatomy:
 *   Numeral (display weight) over a label (uppercase eyebrow). When
 *   clustered, four numerals sit on a grid-template-columns: repeat(4, 1fr).
 *
 * Tokens:
 *   Numeral: --lb-text-primary by default. Label: --lb-text-secondary.
 *   Delta tone: positive --lb-signal-positive, negative/warning --lb-signal-alert,
 *   neutral --lb-text-secondary. Numeral always uses font-variant-numeric:
 *   tabular-nums.
 *
 * States:
 *   Static. No hover state -- these are not interactive.
 *
 * Density:
 *   Default size is md. lg = larger numeral (40px). sm = inline body size.
 *   Use lg for the tag-page atlas hero and the front-door hero. Use md
 *   everywhere else.
 *
 * Content:
 *   Numeral is a single integer or short string. Labels are 1-3 words,
 *   rendered as uppercase eyebrow with letter-spacing 0.04em. No units
 *   in the label unless they carry weight.
 *
 * A11y:
 *   The numeral and label sit in the same parent so screen readers read
 *   "47, entries" not "47 entries number".
 *
 * Motion:
 *   None. If incoming count changes, host code should use a 220ms ease-out
 *   crossfade -- never count-up animation, never bouncing.
 *
 * Responsiveness:
 *   Cluster CSS (.hero-num-cluster) collapses 4-up to 2-up at 960px and
 *   stacks at 600px. The primitive itself does not change.
 *
 * Composition:
 *   Tag-page hero (4-up cluster), creator profile stat row, vault reader
 *   summary lens, tag-index card.
 *
 * Variants:
 *   size: 'sm' | 'md' (default) | 'lg'.
 *   deltaTone: 'positive' | 'negative' | 'neutral'.
 *
 * Data:
 *   { value: string | number, label: string, delta?: string | null,
 *     deltaTone?: 'positive' | 'negative' | 'neutral',
 *     size?: 'sm' | 'md' | 'lg' }
 *
 * Boundaries:
 *   When the number is incidental (inline next to body text, count chips),
 *   use plain mono numerals. Hero is reserved for numbers that own a region.
 *
 * Anti-patterns:
 *   Never put more than four numerals in a single cluster. Never use color
 *   to encode value direction here -- that's delta territory and lives in
 *   the delta line below the numeral.
 */
export function renderHeroNumber(
  data: HeroNumberData,
  _options: Partial<PrimitiveOptions> = {}
): HTMLElement {
  const size = data.size ?? 'md'
  const root = document.createElement('div')
  root.className = `lb-hero-number lb-hero-number-${size}`
  root.style.display = 'inline-flex'
  root.style.flexDirection = 'column'
  root.style.alignItems = 'flex-start'
  root.style.gap = '2px'

  const value = document.createElement('span')
  value.className = 'lb-hero-number-value'
  value.textContent = String(data.value)
  value.style.fontSize = SIZE_VALUE_FONT[size]
  value.style.fontWeight = '700'
  value.style.lineHeight = '1'
  value.style.color = 'var(--lb-text-primary)'
  value.style.fontVariantNumeric = 'tabular-nums'
  root.appendChild(value)

  const label = document.createElement('span')
  label.className = 'lb-hero-number-label'
  label.textContent = data.label
  label.style.fontSize = SIZE_LABEL_FONT[size]
  label.style.fontWeight = '500'
  label.style.color = 'var(--lb-text-secondary)'
  label.style.textTransform = 'uppercase'
  label.style.letterSpacing = '0.04em'
  root.appendChild(label)

  if (data.delta != null) {
    const delta = document.createElement('span')
    delta.className = 'lb-hero-number-delta'
    delta.textContent = data.delta
    delta.style.fontSize = 'var(--lb-font-size-small)'
    delta.style.color = DELTA_TONE_COLOR[data.deltaTone ?? 'neutral']
    root.appendChild(delta)
  }

  return root
}
