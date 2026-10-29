import { describe, it, expect, beforeEach } from 'vitest'
import { applyTheme, DARK_TOKENS, LIGHT_TOKENS } from '../src/engine/theme.js'
import { createContainer } from './helpers.js'

describe('theme engine', () => {
  let container: HTMLElement

  beforeEach(() => {
    container = createContainer()
  })

  it('dark theme applies correct CSS vars', () => {
    applyTheme(container, 'dark')
    const style = container.style
    expect(style.getPropertyValue('--lb-bg-primary')).toBe('rgb(var(--surface, 30 30 46))')
    expect(style.getPropertyValue('--lb-text-primary')).toBe('rgb(var(--text, 228 228 231))')
    expect(style.getPropertyValue('--lb-accent')).toBe('rgb(var(--accent, 192 122 10))')
  })

  it('light theme applies correct CSS vars', () => {
    applyTheme(container, 'light')
    const style = container.style
    expect(style.getPropertyValue('--lb-bg-primary')).toBe('rgb(var(--surface, 250 250 250))')
    expect(style.getPropertyValue('--lb-text-primary')).toBe('rgb(var(--text, 30 30 46))')
    expect(style.getPropertyValue('--lb-accent')).toBe('rgb(var(--accent, 217 119 6))')
  })

  it('dark token set includes all required variables', () => {
    const keys = Object.keys(DARK_TOKENS)
    expect(keys.some(k => k.startsWith('--lb-bg'))).toBe(true)
    expect(keys.some(k => k.startsWith('--lb-text'))).toBe(true)
    expect(keys.some(k => k === '--lb-accent')).toBe(true)
    expect(keys.some(k => k.startsWith('--lb-signal'))).toBe(true)
    expect(keys.some(k => k.startsWith('--lb-font'))).toBe(true)
    expect(keys.some(k => k === '--lb-radius')).toBe(true)
  })

  it('light token set includes all required variables', () => {
    const keys = Object.keys(LIGHT_TOKENS)
    expect(keys.some(k => k.startsWith('--lb-bg'))).toBe(true)
    expect(keys.some(k => k.startsWith('--lb-text'))).toBe(true)
    expect(keys.some(k => k === '--lb-accent')).toBe(true)
    expect(keys.some(k => k.startsWith('--lb-signal'))).toBe(true)
    expect(keys.some(k => k.startsWith('--lb-font'))).toBe(true)
    expect(keys.some(k => k === '--lb-radius')).toBe(true)
  })

  it('switching theme replaces variables', () => {
    applyTheme(container, 'dark')
    expect(container.style.getPropertyValue('--lb-bg-primary')).toBe('rgb(var(--surface, 30 30 46))')

    applyTheme(container, 'light')
    expect(container.style.getPropertyValue('--lb-bg-primary')).toBe('rgb(var(--surface, 250 250 250))')
    expect(container.style.getPropertyValue('--lb-text-primary')).toBe('rgb(var(--text, 30 30 46))')
    expect(container.style.getPropertyValue('--lb-accent')).toBe('rgb(var(--accent, 217 119 6))')
  })
})
