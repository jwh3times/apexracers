import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { vi, describe, it, expect } from 'vitest';
import Sidebar from '../Sidebar';
import type { User } from '../../context/AuthContext';

let mockUser: User | null = null;

vi.mock('../../context/AuthContext', () => ({
  useAuth: () => ({ user: mockUser }),
}));

function renderSidebar() {
  return render(<MemoryRouter><Sidebar /></MemoryRouter>);
}

describe('Sidebar', () => {
  it('shows guest nav when no user is logged in', () => {
    mockUser = null;
    renderSidebar();
    expect(screen.getByRole('link', { name: /browse series/i })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /dashboard/i })).not.toBeInTheDocument();
  });

  it('shows authenticated nav when a user is logged in', () => {
    mockUser = { token: 't', userId: 'u1', displayName: 'Jerry', email: 'j@j.com', iRacingCustomerId: null, role: 'Standard' };
    renderSidebar();
    expect(screen.getByRole('link', { name: /dashboard/i })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /admin panel/i })).not.toBeInTheDocument();
  });

  it('shows Admin Panel link for Admin users', () => {
    mockUser = { token: 't', userId: 'u1', displayName: 'Jerry', email: 'j@j.com', iRacingCustomerId: null, role: 'Admin' };
    renderSidebar();
    expect(screen.getByRole('link', { name: /admin panel/i })).toBeInTheDocument();
  });
});
