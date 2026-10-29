/**
 * Minimal token-driven control kit (design pass Q5). Shared by every
 * steering surface (settings now; routes editor and protocol config
 * later). Components take data + callbacks and never import views.
 */

export function ckButton(
  label: string,
  onClick: () => void,
  opts: { kind?: 'primary' | 'secondary' | 'ghost'; action?: string } = {},
): HTMLButtonElement {
  const button = document.createElement('button')
  button.type = 'button'
  button.className =
    opts.kind === 'secondary' ? 'btn secondary' : opts.kind === 'ghost' ? 'btn ghost' : 'btn'
  button.textContent = label
  if (opts.action) button.dataset.action = opts.action
  button.addEventListener('click', onClick)
  return button
}

export function ckSelect<T extends string>(
  label: string,
  options: readonly T[],
  value: T,
  onChange: (v: T) => void,
): HTMLElement {
  const wrapper = document.createElement('label')
  wrapper.className = 'ck-select'

  const span = document.createElement('span')
  span.className = 'ck-select-label'
  span.textContent = label

  const shell = document.createElement('span')
  shell.className = 'ck-select-shell'
  const select = document.createElement('select')
  for (const option of options) {
    const el = document.createElement('option')
    el.value = option
    el.textContent = option
    el.selected = option === value
    select.appendChild(el)
  }
  select.addEventListener('change', () => onChange(select.value as T))
  const chevron = document.createElement('span')
  chevron.className = 'ck-select-chevron'
  chevron.setAttribute('aria-hidden', 'true')
  chevron.textContent = '⌄'
  shell.append(select, chevron)

  wrapper.append(span, shell)
  return wrapper
}

export type CkPillTone = 'positive' | 'neutral' | 'dim'

export function ckStatusPill(label: string, tone: CkPillTone): HTMLElement {
  const pill = document.createElement('span')
  pill.className = `ck-pill ck-pill-${tone}`
  pill.textContent = label
  return pill
}

export function ckCapabilityChip(name: string, on: boolean): HTMLElement {
  const el = document.createElement('span')
  el.className = on ? 'ck-cap on' : 'ck-cap off'
  const dot = document.createElement('span')
  dot.className = 'ck-cap-dot'
  dot.setAttribute('aria-hidden', 'true')
  el.append(dot, document.createTextNode(name))
  el.title = on ? `${name} is available` : `${name} is not available on this adapter`
  return el
}

export interface CkDragListItem {
  id: string
  render(): HTMLElement
}

/**
 * jsdom (the test harness's DOM) has no global `CSS`, so `CSS.escape` is
 * undefined there - use it when present (identical browser behavior) and
 * fall back to a minimal attribute-value escape otherwise, so the
 * querySelector lookups in ckDragList's keyboard path don't throw in tests.
 */
export const escapeAttr = (v: string): string =>
  typeof CSS !== 'undefined' && CSS.escape ? CSS.escape(v) : v.replace(/"/g, '\\"')

/**
 * Drag-to-reorder list. Rows carry a grip glyph and are HTML5-draggable;
 * ArrowUp/ArrowDown on a focused row is the keyboard path. onReorder
 * fires with the new id order after every move.
 */
export function ckDragList(items: CkDragListItem[], onReorder: (order: string[]) => void): HTMLElement {
  const list = document.createElement('div')
  list.className = 'ck-drag-list'
  let order = items.map((item) => item.id)
  const byId = new Map(items.map((item) => [item.id, item]))

  const currentOrder = (): string[] => [...order]

  const move = (id: string, delta: number): void => {
    const from = order.indexOf(id)
    const to = from + delta
    if (from < 0 || to < 0 || to >= order.length) return
    ;[order[from], order[to]] = [order[to], order[from]]
    renderRows()
    onReorder(currentOrder())
  }

  const renderRows = (): void => {
    list.replaceChildren()
    for (const id of order) {
      const item = byId.get(id)
      if (!item) continue
      const row = document.createElement('div')
      row.className = 'ck-drag-row'
      row.draggable = true
      row.tabIndex = 0
      row.dataset.dragId = id
      row.setAttribute('role', 'listitem')
      row.setAttribute('aria-label', `${id} - drag or use arrow keys to reorder`)

      const grip = document.createElement('span')
      grip.className = 'ck-drag-grip'
      grip.setAttribute('aria-hidden', 'true')
      grip.textContent = '⠿'

      row.append(grip, item.render())

      row.addEventListener('dragstart', (e) => {
        e.dataTransfer?.setData('text/plain', id)
        row.classList.add('dragging')
      })
      row.addEventListener('dragend', () => row.classList.remove('dragging'))
      row.addEventListener('dragover', (e) => {
        e.preventDefault()
        row.classList.add('drag-over')
      })
      row.addEventListener('dragleave', () => row.classList.remove('drag-over'))
      row.addEventListener('drop', (e) => {
        e.preventDefault()
        row.classList.remove('drag-over')
        const draggedId = e.dataTransfer?.getData('text/plain')
        if (!draggedId || draggedId === id) return
        const from = order.indexOf(draggedId)
        const to = order.indexOf(id)
        if (from < 0 || to < 0) return
        order.splice(from, 1)
        order.splice(to, 0, draggedId)
        renderRows()
        onReorder(currentOrder())
      })
      row.addEventListener('keydown', (e) => {
        if (e.key === 'ArrowUp') {
          e.preventDefault()
          move(id, -1)
          list.querySelector<HTMLElement>(`[data-drag-id="${escapeAttr(id)}"]`)?.focus()
        } else if (e.key === 'ArrowDown') {
          e.preventDefault()
          move(id, 1)
          list.querySelector<HTMLElement>(`[data-drag-id="${escapeAttr(id)}"]`)?.focus()
        }
      })

      list.appendChild(row)
    }
  }

  renderRows()
  return list
}
