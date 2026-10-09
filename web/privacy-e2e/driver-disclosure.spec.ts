import { expect, test, type Page } from '@playwright/test';

const recipient = 'aaaaaaaa-3750-4000-8000-000000000001';
const target = 'aaaaaaaa-3750-4000-8000-000000000002';

async function signIn(page: Page, actor = recipient, origin = '') {
  const response = await page.request.get(`/control/session/${actor}`);
  expect(response.ok()).toBeTruthy();
  const { token } = (await response.json()) as { token: string };
  await page.goto(`${origin}/`);
  await page.evaluate(async token => {
    await new Promise<void>((resolve, reject) => {
      const request = indexedDB.open('apexracers', 1);
      request.onupgradeneeded = () => request.result.createObjectStore('session');
      request.onerror = () => reject(request.error ?? new Error('Session database failed'));
      request.onsuccess = () => {
        const db = request.result;
        const transaction = db.transaction('session', 'readwrite');
        transaction.objectStore('session').put(token, 'ar_token');
        transaction.oncomplete = () => {
          db.close();
          resolve();
        };
        transaction.onerror = () => reject(transaction.error ?? new Error('Session write failed'));
      };
    });
  }, token);
  await page.goto(`${origin}${origin ? '/profile' : '/dashboard'}`);
  if (origin) await expect(page.getByText('Class A · 3.50 SR', { exact: true })).toBeVisible();
  else
    await expect(
      page.getByText(
        actor === target ? 'Synthetic Reference Driver' : 'Synthetic Reference Owner',
        { exact: true }
      )
    ).toBeVisible();
  return token;
}

test.beforeEach(async ({ request }) => {
  expect((await request.post('/control/browser/renew')).ok()).toBeTruthy();
});

test('two actual pages retain useful personal and consented shared data and clear withdrawal', async ({
  page,
  context,
}) => {
  const token = await signIn(page);
  const comparison = await context.newPage();
  await comparison.goto('/compare');
  const shared = comparison.getByRole('region', { name: 'Shared Drivers' });
  await expect(shared.getByText('Synthetic Reference Driver', { exact: true })).toBeVisible();
  await shared.getByRole('button', { name: 'Compare shared Driver' }).click();
  await expect(shared.getByText(/lap gap 0.260s/)).toBeVisible();
  await shared.getByRole('button', { name: 'Follow shared Driver' }).click();
  await expect(shared.getByText('Private follows: Synthetic Reference Driver')).toBeVisible();
  const response = await page.request.get('/api/drivers/scoped/personal', {
    headers: { Authorization: `Bearer ${token}` },
  });
  expect(response.status()).toBe(200);
  expect(response.headers()['cache-control']).toContain('no-store');
  let withdrawalId = '';
  await page.route('**/api/drivers/privacy/withdrawal', async route => {
    const body = route.request().postDataJSON() as { operationId: string };
    withdrawalId = body.operationId;
    expect(
      (await page.request.post(`/control/hold/${withdrawalId}/before-journal`)).ok()
    ).toBeTruthy();
    await route.continue();
  });
  await page.getByRole('button', { name: 'Withdraw personal consent' }).click();
  await expect(page.getByText('Synthetic Reference Owner', { exact: true })).toHaveCount(0);
  await expect(shared.getByText('Synthetic Reference Driver', { exact: true })).toHaveCount(0);
  await expect.poll(() => withdrawalId).not.toBe('');
  expect(
    (await page.request.get(`/control/wait/${withdrawalId}/before-journal`)).ok()
  ).toBeTruthy();
  const beforeRecorded = await page.request.get('/api/drivers/scoped/personal', {
    headers: { Authorization: `Bearer ${token}` },
  });
  expect(beforeRecorded.status()).toBe(200);
  await expect(page.getByText('Synthetic Reference Owner', { exact: true })).toHaveCount(0);
  expect(
    (await page.request.post(`/control/release/${withdrawalId}/before-journal`)).ok()
  ).toBeTruthy();
  await expect(
    page.getByRole('region', { name: 'Personal Driver data' }).getByRole('status')
  ).toContainText('Withdrawal recorded');
  await expect(
    page.getByRole('region', { name: 'Personal Driver data' }).getByRole('status')
  ).toContainText('Live erasure and backup expiry have not been verified');
  await expect(
    page.getByRole('region', { name: 'Personal Driver data' }).getByRole('status')
  ).toContainText('Already delivered or exported bytes cannot be recalled');
  const denied = await page.request.get('/api/drivers/scoped/personal', {
    headers: { Authorization: `Bearer ${token}` },
  });
  expect(denied.status()).toBe(503);
  expect(denied.headers()['cache-control']).toContain('no-store');
  await page.reload();
  await expect(page.getByRole('heading', { name: /Welcome back/ })).toBeVisible();
  await expect(page.getByText('Synthetic Reference Owner', { exact: true })).toHaveCount(0);
  await comparison.reload();
  await expect(comparison.getByRole('heading', { name: 'Driver Comparison' })).toBeVisible();
  await expect(comparison.getByText('Synthetic Reference Driver', { exact: true })).toHaveCount(0);
});

test('delayed real responses expire from check start and obsolete success cannot revive display', async ({
  page,
  request,
}) => {
  await signIn(page);
  await page.clock.install();
  await page.clock.pauseAt(new Date());
  let sequence = 0;
  await page.route('**/api/drivers/scoped/personal', route =>
    route.continue({
      headers: { ...route.request().headers(), 'X-Rehearsal-Writer': `delay-${sequence++}` },
    })
  );
  for (const id of ['delay-0', 'delay-1', 'delay-2'])
    expect((await request.post(`/control/hold/${id}/before-admission`)).ok()).toBeTruthy();
  await page.reload();
  expect((await request.get('/control/wait/delay-0/before-admission')).ok()).toBeTruthy();
  await page.clock.runFor(14_000);
  expect((await request.post('/control/release/delay-0/before-admission')).ok()).toBeTruthy();
  await expect(page.getByText('Synthetic Reference Owner', { exact: true })).toBeVisible();
  await page.clock.runFor(1_000);
  expect((await request.get('/control/wait/delay-1/before-admission')).ok()).toBeTruthy();
  await page.clock.runFor(14_999);
  await expect(page.getByText('Synthetic Reference Owner', { exact: true })).toBeVisible();
  await page.clock.runFor(1);
  await expect(page.getByText('Synthetic Reference Owner', { exact: true })).toHaveCount(0);
  expect((await request.post('/control/release/delay-1/before-admission')).ok()).toBeTruthy();
  expect((await request.get('/control/wait/delay-2/before-admission')).ok()).toBeTruthy();
  await expect(page.getByText('Synthetic Reference Owner', { exact: true })).toHaveCount(0);
  expect((await request.post('/control/release/delay-2/before-admission')).ok()).toBeTruthy();
  await expect(page.getByText('Synthetic Reference Owner', { exact: true })).toBeVisible();
});

test('offline, focus and resumed pages clear first and require a fresh protected response', async ({
  page,
  context,
  request,
}) => {
  await signIn(page);
  let current = 'resume-offline';
  await page.route('**/api/drivers/scoped/personal', route =>
    route.continue({ headers: { ...route.request().headers(), 'X-Rehearsal-Writer': current } })
  );
  expect((await request.post(`/control/hold/${current}/before-admission`)).ok()).toBeTruthy();
  await context.setOffline(true);
  await expect(page.getByText('Synthetic Reference Owner', { exact: true })).toHaveCount(0);
  await context.setOffline(false);
  expect((await request.get(`/control/wait/${current}/before-admission`)).ok()).toBeTruthy();
  await expect(page.getByText('Synthetic Reference Owner', { exact: true })).toHaveCount(0);
  expect((await request.post(`/control/release/${current}/before-admission`)).ok()).toBeTruthy();
  await expect(page.getByText('Synthetic Reference Owner', { exact: true })).toBeVisible();
  for (const event of ['focus', 'pageshow']) {
    current = `resume-${event}`;
    expect((await request.post(`/control/hold/${current}/before-admission`)).ok()).toBeTruthy();
    await page.evaluate(event => window.dispatchEvent(new Event(event)), event);
    expect((await request.get(`/control/wait/${current}/before-admission`)).ok()).toBeTruthy();
    await expect(page.getByText('Synthetic Reference Owner', { exact: true })).toHaveCount(0);
    expect((await request.post(`/control/release/${current}/before-admission`)).ok()).toBeTruthy();
    await expect(page.getByText('Synthetic Reference Owner', { exact: true })).toBeVisible();
  }
  await page.evaluate(() => window.dispatchEvent(new Event('pagehide')));
  await expect(page.getByText('Synthetic Reference Owner', { exact: true })).toHaveCount(0);
  expect((await request.post('/control/fault/reference-source-unavailable')).ok()).toBeTruthy();
  const denied = page.waitForResponse(
    response => response.url().endsWith('/api/drivers/scoped/personal') && response.status() === 503
  );
  await page.evaluate(() => window.dispatchEvent(new Event('pageshow')));
  await expect(page.getByText('Synthetic Reference Owner', { exact: true })).toHaveCount(0);
  const response = await denied;
  expect(response.headers()['cache-control']).toContain('no-store');
  expect((await request.post('/control/clear/reference-source-unavailable')).ok()).toBeTruthy();
  await page.reload();
  await expect(page.getByText('Synthetic Reference Owner', { exact: true })).toBeVisible();
});

test('reload retains an unconfirmed withdrawal veto and retries the same operation', async ({
  page,
}) => {
  await signIn(page);
  let operation = '';
  await page.route('**/api/drivers/privacy/withdrawal', async route => {
    operation = (route.request().postDataJSON() as { operationId: string }).operationId;
    await route.abort('failed');
  });
  await page.getByRole('button', { name: 'Withdraw personal consent' }).click();
  await expect(page.getByText(/Withdrawal has not been confirmed/)).toBeVisible();
  await expect(page.getByText('Synthetic Reference Owner', { exact: true })).toHaveCount(0);
  await page.unroute('**/api/drivers/privacy/withdrawal');
  await page.reload();
  await expect(page.getByRole('button', { name: 'Retry withdrawal' })).toBeVisible();
  await expect(page.getByText('Synthetic Reference Owner', { exact: true })).toHaveCount(0);
  const persisted = await page.evaluate(
    owner => localStorage.getItem(`ar_driver_withdrawal_${owner}`),
    recipient
  );
  expect(JSON.parse(persisted!) as unknown).toEqual({ operationId: operation, scope: 'personal' });
  const sent = page.waitForRequest(request =>
    request.url().endsWith('/api/drivers/privacy/withdrawal')
  );
  await page.getByRole('button', { name: 'Retry withdrawal' }).click();
  expect((await sent).postDataJSON() as unknown).toEqual({
    operationId: operation,
    scope: 'personal',
  });
  await expect(page.getByText(/Withdrawal recorded/)).toBeVisible();
  await expect(page.getByText('Synthetic Reference Owner', { exact: true })).toHaveCount(0);
});

test('ordinary startup preserves useful credential-free Demo without granting Real ownership', async ({
  page,
}) => {
  const token = await signIn(page, recipient, 'http://127.0.0.1:8186');
  await expect(page.getByRole('status').filter({ hasText: 'Demo data' })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Career by Category' })).toBeVisible();
  await expect(page.getByText('Clean Driver', { exact: true })).toBeVisible();
  const headers = {
    Authorization: `Bearer ${token}`,
    'X-ApexRacers-Driver-Evidence-Namespace': 'real',
  };
  const profile = await page.request.get('http://127.0.0.1:8186/api/users/me/profile-stats', {
    headers,
  });
  expect(profile.status()).toBe(200);
  expect(profile.headers()['cache-control']).toContain('no-store');
  expect(profile.headers()['x-apexracers-driver-evidence-namespace']).toBe('demo');
  expect(((await profile.json()) as { driverName: string }).driverName).toBe('Demo Driver');
  const owner = await page.request.get('http://127.0.0.1:8186/api/drivers/scoped/personal', {
    headers,
  });
  expect(owner.status()).toBe(503);
  await page.goto('http://127.0.0.1:8186/progression');
  await expect(page.getByRole('heading', { name: 'iRating & Safety Rating' })).toBeVisible();
  await expect(page.getByText(/iRating history \(/).first()).toBeVisible();
  await expect(page.getByRole('status').filter({ hasText: 'Demo data' })).toBeVisible();
});

test('sharing withdrawal clears the recipient while preserving the withdrawing Driver personal data', async ({
  page,
  browser,
}) => {
  await signIn(page);
  await page.clock.install();
  await page.clock.pauseAt(new Date());
  await page.goto('/compare');
  await expect(page.getByText('Synthetic Reference Driver', { exact: true })).toBeVisible();
  const ownerContext = await browser.newContext({ baseURL: 'http://127.0.0.1:8185' });
  try {
    const owner = await ownerContext.newPage();
    await signIn(owner, target);
    await owner.getByRole('button', { name: 'Withdraw sharing consent' }).click();
    await expect(owner.getByText(/Withdrawal recorded/)).toBeVisible();
    await expect(owner.getByText('Synthetic Reference Driver', { exact: true })).toBeVisible();
    await page.clock.runFor(15_000);
    await expect(page.getByText('Synthetic Reference Driver', { exact: true })).toHaveCount(0);
    await page.goto('/dashboard');
    await expect(page.getByText('Synthetic Reference Owner', { exact: true })).toBeVisible();
  } finally {
    await ownerContext.close();
  }
});
