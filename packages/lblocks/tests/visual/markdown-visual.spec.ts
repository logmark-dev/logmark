import { test, expect } from '@playwright/test'
import { readFileSync } from 'fs'
import { resolve } from 'path'

const FIXTURES_DIR = resolve(import.meta.dirname, '../markdown-fixtures')

const SNAPSHOT_FIXTURES = ['minimal', 'rich', 'tables', 'frontmatter']
// 'images' runs as smoke (no pixel diff) because remote image loading is
// nondeterministic. 'multi-block' runs as smoke because it overlaps 'rich'
// for pixel coverage and exists to assert that longer documents compose
// without error.
const SMOKE_ONLY = ['images', 'multi-block']
const THEMES: Array<'dark' | 'light'> = ['dark', 'light']

test.describe('markdown visual regression', () => {
  for (const theme of THEMES) {
    for (const name of SNAPSHOT_FIXTURES) {
      test(`${name} (${theme}) renders consistently`, async ({ page }) => {
        const json = readFileSync(resolve(FIXTURES_DIR, `${name}.json`), 'utf-8')
        await page.goto(`/markdown-fixture.html?theme=${theme}`)
        await page.evaluate((j) => (window as unknown as { renderFixture: (j: string) => void }).renderFixture(j), json)
        await page.waitForSelector('.lb-md-root')
        await page.evaluate(() => document.fonts.ready)
        // Direct locator.screenshot() bypasses toHaveScreenshot's stability
        // retry, which on Windows hangs in scroll-into-view checks for this
        // simple harness. The bounding rect IS stable (verified by manual
        // double-read); the retry framework just doesn't notice in time.
        const buffer = await page.locator('.lb-md-root').screenshot({ animations: 'disabled' })
        expect(buffer).toMatchSnapshot(`${name}-${theme}.png`, { maxDiffPixelRatio: 0.001 })
      })
    }
  }

  for (const name of SMOKE_ONLY) {
    test(`${name} renders without error`, async ({ page }) => {
      const json = readFileSync(resolve(FIXTURES_DIR, `${name}.json`), 'utf-8')
      const errors: string[] = []
      page.on('pageerror', (err) => errors.push(err.message))
      await page.goto('/markdown-fixture.html')
      await page.evaluate((j) => (window as unknown as { renderFixture: (j: string) => void }).renderFixture(j), json)
      await page.waitForSelector('.lb-md-root')
      expect(errors).toEqual([])
    })
  }
})
