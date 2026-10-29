import { describe, expect, test } from 'vitest'
import { parseInline } from '../src/engine/markdown-parser.js'

describe('parseInline', () => {
  test('plain text -> single TextRun', () => {
    expect(parseInline('hello world')).toEqual([
      { kind: 'text', text: 'hello world' },
    ])
  })

  test('bold with **', () => {
    expect(parseInline('hello **bold** end')).toEqual([
      { kind: 'text', text: 'hello ' },
      { kind: 'emphasis', style: 'bold', children: [{ kind: 'text', text: 'bold' }] },
      { kind: 'text', text: ' end' },
    ])
  })

  test('bold with __', () => {
    const result = parseInline('__strong__')
    expect(result).toHaveLength(1)
    expect(result[0]).toMatchObject({ kind: 'emphasis', style: 'bold' })
  })

  test('italic with *', () => {
    expect(parseInline('an *italic* word')).toEqual([
      { kind: 'text', text: 'an ' },
      { kind: 'emphasis', style: 'italic', children: [{ kind: 'text', text: 'italic' }] },
      { kind: 'text', text: ' word' },
    ])
  })

  test('italic with _', () => {
    const result = parseInline('_em_')
    expect(result).toHaveLength(1)
    expect(result[0]).toMatchObject({ kind: 'emphasis', style: 'italic' })
  })

  test('inline code', () => {
    expect(parseInline('use `foo()` here')).toEqual([
      { kind: 'text', text: 'use ' },
      { kind: 'code-span', text: 'foo()' },
      { kind: 'text', text: ' here' },
    ])
  })

  test('inline code with special chars inside', () => {
    expect(parseInline('`<script>`')).toEqual([
      { kind: 'code-span', text: '<script>' },
    ])
  })

  test('standard link', () => {
    expect(parseInline('see [the docs](https://example.com)')).toEqual([
      { kind: 'text', text: 'see ' },
      { kind: 'link', href: 'https://example.com', children: [{ kind: 'text', text: 'the docs' }] },
    ])
  })

  test('standard link with title', () => {
    const result = parseInline('[a](b "title")')
    expect(result).toHaveLength(1)
    expect(result[0]).toMatchObject({ kind: 'link', href: 'b', title: 'title' })
  })

  test('wikilink without label', () => {
    expect(parseInline('see [[my-page]] now')).toEqual([
      { kind: 'text', text: 'see ' },
      { kind: 'link', href: 'my-page', children: [{ kind: 'text', text: 'my-page' }] },
      { kind: 'text', text: ' now' },
    ])
  })

  test('wikilink with label', () => {
    expect(parseInline('see [[path/to/page|Display Name]]')).toEqual([
      { kind: 'text', text: 'see ' },
      { kind: 'link', href: 'path/to/page', children: [{ kind: 'text', text: 'Display Name' }] },
    ])
  })

  test('bold containing italic', () => {
    const result = parseInline('**bold _italic_ end**')
    expect(result).toHaveLength(1)
    expect(result[0].kind).toBe('emphasis')
    expect((result[0] as any).style).toBe('bold')
    const inner = (result[0] as any).children
    expect(inner).toContainEqual({ kind: 'text', text: 'bold ' })
    expect(inner).toContainEqual(
      { kind: 'emphasis', style: 'italic', children: [{ kind: 'text', text: 'italic' }] }
    )
  })

  test('unclosed bold delimiter -> literal', () => {
    expect(parseInline('**unclosed')).toEqual([
      { kind: 'text', text: '**unclosed' },
    ])
  })

  test('escape sequence: \\*', () => {
    expect(parseInline('a \\* b')).toEqual([
      { kind: 'text', text: 'a * b' },
    ])
  })

  test('escape sequence: \\[', () => {
    expect(parseInline('\\[not a link\\]')).toEqual([
      { kind: 'text', text: '[not a link]' },
    ])
  })

  test('backslash before a non-punctuation char is literal (Windows path)', () => {
    expect(parseInline('C:\\projects\\sync\\notes')).toEqual([
      { kind: 'text', text: 'C:\\projects\\sync\\notes' },
    ])
  })

  test('empty string -> empty array', () => {
    expect(parseInline('')).toEqual([])
  })
})
