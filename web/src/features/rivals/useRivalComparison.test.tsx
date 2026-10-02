import { act, renderHook, waitFor } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { type ComparisonSide, type DriverComparison } from '../../services/api';
import { useRivalComparison } from './useRivalComparison';

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason: unknown) => void;
  const promise = new Promise<T>((res, rej) => {
    resolve = res;
    reject = rej;
  });
  return { promise, resolve, reject };
}

const side = (customerId: number): ComparisonSide => ({
  customerId,
  driverName: `Driver ${customerId}`,
  country: null,
  countryCode: null,
  memberSince: null,
  licenses: [],
  career: [],
  iRatingHistory: [],
});
const comparison: DriverComparison = {
  you: side(100),
  rival: side(200),
  shared: { totalShared: 0, youAhead: 0, rivalAhead: 0, races: [], trackPace: [] },
};

describe('useRivalComparison', () => {
  it.each(['success', 'failure'])('aborts on unmount and discards late %s', async outcome => {
    const pending = deferred<DriverComparison>();
    let signal: AbortSignal | undefined;
    const transport = {
      compareRival: vi.fn<(id: number, signal?: AbortSignal) => Promise<DriverComparison>>(
        (_id, current) => {
          signal = current;
          return pending.promise;
        }
      ),
      removeRival: vi.fn<(id: number) => Promise<void>>().mockResolvedValue(undefined),
    };
    const { result, unmount } = renderHook(() => useRivalComparison(transport));
    act(() => result.current.compare(200));
    expect(result.current.selected).toBe(200);
    expect(result.current.comparison.status).toBe('comparing');
    unmount();
    expect(signal?.aborted).toBe(true);
    await act(() => {
      if (outcome === 'success') pending.resolve(comparison);
      else pending.reject(new Error('late failure'));
      return Promise.resolve();
    });
    expect(result.current.comparison.status).toBe('comparing');
  });

  it('keeps the selected result when removal fails', async () => {
    const transport = {
      compareRival: vi
        .fn<(id: number, signal?: AbortSignal) => Promise<DriverComparison>>()
        .mockResolvedValue(comparison),
      removeRival: vi
        .fn<(id: number) => Promise<void>>()
        .mockRejectedValue(new Error('remove failed')),
    };
    const { result } = renderHook(() => useRivalComparison(transport));
    act(() => result.current.compare(200));
    await waitFor(() =>
      expect(result.current.comparison).toEqual({ status: 'ok', data: comparison })
    );
    await act(async () => {
      await expect(result.current.remove(200)).rejects.toThrow('remove failed');
    });
    expect(result.current.selected).toBe(200);
    expect(result.current.comparison).toEqual({ status: 'ok', data: comparison });
  });

  it('uses the comparison fallback for a non-Error failure', async () => {
    const transport = {
      compareRival: vi
        .fn<(id: number, signal?: AbortSignal) => Promise<DriverComparison>>()
        .mockRejectedValue('offline'),
      removeRival: vi.fn<(id: number) => Promise<void>>().mockResolvedValue(undefined),
    };
    const { result } = renderHook(() => useRivalComparison(transport));
    act(() => result.current.compare(200));
    await waitFor(() =>
      expect(result.current.comparison).toEqual({
        status: 'error',
        message: 'Failed to load comparison.',
      })
    );
    expect(result.current.selected).toBe(200);
  });
});
