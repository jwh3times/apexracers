import { render, screen, act } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { vi, describe, it, expect, beforeEach } from 'vitest';
import { ThemeProvider, useTheme } from '../ThemeContext';

const mockUpdateTheme = vi.fn();

vi.mock('../../services/api', () => ({
  api: { updateTheme: (...args: unknown[]) => mockUpdateTheme(...args) },
}));

function Consumer() {
  const { theme, setTheme, syncFromJwt } = useTheme();
  return (
    <div>
      <span data-testid="theme">{theme}</span>
      <button onClick={() => setTheme('light')}>set light</button>
      <button onClick={() => setTheme('dark')}>set dark</button>
      <button onClick={() => setTheme('auto')}>set auto</button>
      <button onClick={() => syncFromJwt('light')}>sync light</button>
      <button onClick={() => syncFromJwt('dark')}>sync dark</button>
      <button onClick={() => syncFromJwt('unknown')}>sync unknown</button>
    </div>
  );
}

function renderWithProvider() {
  return render(<ThemeProvider><Consumer /></ThemeProvider>);
}

describe('ThemeContext', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    localStorage.clear();
    document.documentElement.className = '';
    mockUpdateTheme.mockResolvedValue(undefined);
  });

  it('defaults to auto when localStorage is empty', () => {
    renderWithProvider();
    expect(screen.getByTestId('theme')).toHaveTextContent('auto');
  });

  it('reads initial theme from localStorage', () => {
    localStorage.setItem('ar_theme', 'dark');
    renderWithProvider();
    expect(screen.getByTestId('theme')).toHaveTextContent('dark');
  });

  it('falls back to auto for an unrecognised localStorage value', () => {
    localStorage.setItem('ar_theme', 'solarized');
    renderWithProvider();
    expect(screen.getByTestId('theme')).toHaveTextContent('auto');
  });

  it('applies theme-auto class to <html> on mount', () => {
    renderWithProvider();
    expect(document.documentElement.classList.contains('theme-auto')).toBe(true);
  });

  it('setTheme updates state, localStorage, and html class', async () => {
    renderWithProvider();
    await userEvent.click(screen.getByText('set light'));
    expect(screen.getByTestId('theme')).toHaveTextContent('light');
    expect(localStorage.getItem('ar_theme')).toBe('light');
    expect(document.documentElement.classList.contains('theme-light')).toBe(true);
    expect(document.documentElement.classList.contains('theme-auto')).toBe(false);
  });

  it('setTheme calls api.updateTheme', async () => {
    renderWithProvider();
    await userEvent.click(screen.getByText('set dark'));
    expect(mockUpdateTheme).toHaveBeenCalledWith('dark');
  });

  it('setTheme does not throw when api.updateTheme rejects', async () => {
    mockUpdateTheme.mockRejectedValue(new Error('network'));
    renderWithProvider();
    await act(async () => {
      await userEvent.click(screen.getByText('set light'));
    });
    expect(screen.getByTestId('theme')).toHaveTextContent('light');
  });

  it('setTheme removes old theme classes before adding new one', async () => {
    localStorage.setItem('ar_theme', 'dark');
    renderWithProvider();
    await userEvent.click(screen.getByText('set light'));
    expect(document.documentElement.classList.contains('theme-dark')).toBe(false);
    expect(document.documentElement.classList.contains('theme-light')).toBe(true);
  });

  it('syncFromJwt applies light preference', async () => {
    renderWithProvider();
    await userEvent.click(screen.getByText('sync light'));
    expect(screen.getByTestId('theme')).toHaveTextContent('light');
    expect(localStorage.getItem('ar_theme')).toBe('light');
    expect(document.documentElement.classList.contains('theme-light')).toBe(true);
  });

  it('syncFromJwt applies dark preference', async () => {
    renderWithProvider();
    await userEvent.click(screen.getByText('sync dark'));
    expect(screen.getByTestId('theme')).toHaveTextContent('dark');
  });

  it('syncFromJwt falls back to auto for unknown values', async () => {
    renderWithProvider();
    await userEvent.click(screen.getByText('sync unknown'));
    expect(screen.getByTestId('theme')).toHaveTextContent('auto');
    expect(localStorage.getItem('ar_theme')).toBe('auto');
  });

  it('useTheme throws when used outside ThemeProvider', () => {
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => {});
    expect(() => render(<Consumer />)).toThrow('useTheme must be used inside ThemeProvider');
    consoleError.mockRestore();
  });
});
