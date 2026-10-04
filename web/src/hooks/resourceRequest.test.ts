import { describe, expect, it, vi } from 'vitest';
import { createResourceRequest, type Resource } from './resourceRequest';

describe('createResourceRequest', () => {
  it('does not replace a synchronous failure with queued loading', async () => {
    const outcomes: Resource<string>[] = [];
    const request = createResourceRequest<string>();
    request.load(
      () => {
        throw new Error('bad request');
      },
      value => outcomes.push(value)
    );
    await Promise.resolve();
    expect(outcomes).toEqual([{ status: 'error', message: 'bad request' }]);
  });
  it('cancels queued loading and late settlement independently of transport cancellation', async () => {
    let resolve!: (data: string) => void;
    const pending = new Promise<string>(r => (resolve = r));
    const publish = vi.fn<(resource: Resource<string>) => void>();
    let signal!: AbortSignal;
    const request = createResourceRequest<string>();
    request.load(current => {
      signal = current;
      return pending;
    }, publish);
    request.cancel();
    resolve('obsolete');
    await pending;
    expect(signal.aborted).toBe(true);
    expect(publish).not.toHaveBeenCalled();
  });

  it('starts a new read after cancellation and reports a default error for an empty message', async () => {
    const outcomes: Resource<string>[] = [];
    const request = createResourceRequest<string>();
    request.cancel();
    request.load(
      () => Promise.reject(new Error('')),
      value => outcomes.push(value)
    );
    await Promise.resolve();
    expect(outcomes).toEqual([
      { status: 'loading' },
      { status: 'error', message: 'Something went wrong. Please try again.' },
    ]);
  });
});
