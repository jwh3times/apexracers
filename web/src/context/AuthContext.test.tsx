import { render, screen, waitFor, act } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { vi, describe, it, expect, beforeEach } from 'vitest';
import { AuthProvider } from './AuthProvider';
import { useAuth } from './AuthContext';
import type { AuthResult } from '../services/api';
import { session } from '../services/session';

const mockDbGet = vi.fn();
const mockDbSet = vi.fn();
const mockDbRemove = vi.fn();
const mockRevokeToken = vi.fn();

// The db mock IS the storage seam — the real `session` runs against it, so these assertions
// exercise the actual persistence path rather than a stand-in for it. Session *mechanics*
// (refresh dedup, listener fan-out, rotation) are covered directly in services/session.test.ts;
// what this file tests is the React binding on top.
vi.mock('../services/db', () => ({
  dbGet: (...args: unknown[]) => mockDbGet(...args),
  dbSet: (...args: unknown[]) => mockDbSet(...args),
  dbRemove: (...args: unknown[]) => mockDbRemove(...args),
}));

vi.mock('../services/api', () => ({
  api: { revokeToken: (...args: unknown[]) => mockRevokeToken(...args) },
}));

const mockSyncFromJwt = vi.fn();
vi.mock('./ThemeContext', () => {
  // Stable references (created once) so AuthProvider's mount effect, which depends
  // on syncFromJwt, runs a single time — mirroring the real useCallback-backed value.
  const setTheme = vi.fn();
  const syncFromJwt = (...args: unknown[]) => mockSyncFromJwt(...args);
  return { useTheme: () => ({ theme: 'auto', setTheme, syncFromJwt }) };
});

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
  beforeEach(async () => {
    vi.resetAllMocks();
    vi.unstubAllGlobals();
    mockDbGet.mockResolvedValue(undefined);
    mockDbSet.mockResolvedValue(undefined);
    mockDbRemove.mockResolvedValue(undefined);
    mockRevokeToken.mockResolvedValue(undefined);
    // `session` is an app-wide singleton, so its in-memory tokens would otherwise leak between
    // tests in this file.
    await session.clear();
    mockDbRemove.mockClear();
    mockDbSet.mockClear();
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
    expect(session.accessToken).toBe(token);
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
    expect(session.accessToken).toBe('tok');
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
    expect(session.accessToken).toBeNull();
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
    expect(session.accessToken).toBe('tok2');
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

  it('login with a refreshToken installs and persists it', async () => {
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
    expect(session.refreshToken).toBe('rt1');
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
    // The session exchanges the token over its own transport (raw fetch, deliberately not the
    // intercepting client — routing it through would recurse on the 401 it exists to handle).
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      json: () => Promise.resolve({ token: newToken, refreshToken: 'new-rt' }),
    });
    vi.stubGlobal('fetch', fetchMock);

    await act(async () => {
      render(
        <AuthProvider>
          <Consumer />
        </AuthProvider>
      );
    });
    await waitFor(() => expect(screen.getByTestId('user')).toHaveTextContent('Jerry'));
    expect(fetchMock).toHaveBeenCalledWith(
      '/api/auth/refresh',
      expect.objectContaining({ body: JSON.stringify({ refreshToken: 'old-rt' }) })
    );
    expect(session.accessToken).toBe(newToken);
    expect(mockDbSet).toHaveBeenCalledWith('ar_refresh_token', 'new-rt');
  });

  it('user stays null on mount when expired token has no refresh token', async () => {
    const expiredToken = makeExpiredJwt({ sub: 'u1', email: 'a@b.com', name: 'Jerry' });
    mockDbGet.mockImplementation((key: string) => {
      if (key === 'ar_token') return Promise.resolve(expiredToken);
      return Promise.resolve(undefined);
    });
    const fetchMock = vi.fn();
    vi.stubGlobal('fetch', fetchMock);
    await act(async () => {
      render(
        <AuthProvider>
          <Consumer />
        </AuthProvider>
      );
    });
    expect(screen.getByTestId('user')).toHaveTextContent('null');
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('user stays null when silent refresh fails and cleans up db', async () => {
    const expiredToken = makeExpiredJwt({ sub: 'u1', email: 'a@b.com', name: 'Jerry' });
    mockDbGet.mockImplementation((key: string) => {
      if (key === 'ar_token') return Promise.resolve(expiredToken);
      if (key === 'ar_refresh_token') return Promise.resolve('bad-rt');
      return Promise.resolve(undefined);
    });
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: false, status: 401 }));
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

  // ── Reacting to the session ─────────────────────────────────────────────────
  //
  // These used to reach in and invoke a captured callback slot. They now drive the real session
  // and assert the provider follows, which is the binding this component actually owns.

  it('syncs the theme from a restored token that carries a preference', async () => {
    const token = `header.${btoa(
      JSON.stringify({ sub: 'u1', email: 'a@b.com', name: 'Jerry', theme_preference: 'dark' })
    )}.signature`;
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

    expect(mockSyncFromJwt).toHaveBeenCalledWith('dark');
  });

  it('does not sync the theme when the token carries no preference', async () => {
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

    expect(mockSyncFromJwt).not.toHaveBeenCalled();
  });

  it('syncs the theme only once, so a later login cannot override a manual change', async () => {
    const token = `header.${btoa(
      JSON.stringify({ sub: 'u1', email: 'a@b.com', name: 'Jerry', theme_preference: 'dark' })
    )}.signature`;
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
    expect(mockSyncFromJwt).toHaveBeenCalledTimes(1);

    await user.click(screen.getByText('login'));
    await waitFor(() => expect(screen.getByTestId('user')).toHaveTextContent('New'));

    expect(mockSyncFromJwt).toHaveBeenCalledTimes(1);
  });

  it('drops the user when the session clears itself', async () => {
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

    await act(async () => {
      await session.clear();
    });

    await waitFor(() => expect(screen.getByTestId('user')).toHaveTextContent('null'));
  });

  it('follows a background token refresh into state and storage', async () => {
    const token = makeJwt({ sub: 'u1', email: 'a@b.com', name: 'Jerry' });
    const newToken = makeJwt({ sub: 'u1', email: 'a@b.com', name: 'Renamed' });
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

    await act(async () => {
      await session.adopt({ accessToken: newToken, refreshToken: 'new-rt' });
    });

    await waitFor(() => expect(screen.getByTestId('user')).toHaveTextContent('Renamed'));
    expect(mockDbSet).toHaveBeenCalledWith('ar_token', newToken);
    expect(mockDbSet).toHaveBeenCalledWith('ar_refresh_token', 'new-rt');
  });

  it('unsubscribes on unmount, so a later session change cannot update a dead provider', async () => {
    // The old interceptor exposed a single global callback slot with no way to deregister; a
    // second provider silently replaced the first and an unmounted one kept being called.
    const errorSpy = vi.spyOn(console, 'error').mockImplementation(() => {});
    const { unmount } = render(
      <AuthProvider>
        <Consumer />
      </AuthProvider>
    );
    await waitFor(() => expect(screen.getByTestId('loading')).toHaveTextContent('false'));

    unmount();
    await act(async () => {
      await session.adopt({ accessToken: makeJwt({ sub: 'u1', email: 'a@b.com', name: 'Late' }) });
    });

    expect(errorSpy).not.toHaveBeenCalled();
    errorSpy.mockRestore();
  });
});
