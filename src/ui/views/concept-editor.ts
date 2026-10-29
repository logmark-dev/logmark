import { api } from '../api.js'
import type { ConceptPage } from '../../types/concept-page.js'
import { markdownToLBlocksDocument } from '../../lib/markdown-to-lblocks.js'
import { renderMarkdownInto } from '../lib/lblocks-render.js'

const SLUG_RE = /^[a-z0-9]+(?:-[a-z0-9]+)*$/
const STATUSES: ConceptPage['status'][] = ['draft', 'mature', 'archived']

export type ConceptEditorOptions =
  | { mode: 'new' }
  | { mode: 'edit'; slug: string }

export async function renderConceptEditor(options: ConceptEditorOptions): Promise<HTMLElement> {
  const root = document.createElement('div')
  root.className = 'page-wrap concept-editor-page'

  let existing: ConceptPage | null = null
  if (options.mode === 'edit') {
    const response = await api.readConcept(options.slug)
    if (!response.page || response.error) {
      root.appendChild(banner(`Concept not found: ${options.slug}`))
      return root
    }
    existing = response.page
  }

  const page = existing ?? newConceptPage()
  const form = document.createElement('form')
  form.className = 'concept-editor-form'

  const header = document.createElement('header')
  header.className = 'concept-editor-header'
  const eyebrow = document.createElement('div')
  eyebrow.className = 'cp-eyebrow'
  eyebrow.textContent = options.mode === 'new' ? 'concept / new' : `concept / ${page.slug} / edit`
  const title = document.createElement('h1')
  title.textContent = options.mode === 'new' ? 'New concept' : 'Concept editor'
  const intro = document.createElement('p')
  intro.textContent = 'Edit frontmatter and markdown before writing the concept page.'
  header.append(eyebrow, title, intro)

  const layout = document.createElement('div')
  layout.className = 'concept-editor-layout'

  const fields = document.createElement('section')
  fields.className = 'concept-editor-fields'

  const slugInput = textInput('slug', 'slug', page.slug)
  const titleInput = textInput('title', 'title', page.title)
  const statusSelect = statusInput(page.status)
  const tagsInput = textInput('tags', 'tags', page.tags.join(', '))
  const bodyInput = document.createElement('textarea')
  bodyInput.name = 'body'
  bodyInput.rows = 18
  bodyInput.value = page.body
  bodyInput.className = 'concept-editor-markdown'

  fields.append(
    field('Slug', slugInput),
    field('Title', titleInput),
    field('Status', statusSelect),
    field('Tags', tagsInput),
    field('Markdown', bodyInput),
  )

  const side = document.createElement('aside')
  side.className = 'concept-editor-side'

  const preview = document.createElement('div')
  preview.className = 'concept-editor-preview'
  renderMarkdownInto(preview, markdownToLBlocksDocument(bodyInput.value))
  bodyInput.addEventListener('input', () => {
    preview.replaceChildren()
    renderMarkdownInto(preview, markdownToLBlocksDocument(bodyInput.value))
  })

  const previewPanel = panel('Preview')
  previewPanel.appendChild(preview)

  const metaPanel = panel('Preserved metadata')
  const metaRows = document.createElement('div')
  metaRows.className = 'frontmatter'
  metaRows.append(
    kv('created', page.created),
    kv('last_distilled', page.last_distilled),
    kv('version', String(page.distillation_version)),
    kv('sources', String(page.sources.length)),
    kv('related', page.related_concepts.join(', ') || 'none'),
    kv('draft', page.draft === undefined ? 'unset' : String(page.draft)),
    kv('density', page.density === undefined ? 'unset' : String(page.density)),
  )
  metaPanel.appendChild(metaRows)

  const errors = document.createElement('div')
  errors.className = 'concept-editor-errors'
  errors.setAttribute('aria-live', 'polite')

  const saveStatus = document.createElement('div')
  saveStatus.className = 'mono concept-editor-save-status'
  saveStatus.setAttribute('aria-live', 'polite')

  const saveButton = document.createElement('button')
  saveButton.type = 'submit'
  saveButton.className = 'btn concept-editor-save'
  saveButton.dataset.action = 'save-concept'
  saveButton.textContent = 'Save concept'

  const savePanel = panel('Write')
  savePanel.append(errors, saveButton, saveStatus)
  side.append(previewPanel, metaPanel, savePanel)
  layout.append(fields, side)
  form.append(header, layout)

  form.addEventListener('submit', async (event) => {
    event.preventDefault()
    saveStatus.textContent = ''
    errors.replaceChildren()

    const draft = buildPage(page, {
      slug: slugInput.value,
      title: titleInput.value,
      status: statusSelect.value,
      tags: tagsInput.value,
      body: bodyInput.value,
    })
    const validation = validatePage(draft)
    if (validation.length > 0) {
      errors.appendChild(errorList(validation))
      return
    }

    saveButton.disabled = true
    saveStatus.textContent = 'Saving...'
    try {
      const result = await api.saveConcept(draft.slug, draft)
      saveStatus.textContent = result.ok ? 'Saved.' : 'Save failed.'
    } catch (err) {
      saveStatus.textContent = `Save failed: ${(err as Error).message}`
    } finally {
      saveButton.disabled = false
    }
  })

  root.appendChild(form)
  return root
}

function newConceptPage(): ConceptPage {
  const now = new Date().toISOString()
  return {
    slug: '',
    title: '',
    body: '',
    status: 'draft',
    tags: [],
    sources: [],
    related_concepts: [],
    created: now,
    last_distilled: now,
    distillation_version: 1,
  }
}

function buildPage(
  base: ConceptPage,
  values: { slug: string; title: string; status: string; tags: string; body: string },
): ConceptPage {
  const next: ConceptPage = {
    ...base,
    slug: values.slug.trim(),
    title: values.title.trim(),
    status: values.status as ConceptPage['status'],
    tags: parseTags(values.tags),
    body: values.body,
  }
  return next
}

function validatePage(page: ConceptPage): string[] {
  const errors: string[] = []
  if (!SLUG_RE.test(page.slug)) {
    errors.push('Slug must use lowercase letters, numbers, and hyphens.')
  }
  if (page.title.length === 0) {
    errors.push('Title is required.')
  }
  if (page.body.trim().length === 0) {
    errors.push('Body is required.')
  }
  if (!STATUSES.includes(page.status)) {
    errors.push('Status must be draft, mature, or archived.')
  }
  return errors
}

function parseTags(value: string): string[] {
  return value
    .split(',')
    .map((tag) => tag.trim())
    .filter(Boolean)
}

function textInput(name: string, label: string, value: string): HTMLInputElement {
  const input = document.createElement('input')
  input.name = name
  input.id = `concept-editor-${name}`
  input.value = value
  input.setAttribute('aria-label', label)
  return input
}

function statusInput(value: ConceptPage['status']): HTMLSelectElement {
  const select = document.createElement('select')
  select.name = 'status'
  select.id = 'concept-editor-status'
  select.setAttribute('aria-label', 'status')
  for (const status of STATUSES) {
    const option = document.createElement('option')
    option.value = status
    option.textContent = status
    option.selected = status === value
    select.appendChild(option)
  }
  return select
}

function field(labelText: string, control: HTMLElement): HTMLElement {
  const label = document.createElement('label')
  label.className = 'concept-editor-field'
  const span = document.createElement('span')
  span.textContent = labelText
  label.append(span, control)
  return label
}

function panel(title: string): HTMLElement {
  const section = document.createElement('section')
  section.className = 'concept-editor-panel'
  const head = document.createElement('div')
  head.className = 'gutter-head'
  head.textContent = title
  section.appendChild(head)
  return section
}

function kv(key: string, value: string): HTMLElement {
  const row = document.createElement('div')
  row.className = 'fm-row'
  const k = document.createElement('span')
  k.className = 'k'
  k.textContent = key
  const v = document.createElement('span')
  v.className = 'v'
  v.textContent = value
  row.append(k, v)
  return row
}

function errorList(messages: string[]): HTMLElement {
  const list = document.createElement('ul')
  list.className = 'concept-editor-error-list'
  for (const message of messages) {
    const item = document.createElement('li')
    item.textContent = message
    list.appendChild(item)
  }
  return list
}

function banner(text: string): HTMLElement {
  const el = document.createElement('div')
  el.className = 'banner'
  el.textContent = text
  return el
}
