import { useEffect, useRef, useState } from 'react';
import type { ReactNode } from 'react';
import {
  api,
  setToken,
  clearToken,
  setRefreshToken,
  onTokenRefreshed,
  onSessionExpired,
} from '../services/api';
import type { AuthResult } from '../services/api';
import { dbGet, dbSet, dbRemove } from '../services/db';
import { useTheme } from './ThemeContext';
import { AuthContext, type User } from './AuthContext';

function decodeJwt(token: string): {
  sub: string;
  email: string;
  name: string;
  iracing_id?: string;
  role?: string;
  theme_preference?: string;
  exp?: number;
} | null {
  try {
    const payload = token.split('.')[1];
    return JSON.parse(atob(payload.replace(/-/g, '+').replace(/_/g, '/')));
  } catch {
    return null;
  }
}

function isTokenExpired(token: string): boolean {
  const claims = decodeJwt(token);
  if (!claims?.exp) return false;
  return claims.exp * 1000 < Date.now();
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null>(null);
  const [loading, setLoading] = useState(true);
  const [alertsEnabled, setAlertsEnabledState] = useState(true);
  const { syncFromJwt } = useTheme();
  const didSyncRef = useRef(false);
  const refreshTokenRef = useRef<string | null>(null);

  useEffect(() => {
    // Register interceptor callbacks so silent background refreshes persist to storage
    onTokenRefreshed(async (newToken, newRefresh) => {
      refreshTokenRef.current = newRefresh;
      await dbSet('ar_token', newToken);
      await dbSet('ar_refresh_token', newRefresh);
      const claims = decodeJwt(newToken);
      if (claims) {
        setUser(prev =>
          prev ? { ...prev, token: newToken, role: claims.role ?? prev.role } : prev
        );
      }
    });
    onSessionExpired(() => {
      setUser(null);
      refreshTokenRef.current = null;
      Promise.all([dbRemove('ar_token'), dbRemove('ar_refresh_token')]).catch(() => {});
    });

    Promise.all([
      dbGet<string>('ar_token'),
      dbGet<string>('ar_refresh_token'),
      dbGet<boolean>('ar_alerts'),
    ])
      .then(async ([token, refreshToken, alerts]) => {
        if (alerts !== undefined) setAlertsEnabledState(alerts);

        if (refreshToken) {
          refreshTokenRef.current = refreshToken;
          setRefreshToken(refreshToken);
        }

        if (token && !isTokenExpired(token)) {
          const claims = decodeJwt(token);
          if (claims) {
            setUser({
              token,
              userId: claims.sub,
              displayName: claims.name,
              email: claims.email,
              iRacingCustomerId: claims.iracing_id ? Number(claims.iracing_id) : null,
              role: claims.role ?? 'Standard',
            });
            setToken(token);
            if (!didSyncRef.current && claims.theme_preference) {
              didSyncRef.current = true;
              syncFromJwt(claims.theme_preference);
            }
          }
        } else if (refreshToken) {
          try {
            const result = await api.refreshTokens(refreshToken);
            const claims = decodeJwt(result.token);
            if (claims) {
              setUser({
                token: result.token,
                userId: result.userId,
                displayName: result.displayName,
                email: claims.email,
                iRacingCustomerId: claims.iracing_id ? Number(claims.iracing_id) : null,
                role: claims.role ?? 'Standard',
              });
              setToken(result.token);
              await dbSet('ar_token', result.token);
              if (result.refreshToken) {
                refreshTokenRef.current = result.refreshToken;
                setRefreshToken(result.refreshToken);
                await dbSet('ar_refresh_token', result.refreshToken);
              }
              if (!didSyncRef.current && claims.theme_preference) {
                didSyncRef.current = true;
                syncFromJwt(claims.theme_preference);
              }
            }
          } catch {
            refreshTokenRef.current = null;
            await Promise.all([dbRemove('ar_token'), dbRemove('ar_refresh_token')]).catch(() => {});
          }
        }
      })
      .catch(() => {})
      .finally(() => setLoading(false));
  }, [syncFromJwt]);

  async function login(result: AuthResult, email: string) {
    const claims = decodeJwt(result.token);
    const u: User = {
      token: result.token,
      userId: result.userId,
      displayName: result.displayName,
      email,
      iRacingCustomerId: claims?.iracing_id ? Number(claims.iracing_id) : null,
      role: claims?.role ?? 'Standard',
    };
    setUser(u);
    setToken(result.token);
    await dbSet('ar_token', result.token);
    if (result.refreshToken) {
      refreshTokenRef.current = result.refreshToken;
      setRefreshToken(result.refreshToken);
      await dbSet('ar_refresh_token', result.refreshToken);
    }
    if (claims?.theme_preference) syncFromJwt(claims.theme_preference);
  }

  async function logout() {
    if (refreshTokenRef.current) {
      await api.revokeToken(refreshTokenRef.current);
    }
    refreshTokenRef.current = null;
    setUser(null);
    clearToken();
    await dbRemove('ar_token');
    await dbRemove('ar_refresh_token');
  }

  async function updateSession(result: AuthResult) {
    const claims = decodeJwt(result.token);
    const iRacingCustomerId = claims?.iracing_id ? Number(claims.iracing_id) : null;
    const role = claims?.role ?? 'Standard';
    const email = claims?.email;
    setUser(prev =>
      prev
        ? {
            ...prev,
            token: result.token,
            displayName: result.displayName,
            iRacingCustomerId,
            role,
            ...(email ? { email } : {}),
          }
        : prev
    );
    setToken(result.token);
    await dbSet('ar_token', result.token);
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
