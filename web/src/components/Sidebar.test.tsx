import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router';
import { vi, describe, it, expect, beforeEach } from 'vitest';
import Sidebar from './Sidebar';
import type { User } from '../context/AuthContext';

let mockUser: User | null = null;

vi.mock('../context/AuthContext', () => ({
  useAuth: () => ({ user: mockUser }),
}));

let mockFlag = true;
vi.mock('../context/FeatureFlagContext', () => ({
  useIracingSurface: () => ({ enabled: mockFlag, ready: true }),
}));

const LOGGED_IN: User = {
  token: 't',
  userId: 'u1',
  displayName: 'Jerry',
  email: 'j@j.com',
  iRacingCustomerId: null,
  role: 'Standard',
};

function renderSidebar() {
  return render(
    <MemoryRouter>
      <Sidebar />
    </MemoryRouter>
  );
}

describe('Sidebar', () => {
  beforeEach(() => {
    localStorage.clear();
    mockUser = null;
    mockFlag = true;
  });

  it('shows guest nav when no user is logged in', () => {
    mockUser = null;
    renderSidebar();
    expect(screen.getByRole('link', { name: /browse series/i })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /dashboard/i })).not.toBeInTheDocument();
  });

  it('shows authenticated nav when a user is logged in', () => {
    mockUser = {
      token: 't',
      userId: 'u1',
      displayName: 'Jerry',
      email: 'j@j.com',
      iRacingCustomerId: null,
      role: 'Standard',
    };
    renderSidebar();
    expect(screen.getByRole('link', { name: /dashboard/i })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /admin panel/i })).not.toBeInTheDocument();
  });

  it('shows Admin Panel link for Admin users', () => {
    mockUser = {
      token: 't',
      userId: 'u1',
      displayName: 'Jerry',
      email: 'j@j.com',
      iRacingCustomerId: null,
      role: 'Admin',
    };
    renderSidebar();
    expect(screen.getByRole('link', { name: /admin panel/i })).toBeInTheDocument();
  });

  // ── Collapse / icon rail (T13) ─────────────────────────────────────────────

  it('collapses to an icon rail and persists the choice', () => {
    mockUser = LOGGED_IN;
    renderSidebar();
    expect(screen.getByText('Dashboard')).toBeInTheDocument(); // label visible when expanded

    fireEvent.click(screen.getByRole('button', { name: /collapse sidebar/i }));

    expect(screen.queryByText('Dashboard')).not.toBeInTheDocument(); // labels hidden in the rail
    expect(localStorage.getItem('ar_sidebar_collapsed')).toBe('true');
    expect(screen.getByRole('button', { name: /expand sidebar/i })).toBeInTheDocument();
  });

  it('restores the collapsed state from localStorage', () => {
    localStorage.setItem('ar_sidebar_collapsed', 'true');
    mockUser = LOGGED_IN;
    renderSidebar();
    expect(screen.queryByText('Dashboard')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: /expand sidebar/i })).toBeInTheDocument();
  });

  it('hides gated nav items but keeps the always-on tools when iracing-live is off', () => {
    mockUser = LOGGED_IN;
    mockFlag = false;
    renderSidebar();
    // Always-on tools remain
    expect(screen.getByRole('link', { name: /my laps/i })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /telemetry/i })).toBeInTheDocument();
    // Gated items are gone
    expect(screen.queryByRole('link', { name: /browse series/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /analytics/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /leaderboards/i })).not.toBeInTheDocument();
  });

  it('hides gated items and keeps Home for guest when iracing-live is off', () => {
    mockUser = null;
    mockFlag = false;
    renderSidebar();
    expect(screen.getByRole('link', { name: /home/i })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /browse series/i })).not.toBeInTheDocument();
  });
});
