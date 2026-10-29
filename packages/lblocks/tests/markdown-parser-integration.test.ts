import { describe, expect, test, beforeEach } from 'vitest'
import { JSDOM } from 'jsdom'
import { parseMarkdown } from '../src/engine/markdown-parser.js'
import { composeMarkdown } from '../src/engine/markdown-compositor.js'

describe('parseMarkdown -> composeMarkdown integration', () => {
  beforeEach(() => {
    const dom = new JSDOM('<!DOCTYPE html><html><body></body></html>')
    ;(globalThis as any).document = dom.window.document
    ;(globalThis as any).HTMLElement = dom.window.HTMLElement
  })

  test('mixed document round-trips through render', () => {
    const md = `# Title

A paragraph with **bold** and *italic* and \`code\`.

- item one
- item two with [link](https://example.com)

\`\`\`ts
const x = 1
\`\`\`
`
    const blocks = parseMarkdown(md)
    const root = composeMarkdown(
      { version: 1, blocks },
      { theme: 'dark' }
    )
    expect(root.querySelector('h1')?.textContent).toContain('Title')
    expect(root.querySelector('strong')).not.toBeNull()
    expect(root.querySelector('em')).not.toBeNull()
    expect(root.querySelector('code')).not.toBeNull()
    expect(root.querySelectorAll('li')).toHaveLength(2)
    expect(root.querySelector('a[href="https://example.com"]')).not.toBeNull()
    expect(root.querySelector('pre')).not.toBeNull()
  })

  test('empty input renders empty container', () => {
    const blocks = parseMarkdown('')
    const root = composeMarkdown({ version: 1, blocks }, { theme: 'dark' })
    expect(root.children).toHaveLength(0)
  })

  test('exact diary fixture (regression: only-heading repro)', () => {
    // Mirrors a real diary page whose body rendered only the heading.
    const md = [
      '# Agentic Browser Stack: OSS, MCP-Driven, Anti-Detection-Aware',
      '',
      '**Date**: 2026-05-09',
      '**Route**: llm/tools-mcp',
      '**Filesystem archive**: `C:\\workspace\\agent-chats\\domains\\ai-llm\\tools-mcp\\chats\\2026-05-09-agentic-browser-mcp-stack\\`',
      '',
      '## Trigger',
      '',
      'Some prose paragraph.',
      '',
      '- Filesystem archive: `C:\\workspace\\agent-chats\\domains\\ai-llm\\tools-mcp\\chats\\2026-05-09-agentic-browser-mcp-stack\\`',
      '- Friction observation: `C:\\workspace\\agent-chats\\protocol-journal\\failures\\2026-05-09-agentic-browser-mcp-stack.md`',
      '- Insight: `C:\\workspace\\agent-chats\\protocol-journal\\context\\observations.md`',
      '',
      'Final prose.',
    ].join('\n')
    const blocks = parseMarkdown(md)
    const root = composeMarkdown({ version: 1, blocks }, { theme: 'dark' })
    expect(root.querySelector('h1')?.textContent).toContain('Agentic Browser Stack')
    expect(root.querySelector('h2')?.textContent).toContain('Trigger')
    expect(root.textContent).toContain('Final prose')
    expect(root.querySelectorAll('span[role="link"][data-path*="agent-chats"]').length).toBeGreaterThanOrEqual(3)
  })

  test('document with absolute-path code-span renders all blocks', () => {
    // Regression: a paragraph that contains an absolute-path code-span
    // (which renderCodeSpan now turns into an anchor) must not break
    // composition for subsequent blocks.
    const md = `# Heading

**Filesystem archive**: \`C:\\workspace\\foo\\bar\\\`

After paragraph with another path \`C:/some/dir/file.md\`.

- list with path \`D:\\notes\\plan.md\`
- list item two

Final paragraph.`
    const blocks = parseMarkdown(md)
    const root = composeMarkdown({ version: 1, blocks }, { theme: 'dark' })
    expect(root.querySelector('h1')?.textContent).toContain('Heading')
    expect(root.querySelectorAll('p').length).toBeGreaterThanOrEqual(3)
    expect(root.querySelector('ul')).not.toBeNull()
    expect(root.querySelectorAll('li')).toHaveLength(2)
    expect(root.textContent).toContain('Final paragraph')
    expect(root.querySelector('span[role="link"][data-path*="workspace"]')).not.toBeNull()
  })
})
