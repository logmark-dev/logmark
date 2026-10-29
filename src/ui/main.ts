import { Router } from './router.js'
import { registerRetiredRoutes } from './retired-routes.js'
import { setNavigator } from './lib/nav.js'
import { ckButton } from './lib/controls.js'
import { bindStatusHints } from './lib/status-hints.js'
import { bindEntryPosition } from './lib/entry-position.js'
import { browserLiveEventDeps, connectLiveEvents } from './lib/live-events.js'
import { loadShellModel } from './models/view-data.js'
import { createAppShell } from './shell/app-shell.js'
import { applyTheme, getStoredTheme } from './shell/theme.js'
import { renderRootSummary } from './views/root-summary.js'
import { renderEntryDetail } from './views/entry-detail.js'
import { renderSearch } from './views/search.js'
import { renderConceptsIndexRoute } from './views/concepts-list.js'
import { renderConceptPageView } from './views/concept-page.js'
import { renderConceptEditor } from './views/concept-editor.js'
import { renderConstitution } from './views/constitution.js'
import { renderHealth } from './views/health.js'
import { renderRouteLanding } from './views/route-landing.js'
import { renderRoutesIndex } from './views/routes-index.js'
import { renderSettings } from './views/settings.js'
import { renderTimeline } from './views/timeline.js'
import { renderTypeLens } from './views/type-lens.js'
import { renderPageView } from './views/page-view.js'

const root = document.getElementById('root')!
applyTheme(getStoredTheme())

;(async () => {
  let shellModel = await loadShellModel()
  const shell = createAppShell(shellModel)
  root.replaceChildren(shell.root)
  bindStatusHints(shell.setContextHints)
  bindEntryPosition(shell.setEntryPosition)

  const router = new Router(shell.content, async (pathname) => {
    shellModel = await loadShellModel()
    shell.update(pathname, shellModel)
  })
  setNavigator((path) => { void router.navigate(path) })
  router.register('/', () => renderRootSummary())
  router.register('/entries/:id', (p) => renderEntryDetail(p.id))
  router.register('/routes', () => renderRoutesIndex())
  router.register('/routes/:route', (p) => renderRouteLanding(p.route))
  router.register('/timeline', () => renderTimeline())
  router.register('/lens/:type', (p) => renderTypeLens(p.type))
  router.register('/search', (_p, q) => renderSearch(q.get('q') ?? ''))
  router.register('/pages', (_p, q) => renderPageView(q.get('path') ?? ''))
  // Retired routes redirect (/feed, /graph -> vault root; a distill URL ->
  // its concept page). Registered before /concepts/:slug patterns.
  registerRetiredRoutes(router)
  // Host-gated (lmk-hqgm): redirects to the vault root on a host that
  // reports hasConceptProducer: false.
  router.register('/concepts', () => renderConceptsIndexRoute())
  router.register('/concepts/new', () => renderConceptEditor({ mode: 'new' }))
  router.register('/concepts/:slug/edit', (p) => renderConceptEditor({ mode: 'edit', slug: p.slug }))
  router.register('/concepts/:slug', (p) => renderConceptPageView(p.slug))
  router.register('/constitution', () => renderConstitution())
  router.register('/health', () => renderHealth())
  router.register('/settings', () => renderSettings())

  addEventListener('keydown', (e) => {
    if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') {
      e.preventDefault()
      const onSearch = location.pathname === '/search'
      const input = document.querySelector<HTMLInputElement>('[data-search-input]')
      if (onSearch && input) {
        input.focus()
        input.select()
      } else {
        void router.navigate('/search')
      }
    }
  })

  wireLiveUpdates(() => router.render(), (live) => shell.setLive(live))

  router.start()
})().catch((err) => {
  const wrap = document.createElement('div')
  wrap.className = 'boot-error'
  const h = document.createElement('h1')
  h.textContent = 'The cockpit could not start'
  const detail = document.createElement('p')
  detail.className = 'route-error-detail mono'
  detail.textContent = (err as Error).message
  const hint = document.createElement('p')
  hint.className = 'section-meta'
  hint.textContent = 'The vault host did not answer. Check that `logmark serve` or the LogMark desktop app is still running, then reload.'
  const btn = ckButton('Reload', () => location.reload())
  wrap.append(h, detail, hint, btn)
  root.replaceChildren(wrap)
})

interface VaultEventPayload {
  kind: 'entries-changed' | 'concepts-changed' | 'constitution-changed'
}

// Which route prefixes re-render for which event kind.
const AFFECTED: Record<VaultEventPayload['kind'], RegExp[]> = {
  'entries-changed': [/^\/$/, /^\/timeline$/, /^\/lens\//, /^\/search$/, /^\/entries\//, /^\/routes\//],
  'concepts-changed': [/^\/concepts/, /^\/health$/],
  'constitution-changed': [/^\/constitution$/],
}

function wireLiveUpdates(rerender: () => Promise<void>, onState?: (live: boolean) => void): void {
  // One stream per browser, shared across tabs (lmk-9ozu).
  const deps = browserLiveEventDeps()
  if (!deps) return
  let timer: ReturnType<typeof setTimeout> | null = null
  connectLiveEvents((data) => {
    let payload: VaultEventPayload
    try {
      payload = JSON.parse(data) as VaultEventPayload
    } catch {
      return
    }
    const patterns = AFFECTED[payload.kind] ?? []
    if (!patterns.some((p) => p.test(location.pathname))) return
    // Never yank the DOM out from under an open editor.
    if (document.querySelector('textarea')) return
    if (timer) clearTimeout(timer)
    timer = setTimeout(() => {
      timer = null
      void rerender()
    }, 500)
  }, (live) => onState?.(live), deps)
}
