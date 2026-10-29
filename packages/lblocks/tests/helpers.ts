import { readFileSync } from 'fs'
import { resolve } from 'path'

const FIXTURES_DIR = resolve(import.meta.dirname, '../../../schemas/viewblocks/fixtures')

export function loadFixture(blockType: string, name: string) {
  const raw = readFileSync(
    resolve(FIXTURES_DIR, `${blockType}.fixture.json`),
    'utf-8'
  )
  const fixture = JSON.parse(raw)
  const entry = fixture.valid.find((v: { name: string }) => v.name === name)
  if (!entry) throw new Error(`Fixture "${name}" not found in ${blockType}`)
  return entry.envelope
}

export function createContainer(): HTMLElement {
  const el = document.createElement('div')
  document.body.appendChild(el)
  return el
}
