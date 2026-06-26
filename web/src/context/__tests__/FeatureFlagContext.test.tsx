import { render, screen, waitFor, act } from '@testing-library/react';
import { vi, describe, it, expect, beforeEach } from 'vitest';
import { FeatureFlagProvider } from '../FeatureFlagProvider';
import { useFeatureFlag } from '../FeatureFlagContext';
import { api } from '../../services/api';
import type { User } from '../AuthContext';

let mockUser: User | null = null;

vi.mock('../AuthContext', () => ({
  useAuth: () => ({ user: mockUser }),
}));

vi.mock('../../services/api', () => ({
  api: { getFeatureFlags: vi.fn() },
}));

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

  it('isEnabled returns false when there is no user', async () => {
    await act(async () => {
      renderWithProvider();
    });
    expect(screen.getByTestId('result')).toHaveTextContent('off');
    expect(vi.mocked(api.getFeatureFlags)).not.toHaveBeenCalled();
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

  it('clears flags and returns false after user logs out', async () => {
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
    const { rerender } = renderWithProvider('test.flag');
    await waitFor(() => expect(screen.getByTestId('result')).toHaveTextContent('on'));

    mockUser = null;
    await act(async () => {
      rerender(
        <FeatureFlagProvider>
          <Consumer flagKey="test.flag" />
        </FeatureFlagProvider>
      );
    });
    expect(screen.getByTestId('result')).toHaveTextContent('off');
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
