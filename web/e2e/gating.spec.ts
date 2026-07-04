import { test, expect } from '@playwright/test';
import { auditA11y, gotoAndSettle } from './helpers/a11y';
import { registerNewUser } from './helpers/users';
import { runSql } from './helpers/db';

// Mirrors CiCatalogSeeder (Task 8) — keep in sync.
const CAR_ID = 9911;
const TRACK_ID = 9951;

const setDemoFlag = (enabled: boolean) =>
  runSql(
    `UPDATE iracing."FeatureFlags" SET "IsEnabled" = ${enabled} WHERE "Key" = 'iracing-demo';`
  );

test.describe('feature-flag gating', () => {
  test.skip(!process.env.E2E_DEMO, 'Needs the seeded CI stack (E2E_DEMO=1)');

  test('flags off: gated routes render ComingSoon, nav hides, page passes axe', async ({
    page,
  }) => {
    // CI runs with workers=1, so this off-window can't race other tests.
    test.skip(!process.env.CI, 'Flag toggling is serialized (workers=1) only in CI');
    setDemoFlag(false);
    try {
      await registerNewUser(page);
      await page.goto('/series');
      await expect(
        page.getByRole('heading', { name: 'Live iRacing analytics arriving soon' })
      ).toBeVisible();
      await expect(page.getByRole('link', { name: 'Leaderboards' })).toHaveCount(0); // nav hidden
      await auditA11y(page); // restores the ComingSoonPage audit lost when CI enabled the flag
    } finally {
      setDemoFlag(true);
    }
  });

  test('demo on: gated routes render synthetic content with the demo banner', async ({ page }) => {
    await registerNewUser(page);
    await gotoAndSettle(page, '/series');
    await expect(page.getByText(/demo data/i).first()).toBeVisible(); // DemoBanner
    await gotoAndSettle(page, `/cars/${CAR_ID}`);
    await expect(page.getByText('Apex GT3 Falcon').first()).toBeVisible();
    await gotoAndSettle(page, `/tracks/${TRACK_ID}`);
    await expect(page.getByText('Cypress International Circuit').first()).toBeVisible();
  });

  test('demo on: a signed-out guest also sees gated content (anonymous flag read)', async ({
    page,
  }) => {
    // No registerNewUser — CI seeds iracing-demo enabled at MinimumRole=Standard, and the
    // feature-flags endpoint is anonymous, so a guest resolves the flag true too.
    await gotoAndSettle(page, '/series');
    await expect(page.getByText(/demo data/i).first()).toBeVisible(); // DemoBanner
  });
});
