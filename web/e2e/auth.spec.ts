import { test, expect } from '@playwright/test';
import {
  registerNewUser,
  confirmEmail,
  logout,
  login,
  uniqueEmail,
  TEST_PASSWORD,
} from './helpers/users';
import { waitForEmailedToken } from './helpers/mail';

test.describe('auth flows', () => {
  test('logout ends the session and protects authed routes', async ({ page }) => {
    await registerNewUser(page);
    await logout(page);
    await page.goto('/dashboard');
    await expect(page).toHaveURL(/\/login$/); // RequireAuth bounced us
  });

  test('registration never reveals whether an address is already registered', async ({ page }) => {
    const taken = await registerNewUser(page);
    await logout(page);

    const free = uniqueEmail();
    const attackerPassword = 'Guessed789';

    const onFree = await page.request.post('/api/auth/register', {
      data: { email: free, password: attackerPassword },
    });
    const onTaken = await page.request.post('/api/auth/register', {
      data: { email: taken, password: attackerPassword },
    });

    // Same status and byte-identical body. Registration used to answer the taken case with
    // Identity's "Email '…' is already taken." (GHSA-72v6-mw4c-q96r).
    expect(onTaken.status()).toBe(onFree.status());
    expect(await onTaken.json()).toEqual(await onFree.json());

    // The follow-up sign-in must not leak it either. Both addresses now hold an account the
    // attacker cannot use: the free one is unconfirmed, and the taken one kept its own password.
    for (const email of [free, taken]) {
      const attempt = await page.request.post('/api/auth/login', {
        data: { email, password: attackerPassword },
      });
      expect(attempt.status()).toBe(401);
    }
  });

  test('a registered account cannot sign in until the emailed link is followed', async ({
    page,
  }) => {
    const email = uniqueEmail();

    await page.goto('/login');
    await page.getByRole('tab', { name: 'Create Account' }).click();
    await page.getByLabel('Email Address').fill(email);
    await page.getByLabel('Password', { exact: true }).fill(TEST_PASSWORD);
    await page.getByLabel('Confirm Password').fill(TEST_PASSWORD);
    await page.getByRole('button', { name: 'Create Account' }).click();
    await expect(page.getByRole('status')).toContainText(/confirmation link/i);

    const beforeConfirming = await page.request.post('/api/auth/login', {
      data: { email, password: TEST_PASSWORD },
    });
    expect(beforeConfirming.status()).toBe(401);

    await confirmEmail(page, email);
    await login(page, email, TEST_PASSWORD);
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
