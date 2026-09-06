import { test, expect } from '@playwright/test';
import { registerNewUser, TEST_PASSWORD, uniqueEmail } from './helpers/users';

test.describe('sensitive account changes', () => {
  test('initial and changed iRacing identity require the current password', async ({ page }) => {
    await registerNewUser(page);
    await page.goto('/settings');
    const identity = page.getByLabel('iRacing Customer ID', { exact: true });
    const password = page.getByLabel('Password to change iRacing identity');
    const save = page.getByRole('button', { name: 'Save Changes' });
    const firstId = String(100_000_000 + Math.floor(Math.random() * 500_000_000));

    await expect(password).toHaveCount(0);
    await identity.fill(firstId);
    await expect(password).toHaveAttribute('required', '');
    await save.click();
    await expect(password).toBeFocused();

    await password.fill('WrongPassword123');
    const rejected = page.waitForResponse('**/api/auth/profile');
    await save.click();
    expect((await rejected).status()).toBe(400);
    await expect(page.getByText('Current password is incorrect.')).toBeVisible();
    await expect(password).toHaveValue('');
    await page.reload();
    await expect(identity).toHaveValue('');

    await identity.fill(firstId);
    await password.fill(TEST_PASSWORD);
    await save.click();
    await expect(page.getByRole('button', { name: 'Saved ✓' })).toBeVisible();
    await expect(password).toHaveCount(0);
    await page.reload();
    await expect(identity).toHaveValue(firstId);

    const nextId = String(Number(firstId) + 1);
    await identity.fill(nextId);
    await password.fill('WrongPassword123');
    const changedRejected = page.waitForResponse('**/api/auth/profile');
    await save.click();
    expect((await changedRejected).status()).toBe(400);
    await expect(page.getByText('Current password is incorrect.')).toBeVisible();
    await page.reload();
    await expect(identity).toHaveValue(firstId);

    await identity.fill(nextId);
    await password.fill(TEST_PASSWORD);
    await save.click();
    await expect(page.getByRole('button', { name: 'Saved ✓' })).toBeVisible();
    await expect(password).toHaveCount(0);
    await page.reload();
    await expect(identity).toHaveValue(nextId);
  });

  test('email verification requires the current password and clears it after each attempt', async ({
    page,
  }) => {
    const originalEmail = await registerNewUser(page);
    await page.goto('/settings');
    const email = page.getByLabel('Email Address', { exact: true });
    const password = page.getByLabel('Password to change email');
    const verify = page.getByRole('button', { name: 'Verify new email' });
    const newEmail = uniqueEmail('reauth');
    await email.fill(newEmail);
    await verify.click();
    await expect(password).toBeFocused();

    await password.fill('WrongPassword123');
    const rejected = page.waitForResponse('**/api/auth/request-email-change');
    await verify.click();
    expect((await rejected).status()).toBe(400);
    await expect(page.getByText('Current password is incorrect.')).toBeVisible();
    await expect(password).toHaveValue('');
    await expect(page.getByText(/pending verification/i)).toHaveCount(0);

    await password.fill(TEST_PASSWORD);
    await verify.click();
    await expect(
      page.getByText(`Pending verification: ${newEmail}. Check that inbox to confirm the change.`)
    ).toBeVisible();
    await expect(password).toHaveValue('');
    await page.reload();
    await expect(email).toHaveValue(originalEmail);
  });
});
