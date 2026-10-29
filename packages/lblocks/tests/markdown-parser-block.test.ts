import { describe, expect, test } from 'vitest'
import { parseMarkdown } from '../src/engine/markdown-parser.js'

describe('parseMarkdown - block level', () => {
  test('empty input', () => {
    expect(parseMarkdown('')).toEqual([])
  })

  test('whitespace-only input', () => {
    expect(parseMarkdown('   \n\n   ')).toEqual([])
  })

  test('single paragraph', () => {
    const result = parseMarkdown('hello world')
    expect(result).toHaveLength(1)
    expect(result[0].kind).toBe('paragraph')
    expect((result[0] as any).children).toEqual([{ kind: 'text', text: 'hello world' }])
  })

  test('two paragraphs separated by blank', () => {
    const result = parseMarkdown('first para\n\nsecond para')
    expect(result).toHaveLength(2)
    expect(result.every(b => b.kind === 'paragraph')).toBe(true)
  })

  test('paragraph soft-break (single newline within paragraph)', () => {
    const result = parseMarkdown('line one\nline two')
    expect(result).toHaveLength(1)
    expect((result[0] as any).children).toEqual([
      { kind: 'text', text: 'line one' },
      { kind: 'line-break' },
      { kind: 'text', text: 'line two' },
    ])
  })

  test.each([1, 2, 3, 4, 5, 6])('ATX heading level %i', (level) => {
    const md = '#'.repeat(level) + ' My Heading'
    const result = parseMarkdown(md)
    expect(result).toHaveLength(1)
    expect(result[0]).toMatchObject({ kind: 'heading', level })
    expect((result[0] as any).children[0].text).toBe('My Heading')
  })

  test('heading with trailing #', () => {
    const result = parseMarkdown('## Title ##')
    expect(result[0]).toMatchObject({ kind: 'heading', level: 2 })
    expect((result[0] as any).children[0].text).toBe('Title')
  })

  test('unordered list with -', () => {
    const result = parseMarkdown('- one\n- two\n- three')
    expect(result).toHaveLength(1)
    expect(result[0].kind).toBe('list')
    expect((result[0] as any).ordered).toBe(false)
    expect((result[0] as any).items).toHaveLength(3)
  })

  test('unordered list with *', () => {
    const result = parseMarkdown('* a\n* b')
    expect(result[0]).toMatchObject({ kind: 'list', ordered: false })
  })

  test('ordered list', () => {
    const result = parseMarkdown('1. first\n2. second')
    expect(result[0]).toMatchObject({ kind: 'list', ordered: true })
    expect((result[0] as any).items).toHaveLength(2)
  })

  test('ordered list with non-1 start', () => {
    const result = parseMarkdown('5. fifth\n6. sixth')
    expect(result[0]).toMatchObject({ kind: 'list', ordered: true, start: 5 })
  })

  test('fenced code block with language', () => {
    const md = '```ts\nconst x = 1\n```'
    const result = parseMarkdown(md)
    expect(result).toHaveLength(1)
    expect(result[0]).toMatchObject({
      kind: 'codeblock',
      language: 'ts',
      text: 'const x = 1',
    })
  })

  test('fenced code block without language', () => {
    const md = '```\nplain\n```'
    expect(parseMarkdown(md)[0]).toMatchObject({ kind: 'codeblock', language: '', text: 'plain' })
  })

  test('fenced code block preserves blank lines and special chars', () => {
    const md = '```\nline 1\n\nline 3 with **stars**\n```'
    expect((parseMarkdown(md)[0] as any).text).toBe('line 1\n\nline 3 with **stars**')
  })

  test('blockquote single line', () => {
    const result = parseMarkdown('> quoted')
    expect(result[0].kind).toBe('blockquote')
    const children = (result[0] as any).children
    expect(children).toHaveLength(1)
    expect(children[0].kind).toBe('paragraph')
  })

  test('blockquote multi-line', () => {
    const result = parseMarkdown('> line one\n> line two')
    expect(result[0].kind).toBe('blockquote')
  })

  test('horizontal rule with ---', () => {
    const result = parseMarkdown('above\n\n---\n\nbelow')
    expect(result).toHaveLength(3)
    expect(result[1].kind).toBe('divider')
  })

  test('horizontal rule with ***', () => {
    expect(parseMarkdown('***')[0].kind).toBe('divider')
  })

  test('GFM table', () => {
    const md = '| a | b |\n| --- | --- |\n| 1 | 2 |\n| 3 | 4 |'
    const result = parseMarkdown(md)
    expect(result).toHaveLength(1)
    expect(result[0].kind).toBe('table')
    expect((result[0] as any).header.cells).toHaveLength(2)
    expect((result[0] as any).rows).toHaveLength(2)
  })

  test('GFM table with alignment', () => {
    const md = '| a | b | c |\n| :--- | :---: | ---: |\n| 1 | 2 | 3 |'
    const result = parseMarkdown(md)
    expect((result[0] as any).alignments).toEqual(['left', 'center', 'right'])
  })

  test('block-level image', () => {
    const result = parseMarkdown('![alt text](image.png)')
    expect(result[0]).toMatchObject({ kind: 'image', alt: 'alt text', src: 'image.png' })
  })

  test('mixed document', () => {
    const md = `# Title

Para one.

- item a
- item b

\`\`\`ts
code
\`\`\`

Para two.`
    const result = parseMarkdown(md)
    expect(result.map(b => b.kind)).toEqual([
      'heading', 'paragraph', 'list', 'codeblock', 'paragraph',
    ])
  })

  test('does not parse frontmatter (treats --- as divider)', () => {
    const md = '---\nfoo: bar\n---\n\ncontent'
    const result = parseMarkdown(md)
    // First --- is a divider; "foo: bar" is a paragraph; second --- is a divider; "content" is a paragraph
    expect(result.map(b => b.kind)).toContain('divider')
  })

  test('inline parsing applied within heading', () => {
    const result = parseMarkdown('# Title with **bold**')
    const children = (result[0] as any).children
    expect(children.some((c: any) => c.kind === 'emphasis' && c.style === 'bold')).toBe(true)
  })

  test('inline parsing applied within paragraph', () => {
    const result = parseMarkdown('Hello [link](url) world')
    const children = (result[0] as any).children
    expect(children.some((c: any) => c.kind === 'link')).toBe(true)
  })

  test('inline parsing applied within list item', () => {
    const result = parseMarkdown('- item with **bold**\n- item two')
    const items = (result[0] as any).items
    const firstPara = items[0].children[0]
    expect(firstPara.kind).toBe('paragraph')
    expect(firstPara.children.some((c: any) => c.kind === 'emphasis')).toBe(true)
  })
})
