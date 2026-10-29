import { ckButton } from './lib/controls.js'

/**
 * A handler result that sends the browser to another path instead of
 * rendering a view. The router swaps the URL in place (replaceState, no
 * extra history entry) and renders the target. Used for retired routes and
 * host-gated surfaces, so a deep link never lands on an empty shell.
 */
export interface RouteRedirect {
  redirect: string
}

export function redirectTo(path: string): RouteRedirect {
  return { redirect: path }
}

function isRedirect(value: HTMLElement | RouteRedirect): value is RouteRedirect {
  return typeof (value as RouteRedirect).redirect === 'string'
}

/** Redirect chains longer than this render the route-error state instead of looping. */
const MAX_REDIRECT_HOPS = 4

type RouteHandler = (
  params: Record<string, string>,
  query: URLSearchParams,
) => Promise<HTMLElement | RouteRedirect>

interface RouteDef {
  pattern: RegExp
  paramNames: string[]
  handler: RouteHandler
}

export class Router {
  private routes: RouteDef[] = []
  constructor(
    private readonly mountEl: HTMLElement,
    private readonly onRouteRendered?: (pathname: string) => void | Promise<void>,
  ) {}

  register(path: string, handler: RouteHandler): void {
    const paramNames: string[] = []
    const pattern = new RegExp(
      '^' +
        path.replace(/:(\w+)/g, (_, name) => {
          paramNames.push(name)
          return '([^/]+)'
        }) +
        '$'
    )
    this.routes.push({ pattern, paramNames, handler })
  }

  async navigate(path: string): Promise<void> {
    const url = new URL(path, location.origin)
    history.pushState({}, '', url.pathname + url.search)
    await this.render()
  }

  async render(): Promise<void> {
    return this.renderAt(0)
  }

  private async renderAt(hops: number): Promise<void> {
    const url = new URL(location.href)
    for (const r of this.routes) {
      const m = url.pathname.match(r.pattern)
      if (m) {
        const params: Record<string, string> = {}
        r.paramNames.forEach((name, i) => { params[name] = decodeURIComponent(m[i + 1]) })
        let view: HTMLElement
        try {
          const result = await r.handler(params, url.searchParams)
          if (isRedirect(result)) {
            if (hops < MAX_REDIRECT_HOPS) {
              const target = new URL(result.redirect, location.origin)
              history.replaceState({}, '', target.pathname + target.search)
              return this.renderAt(hops + 1)
            }
            view = renderRouteError(url.pathname, new Error('Redirect loop'), () => { void this.render() })
          } else {
            view = result
          }
        } catch (err) {
          view = renderRouteError(url.pathname, err as Error, () => { void this.render() })
        }
        this.mountEl.replaceChildren(view)
        try {
          await this.onRouteRendered?.(url.pathname)
        } catch {
          // Shell chrome refresh failing (e.g. daemon down) must not
          // clobber the just-rendered error view.
        }
        return
      }
    }
    const fallback = document.createElement('div')
    fallback.className = 'page-wrap route-error'
    const h = document.createElement('h1')
    h.textContent = 'Not found'
    const detail = document.createElement('p')
    detail.className = 'route-error-detail mono'
    detail.textContent = url.pathname
    fallback.append(h, detail)
    this.mountEl.replaceChildren(fallback)
    try {
      await this.onRouteRendered?.(url.pathname)
    } catch {
      // Shell chrome refresh failing (e.g. daemon down) must not
      // clobber the just-rendered error view.
    }
  }

  start(): void {
    addEventListener('popstate', () => { void this.render() })
    addEventListener('click', (e) => {
      const t = e.target as HTMLElement
      const a = t.closest('a[data-route]') as HTMLAnchorElement | null
      if (a) {
        e.preventDefault()
        void this.navigate(a.getAttribute('href') ?? '/')
      }
    })
    void this.render()
  }
}

/**
 * Designed failure surface (release review P0-2): a view whose data load
 * threw. Shown in place of the view; the URL keeps pointing at the
 * intended destination so Retry can re-run the same route.
 */
function renderRouteError(pathname: string, err: Error, retry: () => void): HTMLElement {
  const wrap = document.createElement('div')
  wrap.className = 'page-wrap route-error'
  const eyebrow = document.createElement('div')
  eyebrow.className = 'hero-eyebrow'
  eyebrow.textContent = 'unreachable'
  const h = document.createElement('h1')
  h.textContent = 'This view could not load'
  const detail = document.createElement('p')
  detail.className = 'route-error-detail mono'
  detail.textContent = err.message
  const hint = document.createElement('p')
  hint.className = 'section-meta'
  hint.textContent = `The vault adapter did not answer for ${pathname}. If the daemon is down, restart it and retry.`
  const btn = ckButton('Retry', retry, { action: 'route-retry' })
  wrap.append(eyebrow, h, detail, hint, btn)
  return wrap
}
