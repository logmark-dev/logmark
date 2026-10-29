import type { PrimitiveOptions } from '../types/primitives.js'
import type {
  TagPageMetaSidebarData,
  TagPageMetaSidebarVault,
  TagPageMetaSidebarCreator,
} from '../types/reader.js'
import { renderAttributionChip } from './attribution-chip.js'

function sectionHeading(text: string): HTMLElement {
  const h = document.createElement('h3')
  h.className = 'lb-tag-meta-sidebar-heading'
  h.textContent = text
  h.style.margin = '0 0 8px'
  h.style.fontSize = 'var(--lb-font-size-small)'
  h.style.fontWeight = '700'
  h.style.textTransform = 'uppercase'
  h.style.letterSpacing = '0.06em'
  h.style.color = 'var(--lb-text-secondary)'
  return h
}

function sectionWrap(): HTMLElement {
  const sec = document.createElement('section')
  sec.className = 'lb-tag-meta-sidebar-section'
  sec.style.marginBottom = '20px'
  return sec
}

function vaultRow(vault: TagPageMetaSidebarVault): HTMLElement {
  const row = document.createElement('div')
  row.className = 'lb-tag-meta-sidebar-vault-row'
  row.style.display = 'flex'
  row.style.alignItems = 'center'
  row.style.gap = '8px'
  row.style.padding = '6px 0'
  row.style.borderBottom = '1px solid var(--lb-border-subtle, var(--lb-border))'
  const body = document.createElement('div')
  body.style.flex = '1'
  body.style.minWidth = '0'
  const titleEl = document.createElement(vault.href ? 'a' : 'span')
  titleEl.className = 'lb-tag-meta-sidebar-vault-title'
  titleEl.textContent = vault.title
  if (vault.href) {
    titleEl.setAttribute('href', vault.href)
    titleEl.style.textDecoration = 'none'
  }
  titleEl.style.display = 'block'
  titleEl.style.fontSize = 'var(--lb-font-size-body)'
  titleEl.style.fontWeight = '500'
  titleEl.style.color = 'var(--lb-text-primary)'
  titleEl.style.overflow = 'hidden'
  titleEl.style.textOverflow = 'ellipsis'
  titleEl.style.whiteSpace = 'nowrap'
  body.appendChild(titleEl)
  const meta = document.createElement('div')
  meta.style.display = 'flex'
  meta.style.alignItems = 'center'
  meta.style.gap = '6px'
  meta.style.marginTop = '2px'
  const handle = document.createElement('span')
  handle.textContent = `@${vault.publisherHandle}`
  handle.style.fontSize = 'var(--lb-font-size-small)'
  handle.style.color = 'var(--lb-text-secondary)'
  meta.appendChild(handle)
  meta.appendChild(renderAttributionChip({ kind: vault.publisherKind }))
  const count = document.createElement('span')
  count.textContent = `${vault.entryCount}`
  count.style.fontSize = 'var(--lb-font-size-small)'
  count.style.color = 'var(--lb-text-dim)'
  count.style.marginLeft = 'auto'
  meta.appendChild(count)
  body.appendChild(meta)
  row.appendChild(body)
  return row
}

function creatorRow(creator: TagPageMetaSidebarCreator): HTMLElement {
  const row = document.createElement('div')
  row.className = 'lb-tag-meta-sidebar-creator-row'
  row.style.display = 'flex'
  row.style.alignItems = 'center'
  row.style.gap = '8px'
  row.style.padding = '5px 0'
  row.style.borderBottom = '1px solid var(--lb-border-subtle, var(--lb-border))'
  const handleEl = document.createElement(creator.href ? 'a' : 'span')
  handleEl.className = 'lb-tag-meta-sidebar-creator-handle'
  handleEl.textContent = `@${creator.handle}`
  if (creator.href) {
    handleEl.setAttribute('href', creator.href)
    handleEl.style.textDecoration = 'none'
  }
  handleEl.style.flex = '1'
  handleEl.style.fontSize = 'var(--lb-font-size-body)'
  handleEl.style.fontWeight = '500'
  handleEl.style.color = 'var(--lb-text-primary)'
  row.appendChild(handleEl)
  row.appendChild(renderAttributionChip({ kind: creator.kind }))
  const count = document.createElement('span')
  count.textContent = `${creator.entryCount}`
  count.style.fontSize = 'var(--lb-font-size-small)'
  count.style.color = 'var(--lb-text-dim)'
  row.appendChild(count)
  return row
}

/**
 * tag-page-meta-sidebar -- Right column on the tag page.
 *
 * Three sections: vaults containing this tag, publishers using it,
 * related tags. Plus an optional subscribe affordance hosted by the
 * surrounding surface (not this primitive).
 *
 * Anatomy:
 *   Three vertically stacked sections, each with an eyebrow heading +
 *   a list of rows.
 *
 * Tokens:
 *   Sections use --lb-bg / flat (no shadow). Vault rows show the
 *   attribution chip; publisher rows show handle + count + attribution
 *   chip. Section eyebrow: --lb-text-secondary, 700 weight,
 *   letter-spacing 0.06em, uppercase.
 *
 * States:
 *   Each row hovers to --lb-card-elevated (host CSS). Row dividers via
 *   --lb-border-subtle bottom border.
 *
 * Density:
 *   Sections stack. Each section is recommended to cap at 6 visible
 *   items (overflow handled at the host -- this primitive renders
 *   exactly what data carries).
 *
 * Content:
 *   Vault section: vault title + attribution chip + entry count.
 *   Publisher section: handle + AGENTIC pill via attribution-chip +
 *   entry count.
 *   Related tags: wrap of #tag pills.
 *
 * A11y:
 *   <aside> wrapper for landmark role. Each row is a single button or
 *   anchor (when href provided).
 *
 * Motion:
 *   Standard hover transitions via host CSS.
 *
 * Responsiveness:
 *   On viewports under 960px the sidebar flows below the main column
 *   (host CSS); each section becomes a normal-flow block.
 *
 * Composition:
 *   Tag page (3.3 in the surface inventory) only.
 *
 * Variants:
 *   None.
 *
 * Data:
 *   { vaults?: TagPageMetaSidebarVault[],
 *     creators?: TagPageMetaSidebarCreator[],
 *     relatedTags?: Array<{ tag, coCount, href? }> }
 *
 * Boundaries:
 *   Each section can be empty; the primitive simply omits the section
 *   (host can render a polite empty-state on its own if needed).
 *
 * Anti-patterns:
 *   Don't add a fourth section without product alignment. The sidebar
 *   is short on purpose. Never include the subscribe button in this
 *   primitive -- it's a separate surface concern.
 */
export function renderTagPageMetaSidebar(
  data: TagPageMetaSidebarData,
  _options: Partial<PrimitiveOptions> = {}
): HTMLElement {
  const root = document.createElement('aside')
  root.className = 'lb-tag-page-meta-sidebar'
  if (data.vaults && data.vaults.length > 0) {
    const sec = sectionWrap()
    sec.appendChild(sectionHeading('Vaults'))
    for (const vault of data.vaults) {
      sec.appendChild(vaultRow(vault))
    }
    root.appendChild(sec)
  }
  if (data.creators && data.creators.length > 0) {
    const sec = sectionWrap()
    sec.appendChild(sectionHeading('Creators'))
    for (const creator of data.creators) {
      sec.appendChild(creatorRow(creator))
    }
    root.appendChild(sec)
  }
  if (data.relatedTags && data.relatedTags.length > 0) {
    const sec = sectionWrap()
    sec.appendChild(sectionHeading('Related'))
    const tagWrap = document.createElement('div')
    tagWrap.className = 'lb-tag-meta-sidebar-related-tags'
    tagWrap.style.display = 'flex'
    tagWrap.style.flexWrap = 'wrap'
    tagWrap.style.gap = '6px'
    for (const rt of data.relatedTags) {
      const tagEl = document.createElement(rt.href ? 'a' : 'span')
      tagEl.className = 'lb-tag-meta-sidebar-related-tag'
      tagEl.textContent = `#${rt.tag}`
      if (rt.href) {
        tagEl.setAttribute('href', rt.href)
        tagEl.style.textDecoration = 'none'
      }
      tagEl.style.fontSize = 'var(--lb-font-size-small)'
      tagEl.style.fontWeight = '500'
      tagEl.style.color = 'var(--lb-signal-positive)'
      tagEl.style.padding = '2px 6px'
      tagEl.style.border = '1px solid color-mix(in srgb, var(--lb-signal-positive) 25%, transparent)'
      tagEl.style.borderRadius = '9999px'
      tagEl.style.background = 'color-mix(in srgb, var(--lb-signal-positive) 6%, transparent)'
      tagWrap.appendChild(tagEl)
    }
    sec.appendChild(tagWrap)
    root.appendChild(sec)
  }
  return root
}
