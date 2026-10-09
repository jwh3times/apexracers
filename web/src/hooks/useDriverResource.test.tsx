import { act, renderHook, waitFor } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { useDriverResource } from './useDriverResource';

describe('useDriverResource', () => {
  it('never renders the previous owner or selection while the new request is pending', async () => {
    let obsolete!: (data: string) => void;
    const fetcher = vi
      .fn<(signal: AbortSignal, owner: string) => Promise<string>>()
      .mockResolvedValueOnce('owner one data')
      .mockImplementationOnce(
        () =>
          new Promise<string>(r => {
            obsolete = r;
          })
      )
      .mockImplementation(() => new Promise<string>(() => {}));
    const { result, rerender, unmount } = renderHook(
      ({ owner }) => useDriverResource(signal => fetcher(signal, owner), [owner]),
      { initialProps: { owner: 'one' } }
    );
    await waitFor(() => expect(result.current).toEqual({ status: 'ok', data: 'owner one data' }));
    rerender({ owner: 'two' });
    expect(result.current).toEqual({ status: 'loading' });
    rerender({ owner: 'three' });
    await act(() => {
      obsolete('owner two data');
      return Promise.resolve();
    });
    expect(result.current).toEqual({ status: 'loading' });
    unmount();
  });

  it('removes displayed data offline and rechecks before showing it after reconnect', async () => {
    let fresh!: (data: string) => void;
    const fetcher = vi
      .fn<(signal: AbortSignal) => Promise<string>>()
      .mockResolvedValueOnce('permitted before disconnect')
      .mockImplementationOnce(
        () =>
          new Promise<string>(r => {
            fresh = r;
          })
      );
    const { result, unmount } = renderHook(() => useDriverResource(fetcher, []));
    await waitFor(() => expect(result.current.status).toBe('ok'));
    act(() => {
      window.dispatchEvent(new Event('offline'));
    });
    expect(result.current).toEqual({ status: 'loading' });
    act(() => {
      window.dispatchEvent(new Event('online'));
    });
    expect(result.current).toEqual({ status: 'loading' });
    await act(() => {
      fresh('fresh after reconnect');
      return Promise.resolve();
    });
    expect(result.current).toEqual({ status: 'ok', data: 'fresh after reconnect' });
    unmount();
  });

  it('rechecks on resume when the browser missed the online event while suspended', async () => {
    const fetcher = vi
      .fn<(signal: AbortSignal) => Promise<string>>()
      .mockResolvedValueOnce('before suspension')
      .mockResolvedValueOnce('after a fresh check');
    const { result, unmount } = renderHook(() => useDriverResource(fetcher, []));
    await waitFor(() => expect(result.current.status).toBe('ok'));
    act(() => {
      window.dispatchEvent(new Event('offline'));
    });
    expect(result.current.status).toBe('loading');
    act(() => {
      window.dispatchEvent(new Event('pageshow'));
    });
    await waitFor(() =>
      expect(result.current).toEqual({ status: 'ok', data: 'after a fresh check' })
    );
    unmount();
  });
});
