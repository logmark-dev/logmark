import { describe, it, expect } from 'vitest'
import { resolve } from '../src/engine/resolver.js'
import { compose } from '../src/engine/compositor.js'
import type { ViewBlock } from '../src/types/viewblock.js'

const unknownBlock = {
  type: 'holo-deck-3000',
  version: 1,
  data: { anything: true },
} as unknown as ViewBlock

describe('resolver degradation', () => {
  it('resolve() returns the unknown-block placeholder instead of throwing', () => {
    const resolved = resolve(unknownBlock)
    expect(resolved.primitive).toBe('unknown-block')
    expect(resolved.data).toEqual({ type: 'holo-deck-3000' })
    expect(resolved.sectionLabel).toBe('')
  })

  it('compose() renders a card for an unknown block without throwing', () => {
    const root = compose([unknownBlock], { theme: 'dark' })
    expect(root.querySelector('.lb-unknown-block')).not.toBeNull()
    expect(root.textContent).toContain('holo-deck-3000')
  })

  it('unknown envelope fields are ignored', () => {
    const withExtras = {
      type: 'narrative-summary',
      version: 1,
      specVersion: 1,
      futureField: 'ignored',
      data: { text: 'hello' },
    } as unknown as ViewBlock
    const resolved = resolve(withExtras)
    expect(resolved.primitive).toBe('narrative')
  })
})
