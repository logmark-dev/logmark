import { describe, it, expect } from 'vitest'
import { readFileSync, readdirSync } from 'fs'
import { resolve as resolvePath } from 'path'
import { resolve, BLOCK_PRIMITIVE_MAP } from '../src/engine/resolver.js'
import { compose } from '../src/engine/compositor.js'
import type { ViewBlock } from '../src/types/viewblock.js'

const SCHEMAS_DIR = resolvePath(import.meta.dirname, '../../../schemas/viewblocks')
const FIXTURES_DIR = resolvePath(SCHEMAS_DIR, 'fixtures')

interface FixtureFile {
  valid: Array<{ name: string; envelope: ViewBlock }>
  invalid: Array<{ name: string; expectedError: string; envelope: unknown }>
}

const registry = JSON.parse(
  readFileSync(resolvePath(SCHEMAS_DIR, 'block-types.json'), 'utf-8'),
) as { specVersion: number; types: string[] }

const fixtureFiles = readdirSync(FIXTURES_DIR).filter((f) => f.endsWith('.fixture.json'))

const validEnvelopes: Array<[string, ViewBlock]> = fixtureFiles.flatMap((file) => {
  const fixture = JSON.parse(
    readFileSync(resolvePath(FIXTURES_DIR, file), 'utf-8'),
  ) as FixtureFile
  return (fixture.valid ?? []).map(
    (v): [string, ViewBlock] => [`${file} :: ${v.name}`, v.envelope],
  )
})

describe('viewblock conformance (shared fixtures)', () => {
  it('found the shared fixture corpus', () => {
    expect(fixtureFiles.length).toBeGreaterThanOrEqual(6)
    expect(validEnvelopes.length).toBeGreaterThanOrEqual(6)
  })

  it.each(validEnvelopes)('%s parses, resolves, and composes without throwing', (_label, block) => {
    expect(registry.types).toContain(block.type)
    const resolved = resolve(block)
    expect(resolved.primitive).toBeTruthy()
    const root = compose([block], { theme: 'dark' })
    expect(root.children.length).toBeGreaterThan(0)
  })

  it('every resolver mapping key is a registered block type', () => {
    for (const key of Object.keys(BLOCK_PRIMITIVE_MAP)) {
      expect(registry.types, `resolver maps unregistered type '${key}'`).toContain(key)
    }
  })
})
