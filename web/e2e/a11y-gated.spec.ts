import { test } from '@playwright/test';
import { auditA11y, gotoAndSettle } from './helpers/a11y';
import { registerNewUser } from './helpers/users';

/**
 * Axe audits for every iRacing-gated route, rendered with real (synthetic) content
 * under the iracing-demo flag. Requires a DB seeded via
 * `dotnet run --project src/ApexRacers.Seeder -- --ci --demo` with the iracing-demo
 * flag enabled for Standard (e2e.yml does both; locally, seed + flip the flag, then
 * run with E2E_DEMO=1).
 *
 * Ids mirror src/ApexRacers.Seeder/CiCatalogSeeder.cs (CiCatalog) — keep in sync.
 */
const SERIES_ID = 9900;
const SEASON_ID = 99001;
const WEEK = 1;
const CAR_ID = 9911;
const TRACK_ID = 9951;
const SUBSESSION_ID = -(SEASON_ID * 10000 + WEEK * 100); // week 1, car index 0

const PUBLIC_GATED = [
  '/series',
  `/series/${SERIES_ID}/schedule`,
  `/series/${SERIES_ID}/standings`,
  `/series/${SERIES_ID}/weeks/${WEEK}`,
  `/series/${SERIES_ID}/weeks/${WEEK}/strategy`,
  `/races/${SUBSESSION_ID}`,
  '/cars',
  `/cars/${CAR_ID}`,
  '/tracks',
  `/tracks/${TRACK_ID}`,
];

const AUTHED_GATED = [
  '/analytics',
  '/progression',
  '/recommendations',
  '/races',
  '/leaderboards',
  '/compare',
  '/live',
  `/series/${SERIES_ID}/weeks/${WEEK}/cars/${CAR_ID}/percentile`,
];

test.describe('accessibility: iRacing-gated pages under iracing-demo (WCAG 2.1 A/AA)', () => {
  test.skip(
    !process.env.E2E_DEMO,
    'Needs --ci --demo seed + iracing-demo enabled (set E2E_DEMO=1)'
  );

  test('public gated pages have no violations', async ({ page }) => {
    await registerNewUser(page); // flags are fetched authenticated — see spec header
    for (const path of PUBLIC_GATED) {
      await gotoAndSettle(page, path);
      await auditA11y(page);
    }
  });

  test('authed gated pages have no violations', async ({ page }) => {
    await registerNewUser(page);
    for (const path of AUTHED_GATED) {
      await gotoAndSettle(page, path);
      await auditA11y(page);
    }
  });
});
