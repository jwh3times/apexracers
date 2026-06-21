import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { vi, describe, it, expect, beforeEach } from 'vitest';
import Sidebar from '../Sidebar';
import type { User } from '../../context/AuthContext';

let mockUser: User | null = null;

vi.mock('../../context/AuthContext', () => ({
  useAuth: () => ({ user: mockUser }),
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
});
