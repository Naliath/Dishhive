import { defineConfig } from '@playwright/test';

const baseURL = process.env['DISHHIVE_WORKFLOW_BASE_URL'];
if (!baseURL || !['127.0.0.1', 'localhost', '[::1]'].includes(new URL(baseURL).hostname)) {
  throw new Error('The API workflow must use the disposable loopback test host started by LocalWorkflowTests.');
}

export default defineConfig({
  testDir: './e2e',
  testMatch: 'api-workflow.spec.ts',
  workers: 1,
  timeout: 90_000,
  reporter: 'list',
  outputDir: `${process.env['DISHHIVE_LOCAL_ARTIFACTS'] ?? 'test-results'}/api-browser`,
  use: {
    baseURL, channel: 'chrome', viewport: { width: 1280, height: 900 },
    locale: 'en-US', reducedMotion: 'reduce', serviceWorkers: 'block', trace: 'retain-on-failure'
  }
});
