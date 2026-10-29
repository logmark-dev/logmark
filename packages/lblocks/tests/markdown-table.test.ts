import { describe, it, expect } from 'vitest'
import { renderTable } from '../src/primitives/markdown-table.js'
import type { TableBlock, MarkdownRenderOptions } from '../src/types/markdown-doc.js'

const opts: MarkdownRenderOptions = { theme: 'dark' }

function cell(text: string) {
  return { children: [{ kind: 'text' as const, text }] }
}

describe('markdown-table primitive', () => {
  it('renders <table> with thead and tbody', () => {
    const block: TableBlock = {
      kind: 'table',
      header: { cells: [cell('A'), cell('B')] },
      rows: [{ cells: [cell('1'), cell('2')] }],
      alignments: [null, null],
    }
    const wrapper = renderTable(block, opts)
    const el = wrapper.querySelector('table') as HTMLTableElement
    expect(el.tagName).toBe('TABLE')
    expect(el.querySelector('thead')).toBeTruthy()
    expect(el.querySelector('tbody')).toBeTruthy()
  })

  it('renders header cells as <th>', () => {
    const block: TableBlock = {
      kind: 'table',
      header: { cells: [cell('A'), cell('B')] },
      rows: [],
      alignments: [null, null],
    }
    const wrapper = renderTable(block, opts)
    const ths = wrapper.querySelectorAll('thead th')
    expect(ths).toHaveLength(2)
    expect(ths[0].textContent).toBe('A')
    expect(ths[1].textContent).toBe('B')
  })

  it('renders body cells as <td>', () => {
    const block: TableBlock = {
      kind: 'table',
      header: { cells: [cell('h')] },
      rows: [{ cells: [cell('x')] }, { cells: [cell('y')] }],
      alignments: [null],
    }
    const wrapper = renderTable(block, opts)
    const tds = wrapper.querySelectorAll('tbody td')
    expect(tds).toHaveLength(2)
    expect(tds[0].textContent).toBe('x')
    expect(tds[1].textContent).toBe('y')
  })

  it('applies text-align from alignments', () => {
    const block: TableBlock = {
      kind: 'table',
      header: { cells: [cell('a'), cell('b'), cell('c')] },
      rows: [{ cells: [cell('1'), cell('2'), cell('3')] }],
      alignments: ['left', 'center', 'right'],
    }
    const wrapper = renderTable(block, opts)
    const headerCells = wrapper.querySelectorAll<HTMLTableCellElement>('thead th')
    expect(headerCells[0].style.textAlign).toBe('left')
    expect(headerCells[1].style.textAlign).toBe('center')
    expect(headerCells[2].style.textAlign).toBe('right')
    const bodyCells = wrapper.querySelectorAll<HTMLTableCellElement>('tbody td')
    expect(bodyCells[0].style.textAlign).toBe('left')
    expect(bodyCells[1].style.textAlign).toBe('center')
    expect(bodyCells[2].style.textAlign).toBe('right')
  })

  it('renders inline content in cells', () => {
    const block: TableBlock = {
      kind: 'table',
      header: { cells: [{ children: [{ kind: 'text', text: 'h' }] }] },
      rows: [
        {
          cells: [
            {
              children: [
                { kind: 'text', text: 'a ' },
                { kind: 'emphasis', style: 'bold', children: [{ kind: 'text', text: 'B' }] },
              ],
            },
          ],
        },
      ],
      alignments: [null],
    }
    const wrapper = renderTable(block, opts)
    expect(wrapper.querySelector('tbody td strong')?.textContent).toBe('B')
  })

  it('has lb-md-table class', () => {
    const block: TableBlock = {
      kind: 'table',
      header: { cells: [cell('a')] },
      rows: [],
      alignments: [null],
    }
    const wrapper = renderTable(block, opts)
    const table = wrapper.querySelector('table') as HTMLTableElement
    expect(table.classList.contains('lb-md-table')).toBe(true)
  })

  it('wraps the table in an overflow-x scroll container', () => {
    const block: TableBlock = {
      kind: 'table',
      header: { cells: [cell('a')] },
      rows: [],
      alignments: [null],
    }
    const wrapper = renderTable(block, opts)
    expect(wrapper.tagName).toBe('DIV')
    expect(wrapper.style.overflowX).toBe('auto')
    expect(wrapper.querySelector('table')).toBeTruthy()
  })

  it('table fills the wrapper but grows past it for wide content', () => {
    const block: TableBlock = {
      kind: 'table',
      header: { cells: [cell('a')] },
      rows: [],
      alignments: [null],
    }
    const table = renderTable(block, opts).querySelector<HTMLTableElement>('table')!
    expect(table.style.width).toBe('100%')
    expect(table.style.minWidth).toBe('max-content')
  })

  it('applies zebra background to even-indexed body rows', () => {
    const block: TableBlock = {
      kind: 'table',
      header: { cells: [cell('h')] },
      rows: [{ cells: [cell('1')] }, { cells: [cell('2')] }, { cells: [cell('3')] }],
      alignments: [null],
    }
    const trs = renderTable(block, opts).querySelectorAll<HTMLTableRowElement>('tbody tr')
    // even-indexed (0, 2) get zebra background
    expect(trs[0].style.backgroundColor).toBe('')
    expect(trs[1].style.backgroundColor).not.toBe('')
  })

  it('makes the thead sticky', () => {
    const block: TableBlock = {
      kind: 'table',
      header: { cells: [cell('h')] },
      rows: [{ cells: [cell('1')] }],
      alignments: [null],
    }
    const thead = renderTable(block, opts).querySelector<HTMLElement>('thead')!
    expect(thead.style.position).toBe('sticky')
    expect(thead.style.top).toBe('0px')
  })

  it('renders <br> inside table cells as line breaks', () => {
    const block: TableBlock = {
      kind: 'table',
      header: { cells: [cell('h')] },
      rows: [{
        cells: [{ children: [
          { kind: 'text', text: 'a' },
          { kind: 'line-break' },
          { kind: 'text', text: 'b' },
        ] }],
      }],
      alignments: [null],
    }
    const td = renderTable(block, opts).querySelector('tbody td')!
    expect(td.querySelector('br')).toBeTruthy()
  })
})
