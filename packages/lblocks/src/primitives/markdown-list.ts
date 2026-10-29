import type {
  ComposeBlockFn,
  ListBlock,
  ListItem,
  MarkdownRenderOptions,
  ParagraphBlock,
} from '../types/markdown-doc.js'
import { renderInline } from './markdown-inline.js'

export function renderList(
  block: ListBlock,
  options: MarkdownRenderOptions,
  composeBlock?: ComposeBlockFn
): HTMLElement {
  const tag = block.ordered ? 'ol' : 'ul'
  const el = document.createElement(tag)
  el.className = 'lb-md-list'
  el.style.color = 'var(--lb-text-primary)'
  el.style.fontFamily = 'var(--lb-font-family)'
  el.style.fontSize = 'var(--lb-font-size-body)'
  el.style.margin = '8px 0'
  el.style.paddingLeft = '24px'
  el.style.lineHeight = '1.55'

  if (block.ordered && block.start !== undefined && block.start !== 1) {
    el.setAttribute('start', String(block.start))
  }

  for (const item of block.items) {
    el.appendChild(renderItem(item, options, composeBlock))
  }
  return el
}

function renderItem(
  item: ListItem,
  options: MarkdownRenderOptions,
  composeBlock?: ComposeBlockFn
): HTMLElement {
  const li = document.createElement('li')
  li.style.marginBottom = '4px'

  if (item.checked !== undefined) {
    const cb = renderTaskCheckbox(item.checked)
    li.appendChild(cb)
    li.className = `lb-md-task-item lb-md-task-item--${item.checked ? 'checked' : 'unchecked'}`
    li.setAttribute('role', 'checkbox')
    li.setAttribute('aria-checked', item.checked ? 'true' : 'false')
    li.style.listStyleType = 'none'
    li.style.marginLeft = '-20px'
  }

  if (composeBlock) {
    for (const child of item.children) {
      li.appendChild(composeBlock(child))
    }
  } else {
    // Fallback: inline-only rendering of paragraph children
    for (const child of item.children) {
      if (child.kind === 'paragraph') {
        const para = child as ParagraphBlock
        const span = document.createElement('span')
        span.appendChild(renderInline(para.children, options))
        li.appendChild(span)
      }
    }
  }

  return li
}

function renderTaskCheckbox(checked: boolean): HTMLElement {
  const cb = document.createElement('span')
  cb.className = 'lb-task-checkbox'
  cb.dataset.state = checked ? 'checked' : 'unchecked'
  cb.setAttribute('aria-hidden', 'true')
  cb.style.position = 'relative'
  cb.style.display = 'inline-flex'
  cb.style.alignItems = 'center'
  cb.style.justifyContent = 'center'
  cb.style.width = '14px'
  cb.style.height = '14px'
  cb.style.marginRight = '6px'
  cb.style.verticalAlign = 'middle'
  cb.style.border = '1px solid var(--lb-border)'
  cb.style.borderRadius = 'var(--lb-radius-sm)'
  cb.style.background = checked ? 'var(--lb-accent)' : 'var(--lb-bg-primary)'
  cb.style.boxSizing = 'border-box'
  cb.style.transform = 'translateY(2px)'

  if (checked) {
    const mark = document.createElement('span')
    mark.className = 'lb-task-checkbox-mark'
    mark.style.display = 'block'
    mark.style.width = '4px'
    mark.style.height = '8px'
    mark.style.borderRight = '2px solid var(--lb-text-on-accent)'
    mark.style.borderBottom = '2px solid var(--lb-text-on-accent)'
    mark.style.transform = 'rotate(45deg) translate(-1px, -1px)'
    cb.appendChild(mark)
  }

  return cb
}
