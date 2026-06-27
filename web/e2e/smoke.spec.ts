import { test, expect } from '@playwright/test';
import { registerNewUser } from './helpers/users';

test.describe('smoke: register -> dashboard', () => {
  test('landing renders, new user can register, session persists across reload', async ({
    page,
  }) => {
    // 1. Landing page renders (hero h1; level:1 avoids colliding with the section h2).
    await page.goto('/');
    await expect(page.getByRole('heading', { level: 1, name: /win races/i })).toBeVisible();

    // 2. The "Sign in" CTA routes to the auth page (exact match: avoid substring collisions).
    await page.getByRole('link', { name: 'Sign in', exact: true }).click();
    await expect(page).toHaveURL(/\/login$/);

    // 3. Register a fresh user -> lands authenticated on the dashboard.
    //    (registerNewUser self-navigates to /login, which is harmless here.)
    await registerNewUser(page);

    // 4. Reload: the session rehydrates from IndexedDB; no bounce to /login.
    await page.reload();
    await expect(page).toHaveURL(/\/dashboard$/);
    await expect(page.getByRole('heading', { level: 1, name: /welcome back/i })).toBeVisible();
  });
});
