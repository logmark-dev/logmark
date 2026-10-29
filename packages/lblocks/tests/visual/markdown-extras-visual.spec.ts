import { test, expect } from '@playwright/test'
import { readFileSync } from 'fs'
import { resolve } from 'path'

const FIXTURES_DIR = resolve(import.meta.dirname, '../markdown-fixtures')

const THEMES: Array<'dark' | 'light'> = ['dark', 'light']

test.describe('markdown-extras visual regression', () => {
  for (const theme of THEMES) {
    test(`markdown-extras (${theme}) renders consistently`, async ({ page }) => {
      const json = readFileSync(resolve(FIXTURES_DIR, 'markdown-extras.json'), 'utf-8')
      await page.goto(`/markdown-fixture.html?theme=${theme}`)
      await page.evaluate((j) => (window as unknown as { renderFixture: (j: string) => void }).renderFixture(j), json)
      await page.waitForSelector('.lb-md-root')
      await page.evaluate(() => document.fonts.ready)
      // Wait for images to settle (inline images attempt to load from dev server)
      await page.waitForTimeout(200)
      // Direct locator.screenshot() bypasses toHaveScreenshot's stability
      // retry, which on Windows hangs in scroll-into-view checks for this
      // simple harness. The bounding rect IS stable; the retry framework just
      // doesn't notice in time.
      const buffer = await page.locator('.lb-md-root').screenshot({ animations: 'disabled' })
      expect(buffer).toMatchSnapshot(`markdown-extras-${theme}.png`, { maxDiffPixelRatio: 0.001 })
    })
  }

  for (const theme of THEMES) {
    test(`wide-table (${theme}) scrolls horizontally and stripes rows`, async ({ page }) => {
      // 14-column trading-diary table. At the default 800px viewport the table
      // content overflows the container, exercising overflowX: 'auto' on the
      // wrapper and the zebra-striped tbody rows.
      const json = readFileSync(resolve(FIXTURES_DIR, 'wide-table.json'), 'utf-8')
      await page.goto(`/markdown-fixture.html?theme=${theme}`)
      await page.evaluate((j) => (window as unknown as { renderFixture: (j: string) => void }).renderFixture(j), json)
      await page.waitForSelector('.lb-md-table-wrapper')
      await page.evaluate(() => document.fonts.ready)
      // Direct locator.screenshot() bypasses toHaveScreenshot's stability
      // retry, which on Windows hangs in scroll-into-view checks for this
      // simple harness. The bounding rect IS stable; the retry framework just
      // doesn't notice in time.
      const buffer = await page.locator('.lb-md-table-wrapper').screenshot({ animations: 'disabled' })
      expect(buffer).toMatchSnapshot(`wide-table-${theme}.png`, { maxDiffPixelRatio: 0.001 })
    })
  }

  for (const theme of THEMES) {
    test(`plaintext-log (${theme}) renders monospace with preserved whitespace`, async ({ page }) => {
      // Log-style plaintext block. Exercises the non-markdown renderer path:
      // monospace font, whitespace preservation, and leading-space indentation
      // on the arrow lines.
      const json = readFileSync(resolve(FIXTURES_DIR, 'plaintext-log.json'), 'utf-8')
      await page.goto(`/markdown-fixture.html?theme=${theme}`)
      await page.evaluate((j) => (window as unknown as { renderFixture: (j: string) => void }).renderFixture(j), json)
      await page.waitForSelector('.lb-md-plaintext')
      await page.evaluate(() => document.fonts.ready)
      // Direct locator.screenshot() bypasses toHaveScreenshot's stability
      // retry, which on Windows hangs in scroll-into-view checks for this
      // simple harness. The bounding rect IS stable; the retry framework just
      // doesn't notice in time.
      const buffer = await page.locator('.lb-md-plaintext').screenshot({ animations: 'disabled' })
      expect(buffer).toMatchSnapshot(`plaintext-log-${theme}.png`, { maxDiffPixelRatio: 0.001 })
    })
  }
})
