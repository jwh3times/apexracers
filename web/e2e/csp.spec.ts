import { test, expect, type Page } from '@playwright/test';
import { registerNewUser } from './helpers/users';

// Never bypass CSP here: these checks exercise the built SPA served by ASP.NET,
// not Vite's development server or a synthetic policy attached by the test.
test.use({ bypassCSP: false });

async function watchViolations(page: Page) {
  const violations: string[] = [];
  await page.exposeFunction('recordCspViolation', (directive: string, uri: string) => {
    violations.push(`${directive}: ${uri}`);
  });
  await page.addInitScript(() => {
    document.addEventListener('securitypolicyviolation', event => {
      void (
        window as unknown as {
          recordCspViolation: (directive: string, uri: string) => Promise<void>;
        }
      ).recordCspViolation(event.effectiveDirective, event.blockedURI);
    });
  });
  return violations;
}

async function settle(page: Page) {
  // This is a resource-policy audit: wait for API-backed images/styles as well as
  // the shell. A visible <main> alone can precede the data and hide violations.
  await page.waitForLoadState('networkidle');
  await page.evaluate(async () => {
    await document.fonts.ready;
    await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
  });
}

test('built SPA loads local assets, restores theme, and permits React style properties', async ({
  page,
  baseURL,
}) => {
  const violations = await watchViolations(page);
  const externalRequests: string[] = [];
  page.on('request', request => {
    const url = new URL(request.url());
    if (url.protocol.startsWith('http') && url.origin !== new URL(baseURL!).origin) {
      externalRequests.push(request.url());
    }
  });
  await page.addInitScript(() => localStorage.setItem('ar_theme', 'dark'));
  const response = await page.goto('/');
  const policy = response?.headers()['content-security-policy'];
  expect(policy).toContain("script-src 'self'");
  expect(policy).toContain("style-src 'self'");
  expect(policy).toContain("connect-src 'self'");
  expect(policy).not.toMatch(/unsafe-inline|unsafe-eval/);
  await expect(page.getByRole('heading', { level: 1, name: /win races/i })).toBeVisible();
  await expect(page.locator('html')).toHaveClass(/theme-dark/);
  // HomePage uses React's style prop for this responsive size (CSSOM assignment).
  await expect(page.getByRole('heading', { level: 1 })).toHaveCSS('font-size', '68px');
  await settle(page);
  await page.goto('/login');
  await expect(page.getByRole('button', { name: 'Access Telemetry' })).toBeVisible();
  await settle(page);
  const symbolFonts = await page.evaluate(async () => {
    const faces = await document.fonts.load('16px "Material Symbols Outlined Variable"');
    return faces.map(face => ({ family: face.family, status: face.status }));
  });
  expect(symbolFonts).toEqual([{ family: 'Material Symbols Outlined Variable', status: 'loaded' }]);
  await expect(page.locator('.material-symbols-outlined').first()).toHaveCSS(
    'font-family',
    /Material Symbols Outlined Variable/
  );
  expect(externalRequests).toEqual([]);
  expect(violations).toEqual([]);
});

test('registration and authenticated navigation work without CSP violations', async ({ page }) => {
  const violations = await watchViolations(page);
  await registerNewUser(page);
  for (const path of ['/dashboard', '/profile', '/settings', '/support']) {
    await page.goto(path);
    await expect(page.locator('main')).toBeVisible();
    await settle(page);
  }
  expect(violations).toEqual([]);
});

test('demo routes render under CSP without relaxing dynamic styles', async ({ page }) => {
  test.skip(!process.env.E2E_DEMO, 'Needs --ci --demo seed and enabled demo flag');
  const violations = await watchViolations(page);
  await registerNewUser(page);
  for (const path of [
    '/series',
    '/series/9900/schedule',
    '/series/9900/standings',
    '/series/9900/weeks/1',
    '/series/9900/weeks/1/strategy',
    '/series/9900/weeks/1/cars/9911/percentile',
    '/races/-990010100',
    '/cars',
    '/cars/9911',
    '/tracks',
    '/tracks/9951',
    '/analytics',
    '/progression',
    '/recommendations',
    '/races',
    '/leaderboards',
    '/compare',
    '/live',
  ]) {
    await page.goto(path);
    await expect(page.locator('main')).toBeVisible();
    await settle(page);
    expect(violations, path).toEqual([]);
  }
});

test('CSP blocks injected scripts, stylesheet text, and third-party connections', async ({
  page,
}) => {
  const violations = await watchViolations(page);
  await page.goto('/');
  await expect(page.getByRole('heading', { level: 1, name: /win races/i })).toBeVisible();
  await settle(page);
  expect(violations).toEqual([]);
  await page.evaluate(() => {
    const script = document.createElement('script');
    script.textContent = 'document.documentElement.dataset.cspInjected = "yes"';
    document.head.append(script);
    const style = document.createElement('style');
    style.textContent = 'body { --csp-injected: yes; }';
    document.head.append(style);
    // .invalid cannot be a real exfiltration destination; CSP must reject before network I/O.
    void fetch('https://csp-probe.invalid/probe').catch(() => undefined);
  });
  await expect.poll(() => violations.some(v => v === 'script-src-elem: inline')).toBe(true);
  await expect.poll(() => violations.some(v => v === 'style-src-elem: inline')).toBe(true);
  await expect.poll(() => violations.some(v => v.startsWith('connect-src:'))).toBe(true);
  expect(await page.locator('html').getAttribute('data-csp-injected')).toBeNull();
  expect(
    await page.evaluate(() => getComputedStyle(document.body).getPropertyValue('--csp-injected'))
  ).toBe('');
});

test('development Scalar renders the OpenAPI document under its scoped CSP', async ({ page }) => {
  const violations = await watchViolations(page);
  const response = await page.goto('/scalar/v1');
  expect(response?.status()).toBe(200);
  expect(response?.headers()['cache-control']).toBe('no-store');
  const policy = response?.headers()['content-security-policy'];
  expect(policy).toMatch(/script-src 'self' 'nonce-[A-Za-z0-9+/=]+'/);
  expect(policy).not.toContain('unsafe-eval');
  await expect(page.getByRole('heading', { name: 'ApexRacers API', exact: true })).toBeVisible();
  await settle(page);
  // Scalar's bundled Zod probes Function('') in try/catch, then uses its non-eval
  // fallback. Keep that probe blocked; allow no resource-loading violations.
  expect(violations).toEqual(['script-src: eval']);
  const spec = await page.request.get('/openapi/v1.json');
  expect(spec.ok()).toBe(true);
  const document = (await spec.json()) as { info: { title: string } };
  expect(document.info.title).toBe('ApexRacers API');
  const secondReference = await page.request.get('/scalar/v1');
  expect(secondReference.headers()['content-security-policy']).not.toBe(policy);
  const spa = await page.request.get('/');
  expect(spa.headers()['content-security-policy']).not.toMatch(/nonce-|unsafe-inline|unsafe-eval/);
});
