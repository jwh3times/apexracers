import { render, screen, waitFor, act } from '@testing-library/react';
import { vi, describe, it, expect, beforeEach } from 'vitest';
import { FeatureFlagProvider } from './FeatureFlagProvider';
import {
  FeatureFlagContext,
  useFeatureFlag,
  useFeatureFlags,
  useIracingSurface,
} from './FeatureFlagContext';
import { api } from '../services/api';
import type { FeatureFlag } from '../services/api';
import type { User } from './AuthContext';

let mockUser: User | null = null;

vi.mock('./AuthContext', () => ({
  useAuth: () => ({ user: mockUser }),
}));

vi.mock('../services/api', async importOriginal => {
  const { mockApiModule } = await import('../test/apiMock');
  return mockApiModule(importOriginal);
});

function Consumer({ flagKey }: { flagKey: string }) {
  const enabled = useFeatureFlag(flagKey);
  const { ready } = useFeatureFlags();
  return (
    <>
      <span data-testid="result">{enabled ? 'on' : 'off'}</span>
      <span data-testid="status">{ready ? 'ready' : 'loading'}</span>
    </>
  );
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise;
    reject = rejectPromise;
  });
  return { promise, resolve, reject };
}

function renderWithProvider(flagKey = 'test.flag') {
  return render(
    <FeatureFlagProvider>
      <Consumer flagKey={flagKey} />
    </FeatureFlagProvider>
  );
}

describe('FeatureFlagContext', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    mockUser = null;
  });

  it('is not ready during the initial guest fetch, then settles with the fetched flags', async () => {
    const request = deferred<FeatureFlag[]>();
    vi.mocked(api.getFeatureFlags).mockReturnValue(request.promise);

    renderWithProvider('test.flag');

    expect(screen.getByTestId('status')).toHaveTextContent('loading');
    expect(screen.getByTestId('result')).toHaveTextContent('off');

    await act(async () => {
      request.resolve([
        {
          id: 1,
          key: 'test.flag',
          name: 'Test',
          description: null,
          isEnabled: true,
          minimumRole: 'Standard',
          createdAt: '',
          updatedAt: '',
        },
      ]);
      await request.promise;
    });

    expect(screen.getByTestId('status')).toHaveTextContent('ready');
    expect(screen.getByTestId('result')).toHaveTextContent('on');
  });

  it('fetches the public flag set for a guest (no user) and resolves it', async () => {
    vi.mocked(api.getFeatureFlags).mockResolvedValue([
      {
        id: 1,
        key: 'test.flag',
        name: 'Test',
        description: null,
        isEnabled: true,
        minimumRole: 'Standard',
        createdAt: '',
        updatedAt: '',
      },
    ]);
    await act(async () => {
      renderWithProvider('test.flag');
    });
    await waitFor(() => expect(screen.getByTestId('result')).toHaveTextContent('on'));
    expect(vi.mocked(api.getFeatureFlags)).toHaveBeenCalledTimes(1);
  });

  it('returns false for a guest when the key is not in the public flag set', async () => {
    vi.mocked(api.getFeatureFlags).mockResolvedValue([]);
    await act(async () => {
      renderWithProvider('test.flag');
    });
    await waitFor(() => expect(vi.mocked(api.getFeatureFlags)).toHaveBeenCalled());
    expect(screen.getByTestId('result')).toHaveTextContent('off');
  });

  it('fetches flags when a user is present and marks matching key as enabled', async () => {
    mockUser = {
      token: 't',
      userId: 'u1',
      displayName: 'Jerry',
      email: 'j@j.com',
      iRacingCustomerId: null,
      role: 'Beta',
    };
    vi.mocked(api.getFeatureFlags).mockResolvedValue([
      {
        id: 1,
        key: 'test.flag',
        name: 'Test',
        description: null,
        isEnabled: true,
        minimumRole: 'Beta',
        createdAt: '',
        updatedAt: '',
      },
    ]);
    await act(async () => {
      renderWithProvider('test.flag');
    });
    await waitFor(() => expect(screen.getByTestId('result')).toHaveTextContent('on'));
  });

  it('returns false for a key not in the fetched flags', async () => {
    mockUser = {
      token: 't',
      userId: 'u1',
      displayName: 'Jerry',
      email: 'j@j.com',
      iRacingCustomerId: null,
      role: 'Beta',
    };
    vi.mocked(api.getFeatureFlags).mockResolvedValue([
      {
        id: 1,
        key: 'other.flag',
        name: 'Other',
        description: null,
        isEnabled: true,
        minimumRole: 'Beta',
        createdAt: '',
        updatedAt: '',
      },
    ]);
    await act(async () => {
      renderWithProvider('test.flag');
    });
    await waitFor(() => expect(vi.mocked(api.getFeatureFlags)).toHaveBeenCalled());
    expect(screen.getByTestId('result')).toHaveTextContent('off');
  });

  it('switches to the user set when a guest logs in', async () => {
    vi.mocked(api.getFeatureFlags).mockResolvedValueOnce([]); // guest: public set lacks test.flag
    const { rerender } = renderWithProvider('test.flag');
    await waitFor(() => expect(screen.getByTestId('status')).toHaveTextContent('ready'));
    expect(vi.mocked(api.getFeatureFlags)).toHaveBeenCalledTimes(1);

    const userRequest = deferred<FeatureFlag[]>();
    mockUser = {
      token: 't',
      userId: 'u1',
      displayName: 'Jerry',
      email: 'j@j.com',
      iRacingCustomerId: null,
      role: 'Beta',
    };
    vi.mocked(api.getFeatureFlags).mockReturnValueOnce(userRequest.promise);
    rerender(
      <FeatureFlagProvider>
        <Consumer flagKey="test.flag" />
      </FeatureFlagProvider>
    );
    expect(screen.getByTestId('status')).toHaveTextContent('loading');
    expect(screen.getByTestId('result')).toHaveTextContent('off');

    await act(async () => {
      userRequest.resolve([
        {
          id: 1,
          key: 'test.flag',
          name: 'Test',
          description: null,
          isEnabled: true,
          minimumRole: 'Beta',
          createdAt: '',
          updatedAt: '',
        },
      ]);
      await userRequest.promise;
    });
    await waitFor(() => expect(screen.getByTestId('result')).toHaveTextContent('on'));
    expect(vi.mocked(api.getFeatureFlags)).toHaveBeenCalledTimes(2);
  });

  it('returns to the guest set on logout', async () => {
    mockUser = {
      token: 't',
      userId: 'u1',
      displayName: 'Jerry',
      email: 'j@j.com',
      iRacingCustomerId: null,
      role: 'Beta',
    };
    vi.mocked(api.getFeatureFlags).mockResolvedValueOnce([
      {
        id: 1,
        key: 'test.flag',
        name: 'Test',
        description: null,
        isEnabled: true,
        minimumRole: 'Beta',
        createdAt: '',
        updatedAt: '',
      },
    ]);
    const { rerender } = renderWithProvider('test.flag');
    await waitFor(() => expect(screen.getByTestId('result')).toHaveTextContent('on'));

    const guestRequest = deferred<FeatureFlag[]>();
    mockUser = null;
    vi.mocked(api.getFeatureFlags).mockReturnValueOnce(guestRequest.promise);
    rerender(
      <FeatureFlagProvider>
        <Consumer flagKey="test.flag" />
      </FeatureFlagProvider>
    );
    expect(screen.getByTestId('status')).toHaveTextContent('loading');
    expect(screen.getByTestId('result')).toHaveTextContent('off');

    await act(async () => {
      guestRequest.resolve([]); // guest public set lacks test.flag
      await guestRequest.promise;
    });
    expect(screen.getByTestId('status')).toHaveTextContent('ready');
    expect(screen.getByTestId('result')).toHaveTextContent('off');
    expect(vi.mocked(api.getFeatureFlags)).toHaveBeenCalledTimes(2); // confirms the guest refetch happened
  });

  it('becomes unready while refetching after a role change', async () => {
    mockUser = {
      token: 't',
      userId: 'u1',
      displayName: 'Jerry',
      email: 'j@j.com',
      iRacingCustomerId: null,
      role: 'Standard',
    };
    vi.mocked(api.getFeatureFlags).mockResolvedValueOnce([]);
    const { rerender } = renderWithProvider('test.flag');
    await waitFor(() => expect(screen.getByTestId('status')).toHaveTextContent('ready'));

    const roleRequest = deferred<FeatureFlag[]>();
    mockUser = { ...mockUser, role: 'Beta' };
    vi.mocked(api.getFeatureFlags).mockReturnValueOnce(roleRequest.promise);
    rerender(
      <FeatureFlagProvider>
        <Consumer flagKey="test.flag" />
      </FeatureFlagProvider>
    );

    expect(screen.getByTestId('status')).toHaveTextContent('loading');
    await act(async () => {
      roleRequest.resolve([]);
      await roleRequest.promise;
    });
    expect(screen.getByTestId('status')).toHaveTextContent('ready');
  });

  it('handles API errors gracefully and leaves flags empty', async () => {
    mockUser = {
      token: 't',
      userId: 'u1',
      displayName: 'Jerry',
      email: 'j@j.com',
      iRacingCustomerId: null,
      role: 'Standard',
    };
    vi.mocked(api.getFeatureFlags).mockRejectedValue(new Error('Network error'));
    await act(async () => {
      renderWithProvider('test.flag');
    });
    await waitFor(() => expect(screen.getByTestId('status')).toHaveTextContent('ready'));
    expect(screen.getByTestId('result')).toHaveTextContent('off');
  });

  it('checks both iRacing flags even when the live flag is enabled', () => {
    const isEnabled = vi.fn((key: string) => key === 'iracing-live');

    function IracingConsumer() {
      const surface = useIracingSurface();
      return <span>{surface.enabled ? 'surface on' : 'surface off'}</span>;
    }

    render(
      <FeatureFlagContext.Provider value={{ isEnabled, ready: true }}>
        <IracingConsumer />
      </FeatureFlagContext.Provider>
    );

    expect(screen.getByText('surface on')).toBeInTheDocument();
    expect(isEnabled).toHaveBeenNthCalledWith(1, 'iracing-live');
    expect(isEnabled).toHaveBeenNthCalledWith(2, 'iracing-demo');
  });
});
