import { test, expect } from '@playwright/test';
import { auditA11y } from './helpers/a11y';
import { registerNewUser, TEST_PASSWORD } from './helpers/users';
import { promoteToAdmin } from './helpers/admin';

test.describe('admin panel (WCAG 2.1 A/AA)', () => {
  test('promoted admin reaches /admin and it has no violations', async ({ page }) => {
    const email = await registerNewUser(page);
    promoteToAdmin(email);

    // The current JWT still says role=Standard — log out and back in for an Admin token.
    await page.getByRole('button', { name: 'User menu' }).click();
    await page.getByRole('button', { name: 'Logout' }).click();
    await expect(page).toHaveURL(/\/login$/);

    await page.getByRole('tab', { name: 'Sign In' }).click();
    await page.getByLabel('Email Address').fill(email);
    await page.getByLabel('Password', { exact: true }).fill(TEST_PASSWORD);
    await page.getByRole('button', { name: 'Access Telemetry' }).click();
    await expect(page).toHaveURL(/\/dashboard$/);

    await page.goto('/admin');
    // AdminGuard bounces non-admins to /dashboard — staying on /admin proves the promotion took.
    await expect(page).toHaveURL(/\/admin$/);
    await page.locator('main, h1').first().waitFor({ state: 'visible' });
    await auditA11y(page);
  });
});
