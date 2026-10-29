import type { PrimitiveOptions } from '../types/primitives.js'
import type { ViewerSectionTitleData } from '../types/reader.js'

export function renderViewerSectionTitle(
  data: ViewerSectionTitleData,
  options: Partial<PrimitiveOptions> = {}
): HTMLElement {
  const root = document.createElement('div')
  root.className = 'lb-viewer-section-title'
  root.style.display = 'flex'
  root.style.alignItems = 'center'
  root.style.justifyContent = 'space-between'
  root.style.gap = '12px'
  root.style.minWidth = '0'

  const title = document.createElement('h3')
  title.className = 'lb-viewer-section-title-text'
  title.textContent = data.title
  title.style.margin = '0'
  title.style.fontSize = 'var(--lb-font-size-body)'
  title.style.fontWeight = '600'
  title.style.lineHeight = '1.25'
  title.style.color = 'var(--lb-text-primary)'
  root.appendChild(title)

  const metaText = data.actionLabel ?? data.countLabel
  if (metaText) {
    const meta = document.createElement(data.actionLabel ? 'button' : 'span')
    meta.className = data.actionLabel
      ? 'lb-viewer-section-title-action'
      : 'lb-viewer-section-title-count'
    meta.textContent = metaText
    meta.style.flex = '0 0 auto'
    meta.style.borderRadius = 'var(--lb-radius-sm)'
    meta.style.fontSize = 'var(--lb-font-size-label)'
    meta.style.color = data.actionLabel ? 'var(--lb-accent)' : 'var(--lb-text-muted)'
    meta.style.fontFamily = data.actionLabel ? 'var(--lb-font-family)' : 'var(--lb-font-mono)'
    meta.style.padding = data.actionLabel ? '2px 6px' : '0'
    if (meta instanceof HTMLButtonElement) {
      meta.type = 'button'
      meta.style.border = '1px solid color-mix(in srgb, var(--lb-accent) 30%, transparent)'
      meta.style.background = 'transparent'
      meta.style.cursor = options.onAction ? 'pointer' : 'default'
      if (options.onAction) {
        meta.addEventListener('click', () => {
          options.onAction?.({
            type: 'section-title-action',
            target: data.title,
            source: 'viewer-section-title',
          })
        })
      }
    }
    root.appendChild(meta)
  }

  return root
}
