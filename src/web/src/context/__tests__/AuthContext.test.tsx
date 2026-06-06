import { render, screen, waitFor, act } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { vi, describe, it, expect, beforeEach } from 'vitest';
import { AuthProvider, useAuth } from '../AuthContext';
import type { AuthResult } from '../../services/api';

const mockDbGet = vi.fn();
const mockDbSet = vi.fn();
const mockDbRemove = vi.fn();
const mockSetToken = vi.fn();
const mockClearToken = vi.fn();
const mockSetRefreshToken = vi.fn();
const mockRefreshTokens = vi.fn();
const mockRevokeToken = vi.fn();
let capturedOnTokenRefreshed: ((token: string, refreshToken: string) => void) | null = null;
let capturedOnSessionExpired: (() => void) | null = null;

vi.mock('../../services/db', () => ({
  dbGet: (...args: unknown[]) => mockDbGet(...args),
  dbSet: (...args: unknown[]) => mockDbSet(...args),
  dbRemove: (...args: unknown[]) => mockDbRemove(...args),
}));

vi.mock('../../services/api', () => ({
  setToken: (...args: unknown[]) => mockSetToken(...args),
  clearToken: (...args: unknown[]) => mockClearToken(...args),
  setRefreshToken: (...args: unknown[]) => mockSetRefreshToken(...args),
  onTokenRefreshed: (cb: unknown) => {
    capturedOnTokenRefreshed = cb as (t: string, r: string) => void;
  },
  onSessionExpired: (cb: unknown) => {
    capturedOnSessionExpired = cb as () => void;
  },
  api: {
    refreshTokens: (...args: unknown[]) => mockRefreshTokens(...args),
    revokeToken: (...args: unknown[]) => mockRevokeToken(...args),
  },
}));

vi.mock('../ThemeContext', () => ({
  useTheme: () => ({ theme: 'auto', setTheme: vi.fn(), syncFromJwt: vi.fn() }),
}));

function makeJwt(claims: { sub: string; email: string; name: string }): string {
  return `header.${btoa(JSON.stringify(claims))}.signature`;
}

function makeExpiredJwt(claims: { sub: string; email: string; name: string }): string {
  const exp = Math.floor(Date.now() / 1000) - 3600;
  return `header.${btoa(JSON.stringify({ ...claims, exp }))}.signature`;
}

function Consumer() {
  const auth = useAuth();
  return (
    <div>
      <span data-testid="loading">{String(auth.loading)}</span>
      <span data-testid="user">{auth.user ? auth.user.displayName : 'null'}</span>
      <span data-testid="email">{auth.user?.email ?? 'null'}</span>
      <span data-testid="alerts">{String(auth.alertsEnabled)}</span>
      <button
        onClick={() =>
          auth.login({ token: 'tok', userId: 'u1', displayName: 'New' } as AuthResult, 'a@b.com')
        }
      >
        login
      </button>
      <button onClick={() => auth.logout()}>logout</button>
      <button
        onClick={() =>
          auth.updateSession({ token: 'tok2', userId: 'u1', displayName: 'Updated' } as AuthResult)
        }
      >
        update
      </button>
      <button onClick={() => auth.setAlertsEnabled(false)}>disableAlerts</button>
    </div>
  );
}

describe('AuthContext', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    capturedOnTokenRefreshed = null;
    capturedOnSessionExpired = null;
    mockDbGet.mockResolvedValue(undefined);
    mockDbSet.mockResolvedValue(undefined);
    mockDbRemove.mockResolvedValue(undefined);
    mockRefreshTokens.mockRejectedValue(new Error('no refresh'));
    mockRevokeToken.mockResolvedValue(undefined);
  });

  it('starts loading then settles to false', async () => {
    await act(async () => {
      render(
        <AuthProvider>
          <Consumer />
        </AuthProvider>
      );
    });
    expect(screen.getByTestId('loading')).toHaveTextContent('false');
  });

  it('user is null when no token is stored', async () => {
    await act(async () => {
      render(
        <AuthProvider>
          <Consumer />
        </AuthProvider>
      );
    });
    expect(screen.getByTestId('user')).toHaveTextContent('null');
  });

  it('decodes a valid JWT from db and sets user', async () => {
    const token = makeJwt({ sub: 'u1', email: 'a@b.com', name: 'Jerry' });
    mockDbGet.mockImplementation((key: string) =>
      key === 'ar_token' ? Promise.resolve(token) : Promise.resolve(undefined)
    );
    await act(async () => {
      render(
        <AuthProvider>
          <Consumer />
        </AuthProvider>
      );
    });
    expect(screen.getByTestId('user')).toHaveTextContent('Jerry');
    expect(screen.getByTestId('email')).toHaveTextContent('a@b.com');
    expect(mockSetToken).toHaveBeenCalledWith(token);
  });

  it('ignores a token with an invalid JWT payload', async () => {
    mockDbGet.mockImplementation((key: string) =>
      key === 'ar_token' ? Promise.resolve('invalid-token') : Promise.resolve(undefined)
    );
    await act(async () => {
      render(
        <AuthProvider>
          <Consumer />
        </AuthProvider>
      );
    });
    expect(screen.getByTestId('user')).toHaveTextContent('null');
  });

  it('sets alertsEnabled to false when db stores false', async () => {
    mockDbGet.mockImplementation((key: string) =>
      key === 'ar_alerts' ? Promise.resolve(false) : Promise.resolve(undefined)
    );
    await act(async () => {
      render(
        <AuthProvider>
          <Consumer />
        </AuthProvider>
      );
    });
    expect(screen.getByTestId('alerts')).toHaveTextContent('false');
  });

  it('keeps alertsEnabled true when db returns undefined', async () => {
    await act(async () => {
      render(
        <AuthProvider>
          <Consumer />
        </AuthProvider>
      );
    });
    expect(screen.getByTestId('alerts')).toHaveTextContent('true');
  });

  it('login sets user and persists token to db', async () => {
    const user = userEvent.setup();
    await act(async () => {
      render(
        <AuthProvider>
          <Consumer />
        </AuthProvider>
      );
    });
    await user.click(screen.getByText('login'));
    await waitFor(() => expect(screen.getByTestId('user')).toHaveTextContent('New'));
    expect(screen.getByTestId('email')).toHaveTextContent('a@b.com');
    expect(mockSetToken).toHaveBeenCalledWith('tok');
    expect(mockDbSet).toHaveBeenCalledWith('ar_token', 'tok');
  });

  it('logout clears user and removes token from db', async () => {
    const token = makeJwt({ sub: 'u1', email: 'a@b.com', name: 'Jerry' });
    mockDbGet.mockImplementation((key: string) =>
      key === 'ar_token' ? Promise.resolve(token) : Promise.resolve(undefined)
    );
    const user = userEvent.setup();
    await act(async () => {
      render(
        <AuthProvider>
          <Consumer />
        </AuthProvider>
      );
    });
    await user.click(screen.getByText('logout'));
    await waitFor(() => expect(screen.getByTestId('user')).toHaveTextContent('null'));
    expect(mockClearToken).toHaveBeenCalled();
    expect(mockDbRemove).toHaveBeenCalledWith('ar_token');
  });

  it('updateSession updates display name and saves new token', async () => {
    const token = makeJwt({ sub: 'u1', email: 'a@b.com', name: 'Jerry' });
    mockDbGet.mockImplementation((key: string) =>
      key === 'ar_token' ? Promise.resolve(token) : Promise.resolve(undefined)
    );
    const user = userEvent.setup();
    await act(async () => {
      render(
        <AuthProvider>
          <Consumer />
        </AuthProvider>
      );
    });
    await user.click(screen.getByText('update'));
    await waitFor(() => expect(screen.getByTestId('user')).toHaveTextContent('Updated'));
    expect(mockSetToken).toHaveBeenCalledWith('tok2');
    expect(mockDbSet).toHaveBeenCalledWith('ar_token', 'tok2');
  });

  it('updateSession updates email when new JWT contains a different email', async () => {
    const oldToken = makeJwt({ sub: 'u1', email: 'old@example.com', name: 'Jerry' });
    mockDbGet.mockImplementation((key: string) =>
      key === 'ar_token' ? Promise.resolve(oldToken) : Promise.resolve(undefined)
    );
    const newToken = makeJwt({ sub: 'u1', email: 'new@example.com', name: 'Jerry' });

    function UpdateEmailConsumer() {
      const auth = useAuth();
      return (
        <div>
          <span data-testid="email">{auth.user?.email ?? 'null'}</span>
          <button
            onClick={() =>
              auth.updateSession({
                token: newToken,
                userId: 'u1',
                displayName: 'Jerry',
              } as AuthResult)
            }
          >
            updateEmail
          </button>
        </div>
      );
    }

    const user = userEvent.setup();
    await act(async () => {
      render(
        <AuthProvider>
          <UpdateEmailConsumer />
        </AuthProvider>
      );
    });
    expect(screen.getByTestId('email')).toHaveTextContent('old@example.com');
    await user.click(screen.getByText('updateEmail'));
    await waitFor(() => expect(screen.getByTestId('email')).toHaveTextContent('new@example.com'));
  });

  it('setAlertsEnabled updates state and persists to db', async () => {
    const user = userEvent.setup();
    await act(async () => {
      render(
        <AuthProvider>
          <Consumer />
        </AuthProvider>
      );
    });
    await user.click(screen.getByText('disableAlerts'));
    await waitFor(() => expect(screen.getByTestId('alerts')).toHaveTextContent('false'));
    expect(mockDbSet).toHaveBeenCalledWith('ar_alerts', false);
  });

  it('handles db errors on mount without crashing', async () => {
    mockDbGet.mockRejectedValue(new Error('DB unavailable'));
    await act(async () => {
      render(
        <AuthProvider>
          <Consumer />
        </AuthProvider>
      );
    });
    expect(screen.getByTestId('loading')).toHaveTextContent('false');
    expect(screen.getByTestId('user')).toHaveTextContent('null');
  });

  it('useAuth throws when used outside AuthProvider', () => {
    function Thrower() {
      useAuth();
      return null;
    }
    const spy = vi.spyOn(console, 'error').mockImplementation(() => {});
    expect(() => render(<Thrower />)).toThrow('useAuth must be used inside AuthProvider');
    spy.mockRestore();
  });

  it('login with refreshToken persists it to db and calls setRefreshToken', async () => {
    function LoginWithRefreshConsumer() {
      const auth = useAuth();
      return (
        <div>
          <span data-testid="user">{auth.user ? auth.user.displayName : 'null'}</span>
          <button
            onClick={() =>
              auth.login(
                { token: 'tok', userId: 'u1', displayName: 'N', refreshToken: 'rt1' },
                'a@b.com'
              )
            }
          >
            loginWithRefresh
          </button>
        </div>
      );
    }
    const user = userEvent.setup();
    await act(async () => {
      render(
        <AuthProvider>
          <LoginWithRefreshConsumer />
        </AuthProvider>
      );
    });
    await user.click(screen.getByText('loginWithRefresh'));
    await waitFor(() => expect(screen.getByTestId('user')).toHaveTextContent('N'));
    expect(mockSetRefreshToken).toHaveBeenCalledWith('rt1');
    expect(mockDbSet).toHaveBeenCalledWith('ar_refresh_token', 'rt1');
  });

  it('logout calls revokeToken and removes refresh token from db when session has a refresh token', async () => {
    const token = makeJwt({ sub: 'u1', email: 'a@b.com', name: 'Jerry' });
    mockDbGet.mockImplementation((key: string) => {
      if (key === 'ar_token') return Promise.resolve(token);
      if (key === 'ar_refresh_token') return Promise.resolve('stored-rt');
      return Promise.resolve(undefined);
    });
    const user = userEvent.setup();
    await act(async () => {
      render(
        <AuthProvider>
          <Consumer />
        </AuthProvider>
      );
    });
    await user.click(screen.getByText('logout'));
    await waitFor(() => expect(screen.getByTestId('user')).toHaveTextContent('null'));
    expect(mockRevokeToken).toHaveBeenCalledWith('stored-rt');
    expect(mockDbRemove).toHaveBeenCalledWith('ar_refresh_token');
  });

  it('silently refreshes on mount when stored token is expired', async () => {
    const expiredToken = makeExpiredJwt({ sub: 'u1', email: 'a@b.com', name: 'Jerry' });
    const newToken = makeJwt({ sub: 'u1', email: 'a@b.com', name: 'Jerry' });
    mockDbGet.mockImplementation((key: string) => {
      if (key === 'ar_token') return Promise.resolve(expiredToken);
      if (key === 'ar_refresh_token') return Promise.resolve('old-rt');
      return Promise.resolve(undefined);
    });
    mockRefreshTokens.mockResolvedValue({
      token: newToken,
      userId: 'u1',
      displayName: 'Jerry',
      refreshToken: 'new-rt',
    });
    await act(async () => {
      render(
        <AuthProvider>
          <Consumer />
        </AuthProvider>
      );
    });
    await waitFor(() => expect(screen.getByTestId('user')).toHaveTextContent('Jerry'));
    expect(mockRefreshTokens).toHaveBeenCalledWith('old-rt');
    expect(mockSetToken).toHaveBeenCalledWith(newToken);
    expect(mockDbSet).toHaveBeenCalledWith('ar_refresh_token', 'new-rt');
  });

  it('user stays null on mount when expired token has no refresh token', async () => {
    const expiredToken = makeExpiredJwt({ sub: 'u1', email: 'a@b.com', name: 'Jerry' });
    mockDbGet.mockImplementation((key: string) => {
      if (key === 'ar_token') return Promise.resolve(expiredToken);
      return Promise.resolve(undefined);
    });
    await act(async () => {
      render(
        <AuthProvider>
          <Consumer />
        </AuthProvider>
      );
    });
    expect(screen.getByTestId('user')).toHaveTextContent('null');
    expect(mockRefreshTokens).not.toHaveBeenCalled();
  });

  it('user stays null when silent refresh fails and cleans up db', async () => {
    const expiredToken = makeExpiredJwt({ sub: 'u1', email: 'a@b.com', name: 'Jerry' });
    mockDbGet.mockImplementation((key: string) => {
      if (key === 'ar_token') return Promise.resolve(expiredToken);
      if (key === 'ar_refresh_token') return Promise.resolve('bad-rt');
      return Promise.resolve(undefined);
    });
    mockRefreshTokens.mockRejectedValue(new Error('expired'));
    await act(async () => {
      render(
        <AuthProvider>
          <Consumer />
        </AuthProvider>
      );
    });
    expect(screen.getByTestId('user')).toHaveTextContent('null');
    expect(mockDbRemove).toHaveBeenCalledWith('ar_token');
    expect(mockDbRemove).toHaveBeenCalledWith('ar_refresh_token');
  });

  it('onSessionExpired callback clears user and refresh token', async () => {
    const token = makeJwt({ sub: 'u1', email: 'a@b.com', name: 'Jerry' });
    mockDbGet.mockImplementation((key: string) =>
      key === 'ar_token' ? Promise.resolve(token) : Promise.resolve(undefined)
    );
    await act(async () => {
      render(
        <AuthProvider>
          <Consumer />
        </AuthProvider>
      );
    });
    expect(screen.getByTestId('user')).toHaveTextContent('Jerry');
    // Simulate the session expired callback fired by the api interceptor
    await act(async () => {
      capturedOnSessionExpired?.();
    });
    await waitFor(() => expect(screen.getByTestId('user')).toHaveTextContent('null'));
  });

  it('onTokenRefreshed callback updates user token in state and persists to db', async () => {
    const token = makeJwt({ sub: 'u1', email: 'a@b.com', name: 'Jerry' });
    const newToken = makeJwt({ sub: 'u1', email: 'a@b.com', name: 'Jerry' });
    mockDbGet.mockImplementation((key: string) =>
      key === 'ar_token' ? Promise.resolve(token) : Promise.resolve(undefined)
    );
    await act(async () => {
      render(
        <AuthProvider>
          <Consumer />
        </AuthProvider>
      );
    });
    // Simulate the token refreshed callback fired by the api interceptor
    await act(async () => {
      await capturedOnTokenRefreshed?.(newToken, 'new-rt');
    });
    expect(mockDbSet).toHaveBeenCalledWith('ar_token', newToken);
    expect(mockDbSet).toHaveBeenCalledWith('ar_refresh_token', 'new-rt');
  });
});
