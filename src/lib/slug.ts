const SLUG_PATTERN = /^[a-z0-9]+(-[a-z0-9]+)*$/

export function isValidSlug(s: string): boolean {
  return SLUG_PATTERN.test(s)
}

export function humanizeSlug(slug: string): string {
  return slug
    .split('-')
    .map((part) => (part.length === 0 ? part : part[0].toUpperCase() + part.slice(1)))
    .join(' ')
}

/**
 * Derive a kebab-case slug from a human-typed title (frame V, D5): the
 * live read-only preview in the inline distill-naming panel. Lowercases,
 * collapses any run of non-alphanumeric characters to a single hyphen,
 * and trims leading/trailing hyphens - the result always satisfies
 * `isValidSlug` for non-empty input, and empty/symbol-only input yields
 * '' (the panel's confirm stays disabled on that state, never a coercion
 * to a placeholder slug).
 */
export function slugifyTitle(title: string): string {
  return title
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
}
