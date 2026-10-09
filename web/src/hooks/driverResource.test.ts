import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createDriverResource } from './driverResource';

describe('Driver resource display validity', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(0);
  });
  afterEach(() => vi.useRealTimers());

  it('cannot revive an observed expired display after the wall clock moves backwards', async () => {
    let wall = 0;
    const request = createDriverResource<string>({ now: () => wall, monotonic: () => 0 });
    request.load(() => Promise.resolve('permitted'));
    await Promise.resolve();
    wall = 30_000;
    expect(request.getSnapshot().status).toBe('loading');
    wall = 0;
    expect(request.getSnapshot().status).toBe('loading');
    request.cancel();
  });

  it('expires displayed data from check start while a replacement check is delayed', async () => {
    let resolve!: (value: string) => void;
    const request = createDriverResource<string>({ now: () => Date.now() });
    const fetcher = vi
      .fn<(signal: AbortSignal) => Promise<string>>()
      .mockImplementationOnce(
        () =>
          new Promise<string>(r => {
            resolve = r;
          })
      )
      .mockImplementation(() => new Promise<string>(() => {}));
    request.load(fetcher);
    await vi.advanceTimersByTimeAsync(14_000);
    resolve('permitted synthetic output');
    await Promise.resolve();
    expect(request.getSnapshot()).toEqual({ status: 'ok', data: 'permitted synthetic output' });
    await vi.advanceTimersByTimeAsync(15_999);
    expect(request.getSnapshot().status).toBe('ok');
    await vi.advanceTimersByTimeAsync(1);
    expect(request.getSnapshot()).toEqual({ status: 'loading' });
    request.cancel();
  });

  it('clears a denied check instead of returning a caller-provided cached fallback', async () => {
    const request = createDriverResource<string>({ now: () => Date.now() });
    const fetcher = vi
      .fn<(signal: AbortSignal) => Promise<string>>()
      .mockResolvedValueOnce('currently permitted')
      .mockRejectedValue(new Error('unavailable'));
    request.load(fetcher, { onError: { fallback: 'old private data' } });
    await Promise.resolve();
    expect(request.getSnapshot().status).toBe('ok');
    await vi.advanceTimersByTimeAsync(15_000);
    expect(request.getSnapshot()).toEqual({ status: 'error', message: 'unavailable' });
    request.cancel();
  });

  it('clears on suspension and requires a new check before late responses can display', async () => {
    let old!: (value: string) => void;
    let fresh!: (value: string) => void;
    const request = createDriverResource<string>({ now: () => Date.now() });
    const fetcher = vi
      .fn<(signal: AbortSignal) => Promise<string>>()
      .mockImplementationOnce(
        () =>
          new Promise<string>(r => {
            old = r;
          })
      )
      .mockImplementationOnce(
        () =>
          new Promise<string>(r => {
            fresh = r;
          })
      );
    request.load(fetcher);
    request.pause();
    old('obsolete output');
    await Promise.resolve();
    expect(request.getSnapshot()).toEqual({ status: 'loading' });
    request.revalidate();
    expect(request.getSnapshot()).toEqual({ status: 'loading' });
    fresh('freshly permitted');
    await Promise.resolve();
    expect(request.getSnapshot()).toEqual({ status: 'ok', data: 'freshly permitted' });
    request.cancel();
  });
});
