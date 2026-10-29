import type { LinkClickEvent } from '@logmark/lblocks'
import { navigate } from './nav.js'

/**
 * Standard link/tag handlers for reading-layer bodies (release review
 * P0-3): wikilinks and relative .md links open the subpage viewer; tags
 * search; external links open a new tab. Wire these into EVERY
 * renderMarkdownInto / renderConceptPage call - a body without them
 * regresses to inert text.
 */
export function bodyLinkOptions(): {
  onLinkClick: (event: LinkClickEvent) => void
  onTagClick: (tag: string) => void
} {
  return {
    onLinkClick: (event) => {
      if (event.kind === 'external') {
        window.open(event.href, '_blank', 'noopener')
        return
      }
      navigate(`/pages?path=${encodeURIComponent(event.href)}`)
    },
    onTagClick: (tag) => {
      navigate(`/search?q=${encodeURIComponent('#' + tag)}`)
    },
  }
}
