/**
 * Sidebar "Lenses" group - the four ratify-relevant types (frame Q/O sidebar
 * anatomy). Deliberately narrower than `ENTRY_TYPES` below; the sidebar
 * group does not grow to the full canonical set.
 */
export const LENS_TYPES = ['task', 'block', 'decision', 'question'] as const
export type LensType = (typeof LENS_TYPES)[number]

export const LENS_LABELS: Record<LensType, string> = {
  task: 'Tasks',
  block: 'Blocks',
  decision: 'Decisions',
  question: 'Questions',
}

/**
 * Full canonical entry-type set (the 8 types on the DS palette -
 * `docs/brand/visual-language.md` / `DesignSystem/colors_and_type.css`
 * `--entry-*` family): 4 core + 3 advanced + the implicit `log`. Any type
 * chip or tree type-dot (e.g. the root at-a-glance row's `log · N` chip,
 * usually the largest on the vault root) navigates to `/lens/:type` for one
 * of these, not just the sidebar's ratify-relevant four.
 */
export const ENTRY_TYPES = ['log', 'task', 'idea', 'block', 'decision', 'question', 'gap', 'analogy'] as const
export type EntryLensType = (typeof ENTRY_TYPES)[number]

export const ENTRY_TYPE_LABELS: Record<EntryLensType, string> = {
  log: 'Logs',
  task: 'Tasks',
  idea: 'Ideas',
  block: 'Blocks',
  decision: 'Decisions',
  question: 'Questions',
  gap: 'Gaps',
  analogy: 'Analogies',
}
