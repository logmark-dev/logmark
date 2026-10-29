import type {
  TableBlock,
  TableCell,
  MarkdownRenderOptions,
} from '../types/markdown-doc.js'
import { renderInline } from './markdown-inline.js'

const HOVER_STYLE_INJECTED = Symbol('lb-md-table-hover')

function ensureHoverStyle(): void {
  if ((document as any)[HOVER_STYLE_INJECTED]) return
  const style = document.createElement('style')
  style.textContent = '.lb-md-table tbody tr:hover { background-color: var(--lb-bg-hover) !important; }'
  document.head.appendChild(style)
  ;(document as any)[HOVER_STYLE_INJECTED] = true
}

export function renderTable(
  block: TableBlock,
  options: MarkdownRenderOptions
): HTMLElement {
  ensureHoverStyle()
  const wrapper = document.createElement('div')
  wrapper.className = 'lb-md-table-wrapper'
  wrapper.style.overflowX = 'auto'
  wrapper.style.maxWidth = '100%'
  wrapper.style.margin = '12px 0'

  const table = document.createElement('table')
  table.className = 'lb-md-table'
  table.style.borderCollapse = 'collapse'
  table.style.fontFamily = 'var(--lb-font-family)'
  table.style.fontSize = 'var(--lb-font-size-body)'
  table.style.color = 'var(--lb-text-primary)'
  // Fill the wrapper when the table is narrower than the view, but grow past
  // it (and trigger the wrapper's overflow-x scroll) when the natural width
  // exceeds the view.
  table.style.width = '100%'
  table.style.minWidth = 'max-content'

  const thead = document.createElement('thead')
  thead.style.position = 'sticky'
  thead.style.top = '0px'
  thead.style.backgroundColor = 'var(--lb-bg-tertiary)'
  thead.style.zIndex = '1'
  const headerRow = document.createElement('tr')
  block.header.cells.forEach((c, i) =>
    headerRow.appendChild(renderCell(c, 'th', block.alignments[i] ?? null, options))
  )
  thead.appendChild(headerRow)
  table.appendChild(thead)

  const tbody = document.createElement('tbody')
  for (const [idx, row] of block.rows.entries()) {
    const tr = document.createElement('tr')
    if (idx % 2 === 1) {
      tr.style.backgroundColor = 'var(--lb-bg-secondary)'
    }
    row.cells.forEach((c, i) =>
      tr.appendChild(renderCell(c, 'td', block.alignments[i] ?? null, options))
    )
    tbody.appendChild(tr)
  }
  table.appendChild(tbody)

  wrapper.appendChild(table)
  return wrapper
}

function renderCell(
  cell: TableCell,
  tag: 'th' | 'td',
  align: 'left' | 'center' | 'right' | null,
  options: MarkdownRenderOptions
): HTMLElement {
  const el = document.createElement(tag)
  el.style.padding = '6px 10px'
  el.style.border = '1px solid'
  el.style.borderColor = 'var(--lb-border-subtle)'
  if (tag === 'th') {
    el.style.backgroundColor = 'var(--lb-bg-tertiary)'
    el.style.fontWeight = '600'
    el.style.textAlign = align ?? 'left'
  } else {
    el.style.textAlign = align ?? 'left'
  }
  el.appendChild(renderInline(cell.children, options))
  return el
}
