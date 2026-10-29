/**
 * Host identity (rc6 design D1 / 4.1): `GET /api/v0/capabilities` carries an
 * additive `host` naming the product that serves the vault. The open Engine
 * daemon sends { providerId: 'oss-cli', product: 'LogMark Engine' }; the
 * desktop app's v0 server sends { providerId: 'desktop', product: 'LogMark' }.
 * Older servers and the Node cockpit server omit it.
 */
export interface HostInfo {
  providerId: string
  product: string
}

/**
 * The `/api/capabilities` record the SPA reads. The Node cockpit server
 * builds it in src/server/server.ts; the daemon's compat route aliases
 * `/api/v0/capabilities` (which also carries `versions`, unused here).
 */
export interface CapabilitiesResponse {
  adapter: string
  capabilities: Record<string, boolean>
  vaultPath: string
  host?: HostInfo
}

/**
 * Host-surface features the Node cockpit server serves for every adapter:
 * POST /api/distill, the GET /api/health lint scan, and inference provider
 * order plus model routing. Merged into its /api/capabilities record. The
 * daemon never sends these flags, so absent means not served (rc6 design
 * 4.2: hidden, not bannered).
 */
export const NODE_HOST_FEATURES = {
  hasDistillation: true,
  hasLintScan: true,
  hasInferenceSettings: true,
} as const

/** True for an object with non-empty string `providerId` and `product`. */
export function isHostInfo(value: unknown): value is HostInfo {
  if (typeof value !== 'object' || value === null) return false
  const v = value as Record<string, unknown>
  return (
    typeof v.providerId === 'string' &&
    v.providerId.length > 0 &&
    typeof v.product === 'string' &&
    v.product.length > 0
  )
}
