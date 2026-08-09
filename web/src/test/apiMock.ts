import { vi } from 'vitest';

/**
 * Builds the module shape for `vi.mock('<path>/services/api', …)` with every real export kept
 * and only the `api` object's methods replaced by `vi.fn()`s.
 *
 * Why this exists rather than a hand-written factory per test file: pages branch on
 * `err instanceof IRacingNotLinkedError` / `instanceof ApiError`. A factory that returns its own
 * stand-in class makes that check compare against the *mock's* class, so the test passes because
 * the test supplied both halves — it proves nothing about the real contract. `PercentileCarPage`
 * shipped a dead 404 branch for exactly this reason: two dedicated tests, both green, neither
 * touching the error the client actually throws.
 *
 * Every method on `api` is stubbed, not just the ones a given test names, so adding a call to a
 * page does not require editing its test's mock factory.
 *
 * Usage — the dynamic import inside the factory is required, because `vi.mock` is hoisted above
 * the file's imports and so cannot close over one:
 *
 * ```ts
 * vi.mock('../../services/api', async importOriginal => {
 *   const { mockApiModule } = await import('../../test/apiMock');
 *   return mockApiModule(importOriginal);
 * });
 * ```
 *
 * Then reject with the real class: `vi.mocked(api.getX).mockRejectedValue(new ApiError(404, 'Not Found'))`.
 */
export async function mockApiModule(
  importOriginal: () => Promise<unknown>
): Promise<Record<string, unknown>> {
  const actual = (await importOriginal()) as Record<string, unknown> & {
    api: Record<string, unknown>;
  };

  const stubbedApi = Object.fromEntries(
    Object.keys(actual.api).map(name => [name, vi.fn()])
  ) as Record<string, unknown>;

  return { ...actual, api: stubbedApi };
}
