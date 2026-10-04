import { test, expect, type APIResponse, type Page, type Response } from '@playwright/test';
import { readFile } from 'node:fs/promises';
import { registerNewUser } from './helpers/users';

// fixtures/demo-session.ibt is a minimal, syntactically valid .ibt binary generated
// once from FakeIbtBuilder (src/ApexRacers.Tests/Helpers/FakeIbtBuilder.cs) — mirror
// IbtParserTests.cs's happy-path usage (2 valid laps @ 90.5s) to regenerate it if the
// parser's format expectations ever change.
// Its Car 9911 and Track 9951 match CiCatalogSeeder's synthetic catalog (--ci).
// A valid recording is not ownership proof or Personal Analytics Consent. Until
// telemetry joins protected dispatch, its authorization fence precedes parsing.
const unavailableMessage =
  'This Driver workflow is unavailable while authorization is being implemented.';

async function expectUnavailable(response: Response | APIResponse) {
  expect(response.status()).toBe(503);
  expect(response.headers()['cache-control']).toBe('no-store');
  const problem = (await response.json()) as Record<string, unknown>;
  expect(problem).toEqual(expect.objectContaining({ status: 503, detail: unavailableMessage }));
  expect(problem).not.toHaveProperty('customerId');
  expect(problem).not.toHaveProperty('driverName');
}

async function expectMyLapsUnavailable(page: Page) {
  const lapsResponse = page.waitForResponse(response =>
    response.url().endsWith('/api/telemetry/laps')
  );
  await page.goto('/my-laps');
  await expectUnavailable(await lapsResponse);
  await expect(page.getByText(unavailableMessage, { exact: true })).toBeVisible();
  await expect(page.getByRole('table')).toHaveCount(0);
  await expect(page.getByText(/no laps recorded yet/i)).toHaveCount(0);
}

test.describe('telemetry authorization fence', () => {
  test('a valid recording and a forged Demo namespace cannot authorize attributed laps', async ({
    page,
  }) => {
    await registerNewUser(page);
    await page.goto('/telemetry');
    const uploadResponse = page.waitForResponse(
      response =>
        response.url().endsWith('/api/telemetry/upload') && response.request().method() === 'POST'
    );
    await page.setInputFiles('input[type="file"]', 'e2e/fixtures/demo-session.ibt');
    const deniedUpload = await uploadResponse;
    await expectUnavailable(deniedUpload);
    await expect(page.getByText(unavailableMessage, { exact: true })).toBeVisible();
    await expect(page.getByText(/valid laps/i)).toHaveCount(0);
    await expect(page.getByText(/^ID 12345$/)).toHaveCount(0);
    await expect(page.getByText('Jerry Holland', { exact: true })).toHaveCount(0);
    await expect(page.getByText('1:30.500', { exact: true })).toHaveCount(0);

    // Reuse only this browser's credentials; a caller-selected namespace cannot
    // turn the recorder's ID into a synthetic grant or verified attribution.
    const authorization = (await deniedUpload.request().allHeaders()).authorization;
    expect(authorization).toBeTruthy();
    const forgedUpload = await page.request.post('/api/telemetry/upload', {
      headers: {
        Authorization: authorization,
        'X-ApexRacers-Driver-Evidence-Namespace': 'demo',
      },
      multipart: {
        file: {
          name: 'demo-session.ibt',
          mimeType: 'application/octet-stream',
          buffer: await readFile('e2e/fixtures/demo-session.ibt'),
        },
      },
    });
    await expectUnavailable(forgedUpload);
    await expectMyLapsUnavailable(page);
  });

  for (const [kind, header] of [
    ['car', 'CarID: 9911'],
    ['track', 'TrackID: 9951'],
  ]) {
    test(`denies an unknown ${kind} recording before revealing catalog membership`, async ({
      page,
    }) => {
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
      await expectUnavailable(await uploadResponse);
      await expect(page.getByText(unavailableMessage, { exact: true })).toBeVisible();
      await expect(page.getByText(/car or track is not in the catalog yet/i)).toHaveCount(0);
      await expect(page.getByText(/valid laps/i)).toHaveCount(0);
      await expectMyLapsUnavailable(page);
    });
  }
});
