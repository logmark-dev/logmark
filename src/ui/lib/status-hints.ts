/**
 * Contextual status-bar hint bridge (frame O footer: "↑↓ move · ↵ open
 * full · esc close"). Mirrors nav.ts's navigator-hook pattern so non-shell
 * modules (the entry rail) can drive the status bar's right slot without
 * holding a reference to the AppShell instance. main.ts wires the real
 * setter (`shell.setContextHints`) at boot.
 */
type HintSetter = (hints: string[] | null) => void

let setter: HintSetter | null = null

export function bindStatusHints(fn: HintSetter): void {
  setter = fn
}

export function setStatusHints(hints: string[] | null): void {
  setter?.(hints)
}
