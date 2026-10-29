/**
 * Contextual topbar position bridge (frame P: `n / N in <route>` counter +
 * prev/next arrows, D1/Q2). Mirrors status-hints.ts's binder pattern so
 * non-shell modules (entry-detail.ts) can drive the topbar's right slot
 * without holding a reference to the AppShell instance. main.ts wires the
 * real setter (`shell.setEntryPosition`) at boot.
 */
export interface EntryPositionInfo {
  /** 0-based position of the current entry within its context. */
  index: number
  total: number
  /** Route path, or 'vault' for the unrouted timeline-window fallback. */
  label: string
  prevHref: string | null
  nextHref: string | null
}

type PositionSetter = (position: EntryPositionInfo | null) => void

let setter: PositionSetter | null = null

export function bindEntryPosition(fn: PositionSetter): void {
  setter = fn
}

export function setEntryPosition(position: EntryPositionInfo | null): void {
  setter?.(position)
}
