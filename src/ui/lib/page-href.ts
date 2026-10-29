/**
 * Page paths to request, in order, for a wikilink href (rc6, I3 finding 1).
 *
 * Both page APIs (src/server/routes/pages.ts and the daemon's
 * CockpitPageResolver) resolve paths against the vault CONTENT root
 * (`<vault>/logmark/`). LogMark writes resolved page links vault-relative
 * with a `logmark/` prefix - the Kernel LinkTargetResolver treats any
 * `logmark/...` href (case-insensitive) as vault-relative - so the stripped
 * path goes first and the literal path second (a route that is itself
 * named `logmark`). A `#anchor` never names a file and is dropped.
 * Bare slugs pass through unchanged: the host resolves them.
 */
export function pageLookupPaths(href: string): string[] {
  let path = href.trim().replace(/\\/g, '/')
  const hash = path.indexOf('#')
  if (hash >= 0) path = path.slice(0, hash)
  path = path.replace(/^\/+/, '').trim()
  if (!path) return []
  const prefix = /^logmark\//i.exec(path)
  if (prefix) {
    const stripped = path.slice(prefix[0].length)
    return stripped ? [stripped, path] : [path]
  }
  return [path]
}
