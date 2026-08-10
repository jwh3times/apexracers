import { useCallback, useEffect, useRef, useState } from 'react';
import type { ReactNode } from 'react';
import { api } from '../services/api';
import type { AuthResult } from '../services/api';
import { session, type JwtClaims } from '../services/session';
import { dbGet, dbSet } from '../services/db';
import { useTheme } from './ThemeContext';
import { AuthContext, type User } from './AuthContext';

/**
 * Builds the display-facing user from the token's own claims.
 *
 * Everything here comes from the JWT, so restore, login and refresh all produce the same shape —
 * previously the boot path read `userId`/`displayName` off the `AuthResult` while the restore path
 * read them off the claims, which meant two ways to be right and two ways to drift.
 */
function userFromClaims(claims: JwtClaims | null, overrides: Partial<User> = {}): User | null {
  if (!claims) return null;
  return {
    token: overrides.token ?? '',
    userId: claims.sub,
    displayName: claims.name,
    email: claims.email,
    iRacingCustomerId: claims.iracing_id ? Number(claims.iracing_id) : null,
    role: claims.role ?? 'Standard',
    ...overrides,
  };
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null>(null);
  const [loading, setLoading] = useState(true);
  const [alertsEnabled, setAlertsEnabledState] = useState(true);
  const { syncFromJwt } = useTheme();
  const didSyncRef = useRef(false);

  // Theme lives in another provider, so syncing is a side effect of seeing a new token rather
  // than something the session itself should know about.
  const syncThemeOnce = useCallback(
    (claims: JwtClaims | null) => {
      if (!didSyncRef.current && claims?.theme_preference) {
        didSyncRef.current = true;
        syncFromJwt(claims.theme_preference);
      }
    },
    [syncFromJwt]
  );

  useEffect(() => {
    // A subscription, not a callback slot: mounting a second provider (StrictMode's double
    // invoke, a multi-provider test) adds a listener instead of silently replacing the first,
    // and unmounting removes exactly this one.
    const unsubscribe = session.subscribe(({ accessToken, claims }) => {
      setUser(prev => {
        // Signed out — the only case that drops the user.
        if (!accessToken) return null;
        // A token we can't read is still a token: carry it, but don't discard who is signed in
        // on the strength of a decode failure.
        if (!claims) return prev ? { ...prev, token: accessToken } : prev;
        // Claims are authoritative — the server minted them, including the email.
        return userFromClaims(claims, { token: accessToken });
      });
    });

    let active = true;
    Promise.all([session.restore(), dbGet<boolean>('ar_alerts')])
      .then(([snapshot, alerts]) => {
        if (!active) return;
        if (alerts !== undefined) setAlertsEnabledState(alerts);
        syncThemeOnce(snapshot.claims);
      })
      .catch(() => {})
      .finally(() => {
        if (active) setLoading(false);
      });

    return () => {
      active = false;
      unsubscribe();
    };
  }, [syncThemeOnce]);

  async function login(result: AuthResult, email: string) {
    await session.adopt({ accessToken: result.token, refreshToken: result.refreshToken });
    const claims = session.claims;
    // Identity comes from the AuthResult here rather than the claims: the response is
    // authoritative for this call, and the email is the one the user actually typed.
    setUser({
      token: result.token,
      userId: result.userId,
      displayName: result.displayName,
      email,
      iRacingCustomerId: claims?.iracing_id ? Number(claims.iracing_id) : null,
      role: claims?.role ?? 'Standard',
    });
    syncThemeOnce(claims);
  }

  async function logout() {
    const refreshToken = session.refreshToken;
    if (refreshToken) await api.revokeToken(refreshToken);
    await session.clear();
  }

  async function updateSession(result: AuthResult) {
    await session.adopt({ accessToken: result.token, refreshToken: result.refreshToken });
    const claims = session.claims;
    // A patch, not a replacement: this runs after a profile or email change, so it must not
    // resurrect a user that logout already cleared.
    setUser(prev =>
      prev
        ? {
            ...prev,
            token: result.token,
            displayName: result.displayName,
            iRacingCustomerId: claims?.iracing_id ? Number(claims.iracing_id) : null,
            role: claims?.role ?? 'Standard',
            ...(claims?.email ? { email: claims.email } : {}),
          }
        : prev
    );
  }

  async function setAlertsEnabled(v: boolean) {
    setAlertsEnabledState(v);
    await dbSet('ar_alerts', v);
  }

  return (
    <AuthContext.Provider
      value={{ user, loading, login, logout, updateSession, alertsEnabled, setAlertsEnabled }}
    >
      {children}
    </AuthContext.Provider>
  );
}
