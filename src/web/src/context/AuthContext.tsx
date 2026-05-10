import { createContext, useContext, useEffect, useState } from 'react';
import type { ReactNode } from 'react';
import { setToken, clearToken } from '../services/api';
import { dbGet, dbSet, dbRemove } from '../services/db';
import type { AuthResult } from '../services/api';

export interface User {
  token: string;
  userId: string;
  displayName: string;
  email: string;
}

interface AuthContextValue {
  user: User | null;
  loading: boolean;
  login: (result: AuthResult, email: string) => Promise<void>;
  logout: () => Promise<void>;
  updateSession: (result: AuthResult) => Promise<void>;
  alertsEnabled: boolean;
  setAlertsEnabled: (v: boolean) => Promise<void>;
}

function decodeJwt(token: string): { sub: string; email: string; name: string } | null {
  try {
    const payload = token.split('.')[1];
    return JSON.parse(atob(payload.replace(/-/g, '+').replace(/_/g, '/')));
  } catch {
    return null;
  }
}

export const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null>(null);
  const [loading, setLoading] = useState(true);
  const [alertsEnabled, setAlertsEnabledState] = useState(true);

  useEffect(() => {
    Promise.all([
      dbGet<string>('ar_token'),
      dbGet<boolean>('ar_alerts'),
    ])
      .then(([token, alerts]) => {
        if (token) {
          const claims = decodeJwt(token);
          if (claims) {
            setUser({ token, userId: claims.sub, displayName: claims.name, email: claims.email });
            setToken(token);
          }
        }
        if (alerts !== undefined) setAlertsEnabledState(alerts);
      })
      .catch(() => {})
      .finally(() => setLoading(false));
  }, []);

  async function login(result: AuthResult, email: string) {
    const u: User = { token: result.token, userId: result.userId, displayName: result.displayName, email };
    setUser(u);
    setToken(result.token);
    await dbSet('ar_token', result.token);
  }

  async function logout() {
    setUser(null);
    clearToken();
    await dbRemove('ar_token');
  }

  async function updateSession(result: AuthResult) {
    setUser(prev => prev ? { ...prev, token: result.token, displayName: result.displayName } : prev);
    setToken(result.token);
    await dbSet('ar_token', result.token);
  }

  async function setAlertsEnabled(v: boolean) {
    setAlertsEnabledState(v);
    await dbSet('ar_alerts', v);
  }

  return (
    <AuthContext.Provider value={{ user, loading, login, logout, updateSession, alertsEnabled, setAlertsEnabled }}>
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used inside AuthProvider');
  return ctx;
}
