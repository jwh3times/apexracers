import { render, screen, waitFor, act } from '@testing-library/react';
import { vi, describe, it, expect, beforeEach } from 'vitest';
import { FeatureFlagProvider } from './FeatureFlagProvider';
import { useFeatureFlag } from './FeatureFlagContext';
import { api } from '../services/api';
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
  return <span data-testid="result">{enabled ? 'on' : 'off'}</span>;
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
    await waitFor(() => expect(screen.getByTestId('result')).toHaveTextContent('off'));
    expect(vi.mocked(api.getFeatureFlags)).toHaveBeenCalledTimes(1);

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
    await act(async () => {
      rerender(
        <FeatureFlagProvider>
          <Consumer flagKey="test.flag" />
        </FeatureFlagProvider>
      );
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

    mockUser = null;
    vi.mocked(api.getFeatureFlags).mockResolvedValueOnce([]); // guest public set lacks test.flag
    await act(async () => {
      rerender(
        <FeatureFlagProvider>
          <Consumer flagKey="test.flag" />
        </FeatureFlagProvider>
      );
    });
    await waitFor(() => expect(screen.getByTestId('result')).toHaveTextContent('off'));
    expect(vi.mocked(api.getFeatureFlags)).toHaveBeenCalledTimes(2); // confirms the guest refetch happened
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
    await waitFor(() => expect(vi.mocked(api.getFeatureFlags)).toHaveBeenCalled());
    expect(screen.getByTestId('result')).toHaveTextContent('off');
  });
});
