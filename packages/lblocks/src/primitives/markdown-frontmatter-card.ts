import type { FrontmatterBlock, MarkdownRenderOptions } from '../types/markdown-doc.js'

export function renderFrontmatterCard(
  block: FrontmatterBlock,
  _options: MarkdownRenderOptions
): HTMLElement {
  const card = document.createElement('div')
  card.className = 'lb-md-frontmatter'
  card.style.backgroundColor = 'var(--lb-bg-secondary)'
  card.style.color = 'var(--lb-text-secondary)'
  card.style.fontSize = 'var(--lb-font-size-small)'
  card.style.fontFamily = 'var(--lb-font-family)'
  card.style.padding = '8px 12px'
  card.style.borderRadius = 'var(--lb-radius)'
  card.style.margin = '0 0 16px 0'
  card.style.borderLeft = '3px solid'
  card.style.borderLeftColor = 'var(--lb-border)'

  const dl = document.createElement('dl')
  dl.style.margin = '0'
  dl.style.display = 'grid'
  dl.style.gridTemplateColumns = 'auto 1fr'
  dl.style.gap = '4px 12px'

  for (const entry of block.entries) {
    const dt = document.createElement('dt')
    dt.textContent = entry.key
    dt.style.fontWeight = '600'
    dt.style.color = 'var(--lb-text-muted)'
    dt.style.margin = '0'
    dl.appendChild(dt)

    const dd = document.createElement('dd')
    dd.textContent = entry.value
    dd.style.margin = '0'
    dd.style.color = 'var(--lb-text-primary)'
    dl.appendChild(dd)
  }

  card.appendChild(dl)
  return card
}
