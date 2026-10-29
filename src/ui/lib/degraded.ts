/**
 * Degraded-tier annotations (design pass Q4): annotate, never hide.
 * Capability-gated regions stay visible, muted, and labelled with the
 * capability and the adapters that provide it; degenerate data gets an
 * explicit limited-metadata band. The rich (MCP/daemon) shape is the
 * design baseline; this is the documented, honest fallback.
 */

/** One copy map keyed by capability - the single source of gating prose. */
export const CAPABILITY_COPY: Record<string, { label: string; needs: string; explain: string }> = {
  hasNotation: {
    label: 'notation',
    needs: 'MCP or daemon',
    explain: 'Entry types, tags, and per-entry identity need the notation index.',
  },
  hasSessions: {
    label: 'sessions',
    needs: 'MCP or daemon',
    explain: 'Session provenance links captures to the agent session that wrote them.',
  },
  hasFolderSemantics: {
    label: 'folder-semantics',
    needs: 'MCP or daemon',
    explain: 'Type lenses filter by the vault folder taxonomy.',
  },
  backlinks: {
    label: 'backlinks',
    needs: 'MCP or daemon',
    explain: 'Backlink resolution needs the notation index.',
  },
}

/**
 * The amber limited-metadata band shown over lists/details when the
 * adapter cannot provide real identity or timestamps.
 */
export function limitedMetadataBand(adapter: string): HTMLElement {
  const band = document.createElement('div')
  band.className = 'degraded-band'
  const glyph = document.createElement('span')
  glyph.setAttribute('aria-hidden', 'true')
  glyph.textContent = '⚠'
  const text = document.createElement('span')
  text.textContent = ` ${adapter} adapter · limited metadata - file times stand in for capture time, ids may collapse to the day-file`
  band.append(glyph, text)
  return band
}

/**
 * A capability-gated region: visible, greyed, and labelled rather than
 * silently dropped - the console always shows what it WOULD surface.
 */
export function gatedRegion(capabilityKey: string, adapter: string): HTMLElement {
  const copy = CAPABILITY_COPY[capabilityKey] ?? {
    label: capabilityKey,
    needs: 'MCP or daemon',
    explain: 'This region needs a richer adapter.',
  }
  const region = document.createElement('div')
  region.className = 'degraded-region'

  const head = document.createElement('div')
  head.className = 'degraded-region-head'
  head.textContent = `◇ ${copy.label} - unavailable on the ${adapter} adapter`

  const body = document.createElement('p')
  body.className = 'degraded-region-body'
  body.textContent = `${copy.explain} Connect through ${copy.needs} to see what belongs here - the region stays visible so you always know what the console would surface.`

  region.append(head, body)
  return region
}

/** True when the adapter's data shape degrades entry identity/metadata. */
export function isDegradedMetadata(capabilities: Record<string, boolean>): boolean {
  return capabilities.hasNotation !== true
}
