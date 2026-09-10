import { expect, type Page } from '@playwright/test';
import { waitForEmailedLink } from './mail';

/** A unique, valid email per call so tests never collide on the shared dev DB. */
export function uniqueEmail(prefix = 'apex-e2e'): string {
  const stamp = `${Date.now()}-${Math.floor(Math.random() * 1_000_000)}`;
  return `${prefix}-${stamp}@example.com`;
}

/** Meets the API Identity policy: length >= 8, with a digit, an uppercase, and a lowercase. */
export const TEST_PASSWORD = 'ApexRacer123';

/**
 * Registers a brand-new account through the UI, follows the emailed confirmation link, signs in,
 * and lands on the dashboard. Self-navigates to /login, so callers may call it from any page.
 * Returns the email used.
 *
 * Registration alone no longer authenticates anyone: it returns the same acknowledgement whether or
 * not the address was taken, and the account cannot sign in until confirmed (GHSA-72v6-mw4c-q96r).
 * The confirmation link is read back out of the API's Development mail drop the way a recipient
 * would read it — no endpoint hands it out.
 */
export async function registerNewUser(page: Page): Promise<string> {
  const email = uniqueEmail();

  await page.goto('/login');
  await page.getByRole('tab', { name: 'Create Account' }).click();
  await page.getByLabel('Email Address').fill(email);
  await page.getByLabel('Password', { exact: true }).fill(TEST_PASSWORD);
  await page.getByLabel('Confirm Password').fill(TEST_PASSWORD);
  await page.getByRole('button', { name: 'Create Account' }).click();
  await expect(page.getByRole('status')).toContainText(/confirmation link/i);

  await confirmEmail(page, email);
  await login(page, email, TEST_PASSWORD);

  return email;
}

/**
 * Follows the confirmation link emailed to a freshly registered address. The link's own host is
 * APP_BASE_URL (the production host), so only its query is reused against the stack under test.
 */
export async function confirmEmail(page: Page, email: string): Promise<void> {
  const link = await waitForEmailedLink(email, /confirm your apexracers email/i);

  await page.goto(`/verify-email?${link.searchParams.toString()}`);
  await expect(page.getByText(/your account is active/i)).toBeVisible();
}

/** Logs out via the TopNav profile menu and waits for the login page. */
export async function logout(page: Page): Promise<void> {
  await page.getByRole('button', { name: 'User menu' }).click();
  await page.getByRole('button', { name: 'Logout' }).click();
  await expect(page).toHaveURL(/\/login$/);
}

/** Signs in through the UI with existing credentials, landing on the dashboard. */
export async function login(page: Page, email: string, password: string): Promise<void> {
  await page.goto('/login');
  await page.getByRole('tab', { name: 'Sign In' }).click();
  await page.getByLabel('Email Address').fill(email);
  await page.getByLabel('Password', { exact: true }).fill(password);
  await page.getByRole('button', { name: 'Access Telemetry' }).click();
  await expect(page).toHaveURL(/\/dashboard$/);
}
