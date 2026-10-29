import { describe, it, expect } from 'vitest'
import { renderList } from '../src/primitives/markdown-list.js'
import { parseMarkdown } from '../src/engine/markdown-parser.js'
import type { ListBlock, MarkdownRenderOptions } from '../src/types/markdown-doc.js'

const opts: MarkdownRenderOptions = { theme: 'dark' }

function plainItem(text: string, checked?: boolean) {
  return {
    checked,
    children: [{ kind: 'paragraph' as const, children: [{ kind: 'text' as const, text }] }],
  }
}

describe('task list renderer', () => {
  it('renders unchecked styled checkbox for items with checked=false', () => {
    const block: ListBlock = {
      kind: 'list',
      ordered: false,
      items: [plainItem('todo', false)],
    }
    const el = renderList(block, opts)
    const item = el.querySelector('li')
    const cb = el.querySelector<HTMLElement>('li .lb-task-checkbox')
    expect(cb).toBeTruthy()
    expect(cb?.dataset.state).toBe('unchecked')
    expect(cb?.getAttribute('aria-hidden')).toBe('true')
    expect(cb?.querySelector('.lb-task-checkbox-mark')).toBeNull()
    expect(item?.getAttribute('role')).toBe('checkbox')
    expect(item?.getAttribute('aria-checked')).toBe('false')
    expect(el.querySelector('input[type="checkbox"]')).toBeNull()
  })

  it('renders checked styled checkbox for items with checked=true', () => {
    const block: ListBlock = {
      kind: 'list',
      ordered: false,
      items: [plainItem('done', true)],
    }
    const el = renderList(block, opts)
    const item = el.querySelector('li')
    const cb = el.querySelector<HTMLElement>('li .lb-task-checkbox')
    expect(cb?.dataset.state).toBe('checked')
    expect(cb?.querySelector('.lb-task-checkbox-mark')).toBeTruthy()
    expect(item?.getAttribute('aria-checked')).toBe('true')
  })

  it('renders no checkbox when checked is undefined', () => {
    const block: ListBlock = {
      kind: 'list',
      ordered: false,
      items: [plainItem('regular')],
    }
    expect(renderList(block, opts).querySelector('li .lb-task-checkbox')).toBeNull()
  })

  it('renders mixed checked and unchecked tasks', () => {
    const block: ListBlock = {
      kind: 'list',
      ordered: false,
      items: [plainItem('todo', false), plainItem('done', true)],
    }
    const states = Array.from(renderList(block, opts).querySelectorAll<HTMLElement>('.lb-task-checkbox'))
      .map((cb) => cb.dataset.state)
    expect(states).toEqual(['unchecked', 'checked'])
  })

  it('renders nested task lists with styled checkboxes', () => {
    const block: ListBlock = {
      kind: 'list',
      ordered: false,
      items: [
        {
          checked: false,
          children: [
            { kind: 'paragraph' as const, children: [{ kind: 'text' as const, text: 'outer' }] },
            {
              kind: 'list' as const,
              ordered: false,
              items: [plainItem('inner', true)],
            },
          ],
        },
      ],
    }
    const el = renderList(block, opts, (child) => {
      if (child.kind === 'list') return renderList(child, opts)
      const p = document.createElement('p')
      p.textContent = 'text'
      return p
    })
    expect(el.querySelectorAll('.lb-task-checkbox')).toHaveLength(2)
  })
})

describe('task list parser', () => {
  it('parses - [ ] as unchecked task', () => {
    const blocks = parseMarkdown('- [ ] todo')
    const list = blocks[0] as any
    expect(list.kind).toBe('list')
    expect(list.items[0].checked).toBe(false)
  })

  it('parses - [x] as checked task', () => {
    const blocks = parseMarkdown('- [x] done')
    const list = blocks[0] as any
    expect(list.items[0].checked).toBe(true)
  })

  it('parses - [X] (uppercase) as checked task', () => {
    const blocks = parseMarkdown('- [X] done')
    const list = blocks[0] as any
    expect(list.items[0].checked).toBe(true)
  })

  it('plain - item has checked undefined', () => {
    const blocks = parseMarkdown('- regular')
    const list = blocks[0] as any
    expect(list.items[0].checked).toBeUndefined()
  })
})
