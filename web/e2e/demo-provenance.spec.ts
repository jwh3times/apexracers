import { expect, test } from '@playwright/test';
import { registerNewUser } from './helpers/users';
import type { DriverProfile, RaceHistoryRow } from '../src/services/api';

test.describe('credential-free synthetic Demo provenance', () => {
  test.skip(!process.env.E2E_DEMO, 'Requires --ci --demo and Demo enabled for Standard');

  test('an unlinked User sees useful Demo evidence and cannot select a real namespace', async ({
    page,
  }) => {
    await registerNewUser(page);
    await page.goto('/settings');
    await expect(page.getByLabel('iRacing Customer ID', { exact: true })).toHaveValue('');
    await page.getByRole('button', { name: /^Alpha/ }).click();
    await expect(page.getByText('Access tier updated.')).toBeVisible();

    const progressionResponse = page.waitForResponse(response =>
      response.url().endsWith('/api/users/me/progression')
    );
    await page.goto('/progression');
    const progression = await progressionResponse;
    expect(progression.status()).toBe(200);
    expect(progression.headers()['x-apexracers-driver-evidence-namespace']).toBe('demo');
    await expect(page.getByRole('status').filter({ hasText: 'Demo data' })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'iRating & Safety Rating' })).toBeVisible();
    await expect(page.getByText(/iRating history \(/).first()).toBeVisible();

    // Use the browser's existing request credentials; the User has claimed no Driver and
    // established no real proof or consent. These reads must remain entirely synthetic.
    const auth = (await progression.request().allHeaders()).authorization;
    expect(auth).toBeTruthy();
    const headers = { Authorization: auth, 'X-ApexRacers-Driver-Evidence-Namespace': 'real' };
    const profile = await page.request.get('/api/users/me/profile-stats', { headers });
    expect(profile.status()).toBe(200);
    expect(profile.headers()['x-apexracers-driver-evidence-namespace']).toBe('demo');
    const driverProfile = (await profile.json()) as DriverProfile;
    expect(driverProfile.driverName).toBe('Demo Driver');

    const visibleProfileResponse = page.waitForResponse(response =>
      response.url().endsWith('/api/users/me/profile-stats')
    );
    await page.goto('/profile');
    const visibleProfile = await visibleProfileResponse;
    expect(visibleProfile.status()).toBe(200);
    expect(visibleProfile.headers()['x-apexracers-driver-evidence-namespace']).toBe('demo');
    await expect(page.getByRole('status').filter({ hasText: 'Demo data' })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Licenses', exact: true })).toBeVisible();
    await expect(page.getByText('Class A · 3.50 SR', { exact: true })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Career by Category' })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Trophy Case' })).toBeVisible();
    await expect(page.getByText('Clean Driver', { exact: true })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Open Settings' })).toHaveCount(0);
    await expect(page.getByText(/^ID \d+$/)).toHaveCount(0);

    const races = await page.request.get('/api/users/me/races', { headers });
    expect(races.status()).toBe(200);
    const rows = (await races.json()) as RaceHistoryRow[];
    expect(rows.length).toBeGreaterThan(0);
    const detail = await page.request.get(`/api/subsessions/${rows[0].subsessionId}`, { headers });
    expect(detail.status()).toBe(200);
    expect(detail.headers()['x-apexracers-driver-evidence-namespace']).toBe('demo');
    await page.goto(`/races/${rows[0].subsessionId}`);
    await expect(page.getByRole('status').filter({ hasText: 'Demo data' })).toBeVisible();
    await expect(page.getByRole('table').first()).toBeVisible();

    const unknown = await page.request.get('/api/users/me/rivals/search?term=unseeded-fixture', {
      headers,
    });
    expect(unknown.status()).toBe(503);
    expect(unknown.headers()['x-apexracers-driver-evidence-namespace']).toBe('demo');
  });
});
