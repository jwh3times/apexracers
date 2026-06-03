import { createContext, useContext, useEffect, useState } from 'react';
import type { ReactNode } from 'react';
import { api } from '../services/api';

export type ThemePreference = 'auto' | 'light' | 'dark';

interface ThemeContextValue {
  theme: ThemePreference;
  setTheme: (t: ThemePreference) => void;
  syncFromJwt: (themePreference: string) => void;
}

const ThemeContext = createContext<ThemeContextValue | null>(null);

const ALL: ThemePreference[] = ['auto', 'light', 'dark'];

function applyTheme(t: ThemePreference) {
  const cl = document.documentElement.classList;
  ALL.forEach(c => cl.remove('theme-' + c));
  cl.add('theme-' + t);
}

function parseTheme(v: string | null | undefined): ThemePreference {
  return v === 'light' || v === 'dark' ? v : 'auto';
}

export function ThemeProvider({ children }: { children: ReactNode }) {
  const [theme, setThemeState] = useState<ThemePreference>(() =>
    parseTheme(localStorage.getItem('ar_theme'))
  );

  useEffect(() => {
    applyTheme(theme);
  }, [theme]);

  function setTheme(t: ThemePreference) {
    setThemeState(t);
    localStorage.setItem('ar_theme', t);
    applyTheme(t);
    api.updateTheme(t).catch(() => {});
  }

  function syncFromJwt(themePreference: string) {
    const t = parseTheme(themePreference);
    setThemeState(t);
    localStorage.setItem('ar_theme', t);
    applyTheme(t);
  }

  return (
    <ThemeContext.Provider value={{ theme, setTheme, syncFromJwt }}>
      {children}
    </ThemeContext.Provider>
  );
}

export function useTheme(): ThemeContextValue {
  const ctx = useContext(ThemeContext);
  if (!ctx) throw new Error('useTheme must be used inside ThemeProvider');
  return ctx;
}
