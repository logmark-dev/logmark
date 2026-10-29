import { DARK_TOKENS, renderMarkdown } from '@logmark/lblocks'
import type { LBlocksDocument, MarkdownRenderOptions } from '@logmark/lblocks'
import { bodyLinkOptions } from './body-links.js'

/**
 * Cockpit-side lblocks rendering wrapper.
 *
 * lblocks' own applyTheme stamps inline --lb-* custom properties in the
 * rgb(var(--token)) form, which expects RGB-triple host tokens (the WPF
 * container convention). This cockpit bridges --lb-* directly onto the
 * hex token contract in app.css - theme-neutral, flipping with
 * :root[data-theme]. Inline props would override that bridge (and break
 * over hex tokens), so every render goes through here: render, then
 * strip the inline tokens so the CSS bridge governs. This is also what
 * makes lblocks output flip with the theme toggle - the old hardcoded
 * { theme: 'dark' } call sites never did.
 */
export function stripInlineLbTokens(container: HTMLElement): void {
  for (const key of Object.keys(DARK_TOKENS)) {
    container.style.removeProperty(key)
  }
}

export function renderMarkdownInto(
  container: HTMLElement,
  doc: LBlocksDocument,
  options: Pick<MarkdownRenderOptions, 'onLinkClick' | 'onTagClick'> = bodyLinkOptions(),
): void {
  renderMarkdown(doc, container, { theme: 'dark', ...options })
  stripInlineLbTokens(container)
}
