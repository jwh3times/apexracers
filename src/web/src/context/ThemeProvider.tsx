import { useCallback, useEffect, useState } from 'react';
import type { ReactNode } from 'react';
import { api } from '../services/api';
import { ThemeContext, type ThemePreference } from './ThemeContext';

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

  const setTheme = useCallback((t: ThemePreference) => {
    setThemeState(t);
    localStorage.setItem('ar_theme', t);
    applyTheme(t);
    api.updateTheme(t).catch(() => {});
  }, []);

  const syncFromJwt = useCallback((themePreference: string) => {
    const t = parseTheme(themePreference);
    setThemeState(t);
    localStorage.setItem('ar_theme', t);
    applyTheme(t);
  }, []);

  return (
    <ThemeContext.Provider value={{ theme, setTheme, syncFromJwt }}>
      {children}
    </ThemeContext.Provider>
  );
}
