import { render, screen } from '@testing-library/react';
import { MemoryRouter, Routes, Route } from 'react-router-dom';
import type { ReactElement } from 'react';
import { vi, describe, it, expect, beforeEach } from 'vitest';
import { RequireAuth, AdminGuard, RequireFlag } from '../App';
import type { User } from '../context/AuthContext';

let mockUser: User | null = null;
let mockLoading = false;

vi.mock('../context/AuthContext', () => ({
  useAuth: () => ({ user: mockUser, loading: mockLoading }),
}));

let mockFlag = true;
vi.mock('../context/FeatureFlagContext', () => ({
  useFeatureFlag: () => mockFlag,
}));

const adminUser: User = {
  token: 't',
  userId: 'u1',
  displayName: 'Admin',
  email: 'a@a.com',
  iRacingCustomerId: null,
  role: 'Admin',
};
const standardUser: User = { ...adminUser, role: 'Standard' };

function renderGuard(guard: ReactElement, path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route element={guard}>
          <Route path="/secret" element={<div>secret content</div>} />
          <Route path="/admin" element={<div>admin content</div>} />
        </Route>
        <Route path="/login" element={<div>login page</div>} />
        <Route path="/dashboard" element={<div>dashboard page</div>} />
      </Routes>
    </MemoryRouter>
  );
}

describe('RequireAuth', () => {
  beforeEach(() => {
    mockUser = null;
    mockLoading = false;
  });

  it('renders nothing while the session is still loading', () => {
    mockLoading = true;
    renderGuard(<RequireAuth />, '/secret');
    expect(screen.queryByText('secret content')).not.toBeInTheDocument();
    expect(screen.queryByText('login page')).not.toBeInTheDocument();
  });

  it('redirects to /login when there is no authenticated user', () => {
    mockUser = null;
    renderGuard(<RequireAuth />, '/secret');
    expect(screen.getByText('login page')).toBeInTheDocument();
  });

  it('renders the protected outlet for an authenticated user', () => {
    mockUser = standardUser;
    renderGuard(<RequireAuth />, '/secret');
    expect(screen.getByText('secret content')).toBeInTheDocument();
  });
});

describe('AdminGuard', () => {
  beforeEach(() => {
    mockUser = null;
    mockLoading = false;
  });

  it('renders nothing while the session is still loading', () => {
    mockLoading = true;
    renderGuard(<AdminGuard />, '/admin');
    expect(screen.queryByText('admin content')).not.toBeInTheDocument();
    expect(screen.queryByText('login page')).not.toBeInTheDocument();
  });

  it('redirects unauthenticated users to /login', () => {
    mockUser = null;
    renderGuard(<AdminGuard />, '/admin');
    expect(screen.getByText('login page')).toBeInTheDocument();
  });

  it('redirects authenticated non-admins to /dashboard', () => {
    mockUser = standardUser;
    renderGuard(<AdminGuard />, '/admin');
    expect(screen.getByText('dashboard page')).toBeInTheDocument();
  });

  it('renders the admin outlet for Admin users', () => {
    mockUser = adminUser;
    renderGuard(<AdminGuard />, '/admin');
    expect(screen.getByText('admin content')).toBeInTheDocument();
  });
});

describe('RequireFlag', () => {
  beforeEach(() => {
    mockUser = null;
    mockLoading = false;
    mockFlag = true;
  });

  it('renders the gated outlet when iracing-live is on', () => {
    mockFlag = true;
    renderGuard(<RequireFlag />, '/secret');
    expect(screen.getByText('secret content')).toBeInTheDocument();
  });

  it('renders ComingSoon (not the outlet) when iracing-live is off', () => {
    mockFlag = false;
    renderGuard(<RequireFlag />, '/secret');
    expect(screen.queryByText('secret content')).not.toBeInTheDocument();
    expect(screen.getByText(/live iracing analytics arriving soon/i)).toBeInTheDocument();
  });

  it('renders ComingSoon for a guest (no redirect to login) when off', () => {
    mockUser = null;
    mockFlag = false;
    renderGuard(<RequireFlag />, '/secret');
    expect(screen.queryByText('login page')).not.toBeInTheDocument();
    expect(screen.getByText(/live iracing analytics arriving soon/i)).toBeInTheDocument();
  });
});
