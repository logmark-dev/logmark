import { describe, it, expect, vi } from 'vitest'
import { renderImage } from '../src/primitives/markdown-image.js'
import type { ImageBlock, MarkdownRenderOptions } from '../src/types/markdown-doc.js'

const opts: MarkdownRenderOptions = { theme: 'dark' }

describe('markdown-image primitive', () => {
  it('renders as <img>', () => {
    const block: ImageBlock = { kind: 'image', src: 'pic.png', alt: 'a' }
    expect(renderImage(block, opts).tagName).toBe('IMG')
  })

  it('passes alt and title attributes', () => {
    const block: ImageBlock = { kind: 'image', src: 'pic.png', alt: 'a pic', title: 'Hovered' }
    const el = renderImage(block, opts) as HTMLImageElement
    expect(el.alt).toBe('a pic')
    expect(el.title).toBe('Hovered')
  })

  it('uses resolveAssetUrl when provided', () => {
    const resolveAssetUrl = vi.fn((src: string) => `https://host.local/${src}`)
    const block: ImageBlock = { kind: 'image', src: 'pic.png', alt: 'a' }
    const el = renderImage(block, { ...opts, resolveAssetUrl }) as HTMLImageElement
    expect(resolveAssetUrl).toHaveBeenCalledWith('pic.png')
    expect(el.getAttribute('src')).toBe('https://host.local/pic.png')
  })

  it('falls through to raw src when resolveAssetUrl not provided', () => {
    const block: ImageBlock = { kind: 'image', src: 'pic.png', alt: 'a' }
    const el = renderImage(block, opts) as HTMLImageElement
    expect(el.getAttribute('src')).toBe('pic.png')
  })

  it('has lb-md-image class', () => {
    const block: ImageBlock = { kind: 'image', src: 'pic.png', alt: 'a' }
    expect(renderImage(block, opts).classList.contains('lb-md-image')).toBe(true)
  })

  it('caps max-width and preserves aspect ratio', () => {
    const block: ImageBlock = { kind: 'image', src: 'pic.png', alt: 'a' }
    const el = renderImage(block, opts) as HTMLImageElement
    expect(el.style.maxWidth).toBe('100%')
    expect(el.style.height).toBe('auto')
  })
})
