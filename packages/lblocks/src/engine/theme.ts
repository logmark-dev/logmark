type TokenMap = Record<string, string>

const SHARED_TOKENS: TokenMap = {
  '--lb-font-family': 'var(--font-sans, "Segoe UI", -apple-system, system-ui, sans-serif)',
  '--lb-font-mono': 'var(--font-mono, "Consolas", "Courier New", monospace)',
  '--lb-font-size-header': 'var(--fs-h1, 18px)',
  '--lb-font-size-body': 'var(--fs-body, 13px)',
  '--lb-font-size-label': 'var(--fs-hint, 11px)',
  '--lb-font-size-small': 'var(--fs-hint, 11px)',
  '--lb-text-on-accent': 'rgb(var(--text-on-accent, 255 255 255))',
  '--lb-radius': 'var(--radius-md, 6px)',
  '--lb-radius-sm': 'var(--radius-sm, 4px)',
  '--lb-radius-full': 'var(--radius-full, 999px)',
  '--lb-entry-log': 'rgb(var(--entry-log, 113 113 122))',
  '--lb-entry-task': 'rgb(var(--entry-task, 74 144 184))',
  '--lb-entry-idea': 'rgb(var(--entry-idea, 245 158 11))',
  '--lb-entry-block': 'rgb(var(--entry-block, 194 107 107))',
  '--lb-entry-decision': 'rgb(var(--entry-decision, 154 123 184))',
  '--lb-entry-question': 'rgb(var(--entry-question, 124 58 237))',
  '--lb-entry-gap': 'rgb(var(--entry-gap, 212 134 10))',
  '--lb-entry-analogy': 'rgb(var(--entry-analogy, 212 194 107))',
}

export const DARK_TOKENS: TokenMap = {
  ...SHARED_TOKENS,
  '--lb-bg-primary': 'rgb(var(--surface, 30 30 46))',
  '--lb-bg-secondary': 'rgb(var(--surface-alt, 42 42 62))',
  '--lb-bg-tertiary': 'rgb(var(--surface-elevated, 61 46 20))',
  '--lb-bg-hover': 'rgb(var(--surface-elevated, 61 46 20))',
  '--lb-surface': 'rgb(var(--surface-alt, 42 42 62))',
  '--lb-border': 'rgb(var(--border, 63 63 70))',
  '--lb-border-subtle': 'rgb(var(--border, 63 63 70) / 0.68)',
  '--lb-text-primary': 'rgb(var(--text, 228 228 231))',
  '--lb-text-secondary': 'rgb(var(--text-muted, 161 161 170))',
  '--lb-text-muted': 'rgb(var(--text-muted, 161 161 170))',
  '--lb-text-dim': 'rgb(var(--text-dim, 82 82 91))',
  '--lb-accent': 'rgb(var(--accent, 192 122 10))',
  '--lb-accent-thread': 'var(--lb-accent)',
  '--lb-signal-positive': 'rgb(var(--signal-positive, 107 163 110))',
  '--lb-signal-alert': 'rgb(var(--signal-alert, 212 134 10))',
  '--lb-signal-negative': 'rgb(var(--signal-negative, 194 107 107))',
  '--lb-signal-inquiry': 'rgb(var(--signal-inquiry, 154 123 184))',
  '--lb-signal-focus': 'rgb(var(--signal-focus, 212 194 107))',
  '--lb-code-bg': 'rgb(var(--surface-alt, 42 42 62) / 0.82)',
  '--lb-quote-border': 'var(--lb-text-muted)',
}

export const LIGHT_TOKENS: TokenMap = {
  ...SHARED_TOKENS,
  '--lb-bg-primary': 'rgb(var(--surface, 250 250 250))',
  '--lb-bg-secondary': 'rgb(var(--surface-alt, 244 244 245))',
  '--lb-bg-tertiary': 'rgb(var(--surface-elevated, 232 212 176))',
  '--lb-bg-hover': 'rgb(var(--surface-elevated, 232 212 176))',
  '--lb-surface': 'rgb(var(--surface-alt, 244 244 245))',
  '--lb-border': 'rgb(var(--border, 228 228 231))',
  '--lb-border-subtle': 'rgb(var(--border, 228 228 231) / 0.68)',
  '--lb-text-primary': 'rgb(var(--text, 30 30 46))',
  '--lb-text-secondary': 'rgb(var(--text-muted, 82 82 91))',
  '--lb-text-muted': 'rgb(var(--text-muted, 82 82 91))',
  '--lb-text-dim': 'rgb(var(--text-dim, 161 161 170))',
  '--lb-accent': 'rgb(var(--accent, 217 119 6))',
  '--lb-accent-thread': 'var(--lb-accent)',
  '--lb-signal-positive': 'rgb(var(--signal-positive, 63 107 65))',
  '--lb-signal-alert': 'rgb(var(--signal-alert, 185 106 10))',
  '--lb-signal-negative': 'rgb(var(--signal-negative, 159 72 72))',
  '--lb-signal-inquiry': 'rgb(var(--signal-inquiry, 92 63 122))',
  '--lb-signal-focus': 'rgb(var(--signal-focus, 168 145 27))',
  '--lb-code-bg': 'rgb(var(--surface-alt, 244 244 245) / 0.82)',
  '--lb-quote-border': 'var(--lb-text-muted)',
}

export function applyTheme(container: HTMLElement, theme: 'dark' | 'light'): void {
  const tokens = theme === 'dark' ? DARK_TOKENS : LIGHT_TOKENS
  for (const [key, value] of Object.entries(tokens)) {
    container.style.setProperty(key, value)
  }
}
