import { test, expect } from '@playwright/test';
import { auditA11y } from './helpers/a11y';
import { registerNewUser, logout, login, TEST_PASSWORD } from './helpers/users';
import { promoteToAdmin } from './helpers/admin';

test.describe('admin panel (WCAG 2.1 A/AA)', () => {
  test('promoted admin reaches /admin and it has no violations', async ({ page }) => {
    const email = await registerNewUser(page);
    promoteToAdmin(email);

    // The current JWT still says role=Standard — log out and back in for an Admin token.
    await logout(page);
    await login(page, email, TEST_PASSWORD);

    await page.goto('/admin');
    // AdminGuard bounces non-admins to /dashboard — staying on /admin proves the promotion took.
    await expect(page).toHaveURL(/\/admin$/);
    await page.locator('main, h1').first().waitFor({ state: 'visible' });
    await auditA11y(page);
  });
});
