import { defineConfig } from '@playwright/test';

// Browser test of the vertical: the Angular SPA (dev server, proxying /api to the published API) against the real stack.
// Requires: core stack up, `node scripts/lab/pipeline.mjs`, and the API published with deploy/compose.e2e.yaml.
export default defineConfig({
  testDir: '.',
  testMatch: '*.spec.mjs',
  timeout: 120_000,
  retries: 0,
  reporter: [['list']],
  use: { baseURL: 'http://127.0.0.1:4200', trace: 'retain-on-failure' },
  projects: [{ name: 'chromium', use: { browserName: 'chromium' } }],
  webServer: {
    command: 'npx ng serve --host 127.0.0.1 --port 4200 --proxy-config proxy.conf.json',
    cwd: '../../src/monitoring-web',
    url: 'http://127.0.0.1:4200',
    reuseExistingServer: true,
    timeout: 180_000
  }
});
