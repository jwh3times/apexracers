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
  use: {
    baseURL,
    trace: 'on-first-retry',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: {
    // Locally, reuseExistingServer attaches to `docker compose up` on :8080 and this
    // command is never run. In CI nothing is on :8080, so Playwright launches the API,
    // which serves the SPA from wwwroot and self-migrates against the Postgres service.
    command: 'dotnet run --configuration Release --no-launch-profile --project src/ApexRacers.Api',
    cwd: '..',
    url: baseURL,
    timeout: 240_000,
    reuseExistingServer: true,
    stdout: 'pipe',
    stderr: 'pipe',
  },
});
