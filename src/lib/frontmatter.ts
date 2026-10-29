import matter from 'gray-matter'

export interface ParsedMarkdown {
  data: Record<string, unknown>
  body: string
}

function stripBom(s: string): string {
  return s.charCodeAt(0) === 0xfeff ? s.slice(1) : s
}

export function parseFrontmatter(source: string): ParsedMarkdown {
  const result = matter(stripBom(source))
  return {
    data: (result.data ?? {}) as Record<string, unknown>,
    body: result.content,
  }
}

export function serializeWithFrontmatter(
  data: Record<string, unknown>,
  body: string
): string {
  return matter.stringify(body, data)
}
