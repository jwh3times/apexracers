import { test, expect } from '@playwright/test';
import { formatViolations, type Violation } from './helpers/a11y';
import { auditA11y } from './helpers/a11y';
import { registerNewUser } from './helpers/users';

// Minimal fixture shaped like an axe violation — only the fields formatViolations reads.
const sampleViolation = {
  id: 'color-contrast',
  impact: 'serious',
  help: 'Elements must meet minimum color contrast ratio thresholds',
  helpUrl: 'https://dequeuniversity.com/rules/axe/4.10/color-contrast',
  nodes: [{ target: ['.kpi-value'] }],
};

test.describe('a11y helper: formatViolations', () => {
  test('summarizes rule id, impact, help URL, and node targets', () => {
    const message = formatViolations([sampleViolation] as unknown as Violation[]);
    expect(message).toContain('color-contrast');
    expect(message).toContain('serious');
    expect(message).toContain('dequeuniversity.com');
    expect(message).toContain('.kpi-value');
  });

  test('reports a clean message when there are no violations', () => {
    expect(formatViolations([])).toBe('No accessibility violations.');
  });
});

/** Pages that render real content with no auth in CI. */
const PUBLIC_PAGES = ['/', '/login', '/forgot-password', '/terms', '/privacy'];

/**
 * Authenticated pages that render real content without iRacing creds.
 * `/series` is flag-gated, so it renders ComingSoonPage inside the real AppShell
 * chrome — our single representative for every gated route.
 */
const AUTHED_PAGES = [
  '/dashboard',
  '/my-laps',
  '/telemetry',
  '/profile',
  '/support',
  '/settings',
  '/series',
];

/** Wait for the SPA route to render before auditing, so axe never sees a pre-mount frame. */
async function gotoAndSettle(page: import('@playwright/test').Page, path: string) {
  await page.goto(path);
  await page.locator('main, h1').first().waitFor({ state: 'visible' });
}

test.describe('accessibility: public pages (WCAG 2.1 A/AA)', () => {
  for (const path of PUBLIC_PAGES) {
    test(`${path} has no violations`, async ({ page }) => {
      await gotoAndSettle(page, path);
      await auditA11y(page);
    });
  }
});

test.describe('accessibility: authenticated pages (WCAG 2.1 A/AA)', () => {
  test('authed pages have no violations', async ({ page }) => {
    await registerNewUser(page);
    for (const path of AUTHED_PAGES) {
      await gotoAndSettle(page, path);
      await auditA11y(page);
    }
  });
});
