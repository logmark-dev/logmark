import { isHostInfo } from '../../types/host.js'

/**
 * Which host-surface features the serving host implements (rc6, design
 * 4.2). Read from the `/api/capabilities` record; only the literal `true`
 * counts, so a host that never heard of a flag (the daemon, the desktop v0
 * server, any older build) hides the feature instead of rendering a dead
 * control or a 501 banner. Vault capabilities (notation, backlinks) keep
 * the annotate-never-hide treatment in degraded.ts.
 */
export interface HostFeatures {
  /** GET /api/health lint scan: Health triage, stale counts, source freshness. */
  lintScan: boolean
  /** Inference provider order and model routing in Settings. */
  inferenceSettings: boolean
  /**
   * A producer writes concept pages on this host (the desktop dream loop).
   * The inverse rule to the flags above: ONLY an explicit
   * `hasConceptProducer: false` hides the concept surfaces (Concepts nav,
   * Recent concepts, the Concepts page, the root refinery band, the route
   * Concept coverage band). Absent means shown, so the Node dev server and
   * the vanilla / logmark-mcp adapters keep their concept surfaces. The open
   * daemon (`logmark serve`) reports false; the desktop host reports true.
   */
  conceptProducer: boolean
}

export function hostFeatures(capabilities: Record<string, boolean> | undefined): HostFeatures {
  const caps = capabilities ?? {}
  return {
    lintScan: caps.hasLintScan === true,
    inferenceSettings: caps.hasInferenceSettings === true,
    conceptProducer: caps.hasConceptProducer !== false,
  }
}

/** Sidebar wordmark + tab title for a host (rc6, design D1). */
export interface Brand {
  /** Wordmark name, bold. */
  name: string
  /** Light wordmark suffix, or null for none. */
  suffix: string | null
  /** document.title */
  title: string
}

const DEV_BRAND: Brand = { name: 'logmark', suffix: 'dev', title: 'LogMark dev' }

/**
 * oss-cli -> logmark + engine ("LogMark Engine"); desktop -> "LogMark";
 * missing or malformed host -> logmark + dev ("LogMark dev"). An unknown
 * provider id shows the product it reports rather than claiming to be a
 * dev server. Callers render every field with textContent.
 */
export function brandFor(host: unknown): Brand {
  if (!isHostInfo(host)) return { ...DEV_BRAND }
  if (host.providerId === 'oss-cli') return { name: 'logmark', suffix: 'engine', title: 'LogMark Engine' }
  if (host.providerId === 'desktop') return { name: 'LogMark', suffix: null, title: 'LogMark' }
  return { name: host.product, suffix: null, title: host.product }
}
