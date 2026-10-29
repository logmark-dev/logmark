import type { PrimitiveOptions } from '../types/primitives.js'
import type { SearchResultListData } from '../types/reader.js'

export function renderSearchResultList(
  data: SearchResultListData,
  options: Partial<PrimitiveOptions> = {}
): HTMLElement {
  const root = document.createElement('div')
  root.className = 'lb-search-result-list'
  root.style.display = 'flex'
  root.style.flexDirection = 'column'
  root.style.gap = '6px'

  const summary = document.createElement('div')
  summary.className = 'lb-search-result-list-summary'
  summary.textContent = data.query
    ? `${data.results.length} results for "${data.query}"`
    : `${data.results.length} results`
  summary.style.color = 'var(--lb-text-muted)'
  summary.style.fontSize = '12px'
  root.appendChild(summary)

  if (data.results.length === 0) {
    const empty = document.createElement('div')
    empty.className = 'lb-search-result-list-empty'
    empty.textContent = 'No results'
    empty.style.padding = '10px 0'
    empty.style.color = 'var(--lb-text-muted)'
    root.appendChild(empty)
    return root
  }

  for (const result of data.results) {
    const row = document.createElement('div')
    row.className = 'lb-search-result-list-item'
    row.dataset.resultId = result.id
    row.style.padding = options.density === 'compact' ? '8px 10px' : '10px 12px'
    row.style.border = '1px solid var(--lb-border)'
    row.style.borderRadius = 'var(--lb-radius)'
    row.style.background = 'var(--lb-bg-secondary)'
    row.style.cursor = options.onAction ? 'pointer' : 'default'
    row.style.outline = 'none'

    const emitOpen = () => {
      options.onAction?.({
        type: 'search-result-open',
        target: result.id,
        source: 'search-result-list',
      })
    }

    const title = document.createElement('div')
    title.className = 'lb-search-result-list-title'
    title.textContent = result.title
    title.style.fontWeight = '600'
    title.style.color = 'var(--lb-text-primary)'
    row.appendChild(title)

    const excerpt = document.createElement('div')
    excerpt.className = 'lb-search-result-list-excerpt'
    excerpt.textContent = result.excerpt
    excerpt.style.marginTop = '4px'
    excerpt.style.color = 'var(--lb-text-secondary)'
    row.appendChild(excerpt)

    const meta = document.createElement('div')
    meta.className = 'lb-search-result-list-meta'
    meta.style.display = 'flex'
    meta.style.flexWrap = 'wrap'
    meta.style.gap = '8px'
    meta.style.marginTop = '6px'
    meta.style.fontSize = '12px'
    meta.style.color = 'var(--lb-text-muted)'

    if (result.route) {
      const route = document.createElement('span')
      route.textContent = result.route
      meta.appendChild(route)
    }

    for (const tag of result.tags ?? []) {
      const chip = options.onTagClick
        ? document.createElement('button')
        : document.createElement('span')
      if (chip instanceof HTMLButtonElement) {
        chip.type = 'button'
        chip.setAttribute('aria-label', `Filter by #${tag}`)
        chip.style.border = '0'
        chip.style.background = 'transparent'
        chip.style.color = 'var(--lb-signal-positive)'
        chip.style.cursor = 'pointer'
        chip.style.font = 'inherit'
        chip.style.padding = '0'
        chip.addEventListener('click', (event) => {
          event.preventDefault()
          event.stopPropagation()
          options.onTagClick?.(tag)
        })
      }
      chip.className = 'lb-search-result-list-tag'
      chip.textContent = `#${tag}`
      meta.appendChild(chip)
    }

    if (meta.childNodes.length > 0) row.appendChild(meta)

    if (options.onAction) {
      row.setAttribute('role', 'button')
      row.tabIndex = 0
      row.addEventListener('click', emitOpen)
      row.addEventListener('keydown', (event) => {
        if (event.key !== 'Enter' && event.key !== ' ') return
        event.preventDefault()
        emitOpen()
      })
    }

    root.appendChild(row)
  }

  return root
}
