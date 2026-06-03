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

vi.mock('../../services/db', () => ({
  dbGet: (...args: unknown[]) => mockDbGet(...args),
  dbSet: (...args: unknown[]) => mockDbSet(...args),
  dbRemove: (...args: unknown[]) => mockDbRemove(...args),
}));

vi.mock('../../services/api', () => ({
  setToken: (...args: unknown[]) => mockSetToken(...args),
  clearToken: (...args: unknown[]) => mockClearToken(...args),
}));

vi.mock('../ThemeContext', () => ({
  useTheme: () => ({ theme: 'auto', setTheme: vi.fn(), syncFromJwt: vi.fn() }),
}));

function makeJwt(claims: { sub: string; email: string; name: string }): string {
  return `header.${btoa(JSON.stringify(claims))}.signature`;
}

function Consumer() {
  const auth = useAuth();
  return (
    <div>
      <span data-testid="loading">{String(auth.loading)}</span>
      <span data-testid="user">{auth.user ? auth.user.displayName : 'null'}</span>
      <span data-testid="email">{auth.user?.email ?? 'null'}</span>
      <span data-testid="alerts">{String(auth.alertsEnabled)}</span>
      <button onClick={() => auth.login({ token: 'tok', userId: 'u1', displayName: 'New' } as AuthResult, 'a@b.com')}>
        login
      </button>
      <button onClick={() => auth.logout()}>logout</button>
      <button onClick={() => auth.updateSession({ token: 'tok2', userId: 'u1', displayName: 'Updated' } as AuthResult)}>
        update
      </button>
      <button onClick={() => auth.setAlertsEnabled(false)}>disableAlerts</button>
    </div>
  );
}

describe('AuthContext', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    mockDbGet.mockResolvedValue(undefined);
    mockDbSet.mockResolvedValue(undefined);
    mockDbRemove.mockResolvedValue(undefined);
  });

  it('starts loading then settles to false', async () => {
    await act(async () => {
      render(<AuthProvider><Consumer /></AuthProvider>);
    });
    expect(screen.getByTestId('loading')).toHaveTextContent('false');
  });

  it('user is null when no token is stored', async () => {
    await act(async () => {
      render(<AuthProvider><Consumer /></AuthProvider>);
    });
    expect(screen.getByTestId('user')).toHaveTextContent('null');
  });

  it('decodes a valid JWT from db and sets user', async () => {
    const token = makeJwt({ sub: 'u1', email: 'a@b.com', name: 'Jerry' });
    mockDbGet.mockImplementation((key: string) =>
      key === 'ar_token' ? Promise.resolve(token) : Promise.resolve(undefined),
    );
    await act(async () => {
      render(<AuthProvider><Consumer /></AuthProvider>);
    });
    expect(screen.getByTestId('user')).toHaveTextContent('Jerry');
    expect(screen.getByTestId('email')).toHaveTextContent('a@b.com');
    expect(mockSetToken).toHaveBeenCalledWith(token);
  });

  it('ignores a token with an invalid JWT payload', async () => {
    mockDbGet.mockImplementation((key: string) =>
      key === 'ar_token' ? Promise.resolve('invalid-token') : Promise.resolve(undefined),
    );
    await act(async () => {
      render(<AuthProvider><Consumer /></AuthProvider>);
    });
    expect(screen.getByTestId('user')).toHaveTextContent('null');
  });

  it('sets alertsEnabled to false when db stores false', async () => {
    mockDbGet.mockImplementation((key: string) =>
      key === 'ar_alerts' ? Promise.resolve(false) : Promise.resolve(undefined),
    );
    await act(async () => {
      render(<AuthProvider><Consumer /></AuthProvider>);
    });
    expect(screen.getByTestId('alerts')).toHaveTextContent('false');
  });

  it('keeps alertsEnabled true when db returns undefined', async () => {
    await act(async () => {
      render(<AuthProvider><Consumer /></AuthProvider>);
    });
    expect(screen.getByTestId('alerts')).toHaveTextContent('true');
  });

  it('login sets user and persists token to db', async () => {
    const user = userEvent.setup();
    await act(async () => {
      render(<AuthProvider><Consumer /></AuthProvider>);
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
      key === 'ar_token' ? Promise.resolve(token) : Promise.resolve(undefined),
    );
    const user = userEvent.setup();
    await act(async () => {
      render(<AuthProvider><Consumer /></AuthProvider>);
    });
    await user.click(screen.getByText('logout'));
    await waitFor(() => expect(screen.getByTestId('user')).toHaveTextContent('null'));
    expect(mockClearToken).toHaveBeenCalled();
    expect(mockDbRemove).toHaveBeenCalledWith('ar_token');
  });

  it('updateSession updates display name and saves new token', async () => {
    const token = makeJwt({ sub: 'u1', email: 'a@b.com', name: 'Jerry' });
    mockDbGet.mockImplementation((key: string) =>
      key === 'ar_token' ? Promise.resolve(token) : Promise.resolve(undefined),
    );
    const user = userEvent.setup();
    await act(async () => {
      render(<AuthProvider><Consumer /></AuthProvider>);
    });
    await user.click(screen.getByText('update'));
    await waitFor(() => expect(screen.getByTestId('user')).toHaveTextContent('Updated'));
    expect(mockSetToken).toHaveBeenCalledWith('tok2');
    expect(mockDbSet).toHaveBeenCalledWith('ar_token', 'tok2');
  });

  it('updateSession updates email when new JWT contains a different email', async () => {
    const oldToken = makeJwt({ sub: 'u1', email: 'old@example.com', name: 'Jerry' });
    mockDbGet.mockImplementation((key: string) =>
      key === 'ar_token' ? Promise.resolve(oldToken) : Promise.resolve(undefined),
    );
    const newToken = makeJwt({ sub: 'u1', email: 'new@example.com', name: 'Jerry' });

    function UpdateEmailConsumer() {
      const auth = useAuth();
      return (
        <div>
          <span data-testid="email">{auth.user?.email ?? 'null'}</span>
          <button onClick={() => auth.updateSession({ token: newToken, userId: 'u1', displayName: 'Jerry' } as AuthResult)}>
            updateEmail
          </button>
        </div>
      );
    }

    const user = userEvent.setup();
    await act(async () => {
      render(<AuthProvider><UpdateEmailConsumer /></AuthProvider>);
    });
    expect(screen.getByTestId('email')).toHaveTextContent('old@example.com');
    await user.click(screen.getByText('updateEmail'));
    await waitFor(() => expect(screen.getByTestId('email')).toHaveTextContent('new@example.com'));
  });

  it('setAlertsEnabled updates state and persists to db', async () => {
    const user = userEvent.setup();
    await act(async () => {
      render(<AuthProvider><Consumer /></AuthProvider>);
    });
    await user.click(screen.getByText('disableAlerts'));
    await waitFor(() => expect(screen.getByTestId('alerts')).toHaveTextContent('false'));
    expect(mockDbSet).toHaveBeenCalledWith('ar_alerts', false);
  });

  it('handles db errors on mount without crashing', async () => {
    mockDbGet.mockRejectedValue(new Error('DB unavailable'));
    await act(async () => {
      render(<AuthProvider><Consumer /></AuthProvider>);
    });
    expect(screen.getByTestId('loading')).toHaveTextContent('false');
    expect(screen.getByTestId('user')).toHaveTextContent('null');
  });

  it('useAuth throws when used outside AuthProvider', () => {
    function Thrower() { useAuth(); return null; }
    const spy = vi.spyOn(console, 'error').mockImplementation(() => {});
    expect(() => render(<Thrower />)).toThrow('useAuth must be used inside AuthProvider');
    spy.mockRestore();
  });
});
