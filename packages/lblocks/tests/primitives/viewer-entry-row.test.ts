import { describe, expect, it, vi } from 'vitest'
import { renderViewerEntryRow } from '../../src/primitives/viewer-entry-row.js'
import type { ViewerEntryRowData, ViewerEntryRowEntry } from '../../src/types/reader.js'

const baseEntry: ViewerEntryRowEntry = {
  id: 'entry-1',
  type: 'task',
  timestamp: '2026-05-20T12:00:00Z',
  preview: 'Wire the entry-row primitive into [[lblocks 0.6]] #cockpit',
  tags: ['cockpit', 'lblocks'],
  openUrl: '/@alex/vault/e/entry-1',
  attribution: { handle: 'alex', vaultTitle: 'demo-vault' },
}

const secondEntry: ViewerEntryRowEntry = {
  id: 'entry-2',
  type: 'decision',
  timestamp: '2026-05-21T12:00:00Z',
  preview: 'Ink viewer stays out of the cockpit',
  tags: [],
}

// Rendered by the pre-cockpit implementation (commit state before the
// `variant: 'cockpit'` extension) with exactly the data/options used in
// the guard test below. The Ink reader consumes the default variant in
// production; any diff here is a regression, not a snapshot to refresh
// casually. Timestamps sit at 12:00Z so formatDate() yields the same
// calendar day in every timezone from UTC-12 to UTC+11.
const BASELINE_DEFAULT_HTML =
  '<ul class="lb-viewer-entry-row-list" style="display: flex; flex-direction: column; margin: 0px; padding: 0px;"><li class="lb-viewer-entry-row-item" data-entry-id="entry-1" data-entry-type="task" style="list-style: none; --lb-entry-row-color: var(--lb-entry-task);"><article class="lb-viewer-entry-row ve-row ink-reader-entry-row" style="display: grid; grid-template-columns: 1fr auto; align-items: start; gap: 12px 18px; padding: 12px 0px; border-bottom: 1px solid var(--lb-border-subtle); position: relative; cursor: pointer; transition: background 150ms ease-out;" tabindex="0" role="button" aria-label="Preview entry from May 20, 2026"><div class="lb-viewer-entry-row-body" style="min-width: 0;"><div class="lb-viewer-entry-row-content ve-content ink-reader-entry-content" style="font-size: var(--lb-font-size-lead, var(--lb-font-size-body)); line-height: 1.45; color: var(--lb-text-primary); display: -webkit-box; -webkit-line-clamp: 2; overflow: hidden;">Wire the entry-row primitive into <a class="lb-viewer-entry-row-link-inline" href="lblocks 0.6" style="color: var(--lb-accent); text-decoration: none;">lblocks 0.6</a> <button class="lb-viewer-entry-row-inline-tag lb-viewer-entry-row-inline-tag-button" style="color: var(--lb-accent); border: 0px; background: transparent; padding: 0px; cursor: pointer;" type="button" aria-label="Filter by #cockpit" title="Filter by #cockpit">#cockpit</button></div><div class="lb-viewer-entry-row-meta ve-meta ink-reader-entry-meta" style="display: flex; flex-wrap: wrap; align-items: center; gap: 12px; margin-top: 8px; font-size: var(--lb-font-size-meta, var(--lb-font-size-small)); color: var(--lb-text-muted);"><button type="button" class="lb-viewer-entry-row-type ve-type lb-viewer-entry-row-type-button" style="color: var(--lb-entry-task); font-family: var(--lb-font-mono); font-size: 10px; font-weight: 600; letter-spacing: 0.08em; text-transform: uppercase; border: 0px; background: transparent; padding: 0px; cursor: pointer;" aria-label="Filter by task entries" title="Filter by task entries">task</button><span aria-hidden="true" style="color: var(--lb-text-dim);">·</span><span class="lb-viewer-entry-row-attribution" style="display: inline-flex; align-items: center; gap: 6px;"><span class="lb-viewer-entry-row-handle" style="color: var(--lb-accent);">@alex</span><span aria-hidden="true" style="color: var(--lb-text-dim);">/</span><span class="lb-viewer-entry-row-vault" style="color: var(--lb-text-muted);">demo-vault</span></span><span aria-hidden="true" style="color: var(--lb-text-dim);">·</span><time class="lb-viewer-entry-row-time" datetime="2026-05-20T12:00:00Z">May 20, 2026</time><button class="lb-viewer-entry-row-tag lb-viewer-entry-row-tag-button" style="color: var(--lb-accent); border: 0px; background: transparent; padding: 0px; cursor: pointer;" type="button" aria-label="Filter by #cockpit">#cockpit</button><button class="lb-viewer-entry-row-tag lb-viewer-entry-row-tag-button" style="color: var(--lb-accent); border: 0px; background: transparent; padding: 0px; cursor: pointer;" type="button" aria-label="Filter by #lblocks">#lblocks</button></div></div><a class="lb-viewer-entry-row-open ve-link" href="/@alex/vault/e/entry-1" style="color: var(--lb-text-muted); font-size: var(--lb-font-size-meta, var(--lb-font-size-small)); white-space: nowrap; text-decoration: none; align-self: start; padding-top: 2px;">Open <svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" style="vertical-align: -1px; margin-left: 4px;"><path d="M7 17 17 7"></path><path d="M7 7h10v10"></path></svg></a></article></li><li class="lb-viewer-entry-row-item" data-entry-id="entry-2" data-entry-type="decision" style="list-style: none; --lb-entry-row-color: var(--lb-entry-decision);"><article class="lb-viewer-entry-row ve-row ink-reader-entry-row" style="display: grid; grid-template-columns: 1fr auto; align-items: start; gap: 12px 18px; padding: 12px 0px; border-bottom: 1px solid var(--lb-border-subtle); position: relative; cursor: default; transition: background 150ms ease-out;" tabindex="0" role="button" aria-label="Preview entry from May 21, 2026"><div class="lb-viewer-entry-row-body" style="min-width: 0;"><div class="lb-viewer-entry-row-content ve-content ink-reader-entry-content" style="font-size: var(--lb-font-size-lead, var(--lb-font-size-body)); line-height: 1.45; color: var(--lb-text-primary); display: -webkit-box; -webkit-line-clamp: 2; overflow: hidden;">Ink viewer stays out of the cockpit</div><div class="lb-viewer-entry-row-meta ve-meta ink-reader-entry-meta" style="display: flex; flex-wrap: wrap; align-items: center; gap: 12px; margin-top: 8px; font-size: var(--lb-font-size-meta, var(--lb-font-size-small)); color: var(--lb-text-muted);"><button type="button" class="lb-viewer-entry-row-type ve-type lb-viewer-entry-row-type-button" style="color: var(--lb-entry-decision); font-family: var(--lb-font-mono); font-size: 10px; font-weight: 600; letter-spacing: 0.08em; text-transform: uppercase; border: 0px; background: transparent; padding: 0px; cursor: pointer;" aria-label="Filter by decision entries" title="Filter by decision entries">decision</button><span aria-hidden="true" style="color: var(--lb-text-dim);">·</span><time class="lb-viewer-entry-row-time" datetime="2026-05-21T12:00:00Z">May 21, 2026</time></div></div></article></li></ul>'

function cockpitData(entry: Partial<ViewerEntryRowEntry> = {}): ViewerEntryRowData {
  return {
    variant: 'cockpit',
    showAttribution: false,
    entries: [{ ...baseEntry, ...entry }],
  }
}

describe('renderViewerEntryRow', () => {
  it('default variant output is byte-identical to the pre-cockpit baseline', () => {
    const el = renderViewerEntryRow(
      { entries: [baseEntry, secondEntry] },
      { density: 'compact', onTagClick: () => {}, onAction: () => {} }
    )
    expect(el.outerHTML).toBe(BASELINE_DEFAULT_HTML)
  })

  it('default variant never renders cockpit affordances', () => {
    const el = renderViewerEntryRow(
      { entries: [{ ...baseEntry, route: 'domains/ai-llm', state: 'done' }] },
      { onAction: () => {} }
    )
    expect(el.dataset.variant).toBeUndefined()
    expect(el.querySelector('.lb-viewer-entry-row-cockpit')).toBeNull()
    expect(el.querySelector('.lb-viewer-entry-row-state')).toBeNull()
    expect(el.querySelector('.lb-viewer-entry-row-trailing')).toBeNull()
    expect(el.querySelector('.lb-viewer-entry-row-open')).toBeTruthy()
  })

  it('cockpit variant renders the entry-type rim and the route + time trailing column', () => {
    const el = renderViewerEntryRow(
      cockpitData({ route: 'domains/ai-llm', timeLabel: '20:10' }),
      { onAction: () => {} }
    )
    expect(el.dataset.variant).toBe('cockpit')

    const article = el.querySelector<HTMLElement>('article.lb-viewer-entry-row')
    expect(article?.classList.contains('lb-viewer-entry-row-cockpit')).toBe(true)
    expect(article?.style.borderLeft).toBe('2px solid var(--lb-entry-row-color)')
    expect(article?.style.paddingLeft).toBe('14px')

    const trailing = el.querySelector<HTMLElement>('.lb-viewer-entry-row-trailing')
    expect(trailing).toBeTruthy()
    expect(trailing?.tagName).toBe('A')
    expect(trailing?.getAttribute('href')).toBe('/@alex/vault/e/entry-1')
    expect(trailing?.style.fontFamily).toBe('var(--lb-font-mono)')
    expect(trailing?.style.textAlign).toBe('right')
    const route = trailing?.querySelector<HTMLElement>('.lb-viewer-entry-row-route')
    expect(route?.textContent).toBe('domains/ai-llm')
    expect(route?.style.color).toBe('var(--lb-text-muted)')
    const time = trailing?.querySelector<HTMLElement>('.lb-viewer-entry-row-time')
    expect(time?.textContent).toBe('20:10')
    expect(time?.style.color).toBe('var(--lb-text-dim)')
    expect(trailing?.textContent).toBe('domains/ai-llm·20:10')

    // The Open arrow link is replaced; the meta date moved to the column.
    expect(el.querySelector('.lb-viewer-entry-row-open')).toBeNull()
    expect(el.querySelector('.lb-viewer-entry-row-meta .lb-viewer-entry-row-time')).toBeNull()
    // Type pill and tag chips keep their existing rendering.
    expect(el.querySelector('.lb-viewer-entry-row-type')?.textContent).toBe('task')
    expect(el.querySelectorAll('.lb-viewer-entry-row-tag')).toHaveLength(2)
  })

  it('cockpit trailing column fires entry-open; row body keeps entry-focus', () => {
    const onAction = vi.fn()
    const el = renderViewerEntryRow(cockpitData({ route: 'domains/ai-llm' }), { onAction })

    el.querySelector<HTMLElement>('.lb-viewer-entry-row-trailing')?.click()
    expect(onAction).toHaveBeenLastCalledWith({
      type: 'entry-open',
      target: '/@alex/vault/e/entry-1',
      source: 'viewer-entry-row',
    })

    el.querySelector<HTMLElement>('article.lb-viewer-entry-row')?.click()
    expect(onAction).toHaveBeenLastCalledWith({
      type: 'entry-focus',
      target: 'entry-1',
      source: 'viewer-entry-row',
    })
  })

  it('cockpit trailing column is a span when openUrl is absent', () => {
    const el = renderViewerEntryRow(cockpitData({ openUrl: undefined, timeLabel: '2h ago' }), {})
    const trailing = el.querySelector<HTMLElement>('.lb-viewer-entry-row-trailing')
    expect(trailing?.tagName).toBe('SPAN')
    expect(trailing?.textContent).toBe('2h ago')
  })

  it('cockpit done state renders a read-only span glyph and strikes the content', () => {
    const el = renderViewerEntryRow(cockpitData({ state: 'done' }), {})
    const glyph = el.querySelector<HTMLElement>('.lb-viewer-entry-row-state')
    expect(glyph).toBeTruthy()
    expect(glyph?.tagName).toBe('SPAN')
    expect(glyph?.getAttribute('aria-hidden')).toBe('true')
    expect(glyph?.classList.contains('lb-viewer-entry-row-state-done')).toBe(true)
    expect(glyph?.style.background).toBe('var(--lb-text-dim)')
    // Never a checkbox input, never a button: the cockpit views and
    // ratifies, it does not complete tasks.
    expect(el.querySelector('input')).toBeNull()
    expect(el.querySelector('button.lb-viewer-entry-row-state')).toBeNull()

    const content = el.querySelector<HTMLElement>('.lb-viewer-entry-row-content')
    expect(content?.style.textDecoration).toBe('line-through')
    expect(content?.style.color).toBe('var(--lb-text-muted)')
    expect(content?.firstElementChild).toBe(glyph)
  })

  it('cockpit open state renders an empty outline glyph without strikethrough', () => {
    const el = renderViewerEntryRow(cockpitData({ state: 'open' }), {})
    const glyph = el.querySelector<HTMLElement>('.lb-viewer-entry-row-state')
    expect(glyph?.classList.contains('lb-viewer-entry-row-state-open')).toBe(true)
    expect(glyph?.style.background).toBe('transparent')
    expect(glyph?.style.border).toBe('1px solid var(--lb-text-dim)')
    const content = el.querySelector<HTMLElement>('.lb-viewer-entry-row-content')
    expect(content?.style.textDecoration).toBe('')
  })

  it('cockpit entries without state get no glyph', () => {
    const el = renderViewerEntryRow(cockpitData(), {})
    expect(el.querySelector('.lb-viewer-entry-row-state')).toBeNull()
  })

  it('inline layout renders the one-line row: rim | time | pill | title | route', () => {
    const el = renderViewerEntryRow(
      { ...cockpitData({ route: 'domains/ai-llm', timeLabel: '20:10' }), layout: 'inline' },
      { onAction: () => {} }
    )
    expect(el.dataset.variant).toBe('cockpit')
    expect(el.dataset.layout).toBe('inline')

    const article = el.querySelector<HTMLElement>('article.lb-viewer-entry-row')
    expect(article?.classList.contains('lb-viewer-entry-row-inline')).toBe(true)
    expect(article?.style.display).toBe('grid')
    expect(article?.style.gridTemplateColumns).toBe('3px auto auto 1fr auto')
    expect(article?.style.alignItems).toBe('center')
    // The rim is a grid column, not the stacked variant's border-left.
    expect(article?.style.borderLeft).toBe('')
    const rim = article?.children[0] as HTMLElement
    expect(rim.classList.contains('lb-viewer-entry-row-rim')).toBe(true)
    expect(rim.style.width).toBe('3px')
    expect(rim.style.background).toBe('var(--lb-entry-row-color)')

    // Time sits in the left column, right after the rim.
    const time = article?.children[1] as HTMLElement
    expect(time.classList.contains('lb-viewer-entry-row-time')).toBe(true)
    expect(time.textContent).toBe('20:10')
    expect(time.style.fontFamily).toBe('var(--lb-font-mono)')
    expect(time.style.color).toBe('var(--lb-text-dim)')

    const pill = article?.children[2] as HTMLElement
    expect(pill.classList.contains('lb-viewer-entry-row-type')).toBe(true)
    expect(pill.textContent).toBe('task')

    // Single-line title with ellipsis; inline tags still render in it.
    const title = el.querySelector<HTMLElement>('.lb-viewer-entry-row-title')
    expect(title?.style.whiteSpace).toBe('nowrap')
    expect(title?.style.overflow).toBe('hidden')
    expect(title?.style.textOverflow).toBe('ellipsis')
    expect(title?.querySelector('.lb-viewer-entry-row-inline-tag')?.textContent).toBe('#cockpit')

    // Trailing column shows the route only - no time in it.
    const trailing = el.querySelector<HTMLElement>('.lb-viewer-entry-row-trailing')
    expect(trailing?.tagName).toBe('SPAN')
    expect(trailing?.classList.contains('lb-viewer-entry-row-route')).toBe(true)
    expect(trailing?.textContent).toBe('domains/ai-llm')
    expect(trailing?.style.color).toBe('var(--lb-text-muted)')
    expect(trailing?.querySelector('.lb-viewer-entry-row-time')).toBeNull()

    // No second meta line: no date row, no tag-chip row, no Open link.
    expect(el.querySelector('.lb-viewer-entry-row-meta')).toBeNull()
    expect(el.querySelector('.lb-viewer-entry-row-tag')).toBeNull()
    expect(el.querySelector('.lb-viewer-entry-row-open')).toBeNull()
  })

  it('inline layout wraps the title in an entry-open anchor when openUrl is present', () => {
    const onAction = vi.fn()
    const el = renderViewerEntryRow({ ...cockpitData(), layout: 'inline' }, { onAction })

    const link = el.querySelector<HTMLAnchorElement>('.lb-viewer-entry-row-title-link')
    expect(link).toBeTruthy()
    expect(link?.getAttribute('href')).toBe('/@alex/vault/e/entry-1')
    expect(link?.querySelector('.lb-viewer-entry-row-title')).toBeTruthy()

    // Plain click fires entry-open (and doesn't bubble into entry-focus).
    link?.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }))
    expect(onAction).toHaveBeenCalledTimes(1)
    expect(onAction).toHaveBeenLastCalledWith({
      type: 'entry-open',
      target: '/@alex/vault/e/entry-1',
      source: 'viewer-entry-row',
    })

    // Modifier-click falls through to the browser (no action fired).
    const modClick = new MouseEvent('click', { bubbles: true, cancelable: true, ctrlKey: true })
    link?.dispatchEvent(modClick)
    expect(onAction).toHaveBeenCalledTimes(1)
    expect(modClick.defaultPrevented).toBe(false)

    // Row body keeps entry-focus + keyboard behavior.
    const article = el.querySelector<HTMLElement>('article.lb-viewer-entry-row')
    expect(article?.getAttribute('role')).toBe('button')
    expect(article?.tabIndex).toBe(0)
    article?.click()
    expect(onAction).toHaveBeenLastCalledWith({
      type: 'entry-focus',
      target: 'entry-1',
      source: 'viewer-entry-row',
    })
  })

  it('inline layout renders the title as a plain span when openUrl is absent', () => {
    const el = renderViewerEntryRow(
      { ...cockpitData({ openUrl: undefined }), layout: 'inline' },
      {}
    )
    expect(el.querySelector('.lb-viewer-entry-row-title-link')).toBeNull()
    expect(el.querySelector('.lb-viewer-entry-row-title')).toBeTruthy()
  })

  it('inline layout places the state glyph between pill and title', () => {
    const el = renderViewerEntryRow(
      { ...cockpitData({ state: 'done', route: 'domains/ai-llm' }), layout: 'inline' },
      {}
    )
    const article = el.querySelector<HTMLElement>('article.lb-viewer-entry-row')
    expect(article?.style.gridTemplateColumns).toBe('3px auto auto auto 1fr auto')
    const glyph = article?.children[3] as HTMLElement
    expect(glyph.classList.contains('lb-viewer-entry-row-state-done')).toBe(true)
    expect(glyph.tagName).toBe('SPAN')
    expect(glyph.getAttribute('aria-hidden')).toBe('true')
    expect(glyph.style.background).toBe('var(--lb-text-dim)')
    expect(el.querySelector('input')).toBeNull()
    const title = el.querySelector<HTMLElement>('.lb-viewer-entry-row-title')
    expect(title?.style.textDecoration).toBe('line-through')
    expect(title?.style.color).toBe('var(--lb-text-muted)')
  })

  it('cockpit layout stacked (or absent) keeps the exact current cockpit rendering', () => {
    const data = cockpitData({ route: 'domains/ai-llm', state: 'open', timeLabel: '20:10' })
    const absent = renderViewerEntryRow(data, {})
    const stacked = renderViewerEntryRow({ ...data, layout: 'stacked' }, {})
    expect(stacked.outerHTML).toBe(absent.outerHTML)
    expect(stacked.dataset.layout).toBeUndefined()
  })

  it('layout is ignored when the cockpit variant is not set', () => {
    const plain = renderViewerEntryRow({ entries: [baseEntry, secondEntry] }, {})
    const withLayout = renderViewerEntryRow(
      { entries: [baseEntry, secondEntry], layout: 'inline' },
      {}
    )
    expect(withLayout.outerHTML).toBe(plain.outerHTML)
    expect(withLayout.dataset.layout).toBeUndefined()
    expect(withLayout.querySelector('.lb-viewer-entry-row-inline')).toBeNull()
  })

  it('timeLabel replaces the long date format in both variants', () => {
    const cockpitEl = renderViewerEntryRow(cockpitData({ timeLabel: '20:10' }), {})
    const cockpitTime = cockpitEl.querySelector<HTMLTimeElement>('.lb-viewer-entry-row-time')
    expect(cockpitTime?.textContent).toBe('20:10')
    expect(cockpitTime?.dateTime).toBe('2026-05-20T12:00:00Z')

    const defaultEl = renderViewerEntryRow(
      { entries: [{ ...baseEntry, timeLabel: '2h ago' }] },
      {}
    )
    const defaultTime = defaultEl.querySelector<HTMLTimeElement>(
      '.lb-viewer-entry-row-meta .lb-viewer-entry-row-time'
    )
    expect(defaultTime?.textContent).toBe('2h ago')

    const noLabel = renderViewerEntryRow(cockpitData(), {})
    expect(noLabel.querySelector('.lb-viewer-entry-row-time')?.textContent).toBe('May 20, 2026')
  })

  // Frame T / D7: variants are additive props on this ONE primitive.
  // The 'inline layout renders the one-line row' test above (unchanged
  // by this edit) is the byte-identity guard for cockpit callers that
  // pass none of these new options - it still asserts the exact
  // gridTemplateColumns string and child order with zero regressions.
  describe('frame T row-primitive variants (D7)', () => {
    it('rimTone "signal" overrides the rim + pill color; "type"/absent keeps entryTypeColor', () => {
      const signalEl = renderViewerEntryRow(
        {
          ...cockpitData({ type: 'concept', rimTone: 'signal', signalColor: 'var(--signal-alert)' }),
          layout: 'inline',
        },
        {}
      )
      const item = signalEl.querySelector<HTMLElement>('.lb-viewer-entry-row-item')
      expect(item?.style.getPropertyValue('--lb-entry-row-color')).toBe('var(--signal-alert)')
      const pill = signalEl.querySelector<HTMLElement>('.lb-viewer-entry-row-type')
      expect(pill?.style.color).toBe('var(--signal-alert)')
      expect(pill?.textContent).toBe('concept')

      const typeEl = renderViewerEntryRow(
        { ...cockpitData({ type: 'concept', rimTone: 'type', signalColor: 'var(--signal-alert)' }), layout: 'inline' },
        {}
      )
      const typeItem = typeEl.querySelector<HTMLElement>('.lb-viewer-entry-row-item')
      // 'concept' isn't a canonical entry type; entryTypeColor falls back to log.
      expect(typeItem?.style.getPropertyValue('--lb-entry-row-color')).toBe('var(--lb-entry-log)')
    })

    it('trailingAction renders a ghost button and fires its own action type on click', () => {
      const onAction = vi.fn()
      const el = renderViewerEntryRow(
        {
          ...cockpitData({ trailingAction: { label: 'Review', action: 'health-review' } }),
          layout: 'inline',
        },
        { onAction }
      )
      const button = el.querySelector<HTMLButtonElement>('.lb-viewer-entry-row-trailing-action')
      expect(button).toBeTruthy()
      expect(button?.textContent).toBe('Review')
      expect(button?.tagName).toBe('BUTTON')

      button?.click()
      expect(onAction).toHaveBeenCalledWith({
        type: 'health-review',
        target: 'entry-1',
        source: 'viewer-entry-row',
      })

      // The trailing action click never also fires entry-focus.
      expect(onAction).toHaveBeenCalledTimes(1)
    })

    it('trailingAction is absent from the grid when the entry has none', () => {
      const el = renderViewerEntryRow({ ...cockpitData(), layout: 'inline' }, {})
      expect(el.querySelector('.lb-viewer-entry-row-trailing-action')).toBeNull()
    })

    it('selectable renders a leading checkbox that fires entry-select, not entry-focus', () => {
      const onAction = vi.fn()
      const el = renderViewerEntryRow(
        { ...cockpitData({ checked: false }), layout: 'inline', selectable: true },
        { onAction }
      )
      const article = el.querySelector<HTMLElement>('article.lb-viewer-entry-row')
      const checkbox = el.querySelector<HTMLInputElement>('.lb-viewer-entry-row-checkbox')
      expect(checkbox).toBeTruthy()
      expect(checkbox?.type).toBe('checkbox')
      expect(checkbox?.checked).toBe(false)
      // Checkbox is the second grid child, right after the rim.
      expect(article?.children[1]).toBe(checkbox)

      checkbox?.dispatchEvent(new Event('change', { bubbles: true }))
      expect(onAction).toHaveBeenCalledWith({
        type: 'entry-select',
        target: 'entry-1',
        source: 'viewer-entry-row',
      })
      expect(onAction).toHaveBeenCalledTimes(1)
    })

    it('checked selectable row reflects the checkbox state and tints the row', () => {
      const el = renderViewerEntryRow(
        { ...cockpitData({ checked: true }), layout: 'inline', selectable: true },
        {}
      )
      const checkbox = el.querySelector<HTMLInputElement>('.lb-viewer-entry-row-checkbox')
      expect(checkbox?.checked).toBe(true)
      const article = el.querySelector<HTMLElement>('article.lb-viewer-entry-row')
      expect(article?.style.background).toBe('color-mix(in srgb, var(--lb-accent) 10%, transparent)')
    })

    it('selectable is ignored on the stacked layout and on the default (non-cockpit) variant', () => {
      const stacked = renderViewerEntryRow(
        { ...cockpitData({ checked: true }), layout: 'stacked', selectable: true },
        {}
      )
      expect(stacked.querySelector('.lb-viewer-entry-row-checkbox')).toBeNull()

      const nonCockpit = renderViewerEntryRow(
        { entries: [baseEntry], layout: 'inline', selectable: true },
        {}
      )
      expect(nonCockpit.querySelector('.lb-viewer-entry-row-checkbox')).toBeNull()
    })

    it('selectable grid reserves a 16px checkbox column between the rim and time', () => {
      const el = renderViewerEntryRow(
        { ...cockpitData(), layout: 'inline', selectable: true },
        {}
      )
      const article = el.querySelector<HTMLElement>('article.lb-viewer-entry-row')
      expect(article?.style.gridTemplateColumns).toBe('3px 16px auto auto 1fr')
    })
  })
})
