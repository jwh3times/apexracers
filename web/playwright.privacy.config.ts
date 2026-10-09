import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
  testDir: './privacy-e2e',
  workers: 1,
  fullyParallel: false,
  retries: 0,
  timeout: 60_000,
  use: { baseURL: 'http://127.0.0.1:8185', trace: 'retain-on-failure' },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: {
    command:
      'dotnet run --no-launch-profile --project src/ApexRacers.Tests -- --driver-browser-fixture',
    cwd: '..',
    url: 'http://127.0.0.1:8186/ready',
    timeout: 600_000,
    reuseExistingServer: false,
  },
});
