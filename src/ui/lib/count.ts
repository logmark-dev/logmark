/** "1 entry", "2 entries": a number with its noun in the right number. */
export function count(n: number, singular: string, plural = `${singular}s`): string {
  return `${n.toLocaleString()} ${n === 1 ? singular : plural}`
}
