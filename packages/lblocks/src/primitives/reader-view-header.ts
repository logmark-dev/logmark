import type { ReaderViewHeaderData } from '../types/reader.js'

export function renderReaderViewHeader(data: ReaderViewHeaderData): HTMLElement {
  const root = document.createElement('div')
  root.className = 'lb-reader-view-header'
  root.style.display = 'grid'
  root.style.gridTemplateColumns = data.badge ? 'minmax(0, 1fr) auto' : 'minmax(0, 1fr)'
  root.style.alignItems = 'start'
  root.style.gap = '12px'
  root.style.minWidth = '0'

  const head = document.createElement('div')
  head.className = 'lb-reader-view-header-head'
  head.style.display = 'grid'
  head.style.gridTemplateColumns = '4px minmax(0, 1fr)'
  head.style.gap = '12px'
  head.style.minWidth = '0'
  const rail = document.createElement('span')
  rail.className = 'lb-reader-view-header-rail'
  rail.setAttribute('aria-hidden', 'true')
  rail.style.width = '4px'
  rail.style.minHeight = '54px'
  rail.style.borderRadius = 'var(--lb-radius-full)'
  rail.style.background = 'var(--lb-accent)'
  head.appendChild(rail)

  const copy = document.createElement('div')
  copy.className = 'lb-reader-view-header-copy'
  copy.style.minWidth = '0'
  if (data.eyebrow) {
    const eyebrow = document.createElement('p')
    eyebrow.className = 'lb-reader-view-header-eyebrow'
    eyebrow.textContent = data.eyebrow
    eyebrow.style.margin = '0 0 3px'
    eyebrow.style.color = 'var(--lb-accent)'
    eyebrow.style.fontFamily = 'var(--lb-font-mono)'
    eyebrow.style.fontSize = 'var(--lb-font-size-label)'
    eyebrow.style.fontWeight = '600'
    eyebrow.style.textTransform = 'uppercase'
    eyebrow.style.letterSpacing = '0.02em'
    copy.appendChild(eyebrow)
  }
  const title = document.createElement('h2')
  title.className = 'lb-reader-view-header-title'
  title.textContent = data.title
  title.style.margin = '0'
  title.style.minWidth = '0'
  title.style.overflowWrap = 'anywhere'
  title.style.color = 'var(--lb-text-primary)'
  title.style.fontSize = 'var(--lb-font-size-title)'
  title.style.fontWeight = '600'
  title.style.lineHeight = '1.18'
  copy.appendChild(title)
  if (data.subtitle) {
    const subtitle = document.createElement('p')
    subtitle.className = 'lb-reader-view-header-subtitle'
    subtitle.textContent = data.subtitle
    subtitle.style.margin = '5px 0 0'
    subtitle.style.maxWidth = '72ch'
    subtitle.style.color = 'var(--lb-text-muted)'
    subtitle.style.fontSize = 'var(--lb-font-size-body)'
    subtitle.style.lineHeight = '1.45'
    copy.appendChild(subtitle)
  }
  if (data.countLabel) {
    const count = document.createElement('p')
    count.className = 'lb-reader-view-header-count'
    count.textContent = data.countLabel
    count.style.margin = data.subtitle ? '4px 0 0' : '5px 0 0'
    count.style.color = 'var(--lb-text-muted)'
    count.style.fontFamily = 'var(--lb-font-mono)'
    count.style.fontSize = 'var(--lb-font-size-label)'
    copy.appendChild(count)
  }
  head.appendChild(copy)
  root.appendChild(head)

  if (data.badge) {
    const badge = document.createElement('span')
    badge.className = 'lb-reader-view-header-badge'
    badge.textContent = data.badge
    badge.style.display = 'inline-flex'
    badge.style.alignItems = 'center'
    badge.style.minHeight = '24px'
    badge.style.padding = '3px 8px'
    badge.style.border = '1px solid var(--lb-border-subtle)'
    badge.style.borderRadius = 'var(--lb-radius-sm)'
    badge.style.background = 'var(--lb-bg-secondary)'
    badge.style.color = 'var(--lb-text-secondary)'
    badge.style.fontSize = 'var(--lb-font-size-label)'
    badge.style.fontWeight = '600'
    badge.style.whiteSpace = 'nowrap'
    root.appendChild(badge)
  }

  return root
}
