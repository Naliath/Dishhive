import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: './e2e', testMatch: '**/pwa-freshness.spec.ts', workers: 1, timeout: 120_000,
  reporter: [['list']],
  use: { baseURL: 'http://127.0.0.1:4301', channel: 'chrome', viewport: { width: 360, height: 900 },
    locale: 'en-US', timezoneId: 'UTC', trace: 'retain-on-failure' },
  webServer: { command: 'node e2e/serve-pwa-fixture.cjs', url: 'http://127.0.0.1:4301', reuseExistingServer: false }
});
