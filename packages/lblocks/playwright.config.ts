import { defineConfig } from '@playwright/test'

export default defineConfig({
  testDir: 'tests/visual',
  timeout: 30_000,
  // Single worker keeps the dev server (a tiny Node http server with no
  // pooling) from being overloaded by parallel page loads, which otherwise
  // produces flaky scroll-into-view stability timeouts on Windows.
  workers: 1,
  use: {
    baseURL: 'http://localhost:3712',
    viewport: { width: 800, height: 600 },
  },
  webServer: {
    command: 'npx tsc && node --import tsx dev/server.ts',
    port: 3712,
    reuseExistingServer: true,
  },
  snapshotPathTemplate: 'tests/screenshots/{arg}{ext}',
  expect: {
    toHaveScreenshot: {
      maxDiffPixelRatio: 0.001,
    },
  },
})
