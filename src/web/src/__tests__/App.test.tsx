import { render, screen, act } from '@testing-library/react';
import { describe, it, expect, vi } from 'vitest';
import App from '../App';

// AuthProvider reads from IndexedDB on mount — provide resolved stubs so the
// async effect settles inside act() without warnings.
vi.mock('../services/db', () => ({
  dbGet: vi.fn().mockResolvedValue(undefined),
  dbSet: vi.fn().mockResolvedValue(undefined),
  dbRemove: vi.fn().mockResolvedValue(undefined),
}));

describe('App', () => {
  it('renders the public landing page on the home route', async () => {
    await act(async () => { render(<App />); });
    // "/" renders the public landing page outside AppShell — the landing header
    // shows Sign in and Start free CTAs; the sidebar nav (Home, Browse Series) is not visible
    expect(screen.getAllByRole('link', { name: /sign in/i }).length).toBeGreaterThan(0);
    expect(screen.getAllByRole('link', { name: /start free/i }).length).toBeGreaterThan(0);
    expect(screen.queryByRole('link', { name: 'Home' })).not.toBeInTheDocument();
  });

  it('renders the home page content by default', async () => {
    await act(async () => { render(<App />); });
    expect(
      screen.getByRole('heading', { name: /win races/i })
    ).toBeInTheDocument();
  });
});
