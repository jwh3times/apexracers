import { expect, type Page } from '@playwright/test';

/** A unique, valid email per call so tests never collide on the shared dev DB. */
export function uniqueEmail(prefix = 'apex-e2e'): string {
  const stamp = `${Date.now()}-${Math.floor(Math.random() * 1_000_000)}`;
  return `${prefix}-${stamp}@example.com`;
}

/** Meets the API Identity policy: length >= 8, with a digit, an uppercase, and a lowercase. */
export const TEST_PASSWORD = 'ApexRacer123';

/**
 * Registers a brand-new account through the UI and lands authenticated on the dashboard.
 * Self-navigates to /login, so callers may call it from any page. Returns the email used.
 */
export async function registerNewUser(page: Page): Promise<string> {
  const email = uniqueEmail();

  await page.goto('/login');
  await page.getByRole('tab', { name: 'Create Account' }).click();
  await page.getByLabel('Email Address').fill(email);
  await page.getByLabel('Password', { exact: true }).fill(TEST_PASSWORD);
  await page.getByLabel('Confirm Password').fill(TEST_PASSWORD);
  await page.getByRole('button', { name: 'Create Account' }).click();

  await expect(page).toHaveURL(/\/dashboard$/);
  await expect(page.getByRole('heading', { level: 1, name: /welcome back/i })).toBeVisible();

  return email;
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
