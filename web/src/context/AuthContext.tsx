import { createContext, useContext } from 'react';
import type { AuthResult } from '../services/api';

export interface User {
  token: string;
  userId: string;
  displayName: string;
  email: string;
  iRacingCustomerId: number | null;
  role: string;
}

export interface AuthContextValue {
  user: User | null;
  loading: boolean;
  login: (result: AuthResult, email: string) => Promise<void>;
  logout: () => Promise<void>;
  updateSession: (result: AuthResult) => Promise<void>;
  alertsEnabled: boolean;
  setAlertsEnabled: (v: boolean) => Promise<void>;
}

export const AuthContext = createContext<AuthContextValue | null>(null);

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used inside AuthProvider');
  return ctx;
}
