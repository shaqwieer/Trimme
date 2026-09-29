import { defineConfig, devices } from '@playwright/test';

/**
 * End-to-end tests run against a running stack (default: docker compose, web on :3000 with API behind it).
 * Override with E2E_BASE_URL. Spec §19 flows (E1–E7) are added in their phases under ./flows.
 */
export default defineConfig({
  testDir: '.',
  testMatch: ['smoke/**/*.spec.ts', 'flows/**/*.spec.ts'],
  outputDir: 'test-results',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? [['list'], ['html', { open: 'never' }]] : 'list',
  use: {
    baseURL: process.env.E2E_BASE_URL ?? 'http://localhost:3000',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    // The app's reduced-motion rule makes transitions instant, so axe never measures a colour mid-fade (a slot
    // turning navy measured 2.37:1 on a fast CI runner).
    contextOptions: { reducedMotion: 'reduce' },
  },
  projects: [
    {
      name: 'smoke',
      testMatch: 'smoke/**/*.spec.ts',
      use: { ...devices['Desktop Chrome'], viewport: { width: 1440, height: 900 } },
    },
    {
      name: 'flows',
      testMatch: 'flows/**/*.spec.ts',
      timeout: 90_000,
      use: { ...devices['Desktop Chrome'], viewport: { width: 1280, height: 900 } },
    },
  ],
});
