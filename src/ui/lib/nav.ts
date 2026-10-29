/**
 * Navigation hook - lets non-view modules (lblocks action handlers,
 * shared renderers) trigger SPA navigation without holding the Router.
 * main.ts registers the real navigator at boot; until then navigation
 * falls back to a full location change (still correct, just not SPA).
 */
type Navigator = (path: string) => void

let navigator: Navigator | null = null

export function setNavigator(fn: Navigator): void {
  navigator = fn
}

export function navigate(path: string): void {
  if (navigator) {
    navigator(path)
    return
  }
  location.href = path
}
