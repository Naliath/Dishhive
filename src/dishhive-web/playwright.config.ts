import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: './e2e',
  testIgnore: ['**/api-workflow.spec.ts', '**/pwa-freshness.spec.ts'],
  snapshotPathTemplate: '{testDir}/{testFilePath}-snapshots/{arg}{ext}',
  fullyParallel: false,
  reporter: [['list'], ['html', { outputFolder: 'playwright-report', open: 'never' }]],
  use: {
    baseURL: 'http://127.0.0.1:4300',
    viewport: { width: 360, height: 900 },
    colorScheme: 'light',
    timezoneId: 'UTC',
    locale: 'en-US',
    channel: 'chrome',
    reducedMotion: 'reduce',
    trace: 'retain-on-failure'
  },
  expect: {
    toHaveScreenshot: { animations: 'disabled', maxDiffPixelRatio: 0.005 }
  },
  webServer: {
    command: 'npm start -- --host 127.0.0.1',
    url: 'http://127.0.0.1:4300',
    reuseExistingServer: true,
    timeout: 120_000
  }
});
