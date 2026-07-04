import { test, expect } from '@playwright/test';

/**
 * Visual regression for the stable public pages. Baselines are Linux/Chromium
 * PNGs committed under e2e/visual.spec.ts-snapshots/, generated in CI (local
 * Windows/macOS font rendering differs, so the suite is CI-only).
 *
 * To refresh baselines: run the E2E workflow via workflow_dispatch with
 * update_snapshots=true, download the visual-baselines artifact, and commit it.
 */
const PAGES: Array<[name: string, path: string]> = [
  ['home', '/'],
  ['login', '/login'],
  ['terms', '/terms'],
  ['privacy', '/privacy'],
];

test.describe('visual regression: public pages', () => {
  test.skip(!process.env.CI, 'Baselines are generated on Linux CI');

  for (const [name, path] of PAGES) {
    test(`${name} matches baseline`, async ({ page }) => {
      await page.goto(path);
      await page.locator('main, h1').first().waitFor({ state: 'visible' });
      await expect(page).toHaveScreenshot(`${name}.png`, { fullPage: true });
    });
  }
});
