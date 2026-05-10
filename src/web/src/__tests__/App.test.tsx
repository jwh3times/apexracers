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
  it('renders navigation links on the home route', async () => {
    await act(async () => { render(<App />); });
    // Unauthenticated state: only guest nav items are shown
    expect(screen.getAllByRole('link', { name: 'Home' }).length).toBeGreaterThan(0);
    expect(screen.getAllByRole('link', { name: 'Browse Series' }).length).toBeGreaterThan(0);
    expect(screen.queryByRole('link', { name: 'Recommendations' })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'My Laps' })).not.toBeInTheDocument();
  });

  it('renders the home page content by default', async () => {
    await act(async () => { render(<App />); });
    expect(
      screen.getByRole('heading', { name: /find the car that best fits your pace/i })
    ).toBeInTheDocument();
  });
});
