export type UiTheme = 'dark' | 'light'

const KEY = 'lk-theme'

export function getStoredTheme(): UiTheme {
  const raw = localStorage.getItem(KEY)
  return raw === 'light' || raw === 'dark' ? raw : 'dark'
}

export function applyTheme(theme: UiTheme): void {
  // tokens.css flips on :root[data-theme] - the root element is the
  // canonical stamp. body keeps the attribute one release for any
  // straggler [data-theme] selector (removed in the Task 8 cleanup).
  document.documentElement.dataset.theme = theme
  document.body.dataset.theme = theme
  localStorage.setItem(KEY, theme)
}

export function toggleTheme(): UiTheme {
  const next = getStoredTheme() === 'dark' ? 'light' : 'dark'
  applyTheme(next)
  return next
}
