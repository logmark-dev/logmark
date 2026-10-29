/**
 * The cockpit's single date language (release review P1-10). All views
 * format through these four - never toISOString slices (UTC drift),
 * never toLocaleString (format soup), never bare HH:MM in lists that
 * span months.
 */
const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec']

function parse(iso: string): Date | null {
  if (!iso) return null
  const t = Date.parse(iso)
  return Number.isNaN(t) ? null : new Date(t)
}

const pad = (n: number): string => String(n).padStart(2, '0')

export function localDay(iso: string): string {
  const d = parse(iso)
  return d ? `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}` : iso
}

export function timeOfDay(iso: string): string {
  const d = parse(iso)
  return d ? `${pad(d.getHours())}:${pad(d.getMinutes())}` : ''
}

export function smartTimeLabel(iso: string, now: Date = new Date()): string {
  const d = parse(iso)
  if (!d) return iso
  const day = `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`
  const nowDay = `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`
  if (day === nowDay) return `${pad(d.getHours())}:${pad(d.getMinutes())}`
  if (d.getFullYear() === now.getFullYear()) return `${MONTHS[d.getMonth()]} ${d.getDate()}`
  return day
}

export function dayTime(iso: string): string {
  const d = parse(iso)
  if (!d) return iso
  return `${localDay(iso)} · ${timeOfDay(iso)}`
}
