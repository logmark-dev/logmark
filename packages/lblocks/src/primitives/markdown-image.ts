import type { ImageBlock, MarkdownRenderOptions } from '../types/markdown-doc.js'

export function renderImage(
  block: ImageBlock,
  options: MarkdownRenderOptions
): HTMLElement {
  const img = document.createElement('img')
  img.className = 'lb-md-image'
  const resolved = options.resolveAssetUrl ? options.resolveAssetUrl(block.src) : block.src
  img.setAttribute('src', resolved)
  img.alt = block.alt
  if (block.title) img.title = block.title
  img.style.maxWidth = '100%'
  img.style.height = 'auto'
  img.style.display = 'block'
  img.style.margin = '12px 0'
  img.style.borderRadius = 'var(--lb-radius)'
  return img
}
