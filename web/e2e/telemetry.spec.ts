import { test, expect } from '@playwright/test';
import { registerNewUser } from './helpers/users';

// fixtures/demo-session.ibt is a minimal, syntactically valid .ibt binary generated
// once from FakeIbtBuilder (src/ApexRacers.Tests/Helpers/FakeIbtBuilder.cs) — mirror
// IbtParserTests.cs's happy-path usage (2 valid laps @ 90.5s) to regenerate it if the
// parser's format expectations ever change.
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
});
