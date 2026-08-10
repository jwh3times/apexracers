import { act, renderHook, waitFor } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { IRacingNotLinkedError } from '../services/api';
import { useResource } from './useResource';

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((res, rej) => {
    resolve = res;
    reject = rej;
  });
  return { promise, resolve, reject };
}

describe('useResource', () => {
  it('loads data through the injected fetcher', async () => {
    const fetcher = vi.fn().mockResolvedValue({ id: 42 });
    const { result } = renderHook(() => useResource(fetcher, []));

    await waitFor(() => expect(result.current).toEqual({ status: 'ok', data: { id: 42 } }));
    expect(fetcher).toHaveBeenCalledWith(expect.any(AbortSignal));
  });

  it('classifies the typed not-linked response once', async () => {
    const fetcher = vi.fn().mockRejectedValue(new IRacingNotLinkedError('not linked'));
    const { result } = renderHook(() => useResource(fetcher, []));

    await waitFor(() => expect(result.current).toEqual({ status: 'not-linked' }));
  });

  it('coerces non-Error rejections to the configured fallback', async () => {
    const fetcher = vi.fn().mockRejectedValue('offline');
    const { result } = renderHook(() =>
      useResource(fetcher, [], { fallbackMessage: 'Could not load the resource.' })
    );

    await waitFor(() =>
      expect(result.current).toEqual({
        status: 'error',
        message: 'Could not load the resource.',
      })
    );
  });

  it('classifies a synchronous fetcher failure', async () => {
    const { result } = renderHook(() =>
      useResource(() => {
        throw new Error('bad request');
      }, [])
    );

    await waitFor(() =>
      expect(result.current).toEqual({ status: 'error', message: 'bad request' })
    );
  });

  it('does not call the fetcher while disabled', async () => {
    const fetcher = vi.fn().mockResolvedValue('unused');
    const { result } = renderHook(() => useResource(fetcher, [false], { enabled: false }));

    await act(async () => Promise.resolve());
    expect(fetcher).not.toHaveBeenCalled();
    expect(result.current).toEqual({ status: 'loading' });
  });

  it('aborts and ignores an older request when dependencies change', async () => {
    const first = deferred<string>();
    const second = deferred<string>();
    const signals: AbortSignal[] = [];
    const fetcher = vi.fn((signal: AbortSignal, key: number) => {
      signals.push(signal);
      return key === 1 ? first.promise : second.promise;
    });
    const { result, rerender } = renderHook(
      ({ key }) => useResource(signal => fetcher(signal, key), [key]),
      { initialProps: { key: 1 } }
    );

    rerender({ key: 2 });
    expect(signals[0].aborted).toBe(true);

    await act(async () => second.resolve('new'));
    expect(result.current).toEqual({ status: 'ok', data: 'new' });

    await act(async () => first.resolve('stale'));
    expect(result.current).toEqual({ status: 'ok', data: 'new' });
  });

  it('aborts the request on unmount', () => {
    const request = deferred<string>();
    let signal: AbortSignal | undefined;
    const { unmount } = renderHook(() =>
      useResource(currentSignal => {
        signal = currentSignal;
        return request.promise;
      }, [])
    );

    unmount();
    expect(signal?.aborted).toBe(true);
  });
});
