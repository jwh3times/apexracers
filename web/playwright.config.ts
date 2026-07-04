import { defineConfig, devices } from '@playwright/test';

const PORT = 8080;
const baseURL = `http://localhost:${PORT}`;

export default defineConfig({
  testDir: './e2e',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  workers: process.env.CI ? 1 : undefined,
  reporter: process.env.CI ? [['html', { open: 'never' }], ['list']] : 'list',
  expect: {
    // Visual-regression defaults: kill animation/caret noise; 2% pixel tolerance
    // absorbs minor anti-aliasing drift. Tighten later if diffs stay quiet.
    toHaveScreenshot: { animations: 'disabled', caret: 'hide', maxDiffPixelRatio: 0.02 },
  },
  use: {
    baseURL,
    trace: 'on-first-retry',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: {
    // Locally (CI unset), reuseExistingServer attaches to `docker compose up` on :8080
    // and this command is never run. In CI, reuseExistingServer is false so Playwright
    // always starts a fresh server and fails loudly if :8080 is unexpectedly occupied.
    command: 'dotnet run --configuration Release --no-launch-profile --project src/ApexRacers.Api',
    cwd: '..',
    url: baseURL,
    timeout: 240_000,
    reuseExistingServer: !process.env.CI,
    stdout: 'pipe',
    stderr: 'pipe',
  },
});
