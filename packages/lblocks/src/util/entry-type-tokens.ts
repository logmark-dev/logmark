const CANONICAL: Record<string, string> = {
  task: 'task',
  idea: 'idea',
  decision: 'decision',
  question: 'question',
  block: 'block',
  gap: 'gap',
  analogy: 'analogy',
  log: 'log',
  diary: 'log',
}

export const ENTRY_TYPE_KEYS: string[] = Object.keys(CANONICAL).filter(
  key => CANONICAL[key] === key
)

export function getEntryTypeToken(type: string): string {
  return CANONICAL[type.toLowerCase()] ?? 'log'
}

export function entryTypeColor(type: string): string {
  return `var(--lb-entry-${getEntryTypeToken(type)})`
}
