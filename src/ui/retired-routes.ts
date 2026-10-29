import { redirectTo, type Router } from './router.js'

/**
 * Retired cockpit routes (lmk-oss-cockpit-cut-empty-producer-views-7qcb).
 * Each surface was empty on every host - the feed queue repeated recent
 * activity, the link graph had no edges, and /api/distill answers 501 on
 * both the open daemon and the desktop - so the views were cut outright.
 * Old bookmarks and links still land somewhere real: the vault root, or the
 * concept page a distill URL was about.
 */
export const RETIRED_ROUTES: ReadonlyArray<{ path: string; to: (params: Record<string, string>) => string }> = [
  { path: '/feed', to: () => '/' },
  { path: '/graph', to: () => '/' },
  { path: '/concepts/:slug/distill', to: (p) => `/concepts/${encodeURIComponent(p.slug)}` },
]

/** Register the retired routes as redirects. Call before any wider pattern that would shadow them. */
export function registerRetiredRoutes(router: Router): void {
  for (const route of RETIRED_ROUTES) {
    router.register(route.path, async (params) => redirectTo(route.to(params)))
  }
}
