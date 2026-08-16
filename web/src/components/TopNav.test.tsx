import { render, screen, fireEvent, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router';
import { vi, describe, it, expect, beforeEach } from 'vitest';
import TopNav from './TopNav';
import type { User } from '../context/AuthContext';
import { PaceSourceProvider } from '../context/PaceSourceProvider';

// ---------------------------------------------------------------------------
// Module mocks — must be declared before any imports that use them
// ---------------------------------------------------------------------------

const mockNavigate = vi.fn();
const mockLogout = vi.fn();

vi.mock('react-router', async importOriginal => {
  const actual = await importOriginal<typeof import('react-router')>();
  return { ...actual, useNavigate: () => mockNavigate };
});

let mockUser: User | null = null;

vi.mock('../context/AuthContext', () => ({
  useAuth: () => ({ user: mockUser, logout: mockLogout, alertsEnabled: false }),
}));

let mockFlag = true;
vi.mock('../context/FeatureFlagContext', () => ({
  useIracingSurface: () => ({ enabled: mockFlag, ready: true }),
}));

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function renderTopNav(initialPath = '/') {
  return render(
    <PaceSourceProvider>
      <MemoryRouter initialEntries={[initialPath]}>
        <TopNav />
      </MemoryRouter>
    </PaceSourceProvider>
  );
}

const baseUser: User = {
  token: 'test-token',
  userId: 'u1',
  displayName: 'Jerry',
  email: 'jerry@example.com',
  iRacingCustomerId: null,
  role: 'Standard',
};

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('TopNav', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    mockUser = null;
    mockLogout.mockResolvedValue(undefined);
    mockFlag = true;
  });

  // -------------------------------------------------------------------------
  // TopNav component — basic rendering
  // -------------------------------------------------------------------------

  it('renders the ApexRacers brand name', () => {
    renderTopNav();
    expect(screen.getByText('ApexRacers')).toBeInTheDocument();
  });

  it('renders the user menu button', () => {
    renderTopNav();
    expect(screen.getByRole('button', { name: /user menu/i })).toBeInTheDocument();
  });

  it('shows guest nav items when no user is logged in', () => {
    mockUser = null;
    renderTopNav();
    // GUEST_NAV slice(1) = Browse Series only (Home is index 0, skipped)
    expect(screen.getByRole('link', { name: /browse series/i })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /dashboard/i })).not.toBeInTheDocument();
  });

  it('shows authenticated nav items when a user is logged in', () => {
    mockUser = { ...baseUser };
    renderTopNav();
    // AUTH_NAV slice(1) = all except Dashboard
    expect(screen.getByRole('link', { name: /browse series/i })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /analytics/i })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /recommendations/i })).toBeInTheDocument();
    // Dashboard is index 0 and is sliced off in TopNav
    expect(screen.queryByRole('link', { name: /dashboard/i })).not.toBeInTheDocument();
  });

  // -------------------------------------------------------------------------
  // ProfileDropdown — toggle open/close
  // -------------------------------------------------------------------------

  it('dropdown is not visible before the profile button is clicked', () => {
    renderTopNav();
    expect(screen.queryByRole('link', { name: /profile/i })).not.toBeInTheDocument();
  });

  it('opens the dropdown when the profile button is clicked', async () => {
    const user = userEvent.setup();
    renderTopNav();
    await user.click(screen.getByRole('button', { name: /user menu/i }));
    expect(screen.getByRole('link', { name: /^profile$/i })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /^settings$/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /logout/i })).toBeInTheDocument();
  });

  it('closes the dropdown when the profile button is clicked a second time', async () => {
    const user = userEvent.setup();
    renderTopNav();
    const menuBtn = screen.getByRole('button', { name: /user menu/i });
    await user.click(menuBtn);
    expect(screen.getByRole('link', { name: /^profile$/i })).toBeInTheDocument();
    await user.click(menuBtn);
    await waitFor(() =>
      expect(screen.queryByRole('link', { name: /^profile$/i })).not.toBeInTheDocument()
    );
  });

  it('sets aria-expanded to false when the dropdown is closed', () => {
    renderTopNav();
    const menuBtn = screen.getByRole('button', { name: /user menu/i });
    expect(menuBtn).toHaveAttribute('aria-expanded', 'false');
  });

  it('sets aria-expanded to true when the dropdown is open', async () => {
    const user = userEvent.setup();
    renderTopNav();
    const menuBtn = screen.getByRole('button', { name: /user menu/i });
    await user.click(menuBtn);
    expect(menuBtn).toHaveAttribute('aria-expanded', 'true');
  });

  // -------------------------------------------------------------------------
  // handleClickOutside — click outside closes dropdown
  // -------------------------------------------------------------------------

  it('closes the dropdown when clicking outside of it', async () => {
    const user = userEvent.setup();
    renderTopNav();
    await user.click(screen.getByRole('button', { name: /user menu/i }));
    expect(screen.getByRole('link', { name: /^profile$/i })).toBeInTheDocument();

    fireEvent.mouseDown(document.body);

    await waitFor(() =>
      expect(screen.queryByRole('link', { name: /^profile$/i })).not.toBeInTheDocument()
    );
  });

  it('does not close the dropdown when clicking inside it', async () => {
    const user = userEvent.setup();
    renderTopNav();
    await user.click(screen.getByRole('button', { name: /user menu/i }));

    const profileLink = screen.getByRole('link', { name: /^profile$/i });
    fireEvent.mouseDown(profileLink);

    // The dropdown container contains the click target, so it should remain open
    expect(screen.getByRole('link', { name: /^profile$/i })).toBeInTheDocument();
  });

  it('does not add the mousedown listener when the dropdown is closed', () => {
    const addSpy = vi.spyOn(document, 'addEventListener');
    renderTopNav();
    // dropdown starts closed — no listener should have been added yet
    const calls = addSpy.mock.calls.filter(([event]) => event === 'mousedown');
    expect(calls).toHaveLength(0);
    addSpy.mockRestore();
  });

  // -------------------------------------------------------------------------
  // handleLogout — calls logout(), navigates to /login
  // -------------------------------------------------------------------------

  it('calls logout and navigates to /login when Logout is clicked', async () => {
    const user = userEvent.setup();
    renderTopNav();
    await user.click(screen.getByRole('button', { name: /user menu/i }));
    await user.click(screen.getByRole('button', { name: /logout/i }));

    await waitFor(() => expect(mockLogout).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(mockNavigate).toHaveBeenCalledWith('/login'));
  });

  // -------------------------------------------------------------------------
  // Profile link — closes dropdown on click
  // -------------------------------------------------------------------------

  it('closes the dropdown when the Profile link is clicked', async () => {
    const user = userEvent.setup();
    renderTopNav();
    await user.click(screen.getByRole('button', { name: /user menu/i }));
    expect(screen.getByRole('link', { name: /^profile$/i })).toBeInTheDocument();

    await user.click(screen.getByRole('link', { name: /^profile$/i }));

    await waitFor(() =>
      expect(screen.queryByRole('link', { name: /^profile$/i })).not.toBeInTheDocument()
    );
  });

  // -------------------------------------------------------------------------
  // Settings link — closes dropdown on click
  // -------------------------------------------------------------------------

  it('closes the dropdown when the Settings link is clicked', async () => {
    const user = userEvent.setup();
    renderTopNav();
    await user.click(screen.getByRole('button', { name: /user menu/i }));
    expect(screen.getByRole('link', { name: /^settings$/i })).toBeInTheDocument();

    await user.click(screen.getByRole('link', { name: /^settings$/i }));

    await waitFor(() =>
      expect(screen.queryByRole('link', { name: /^settings$/i })).not.toBeInTheDocument()
    );
  });

  // -------------------------------------------------------------------------
  // NavLink className arrow — isActive true and false paths
  // -------------------------------------------------------------------------

  it('applies the active text class to a NavLink when its route is the current path', () => {
    mockUser = { ...baseUser }; // AUTH_NAV so we have /analytics in slice(1)
    renderTopNav('/analytics');
    const analyticsLink = screen.getByRole('link', { name: /analytics/i });
    expect(analyticsLink).toHaveClass('text-on-surface');
  });

  it('applies the inactive text class to NavLinks that do not match the current path', () => {
    mockUser = { ...baseUser };
    renderTopNav('/analytics');
    const recLink = screen.getByRole('link', { name: /recommendations/i });
    expect(recLink).toHaveClass('text-on-surface-variant');
    expect(recLink).not.toHaveClass('text-on-surface');
  });

  it('applies the active class to the correct guest nav link', () => {
    mockUser = null;
    renderTopNav('/series');
    const seriesLink = screen.getByRole('link', { name: /browse series/i });
    expect(seriesLink).toHaveClass('text-on-surface');
  });

  // -------------------------------------------------------------------------
  // MobileNav — hamburger menu for small screens
  // -------------------------------------------------------------------------

  it('renders the mobile navigation menu button', () => {
    renderTopNav();
    expect(screen.getByRole('button', { name: /open navigation menu/i })).toBeInTheDocument();
  });

  it('mobile nav dropdown is closed by default', () => {
    mockUser = { ...baseUser };
    renderTopNav();
    // Dashboard appears only in the mobile dropdown (inline links slice it off).
    expect(screen.queryByRole('link', { name: /dashboard/i })).not.toBeInTheDocument();
  });

  it('opens the mobile nav dropdown with the full nav item list when clicked', async () => {
    const user = userEvent.setup();
    mockUser = { ...baseUser };
    renderTopNav();
    await user.click(screen.getByRole('button', { name: /open navigation menu/i }));
    // Dashboard is unique to the mobile dropdown, confirming it opened.
    expect(screen.getByRole('link', { name: /dashboard/i })).toBeInTheDocument();
  });

  it('toggles aria-expanded on the mobile nav button', async () => {
    const user = userEvent.setup();
    renderTopNav();
    const menuBtn = screen.getByRole('button', { name: /open navigation menu/i });
    expect(menuBtn).toHaveAttribute('aria-expanded', 'false');
    await user.click(menuBtn);
    expect(menuBtn).toHaveAttribute('aria-expanded', 'true');
  });

  it('shows the Admin Panel link in the mobile nav for Admin users', async () => {
    const user = userEvent.setup();
    mockUser = { ...baseUser, role: 'Admin' };
    renderTopNav();
    await user.click(screen.getByRole('button', { name: /open navigation menu/i }));
    expect(screen.getByRole('link', { name: /admin panel/i })).toBeInTheDocument();
  });

  it('closes the mobile nav when a nav item is clicked', async () => {
    const user = userEvent.setup();
    mockUser = { ...baseUser };
    renderTopNav();
    await user.click(screen.getByRole('button', { name: /open navigation menu/i }));
    const dashboardLink = screen.getByRole('link', { name: /dashboard/i });
    await user.click(dashboardLink);
    await waitFor(() =>
      expect(screen.queryByRole('link', { name: /dashboard/i })).not.toBeInTheDocument()
    );
  });

  // -------------------------------------------------------------------------
  // Breadcrumbs (T13)
  // -------------------------------------------------------------------------

  it('renders navigable breadcrumbs for a deep route', () => {
    renderTopNav('/series/444/weeks/5/strategy');
    const crumb = screen.getByRole('navigation', { name: /breadcrumb/i });
    expect(within(crumb).getByRole('link', { name: 'Series' })).toHaveAttribute('href', '/series');
    // Race Week Index 5 in the route reads as Race Week Number 6, and the link keeps the index.
    expect(within(crumb).getByRole('link', { name: 'Week 6' })).toHaveAttribute(
      'href',
      '/series/444/weeks/5'
    );
    expect(within(crumb).getByText('Strategy')).toBeInTheDocument();
  });

  it('renders a single current-page crumb for a top-level route', () => {
    renderTopNav('/support');
    const crumb = screen.getByRole('navigation', { name: /breadcrumb/i });
    expect(within(crumb).getByText('Support')).toBeInTheDocument();
  });

  it('hides gated inline nav links when iracing-live is off', () => {
    mockUser = { ...baseUser };
    mockFlag = false;
    renderTopNav();
    // /analytics is gated → gone from the inline (slice(1)) links
    expect(screen.queryByRole('link', { name: /analytics/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /browse series/i })).not.toBeInTheDocument();
    // always-on items must still be present (filter must not drop everything)
    expect(screen.getByRole('link', { name: /my laps/i })).toBeInTheDocument();
  });
});
