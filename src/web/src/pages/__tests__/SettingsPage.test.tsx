import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { vi, describe, it, expect, beforeEach } from 'vitest';
import SettingsPage from '../SettingsPage';
import { api } from '../../services/api';
import type { User } from '../../context/AuthContext';

const mockUpdateSession = vi.fn().mockResolvedValue(undefined);
const mockSetAlertsEnabled = vi.fn().mockResolvedValue(undefined);
const mockLogout = vi.fn().mockResolvedValue(undefined);

let mockUser: User | null = null;
let mockAlertsEnabled = true;

vi.mock('../../context/AuthContext', () => ({
  useAuth: () => ({
    user: mockUser,
    loading: false,
    login: vi.fn(),
    logout: mockLogout,
    updateSession: mockUpdateSession,
    alertsEnabled: mockAlertsEnabled,
    setAlertsEnabled: mockSetAlertsEnabled,
  }),
}));

vi.mock('../../services/api', () => ({
  api: { updateProfile: vi.fn(), updateRole: vi.fn() },
}));

function renderPage() {
  return render(<SettingsPage />);
}

describe('SettingsPage', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    mockUser = null;
    mockAlertsEnabled = true;
    mockUpdateSession.mockResolvedValue(undefined);
    mockSetAlertsEnabled.mockResolvedValue(undefined);
    mockLogout.mockResolvedValue(undefined);
  });

  it('renders the Account Settings heading', () => {
    renderPage();
    expect(screen.getByRole('heading', { name: /account settings/i })).toBeInTheDocument();
  });

  it('shows empty display name field when no user is logged in', () => {
    renderPage();
    expect(screen.getByLabelText(/display name/i)).toHaveValue('');
  });

  it('pre-fills display name from auth context', () => {
    mockUser = { token: 'tok', userId: 'u1', displayName: 'Jerry Holland', email: 'j@j.com', iRacingCustomerId: null, role: 'Standard' };
    renderPage();
    expect(screen.getByLabelText(/display name/i)).toHaveValue('Jerry Holland');
  });

  it('pre-fills iRacing Customer ID from auth context', () => {
    mockUser = { token: 'tok', userId: 'u1', displayName: 'Jerry', email: 'j@j.com', iRacingCustomerId: 100042, role: 'Standard' };
    renderPage();
    expect(screen.getByLabelText(/iRacing Customer ID/i)).toHaveValue(100042);
  });

  it('calls api.updateProfile and updateSession when Save Changes is clicked', async () => {
    mockUser = { token: 'tok', userId: 'u1', displayName: '', email: 'j@j.com', iRacingCustomerId: null, role: 'Standard' };
    vi.mocked(api.updateProfile).mockResolvedValue({ token: 'new-tok', userId: 'u1', displayName: 'Speed Demon' });
    const user = userEvent.setup();
    renderPage();
    const input = screen.getByLabelText(/display name/i);
    await user.clear(input);
    await user.type(input, 'Speed Demon');
    await user.click(screen.getByRole('button', { name: /save changes/i }));
    await waitFor(() => {
      expect(vi.mocked(api.updateProfile)).toHaveBeenCalledWith('Speed Demon', null);
      expect(mockUpdateSession).toHaveBeenCalledWith({ token: 'new-tok', userId: 'u1', displayName: 'Speed Demon' });
    });
  });

  it('renders the Connections section with iRacing label', () => {
    renderPage();
    expect(screen.getByText(/connections/i)).toBeInTheDocument();
    expect(screen.getByText(/iracing account/i)).toBeInTheDocument();
  });

  it('shows Not connected when no user is in auth context', () => {
    renderPage();
    expect(screen.getByText(/not connected/i)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /disconnect/i })).not.toBeInTheDocument();
  });

  it('shows Connected and Disconnect button when user is logged in', () => {
    mockUser = { token: 'some.jwt.token', userId: 'u1', displayName: 'Jerry', email: 'j@j.com', iRacingCustomerId: null, role: 'Standard' };
    renderPage();
    expect(screen.getByText(/^connected$/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /disconnect/i })).toBeInTheDocument();
  });

  it('renders current password, new password, and confirm password fields', () => {
    renderPage();
    expect(screen.getByLabelText(/current password/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/new password/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/confirm password/i)).toBeInTheDocument();
  });

  it('renders the notifications preference toggle', () => {
    renderPage();
    expect(screen.getByText(/new series data alerts/i)).toBeInTheDocument();
    expect(screen.getByRole('checkbox')).toBeInTheDocument();
  });

  it('calls setAlertsEnabled when the alerts toggle is clicked', async () => {
    const user = userEvent.setup();
    mockAlertsEnabled = true;
    renderPage();
    const toggle = screen.getByRole('checkbox');
    expect(toggle).toBeChecked();
    await user.click(toggle);
    expect(mockSetAlertsEnabled).toHaveBeenCalledWith(false);
  });

  it('shows an Error message when saveProfile fails with an Error instance', async () => {
    mockUser = { token: 'tok', userId: 'u1', displayName: '', email: 'j@j.com', iRacingCustomerId: null, role: 'Standard' };
    vi.mocked(api.updateProfile).mockRejectedValue(new Error('Server error'));
    const user = userEvent.setup();
    renderPage();
    await user.click(screen.getByRole('button', { name: /save changes/i }));
    await waitFor(() => expect(screen.getByText('Server error')).toBeInTheDocument());
  });

  it('shows fallback message when saveProfile rejects with a non-Error value', async () => {
    mockUser = { token: 'tok', userId: 'u1', displayName: '', email: 'j@j.com', iRacingCustomerId: null, role: 'Standard' };
    vi.mocked(api.updateProfile).mockRejectedValue('oops');
    const user = userEvent.setup();
    renderPage();
    await user.click(screen.getByRole('button', { name: /save changes/i }));
    await waitFor(() => expect(screen.getByText('Failed to save profile.')).toBeInTheDocument());
  });

  // ── Access Tier ─────────────────────────────────────────────────────────────

  it('shows Access Tier section for Standard users', () => {
    mockUser = { token: 't', userId: 'u1', displayName: 'Jerry', email: 'j@j.com', iRacingCustomerId: null, role: 'Standard' };
    renderPage();
    expect(screen.getByText(/access tier/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /standard/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /beta/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /alpha/i })).toBeInTheDocument();
  });

  it('hides Access Tier section for Admin users', () => {
    mockUser = { token: 't', userId: 'u1', displayName: 'Jerry', email: 'j@j.com', iRacingCustomerId: null, role: 'Admin' };
    renderPage();
    expect(screen.queryByText(/access tier/i)).not.toBeInTheDocument();
  });

  it('disables the currently active tier button', () => {
    mockUser = { token: 't', userId: 'u1', displayName: 'Jerry', email: 'j@j.com', iRacingCustomerId: null, role: 'Beta' };
    renderPage();
    const betaButton = screen.getByRole('button', { name: /^beta/i });
    expect(betaButton).toBeDisabled();
    expect(screen.getByRole('button', { name: /^standard/i })).not.toBeDisabled();
  });

  it('calls api.updateRole and updateSession when a tier is selected', async () => {
    mockUser = { token: 't', userId: 'u1', displayName: 'Jerry', email: 'j@j.com', iRacingCustomerId: null, role: 'Standard' };
    vi.mocked(api.updateRole).mockResolvedValue({ token: 'new-tok', userId: 'u1', displayName: 'Jerry' });
    const user = userEvent.setup();
    renderPage();
    await user.click(screen.getByRole('button', { name: /^beta/i }));
    await waitFor(() => {
      expect(vi.mocked(api.updateRole)).toHaveBeenCalledWith('Beta');
      expect(mockUpdateSession).toHaveBeenCalledWith({ token: 'new-tok', userId: 'u1', displayName: 'Jerry' });
    });
  });

  it('shows tier error message when updateRole fails', async () => {
    mockUser = { token: 't', userId: 'u1', displayName: 'Jerry', email: 'j@j.com', iRacingCustomerId: null, role: 'Standard' };
    vi.mocked(api.updateRole).mockRejectedValue(new Error('Not allowed'));
    const user = userEvent.setup();
    renderPage();
    await user.click(screen.getByRole('button', { name: /^beta/i }));
    await waitFor(() => expect(screen.getByText('Not allowed')).toBeInTheDocument());
  });
});
