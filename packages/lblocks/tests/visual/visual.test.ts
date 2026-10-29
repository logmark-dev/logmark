import { test, expect } from '@playwright/test'

test.describe('visual regression', () => {
  test('digest composition - dark theme', async ({ page }) => {
    await page.goto('/')
    await page.waitForSelector('.lb-root')
    const container = page.locator('.digest-container')
    await expect(container).toHaveScreenshot('digest-dark.png')
  })

  test('digest composition - light theme', async ({ page }) => {
    await page.goto('/')
    await page.waitForSelector('.lb-root')
    await page.click('#btn-light')
    await page.waitForTimeout(100)
    const container = page.locator('.digest-container')
    await expect(container).toHaveScreenshot('digest-light.png')
  })

  test('bar-chart primitive - dark theme', async ({ page }) => {
    await page.goto('/')
    await page.waitForSelector('.lb-root')
    const slot = page.locator('.gallery-slot').nth(0)
    await expect(slot).toHaveScreenshot('bar-chart-activity-dark.png')
  })

  test('bar-chart primitive - light theme', async ({ page }) => {
    await page.goto('/')
    await page.waitForSelector('.lb-root')
    await page.click('#btn-light')
    await page.waitForTimeout(100)
    const slot = page.locator('.gallery-slot').nth(0)
    await expect(slot).toHaveScreenshot('bar-chart-activity-light.png')
  })

  test('label-value-list primitive - dark theme', async ({ page }) => {
    await page.goto('/')
    await page.waitForSelector('.lb-root')
    const slot = page.locator('.gallery-slot').nth(2)
    await expect(slot).toHaveScreenshot('label-value-list-dark.png')
  })

  test('label-value-list primitive - light theme', async ({ page }) => {
    await page.goto('/')
    await page.waitForSelector('.lb-root')
    await page.click('#btn-light')
    await page.waitForTimeout(100)
    const slot = page.locator('.gallery-slot').nth(2)
    await expect(slot).toHaveScreenshot('label-value-list-light.png')
  })

  test('narrative primitive - dark theme', async ({ page }) => {
    await page.goto('/')
    await page.waitForSelector('.lb-root')
    const slot = page.locator('.gallery-slot').nth(3)
    await expect(slot).toHaveScreenshot('narrative-dark.png')
  })

  test('narrative primitive - light theme', async ({ page }) => {
    await page.goto('/')
    await page.waitForSelector('.lb-root')
    await page.click('#btn-light')
    await page.waitForTimeout(100)
    const slot = page.locator('.gallery-slot').nth(3)
    await expect(slot).toHaveScreenshot('narrative-light.png')
  })

  test('comparison primitive - dark theme', async ({ page }) => {
    await page.goto('/')
    await page.waitForSelector('.lb-root')
    const slot = page.locator('.gallery-slot').nth(4)
    await expect(slot).toHaveScreenshot('comparison-dark.png')
  })

  test('comparison primitive - light theme', async ({ page }) => {
    await page.goto('/')
    await page.waitForSelector('.lb-root')
    await page.click('#btn-light')
    await page.waitForTimeout(100)
    const slot = page.locator('.gallery-slot').nth(4)
    await expect(slot).toHaveScreenshot('comparison-light.png')
  })
})
