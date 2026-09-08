import { test, expect } from '@playwright/test';
import { readFile } from 'node:fs/promises';
import { registerNewUser } from './helpers/users';

// fixtures/demo-session.ibt is a minimal, syntactically valid .ibt binary generated
// once from FakeIbtBuilder (src/ApexRacers.Tests/Helpers/FakeIbtBuilder.cs) — mirror
// IbtParserTests.cs's happy-path usage (2 valid laps @ 90.5s) to regenerate it if the
// parser's format expectations ever change.
// Its Car 9911 and Track 9951 match CiCatalogSeeder's synthetic catalog (--ci).
test.describe('telemetry upload', () => {
  test('uploading a valid .ibt yields laps on My Laps', async ({ page }) => {
    await registerNewUser(page);
    await page.goto('/telemetry');
    await page.setInputFiles('input[type="file"]', 'e2e/fixtures/demo-session.ibt');
    // TelemetryPage's success card: "<n> valid lap(s) of <n> total" (TelemetryPage.tsx:76-77).
    await expect(page.getByText(/valid laps/i)).toBeVisible({ timeout: 15_000 });

    await page.goto('/my-laps');
    // MyLapsPage's empty state reads "No laps recorded yet." (MyLapsPage.tsx:126) — assert
    // both that it's gone and that the populated table rendered in its place.
    await expect(page.getByText(/no laps recorded yet/i)).toHaveCount(0);
    await expect(page.getByRole('table')).toBeVisible();
  });

  for (const [kind, header] of [
    ['car', 'CarID: 9911'],
    ['track', 'TrackID: 9951'],
  ]) {
    test(`rejects an unknown ${kind} without recording laps`, async ({ page }) => {
      await registerNewUser(page);
      await page.goto('/telemetry');
      const fixture = await readFile('e2e/fixtures/demo-session.ibt');
      const offset = fixture.indexOf(header);
      expect(offset).toBeGreaterThanOrEqual(0);
      // Replace only the same-width ID, preserving every binary header offset.
      fixture.write(header.replace(/\d+$/, '9999'), offset, 'ascii');
      const uploadResponse = page.waitForResponse(
        response =>
          response.url().endsWith('/api/telemetry/upload') && response.request().method() === 'POST'
      );
      await page.setInputFiles('input[type="file"]', {
        name: `unknown-${kind}.ibt`,
        mimeType: 'application/octet-stream',
        buffer: fixture,
      });
      expect((await uploadResponse).status()).toBe(400);
      await expect(page.getByText(/car or track is not in the catalog yet/i)).toBeVisible();
      await page.goto('/my-laps');
      await expect(page.getByText(/no laps recorded yet/i)).toBeVisible();
      await expect(page.getByRole('table')).toHaveCount(0);
    });
  }
});
