import { loadSettingsStatusModel } from '../models/view-data.js'
import { api } from '../api.js'
import type { SettingsStatusModel } from '../models/ui-models.js'
import type { KnowledgeSettings, RoutingMode } from '../../types/settings.js'
import { hostFeatures } from '../lib/host.js'
import { ckButton, ckCapabilityChip, ckDragList, ckSelect, ckStatusPill } from '../lib/controls.js'

type Provider = SettingsStatusModel['providers'][number]

export async function renderSettings(): Promise<HTMLElement> {
  const model = await loadSettingsStatusModel()
  const root = document.createElement('div')
  root.className = 'page-wrap settings-page'

  const { inferenceSettings } = hostFeatures(model.capabilities)
  root.append(renderHeader(inferenceSettings), renderStatusGrid(model))
  // Hidden, not bannered (rc6, design 4.2): provider order and routing
  // only render on a host that consumes them (the Node cockpit server).
  if (inferenceSettings) {
    root.append(renderProviders(model, root), renderRoutingPanel(model, root))
  }

  return root
}

async function rerender(root: HTMLElement): Promise<void> {
  root.replaceWith(await renderSettings())
}

function renderHeader(inferenceSettings: boolean): HTMLElement {
  const header = document.createElement('section')
  header.className = 'settings-header'

  const eyebrow = document.createElement('div')
  eyebrow.className = 'hero-eyebrow'
  eyebrow.textContent = 'Settings'

  const h = document.createElement('h1')
  h.textContent = inferenceSettings ? 'Settings and inference status' : 'Settings'

  const p = document.createElement('p')
  p.textContent = inferenceSettings
    ? 'Runtime state from the current vault adapter. Provider order and routing are editable and persist across restarts.'
    : 'Runtime state from the current vault host.'

  header.append(eyebrow, h, p)
  return header
}

/**
 * Capability flags that never get a chip (lmk-oss-cockpit-cut-empty-producer-views-7qcb,
 * lmk-hqgm). `hasBehaviorCatalog` and `hasFeedSystem` stay in the adapter
 * contract (the Node logmark-mcp adapter sets them), but no cockpit surface
 * consumes them, so their chips advertised features the cockpit cannot show.
 * `hasDistillation` lost its surfaces with distill review.
 * `hasConceptProducer` is a host fact that gates concept surfaces; it is
 * not a vault capability to toggle or inspect here.
 */
const HIDDEN_CAPABILITY_CHIPS = new Set(['hasBehaviorCatalog', 'hasFeedSystem', 'hasDistillation', 'hasConceptProducer'])

function renderStatusGrid(model: SettingsStatusModel): HTMLElement {
  const grid = document.createElement('section')
  grid.className = 'settings-status-grid'

  const vault = panel('Vault and adapter')
  const vaultRows = document.createElement('div')
  vaultRows.className = 'settings-kv-list'
  vaultRows.append(
    kv('adapter', model.adapter),
    kv('vault path', model.vaultPath || 'not configured'),
    kv('entries', model.totalEntries.toLocaleString()),
  )
  // Concept pages are counted only where a producer writes them (lmk-hqgm).
  if (hostFeatures(model.capabilities).conceptProducer) {
    vaultRows.append(kv('concept pages', model.totalConcepts.toLocaleString()))
  }
  // null = this host runs no lint scan, so there are no signals to count:
  // the row is hidden rather than asserting "0" or bannering (rc6).
  if (model.staleCount !== null) {
    vaultRows.append(kv('open health signals', model.staleCount.toLocaleString()))
  }
  vault.appendChild(vaultRows)

  const caps = panel('Capabilities')
  const chipList = document.createElement('div')
  chipList.className = 'settings-chip-list'
  const entries = Object.entries(model.capabilities)
    .filter(([name]) => !HIDDEN_CAPABILITY_CHIPS.has(name))
    .sort(([a], [b]) => a.localeCompare(b))
  if (entries.length === 0) {
    chipList.appendChild(ckCapabilityChip('unknown', false))
  } else {
    for (const [name, enabled] of entries) {
      chipList.appendChild(ckCapabilityChip(displayCapName(name), enabled))
    }
  }
  caps.appendChild(chipList)

  grid.append(vault, caps)
  return grid
}

function displayCapName(key: string): string {
  return key
    .replace(/^has/, '')
    .replace(/([a-z])([A-Z])/g, '$1-$2')
    .toLowerCase()
}

function renderProviders(model: SettingsStatusModel, root: HTMLElement): HTMLElement {
  const section = panel('Inference providers', 'drag to reorder')
  section.classList.add('settings-provider-panel')

  let order = [...model.settings.providerOrder]

  const list = ckDragList(
    order.map((id) => ({
      id,
      render: () => providerRowBody(model.providers.find((p) => p.id === id) ?? fallbackProvider(id, model)),
    })),
    (next) => {
      order = next as typeof order
    },
  )

  const controls = document.createElement('div')
  controls.className = 'settings-control-row'
  const status = document.createElement('span')
  status.className = 'settings-note'

  const apply = ckButton('Apply provider order', () => {
    void (async () => {
      apply.disabled = true
      status.textContent = 'Applying...'
      try {
        await api.saveSettings({ ...model.settings, providerOrder: order })
        await rerender(root)
      } catch (err) {
        status.textContent = `Apply failed: ${(err as Error).message}`
        apply.disabled = false
      }
    })()
  }, { action: 'apply-provider-order' })

  controls.append(apply, status)
  section.append(list, controls)
  return section
}

function fallbackProvider(id: string, model: SettingsStatusModel): Provider {
  return {
    id,
    label: id,
    status: id === model.activeProvider ? 'ready' : 'unknown',
    detail: '',
  }
}

function providerRowBody(provider: Provider): HTMLElement {
  const row = document.createElement('div')
  row.className = 'settings-provider-row'

  const name = document.createElement('div')
  name.className = 'settings-provider-name'
  name.textContent = provider.label

  const detail = document.createElement('div')
  detail.className = 'settings-provider-detail'
  detail.textContent = provider.detail

  const pills = document.createElement('div')
  pills.className = 'settings-provider-pills'
  if (provider.status === 'ready') {
    pills.append(ckStatusPill('ready', 'positive'), ckStatusPill('active', 'positive'))
  } else if (provider.status === 'offline') {
    pills.append(ckStatusPill('offline', 'dim'))
  } else {
    pills.append(ckStatusPill('idle', 'neutral'))
    const probed = document.createElement('span')
    probed.className = 'settings-note'
    probed.textContent = 'probed on apply'
    pills.appendChild(probed)
  }

  row.append(name, detail, pills)
  return row
}

function renderRoutingPanel(model: SettingsStatusModel, root: HTMLElement): HTMLElement {
  const section = panel('Model routing')
  section.classList.add('settings-routing-panel')

  const routing = { ...model.settings.routing }
  const modes = ['default', 'disabled'] as const satisfies readonly RoutingMode[]

  const grid = document.createElement('div')
  grid.className = 'settings-routing-grid'
  grid.append(
    ckSelect('Summarization', modes, routing.summarization, (v) => (routing.summarization = v)),
    ckSelect('Distillation', modes, routing.distillation, (v) => (routing.distillation = v)),
    ckSelect('Search enrichment', modes, routing.searchEnrichment, (v) => (routing.searchEnrichment = v)),
  )

  const controls = document.createElement('div')
  controls.className = 'settings-control-row'
  const status = document.createElement('span')
  status.className = 'settings-note'

  const save = ckButton('Save routing', () => {
    void (async () => {
      save.disabled = true
      status.textContent = 'Saving...'
      try {
        const settings: KnowledgeSettings = { ...model.settings, routing }
        await api.saveSettings(settings)
        await rerender(root)
      } catch (err) {
        status.textContent = `Save failed: ${(err as Error).message}`
        save.disabled = false
      }
    })()
  }, { action: 'save-routing' })

  controls.append(save, status)
  section.append(grid, controls)
  return section
}

function panel(title: string, hint?: string): HTMLElement {
  const section = document.createElement('section')
  section.className = 'settings-panel'
  const head = document.createElement('div')
  head.className = 'settings-panel-head'
  const h = document.createElement('h2')
  h.textContent = title
  head.appendChild(h)
  if (hint) {
    const hintEl = document.createElement('span')
    hintEl.className = 'settings-note'
    hintEl.textContent = hint
    head.appendChild(hintEl)
  }
  section.appendChild(head)
  return section
}

function kv(label: string, value: string): HTMLElement {
  const row = document.createElement('div')
  row.className = 'settings-kv'
  const k = document.createElement('span')
  k.className = 'k'
  k.textContent = label
  const v = document.createElement('span')
  v.className = 'v'
  v.textContent = value
  row.append(k, v)
  return row
}
