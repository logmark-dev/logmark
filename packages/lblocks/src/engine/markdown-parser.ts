import type {
  MarkdownBlock,
  InlineNode,
  InlineImage,
} from '../types/markdown-doc.js'

export function parseMarkdown(raw: string): MarkdownBlock[] {
  const lines = raw.replace(/\r\n/g, '\n').replace(/\t/g, '    ').split('\n')
  const out: MarkdownBlock[] = []
  let i = 0

  while (i < lines.length) {
    const line = lines[i]

    // Blank line - skip
    if (line.trim() === '') {
      i++
      continue
    }

    // Fenced code block
    const fenceMatch = line.match(/^(```|~~~)(\s*\w*)\s*$/)
    if (fenceMatch) {
      const fence = fenceMatch[1]
      const lang = fenceMatch[2].trim()
      const bodyLines: string[] = []
      i++
      while (i < lines.length && !lines[i].startsWith(fence)) {
        bodyLines.push(lines[i])
        i++
      }
      if (i < lines.length) i++ // skip closing fence
      out.push({ kind: 'codeblock', language: lang, text: bodyLines.join('\n') })
      continue
    }

    // Horizontal rule
    if (/^(\*{3,}|-{3,}|_{3,})\s*$/.test(line)) {
      out.push({ kind: 'divider' })
      i++
      continue
    }

    // ATX heading
    const headingMatch = line.match(/^(#{1,6})\s+(.+?)\s*#*\s*$/)
    if (headingMatch) {
      const level = headingMatch[1].length as 1 | 2 | 3 | 4 | 5 | 6
      out.push({
        kind: 'heading',
        level,
        children: parseInline(headingMatch[2]),
      })
      i++
      continue
    }

    // Block-level image (line by itself)
    const imgMatch = line.match(/^!\[([^\]]*)\]\(([^)\s]+)(?:\s+"([^"]*)")?\)\s*$/)
    if (imgMatch) {
      out.push({
        kind: 'image',
        alt: imgMatch[1],
        src: imgMatch[2],
        ...(imgMatch[3] ? { title: imgMatch[3] } : {}),
      })
      i++
      continue
    }

    // Blockquote
    if (/^>\s?/.test(line)) {
      const quoteLines: string[] = []
      while (i < lines.length && /^>\s?/.test(lines[i])) {
        quoteLines.push(lines[i].replace(/^>\s?/, ''))
        i++
      }
      out.push({
        kind: 'blockquote',
        children: parseMarkdown(quoteLines.join('\n')),
      })
      continue
    }

    // GFM table: line starts with | AND next line is separator | --- |
    if (/^\s*\|.*\|\s*$/.test(line) && i + 1 < lines.length && /^\s*\|[-:\s|]+\|\s*$/.test(lines[i + 1])) {
      const headerCells = parseTableRow(line)
      const sepCells = parseTableRow(lines[i + 1])
      const alignments: Array<'left' | 'center' | 'right' | null> = sepCells.map(parseAlignment)
      const rows: Array<{ cells: Array<{ children: InlineNode[] }> }> = []
      i += 2
      while (i < lines.length && /^\s*\|.*\|\s*$/.test(lines[i])) {
        rows.push({ cells: parseTableRow(lines[i]).map(c => ({ children: parseInline(c) })) })
        i++
      }
      out.push({
        kind: 'table',
        header: { cells: headerCells.map(c => ({ children: parseInline(c) })) },
        rows,
        alignments,
      })
      continue
    }

    // Unordered list
    const ulMatch = line.match(/^(\s*)[-*+]\s+(.+)$/)
    if (ulMatch) {
      const block = consumeList(lines, i, false)
      out.push(block.list)
      i = block.next
      continue
    }

    // Ordered list
    const olMatch = line.match(/^(\s*)(\d+)\.\s+(.+)$/)
    if (olMatch) {
      const block = consumeList(lines, i, true)
      out.push(block.list)
      i = block.next
      continue
    }

    // Paragraph (fallback): accumulate until blank line or recognized block start
    const paraLines: string[] = []
    while (i < lines.length && lines[i].trim() !== '' && !startsRecognizedBlock(lines[i], lines[i + 1])) {
      paraLines.push(lines[i].trim())
      i++
    }
    if (paraLines.length > 0) {
      out.push({
        kind: 'paragraph',
        children: parseParagraphLines(paraLines),
      })
    }
  }

  return out
}

function parseParagraphLines(lines: string[]): InlineNode[] {
  const children: InlineNode[] = []
  lines.forEach((line, index) => {
    if (index > 0) children.push({ kind: 'line-break' })
    children.push(...parseInline(line))
  })
  return children
}

function parseTableRow(line: string): string[] {
  // Strip leading/trailing | and any surrounding whitespace, then split.
  const trimmed = line.trim().replace(/^\|/, '').replace(/\|$/, '')
  return trimmed.split('|').map(c => c.trim())
}

function parseAlignment(sep: string): 'left' | 'center' | 'right' | null {
  const t = sep.trim()
  const left = t.startsWith(':')
  const right = t.endsWith(':')
  if (left && right) return 'center'
  if (left) return 'left'
  if (right) return 'right'
  return null
}

function consumeList(
  lines: string[],
  start: number,
  ordered: boolean
): { list: MarkdownBlock; next: number } {
  const itemRe = ordered ? /^(\s*)(\d+)\.\s+(.+)$/ : /^(\s*)[-*+]\s+(.+)$/
  const items: Array<any> = []
  let i = start
  let firstNum: number | undefined

  while (i < lines.length) {
    const m = lines[i].match(itemRe)
    if (!m) break
    if (ordered && firstNum === undefined) firstNum = parseInt(m[2], 10)
    let itemContentFirstLine = ordered ? m[3] : m[2]

    // Detect task list checkbox: [ ], [x], or [X]
    let checked: boolean | undefined
    const taskMatch = itemContentFirstLine.match(/^\[([ xX])\]\s+/)
    if (taskMatch) {
      checked = taskMatch[1].toLowerCase() === 'x'
      itemContentFirstLine = itemContentFirstLine.slice(taskMatch[0].length)
    }

    const itemBody: string[] = [itemContentFirstLine]
    i++
    // Continuation: indented lines that are not new item starts
    while (i < lines.length && lines[i].trim() !== '' && !lines[i].match(itemRe) && lines[i].startsWith('  ')) {
      itemBody.push(lines[i].trim())
      i++
    }
    items.push({
      ...(checked !== undefined ? { checked } : {}),
      children: [{
        kind: 'paragraph',
        children: parseInline(itemBody.join(' ')),
      }],
    })
  }

  const list: MarkdownBlock = ordered
    ? { kind: 'list', ordered: true, items, ...(firstNum !== undefined && firstNum !== 1 ? { start: firstNum } : {}) }
    : { kind: 'list', ordered: false, items }
  return { list, next: i }
}

function startsRecognizedBlock(line: string, nextLine: string | undefined): boolean {
  if (/^(```|~~~)/.test(line)) return true
  if (/^(\*{3,}|-{3,}|_{3,})\s*$/.test(line)) return true
  if (/^#{1,6}\s+/.test(line)) return true
  if (/^>\s?/.test(line)) return true
  if (/^(\s*)[-*+]\s+/.test(line)) return true
  if (/^(\s*)\d+\.\s+/.test(line)) return true
  if (/^!\[[^\]]*\]\(/.test(line)) return true
  if (/^\s*\|.*\|\s*$/.test(line) && nextLine && /^\s*\|[-:\s|]+\|\s*$/.test(nextLine)) return true
  return false
}

// CommonMark "ASCII punctuation": the only characters a backslash may escape.
// A backslash before anything else (letter, digit, space) is literal.
const ASCII_PUNCTUATION = /[!"#$%&'()*+,\-.\/:;<=>?@[\\\]^_`{|}~]/

export function parseInline(text: string): InlineNode[] {
  const out: InlineNode[] = []
  let buf = ''
  let i = 0
  const flush = () => {
    if (buf.length > 0) {
      out.push({ kind: 'text', text: buf })
      buf = ''
    }
  }
  while (i < text.length) {
    const c = text[i]

    // Escape sequence. Per CommonMark, a backslash only escapes ASCII
    // punctuation; before any other character (letter, digit, space) it is a
    // literal backslash. Treating "\" as a universal escape corrupted Windows
    // paths in prose ("C:\projects" -> "C:projects").
    if (c === '\\' && i + 1 < text.length && ASCII_PUNCTUATION.test(text[i + 1])) {
      buf += text[i + 1]
      i += 2
      continue
    }

    // Code span
    if (c === '`') {
      const close = text.indexOf('`', i + 1)
      if (close > i) {
        flush()
        out.push({ kind: 'code-span', text: text.slice(i + 1, close) })
        i = close + 1
        continue
      }
    }

    // Inline image: ![alt](src "title")
    if (c === '!') {
      const remaining = text.slice(i)
      const imgInlineMatch = remaining.match(/^!\[([^\]]*)\]\(([^)\s]+)(?:\s+"([^"]*)")?\)/)
      if (imgInlineMatch) {
        flush()
        const node: InlineImage = {
          kind: 'inline-image',
          src: imgInlineMatch[2],
          alt: imgInlineMatch[1],
          ...(imgInlineMatch[3] ? { title: imgInlineMatch[3] } : {}),
        }
        out.push(node)
        i += imgInlineMatch[0].length
        continue
      }
    }

    // Wikilink [[...]] or [[...|...]]
    if (c === '[' && text[i + 1] === '[') {
      const close = text.indexOf(']]', i + 2)
      if (close > i + 1) {
        flush()
        const inner = text.slice(i + 2, close)
        const pipe = inner.indexOf('|')
        const href = pipe >= 0 ? inner.slice(0, pipe) : inner
        const label = pipe >= 0 ? inner.slice(pipe + 1) : inner
        out.push({
          kind: 'link',
          href,
          children: [{ kind: 'text', text: label }],
        })
        i = close + 2
        continue
      }
    }

    // Standard link [label](href) or [label](href "title")
    if (c === '[') {
      const labelClose = findMatching(text, i, '[', ']')
      if (labelClose > i && text[labelClose + 1] === '(') {
        const hrefClose = findMatching(text, labelClose + 1, '(', ')')
        if (hrefClose > labelClose + 1) {
          flush()
          const label = text.slice(i + 1, labelClose)
          const inner = text.slice(labelClose + 2, hrefClose)
          const titleMatch = inner.match(/^(\S+?)\s+"([^"]*)"$/)
          const link: InlineNode = titleMatch
            ? { kind: 'link', href: titleMatch[1], title: titleMatch[2], children: parseInline(label) }
            : { kind: 'link', href: inner.trim(), children: parseInline(label) }
          out.push(link)
          i = hrefClose + 1
          continue
        }
      }
    }

    // Strikethrough: ~~text~~
    if (c === '~' && text[i + 1] === '~') {
      const close = text.indexOf('~~', i + 2)
      if (close > i + 1) {
        flush()
        out.push({
          kind: 'emphasis',
          style: 'strikethrough',
          children: parseInline(text.slice(i + 2, close)),
        })
        i = close + 2
        continue
      }
    }

    // Bold: ** or __
    if ((c === '*' && text[i + 1] === '*') || (c === '_' && text[i + 1] === '_')) {
      const delim = c + c
      const close = text.indexOf(delim, i + 2)
      if (close > i + 1) {
        flush()
        out.push({
          kind: 'emphasis',
          style: 'bold',
          children: parseInline(text.slice(i + 2, close)),
        })
        i = close + 2
        continue
      }
    }

    // Italic: * or _
    // Require non-empty content (close > i + 1) so that an unclosed bold
    // delimiter like "**unclosed" does not match as italic-of-empty when the
    // bold branch above fails. Same guard prevents "****" from matching.
    if (c === '*' || c === '_') {
      const close = text.indexOf(c, i + 1)
      if (close > i + 1) {
        flush()
        out.push({
          kind: 'emphasis',
          style: 'italic',
          children: parseInline(text.slice(i + 1, close)),
        })
        i = close + 1
        continue
      }
    }

    // HTML <br> (any common form)
    const remaining = text.slice(i)
    const brMatch = remaining.match(/^<br\s*\/?>/i)
    if (brMatch) {
      flush()
      out.push({ kind: 'line-break' })
      i += brMatch[0].length
      continue
    }

    // Other HTML tags: strip the tag, keep inner text via outer parsing loop
    const otherTag = remaining.match(/^<\/?[a-zA-Z][a-zA-Z0-9-]*[^>]*>/)
    if (otherTag) {
      i += otherTag[0].length
      continue
    }

    buf += c
    i++
  }
  flush()
  return out
}

function findMatching(text: string, start: number, open: string, close: string): number {
  // Find matching close char accounting for nested pairs of the same kind.
  // Used for [label] and (href) where naive indexOf fails on `[a [b] c]`.
  let depth = 0
  for (let i = start; i < text.length; i++) {
    if (text[i] === open) depth++
    else if (text[i] === close) {
      depth--
      if (depth === 0) return i
    }
  }
  return -1
}
