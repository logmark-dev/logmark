import { describe, expect, test } from 'vitest'
import { parseChatTranscript, renderChatTranscript } from '../src/primitives/chat-transcript.js'

describe('parseChatTranscript', () => {
  test('standard chat with frontmatter, user, assistant', () => {
    const md = `---
chat_id: abc12345
started: 2026-05-05T14:30:00
transport: cloud
---

## user
### 2026-05-05 14:30:00

What patterns do you see in my vault?

## assistant
### 2026-05-05 14:30:02

I see three patterns: A, B, C.
`
    const result = parseChatTranscript(md)
    expect(result.meta.chatId).toBe('abc12345')
    expect(result.meta.started).toBe('2026-05-05T14:30:00')
    expect(result.meta.transport).toBe('cloud')
    expect(result.turns).toHaveLength(2)
    expect(result.turns[0].kind).toBe('user')
    expect(result.turns[0].timestamp).toBe('2026-05-05 14:30:00')
    expect(result.turns[0].content).toBe('What patterns do you see in my vault?')
    expect(result.turns[1].kind).toBe('assistant')
    expect(result.turns[1].content).toBe('I see three patterns: A, B, C.')
  })

  test('missing frontmatter -> empty meta', () => {
    const md = `## user
### 2026-05-05 14:30:00

hi
`
    const result = parseChatTranscript(md)
    expect(result.meta).toEqual({})
    expect(result.turns).toHaveLength(1)
  })

  test('all turn kinds parsed correctly', () => {
    const md = `---
chat_id: x
started: 2026-05-05T10:00:00
transport: cloud
---

## user
### 2026-05-05 10:00:00

hi

## assistant
### 2026-05-05 10:00:01

hello

## tool_call: search_entries
### 2026-05-05 10:00:02

{"q":"foo"}

## tool_result: search_entries
### 2026-05-05 10:00:03

found 3

## write_confirmed
### 2026-05-05 10:00:04

create_entry: t: fix bug

## write_rejected
### 2026-05-05 10:00:05

create_entry: i: think later

_metadata: session reset_

## system
### 2026-05-05 10:00:06

transport switched
`
    const result = parseChatTranscript(md)
    expect(result.turns).toHaveLength(7)
    expect(result.turns[0].kind).toBe('user')
    expect(result.turns[1].kind).toBe('assistant')
    expect(result.turns[2].kind).toBe('tool_call')
    expect(result.turns[2].toolName).toBe('search_entries')
    expect(result.turns[3].kind).toBe('tool_result')
    expect(result.turns[3].toolName).toBe('search_entries')
    expect(result.turns[4].kind).toBe('write_confirmed')
    expect(result.turns[5].kind).toBe('write_rejected')
    expect(result.turns[5].metadata).toBe('session reset')
    expect(result.turns[6].kind).toBe('system')
  })

  test('multi-line content within a turn', () => {
    const md = `---
chat_id: x
---

## assistant
### 2026-05-05 10:00:00

Line one.

Line two.

Line three.
`
    const result = parseChatTranscript(md)
    expect(result.turns[0].content).toBe('Line one.\n\nLine two.\n\nLine three.')
  })

  test('empty body after frontmatter -> no turns', () => {
    const md = `---
chat_id: empty
---
`
    const result = parseChatTranscript(md)
    expect(result.meta.chatId).toBe('empty')
    expect(result.turns).toEqual([])
  })

  test('completely empty input -> empty meta and turns', () => {
    const result = parseChatTranscript('')
    expect(result.meta).toEqual({})
    expect(result.turns).toEqual([])
  })

  test('unknown kind maps to unknown', () => {
    const md = `## chitchat
### 2026-05-05 10:00:00

random
`
    const result = parseChatTranscript(md)
    expect(result.turns).toHaveLength(1)
    expect(result.turns[0].kind).toBe('unknown')
  })

  test('assistant content containing ## heading is NOT a turn boundary', () => {
    const md = `## assistant
### 2026-05-05 10:00:00

Here are markdown headings:

## My H2 heading

Not a turn boundary because "My H2 heading" is not a recognized kind.
`
    const result = parseChatTranscript(md)
    expect(result.turns).toHaveLength(1)
    expect(result.turns[0].kind).toBe('assistant')
    expect(result.turns[0].content).toContain('## My H2 heading')
    expect(result.turns[0].content).toContain('Not a turn boundary')
  })

  test('missing ### timestamp subheader -> timestamp undefined, content begins immediately', () => {
    const md = `## user

just content, no timestamp line
`
    const result = parseChatTranscript(md)
    expect(result.turns[0].kind).toBe('user')
    expect(result.turns[0].timestamp).toBeUndefined()
    expect(result.turns[0].content).toBe('just content, no timestamp line')
  })

  test('malformed frontmatter (missing colon) -> meta partial', () => {
    const md = `---
chat_id: abc
notakeyvalue
transport: cloud
---

## user

hi
`
    const result = parseChatTranscript(md)
    expect(result.meta.chatId).toBe('abc')
    expect(result.meta.transport).toBe('cloud')
    expect(result.turns).toHaveLength(1)
  })

  test('frontmatter with routeScope and entryScope', () => {
    const md = `---
chat_id: x
started: 2026-05-05T10:00:00
transport: cloud
route_scope: +project
entry_scope: abc1234
---

## user
hi
`
    const result = parseChatTranscript(md)
    expect(result.meta.routeScope).toBe('+project')
    expect(result.meta.entryScope).toBe('abc1234')
  })
})

describe('renderChatTranscript', () => {
  test('renders pills for user and assistant', () => {
    const md = `---
chat_id: x
---

## user

hi

## assistant

hello
`
    const block = { kind: 'chat-transcript' as const, body: md }
    const html = renderChatTranscript(block, { theme: 'dark' } as any)
    const pills = html.querySelectorAll('.lb-chat-pill')
    expect(pills).toHaveLength(2)
    expect(pills[0].textContent).toBe('you')
    expect(pills[1].textContent).toBe('logmark')
  })

  test('renders empty placeholder when no turns', () => {
    const md = `---\nchat_id: empty\n---\n`
    const block = { kind: 'chat-transcript' as const, body: md }
    const html = renderChatTranscript(block, { theme: 'dark' } as any)
    expect(html.querySelector('.lb-chat-empty')?.textContent).toBe('(no messages)')
  })

  test('write_confirmed gets accent left border', () => {
    const md = `## write_confirmed

create_entry: t: x
`
    const block = { kind: 'chat-transcript' as const, body: md }
    const html = renderChatTranscript(block, { theme: 'dark' } as any)
    const turn = html.querySelector('.lb-chat-turn--write_confirmed') as HTMLElement
    expect(turn).not.toBeNull()
    expect(turn.style.borderLeft).toContain('3px')
    expect(turn.style.borderLeft.toLowerCase()).toContain('solid')
  })

  test('tool_call body is monospace and dim', () => {
    const md = `## tool_call: search_entries

{"q":"foo"}
`
    const block = { kind: 'chat-transcript' as const, body: md }
    const html = renderChatTranscript(block, { theme: 'dark' } as any)
    const body = html.querySelector('.lb-chat-turn--tool_call .lb-chat-turn-body') as HTMLElement
    expect(body.style.whiteSpace).toBe('pre-wrap')
    expect(body.textContent).toBe('{"q":"foo"}')
  })
})
