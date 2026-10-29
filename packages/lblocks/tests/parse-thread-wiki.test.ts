import { describe, expect, test } from 'vitest'
import { readFileSync } from 'fs'
import { resolve } from 'path'
import { parseThreadWiki } from '../src/primitives/thread-wiki.js'

const fixturesDir = resolve(__dirname, 'fixtures/thread-wiki')
const loadFixture = (name: string) => readFileSync(resolve(fixturesDir, name), 'utf8')

describe('parseThreadWiki - synthetic cases', () => {
  test('empty input -> empty meta + no sections', () => {
    const result = parseThreadWiki('')
    expect(result.meta.tags).toEqual([])
    expect(result.meta.participants).toEqual([])
    expect(result.meta.sources).toEqual([])
    expect(result.sections).toEqual([])
  })

  test('frontmatter scalar fields parse', () => {
    const md = `---
type: thread-wiki
title: Test Title
slug: test-slug
op: '@alice'
op_display: Alice Wonderland
source_url: https://x.com/alice/status/123
created: 2026-05-05T10:00:00+00:00
reply_count: 5
images: 2
status: draft
---

# Title in Body

Body content.
`
    const r = parseThreadWiki(md)
    expect(r.meta.title).toBe('Title in Body') // body title overrides frontmatter
    expect(r.meta.slug).toBe('test-slug')
    expect(r.meta.op).toBe('@alice')
    expect(r.meta.opDisplay).toBe('Alice Wonderland')
    expect(r.meta.sourceUrl).toBe('https://x.com/alice/status/123')
    expect(r.meta.capturedAt).toBe('2026-05-05T10:00:00+00:00')
    expect(r.meta.replyCount).toBe(5)
    expect(r.meta.imageCount).toBe(2)
    expect(r.meta.status).toBe('draft')
  })

  test('participants list (flat string form) parses + OP excluded', () => {
    const md = `---
type: thread-wiki
op: '@alice'
participants:
- '@alice'
- '@bob'
- '@charlie'
---

# T
`
    const r = parseThreadWiki(md)
    expect(r.meta.participants).toHaveLength(2)
    expect(r.meta.participants.map(p => p.handle)).toEqual(['@bob', '@charlie'])
    expect(r.meta.participants[0].url).toBe('https://x.com/bob')
    expect(r.meta.participants[1].url).toBe('https://x.com/charlie')
  })

  test('tags list parses', () => {
    const md = `---
type: thread-wiki
tags:
- foo
- bar-baz
- qux.quux
---
`
    expect(parseThreadWiki(md).meta.tags).toEqual(['foo', 'bar-baz', 'qux.quux'])
  })

  test('body sections split on ## headings, Sources extracted', () => {
    const md = `---
type: thread-wiki
op: '@a'
---

# T

## Summary
some summary text

## Key insights
- one
- two

## Sources
- [Original tweet](https://x.com/a/status/1)
- [[.assets/x/source|Raw thread]]
- [[.assets/x/images/foo.transcript|Image transcript: foo]]
`
    const r = parseThreadWiki(md)
    expect(r.sections.map(s => s.heading)).toEqual(['Summary', 'Key insights'])
    expect(r.sections[0].body.trim()).toBe('some summary text')
    expect(r.meta.sources).toHaveLength(3)
    expect(r.meta.sources[0]).toMatchObject({
      kind: 'original-tweet',
      href: 'https://x.com/a/status/1',
      isWikilink: false,
    })
    expect(r.meta.sources[1]).toMatchObject({
      kind: 'raw-thread',
      isWikilink: true,
    })
    expect(r.meta.sources[2]).toMatchObject({
      kind: 'image-transcript',
      isWikilink: true,
    })
  })

  test('missing frontmatter -> empty meta + body parsed normally', () => {
    const md = `# T\n\n## Summary\nbody`
    const r = parseThreadWiki(md)
    expect(r.meta.tags).toEqual([])
    expect(r.meta.title).toBe('T')
    expect(r.sections).toHaveLength(1)
  })

  test('malformed YAML never throws', () => {
    const md = `---
type: thread-wiki
broken line without colon
op: '@a'
---

# T
`
    expect(() => parseThreadWiki(md)).not.toThrow()
    expect(parseThreadWiki(md).meta.op).toBe('@a')
  })

  test('untitled fallback', () => {
    const md = `---\ntype: thread-wiki\n---\n\n## Summary\nbody`
    expect(parseThreadWiki(md).meta.title).toBe('(untitled thread)')
  })
})

describe('parseThreadWiki - real fixtures', () => {
  test('qwen fixture parses without error', () => {
    const md = loadFixture('qwen.md')
    const r = parseThreadWiki(md)
    expect(r.meta.title).toBe('Qwen 3.6 Configuration Optimization')
    expect(r.meta.op).toBe('@Michaelzsguo')
    expect(r.meta.replyCount).toBe(43)
    expect(r.meta.imageCount).toBe(3)
    expect(r.meta.participants.length).toBeGreaterThan(20)
    expect(r.meta.participants.every(p => p.handle !== '@Michaelzsguo')).toBe(true) // OP excluded
    // NOTE: this fixture's body uses `Summary:` plain-text labels rather than `## Summary`
    // headings, so the only `##` heading is `## Sources` (which gets stripped). The
    // structural invariant we care about is "Sources is excluded from sections" and
    // "sources are extracted into meta.sources".
    expect(r.sections.every(s => s.heading !== 'Sources')).toBe(true) // Sources stripped
    expect(r.meta.sources.length).toBeGreaterThan(0)
  })

  test('theo fixture (200 participants) parses without performance issues', () => {
    const md = loadFixture('theo.md')
    const start = Date.now()
    const r = parseThreadWiki(md)
    const elapsed = Date.now() - start
    expect(elapsed).toBeLessThan(200) // generous; parser should be <50ms
    expect(r.meta.title).toBe('GitHub Copilot Billing Exploit')
    expect(r.meta.op).toBe('@theo')
    expect(r.meta.replyCount).toBe(200)
    expect(r.meta.participants.length).toBeGreaterThan(150)
  })
})
