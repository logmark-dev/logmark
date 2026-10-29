import { renderMarkdownInto } from '../lib/lblocks-render.js'
import { api } from '../api.js'
import { markdownToLBlocksDocument } from '../../lib/markdown-to-lblocks.js'

export async function renderConstitution(): Promise<HTMLElement> {
  const root = document.createElement('div')
  root.className = 'constitution-influence-page page-wrap'

  const { body } = await api.constitution()
  const constitutionBody = body?.trim() ?? ''

  root.appendChild(renderHero())
  if (constitutionBody.length === 0) {
    const bannerEl = document.createElement('div')
    bannerEl.className = 'banner constitution-empty-banner'
    bannerEl.textContent =
      'No constitution yet. Create vault.md (or legacy constitution.md) at your vault content root - lm-bootstrap writes one for you.'
    root.appendChild(bannerEl)
  }

  const main = document.createElement('div')
  main.className = 'constitution-main'
  main.appendChild(renderRuleList(parseRules(constitutionBody)))
  main.appendChild(renderBodyPanel(constitutionBody))

  root.appendChild(main)

  return root
}

function renderHero(): HTMLElement {
  const hero = document.createElement('section')
  hero.className = 'constitution-hero'

  const eyebrow = document.createElement('div')
  eyebrow.className = 'hero-eyebrow'
  eyebrow.textContent = 'constitution / influence'

  const title = document.createElement('h1')
  title.textContent = 'Constitution influence'

  const copy = document.createElement('p')
  copy.textContent =
    "The rules agents read before they act on this vault. This page shows the constitution itself, not observed usage; the body is editable and writes back to the vault's constitution file (vault.md, or legacy constitution.md)."

  hero.append(eyebrow, title, copy)
  return hero
}

function renderRuleList(rules: string[]): HTMLElement {
  const section = document.createElement('section')
  section.className = 'constitution-panel constitution-rule-panel'

  const header = document.createElement('div')
  header.className = 'constitution-section-head'
  const title = document.createElement('h2')
  title.textContent = 'Rule list'
  const meta = document.createElement('span')
  meta.textContent = 'parsed from markdown lines'
  header.append(title, meta)
  section.appendChild(header)

  if (rules.length === 0) {
    const empty = document.createElement('p')
    empty.className = 'empty-state'
    // A prose-only constitution (the lm-bootstrap shape) is common: rules are
    // the list items, so say that rather than imply the file is empty (lmk-2bym).
    empty.textContent = 'No rules to list: rules are the bulleted or numbered list items in the constitution, and this one is written as prose. The full text is below.'
    section.appendChild(empty)
    return section
  }

  const list = document.createElement('ol')
  list.className = 'constitution-rule-list'
  for (const rule of rules) {
    const item = document.createElement('li')
    item.textContent = rule
    list.appendChild(item)
  }
  section.appendChild(list)
  return section
}

function renderBodyPanel(body: string): HTMLElement {
  const section = document.createElement('section')
  section.className = 'constitution-panel constitution-body-panel'

  const header = document.createElement('div')
  header.className = 'constitution-section-head'
  const title = document.createElement('h2')
  title.textContent = 'Body'
  const meta = document.createElement('span')
  meta.textContent = 'constitution file'

  const edit = document.createElement('button')
  edit.className = 'btn secondary'
  edit.type = 'button'
  edit.textContent = 'Edit'
  edit.dataset.action = 'edit-constitution'
  header.append(title, meta, edit)

  const container = document.createElement('div')
  container.className = 'constitution-body'
  renderMarkdownInto(container, markdownToLBlocksDocument(body))

  edit.addEventListener('click', () => {
    section.replaceWith(renderEditorPanel(body))
  })

  section.append(header, container)
  return section
}

function renderEditorPanel(body: string): HTMLElement {
  const section = document.createElement('section')
  section.className = 'constitution-panel constitution-body-panel'

  const header = document.createElement('div')
  header.className = 'constitution-section-head'
  const title = document.createElement('h2')
  title.textContent = 'Edit body'
  const meta = document.createElement('span')
  meta.textContent = 'constitution file'
  header.append(title, meta)

  const textarea = document.createElement('textarea')
  textarea.className = 'constitution-editor'
  textarea.value = body
  textarea.rows = Math.min(30, Math.max(10, body.split('\n').length + 2))

  const actions = document.createElement('div')
  actions.className = 'constitution-editor-actions'

  const status = document.createElement('span')
  status.className = 'section-meta'

  const save = document.createElement('button')
  save.className = 'btn'
  save.type = 'button'
  save.textContent = 'Save'
  save.dataset.action = 'save-constitution'
  save.addEventListener('click', () => {
    void (async () => {
      save.disabled = true
      cancel.disabled = true
      status.textContent = 'Saving...'
      try {
        await api.saveConstitution(textarea.value)
        section.replaceWith(renderBodyPanel(textarea.value))
      } catch (err) {
        status.textContent = `Save failed: ${(err as Error).message}`
        save.disabled = false
        cancel.disabled = false
      }
    })()
  })

  const cancel = document.createElement('button')
  cancel.className = 'btn secondary'
  cancel.type = 'button'
  cancel.textContent = 'Cancel'
  cancel.addEventListener('click', () => {
    section.replaceWith(renderBodyPanel(body))
  })

  actions.append(save, cancel, status)
  section.append(header, textarea, actions)
  return section
}

/**
 * Bullet/numbered lines only, excluding anything inside a fenced code
 * block (polish sweep, lmk-r8q4 item 7) - a `- like this` line inside
 * ``` fences is source code, not a rule.
 */
function parseRules(body: string): string[] {
  const rules: string[] = []
  let inFence = false
  for (const rawLine of body.split(/\r?\n/)) {
    const line = rawLine.trim()
    if (/^(```|~~~)/.test(line)) {
      inFence = !inFence
      continue
    }
    if (inFence) continue
    if (!/^([-*+]|\d+[.)])\s+/.test(line)) continue
    const stripped = stripMarkdownPrefix(line)
    if (stripped.length > 0) rules.push(stripped)
  }
  return rules
}

function stripMarkdownPrefix(line: string): string {
  return line
    .replace(/^#{1,6}\s+/, '')
    .replace(/^>\s?/, '')
    .replace(/^[-*+]\s+/, '')
    .replace(/^\d+[.)]\s+/, '')
    .trim()
}
