import type { EmptyStateData } from '../types/reader.js'

export function renderEmptyState(data: EmptyStateData): HTMLElement {
  const root = document.createElement('div')
  root.className = 'lb-empty-state'
  root.setAttribute('role', 'status')
  root.style.display = 'flex'
  root.style.flexDirection = 'column'
  root.style.alignItems = 'center'
  root.style.justifyContent = 'center'
  root.style.maxWidth = '480px'
  root.style.margin = '0 auto'
  root.style.padding = '64px 24px'
  root.style.textAlign = 'center'

  if (data.glyph) {
    const glyph = document.createElement('div')
    glyph.className = 'lb-empty-state-glyph'
    glyph.textContent = data.glyph
    glyph.style.fontFamily = 'var(--lb-font-mono)'
    glyph.style.fontSize = '40px'
    glyph.style.lineHeight = '40px'
    glyph.style.color = 'var(--lb-text-dim)'
    glyph.style.marginBottom = '16px'
    root.appendChild(glyph)
  }

  if (data.eyebrow) {
    const eyebrow = document.createElement('div')
    eyebrow.className = 'lb-empty-state-eyebrow'
    eyebrow.textContent = data.eyebrow
    eyebrow.style.fontSize = 'var(--lb-font-size-label)'
    eyebrow.style.fontWeight = '600'
    eyebrow.style.textTransform = 'uppercase'
    eyebrow.style.letterSpacing = '0.02em'
    eyebrow.style.color = 'var(--lb-accent)'
    eyebrow.style.marginBottom = '4px'
    root.appendChild(eyebrow)
  }

  const title = document.createElement('div')
  title.className = 'lb-empty-state-title'
  title.textContent = data.title
  title.style.maxWidth = '360px'
  title.style.fontSize = 'var(--lb-font-size-header)'
  title.style.fontWeight = '600'
  title.style.lineHeight = '1.25'
  title.style.color = 'var(--lb-text-primary)'
  title.style.marginBottom = data.sub ? '8px' : '0'
  root.appendChild(title)

  if (data.sub) {
    const sub = document.createElement('div')
    sub.className = 'lb-empty-state-sub'
    sub.textContent = data.sub
    sub.style.maxWidth = '380px'
    sub.style.fontSize = 'var(--lb-font-size-small)'
    sub.style.lineHeight = '1.55'
    sub.style.color = 'var(--lb-text-muted)'
    root.appendChild(sub)
  }

  if (data.kbdHint) {
    const kbd = document.createElement('kbd')
    kbd.className = 'lb-empty-state-kbd'
    kbd.textContent = data.kbdHint
    kbd.style.marginTop = '16px'
    kbd.style.padding = '2px 6px'
    kbd.style.border = '1px solid var(--lb-border-subtle)'
    kbd.style.borderRadius = 'var(--lb-radius-sm)'
    kbd.style.background = 'var(--lb-surface)'
    kbd.style.color = 'var(--lb-text-secondary)'
    kbd.style.fontFamily = 'var(--lb-font-mono)'
    kbd.style.fontSize = 'var(--lb-font-size-label)'
    root.appendChild(kbd)
  }

  return root
}
