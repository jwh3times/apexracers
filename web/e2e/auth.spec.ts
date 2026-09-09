import { test, expect } from '@playwright/test';
import { registerNewUser, logout, login, TEST_PASSWORD } from './helpers/users';
import { waitForEmailedToken } from './helpers/mail';

test.describe('auth flows', () => {
  test('logout ends the session and protects authed routes', async ({ page }) => {
    await registerNewUser(page);
    await logout(page);
    await page.goto('/dashboard');
    await expect(page).toHaveURL(/\/login$/); // RequireAuth bounced us
  });

  test('password reset via the emailed link', async ({ page }) => {
    const email = await registerNewUser(page);
    await logout(page);

    await page.goto('/forgot-password');
    const respPromise = page.waitForResponse('**/api/auth/forgot-password');
    await page.getByLabel('Email Address').fill(email);
    await page.getByRole('button', { name: 'Send Reset Link' }).click();
    const body: unknown = await (await respPromise).json();

    // The response carries the generic acknowledgement and nothing else. It must never carry the
    // token again: echoing it made any reachable Development instance an account-takeover path
    // (GHSA-qmqp-gxpr-867g).
    expect(body).toEqual({
      message: 'If an account exists for that email, a password reset link has been sent.',
    });

    // The token reaches us the way it reaches a real driver — out of the delivered email, read
    // here from the API's Development mail drop.
    const token = await waitForEmailedToken(email, /reset your apexracers password/i);

    const newPassword = 'ApexRacer456';
    await page.goto(
      `/reset-password?email=${encodeURIComponent(email)}&token=${encodeURIComponent(token)}`
    );
    await page.getByLabel('New Password').fill(newPassword);
    await page.getByLabel('Confirm Password').fill(newPassword);
    await page.getByRole('button', { name: 'Reset Password' }).click();

    // Wait for the success state before navigating. login() below starts with a
    // page.goto(), which aborts the reset POST if it is still in flight — the password
    // then never changes and the login fails for a reason that looks nothing like the
    // cause. This also asserts the success UI actually renders, which nothing else did.
    await expect(page.getByText(/your password has been reset/i)).toBeVisible();

    await login(page, email, newPassword);

    // and the old password no longer works:
    await logout(page);
    await page.goto('/login');
    await page.getByLabel('Email Address').fill(email);
    await page.getByLabel('Password', { exact: true }).fill(TEST_PASSWORD);
    // Wait for the attempt to be rejected before asserting. A bare
    // `expect(page).not.toHaveURL(/dashboard$/)` passes the instant it is evaluated —
    // the URL is still /login — so it held even when the CORRECT password was supplied
    // and never actually proved the old one was refused.
    const rejected = page.waitForResponse('**/api/auth/login');
    await page.getByRole('button', { name: 'Access Telemetry' }).click();
    expect((await rejected).status()).toBe(401);
    await expect(page.getByText('Invalid email or password.')).toBeVisible();
    await expect(page).toHaveURL(/\/login$/);
  });
});
