import { renderViewerEntryRow } from '@logmark/lblocks'
import type { ViewerEntryRowEntry } from '@logmark/lblocks'
import { api, ApiError } from '../api.js'
import { loadHealthTriageModel } from '../models/view-data.js'
import type { HealthTriageModel } from '../models/ui-models.js'
import { toDateLabel } from '../models/normalizers.js'
import { ckButton } from '../lib/controls.js'
import { navigate } from '../lib/nav.js'
import { escapeInlineUnderscores } from '../lib/notation.js'
import { hostFeatures } from '../lib/host.js'

type HealthQueueItem = HealthTriageModel['queue'][number]

/**
 * Wiki health (pass-3 P5 / frame M; row folded onto the shared
 * viewer-entry-row primitive per frame T / D7): the populated state is
 * a list of source-drift signals - signal-colored rim + pill, concept
 * title, a ghost Review per row - with ONE amber primary (Scan now,
 * wired to the real refresh endpoint). The fresh state keeps the shared
 * empty treatment.
 */

// Signal rim (D7): health rows color the rim/pill on --lb-signal-* -
// never the entry-type palette, since 'changed'/'deleted'/'parse-error'
// aren't entry types.
const SIGNAL_ACCENT: Record<HealthQueueItem['kind'], string> = {
  changed: 'var(--lb-signal-focus)',
  deleted: 'var(--lb-signal-negative)',
  'parse-error': 'var(--lb-signal-alert)',
}

const LINT_SCAN_SENTENCE = 'Run logmark lint scan in a terminal to check this vault.'

export async function renderHealth(): Promise<HTMLElement> {
  const root = document.createElement('div')
  root.className = 'page-wrap health-triage-page'

  // Hidden, not bannered (rc6, design 4.2): a host without the lint scan
  // gets one line naming the CLI that checks the vault.
  const caps = await api.capabilities()
  if (!hostFeatures(caps?.capabilities).lintScan) {
    root.appendChild(renderLintScanLine())
    return root
  }

  let model: HealthTriageModel
  try {
    model = await loadHealthTriageModel()
  } catch (err) {
    if (err instanceof ApiError && err.status === 501) {
      root.appendChild(renderLintScanLine())
      return root
    }
    throw err
  }

  root.appendChild(renderHero(model, root))

  if (model.queue.length === 0) {
    root.appendChild(renderEmptyState(model))
    return root
  }

  const head = document.createElement('div')
  head.className = 'hero-eyebrow'
  head.textContent = `Signals · ${model.queue.length}`
  root.appendChild(head)

  const list = document.createElement('div')
  list.className = 'health-signal-list'
  list.appendChild(renderSignalRows(model.queue))
  root.appendChild(list)

  return root
}

function renderHero(model: HealthTriageModel, root: HTMLElement): HTMLElement {
  const hero = document.createElement('section')
  hero.className = 'health-triage-hero'

  const copy = document.createElement('div')

  const eyebrow = document.createElement('div')
  eyebrow.className = 'hero-eyebrow'
  eyebrow.textContent = 'Wiki Health'
  copy.appendChild(eyebrow)

  const h = document.createElement('h1')
  h.textContent = model.queue.length === 0
    ? 'Wiki is fresh.'
    : `The vault is ${model.staleCount} decision${model.staleCount === 1 ? '' : 's'} away from clean.`
  copy.appendChild(h)

  // Compact head (frame W, D9): a one-line meta instead of the old
  // explanatory paragraph.
  const meta = document.createElement('p')
  meta.className = 'section-meta'
  meta.textContent = `${model.conceptCount} concept page${model.conceptCount === 1 ? '' : 's'} · ${model.queue.length} source drift signal${model.queue.length === 1 ? '' : 's'}`
  copy.appendChild(meta)

  // The view's single amber primary (P4): a real scan, not a dead control.
  const actions = document.createElement('div')
  actions.className = 'health-hero-actions'
  const scanStatus = document.createElement('span')
  scanStatus.className = 'settings-note'
  const scan = ckButton('Scan now', () => {
    void (async () => {
      scan.disabled = true
      scanStatus.textContent = 'Scanning...'
      try {
        await api.healthRefresh()
        root.replaceWith(await renderHealth())
      } catch (err) {
        scanStatus.textContent = err instanceof ApiError && err.status === 501
          ? LINT_SCAN_SENTENCE
          : `Scan failed: ${(err as Error).message}`
        scan.disabled = false
      }
    })()
  }, { action: 'health-scan' })
  actions.append(scan, scanStatus)
  copy.appendChild(actions)

  hero.appendChild(copy)
  hero.appendChild(renderMetrics(model))
  return hero
}

function renderMetrics(model: HealthTriageModel): HTMLElement {
  const metrics = document.createElement('div')
  metrics.className = 'health-metrics'

  const changed = model.queue.filter((item) => item.kind === 'changed').length
  const deleted = model.queue.filter((item) => item.kind === 'deleted').length

  const lastScan = metric('last scan', toDateLabel(model.scannedAt))
  lastScan.querySelector<HTMLElement>('.v')?.setAttribute('data-visual-mask', 'health-last-scan')

  metrics.append(
    metric('open signals', String(model.queue.length)),
    metric('changed sources', String(changed)),
    metric('deleted sources', String(deleted)),
    lastScan,
  )

  return metrics
}

/**
 * Rows fold onto the shared viewer-entry-row primitive (frame T / D7):
 * rim + pill + title + trailing ghost 'Review', same anatomy as every
 * other cockpit list. HealthQueueItem isn't an EntrySummary, so this
 * bypasses entry-card.ts's EntrySummary-shaped `renderEntryRows` and
 * calls renderViewerEntryRow directly.
 */
function renderSignalRows(items: HealthQueueItem[]): HTMLElement {
  const hrefById = new Map(items.map((item) => [item.id, `/concepts/${encodeURIComponent(item.conceptSlug)}`]))
  return renderViewerEntryRow(
    {
      variant: 'cockpit',
      layout: 'inline',
      showAttribution: false,
      entries: items.map((item): ViewerEntryRowEntry => ({
        id: item.id,
        type: item.kind,
        timestamp: item.since,
        preview: escapeInlineUnderscores(item.sourceId ? `${item.title} - ${item.sourceId}` : item.title),
        tags: [],
        timeLabel: item.since,
        rimTone: 'signal',
        signalColor: SIGNAL_ACCENT[item.kind] ?? 'var(--lb-signal-alert)',
        trailingAction: { label: 'Review', action: 'health-review' },
      })),
    },
    {
      density: 'compact',
      onAction: (action) => {
        if (action.type === 'entry-focus' || action.type === 'health-review') {
          const href = hrefById.get(action.target)
          if (href) navigate(href)
        }
      },
    },
  )
}

function renderLintScanLine(): HTMLElement {
  const wrap = document.createElement('section')
  wrap.className = 'health-cli'
  const eyebrow = document.createElement('div')
  eyebrow.className = 'hero-eyebrow'
  eyebrow.textContent = 'Health'
  const line = document.createElement('p')
  line.className = 'health-cli-line'
  line.dataset.healthCli = '1'
  const code = document.createElement('code')
  code.textContent = 'logmark lint scan'
  line.append('Run ', code, ' in a terminal to check this vault.')
  wrap.append(eyebrow, line)
  return wrap
}

function renderEmptyState(model: HealthTriageModel): HTMLElement {
  const empty = document.createElement('section')
  empty.className = 'health-empty concepts-empty-card'

  const icon = document.createElement('div')
  icon.className = 'concepts-empty-icon'
  icon.setAttribute('aria-hidden', 'true')
  icon.textContent = '◇'

  const h = document.createElement('h2')
  h.textContent = 'Wiki is fresh.'

  const p = document.createElement('p')
  p.textContent = `${model.conceptCount} concept page${model.conceptCount === 1 ? '' : 's'} scanned. No source drift is waiting for review.`

  empty.append(icon, h, p)
  return empty
}

function metric(label: string, value: string): HTMLElement {
  const row = document.createElement('div')
  row.className = 'health-metric'

  const k = document.createElement('span')
  k.className = 'k'
  k.textContent = label

  const v = document.createElement('span')
  v.className = 'v'
  v.textContent = value

  row.append(k, v)
  return row
}
