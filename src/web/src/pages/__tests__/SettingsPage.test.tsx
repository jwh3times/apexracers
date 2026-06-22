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
  api: {
    updateProfile: vi.fn(),
    updateRole: vi.fn(),
    updateTheme: vi.fn(),
    changePassword: vi.fn(),
  },
}));

vi.mock('../../context/ThemeContext', () => ({
  useTheme: () => ({ theme: 'auto', setTheme: vi.fn() }),
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
    mockUser = {
      token: 'tok',
      userId: 'u1',
      displayName: 'Jerry Holland',
      email: 'j@j.com',
      iRacingCustomerId: null,
      role: 'Standard',
    };
    renderPage();
    expect(screen.getByLabelText(/display name/i)).toHaveValue('Jerry Holland');
  });

  it('pre-fills iRacing Customer ID from auth context', () => {
    mockUser = {
      token: 'tok',
      userId: 'u1',
      displayName: 'Jerry',
      email: 'j@j.com',
      iRacingCustomerId: 100042,
      role: 'Standard',
    };
    renderPage();
    expect(screen.getByLabelText(/iRacing Customer ID/i)).toHaveValue(100042);
  });

  it('calls api.updateProfile and updateSession when Save Changes is clicked', async () => {
    mockUser = {
      token: 'tok',
      userId: 'u1',
      displayName: '',
      email: 'j@j.com',
      iRacingCustomerId: null,
      role: 'Standard',
    };
    vi.mocked(api.updateProfile).mockResolvedValue({
      token: 'new-tok',
      userId: 'u1',
      displayName: 'Speed Demon',
    });
    const user = userEvent.setup();
    renderPage();
    const input = screen.getByLabelText(/display name/i);
    await user.clear(input);
    await user.type(input, 'Speed Demon');
    await user.click(screen.getByRole('button', { name: /save changes/i }));
    await waitFor(() => {
      expect(vi.mocked(api.updateProfile)).toHaveBeenCalledWith('Speed Demon', null);
      expect(mockUpdateSession).toHaveBeenCalledWith({
        token: 'new-tok',
        userId: 'u1',
        displayName: 'Speed Demon',
      });
    });
  });

  it('does not pass email to api.updateProfile (email change is handled separately)', async () => {
    mockUser = {
      token: 'tok',
      userId: 'u1',
      displayName: 'Jerry',
      email: 'old@example.com',
      iRacingCustomerId: null,
      role: 'Standard',
    };
    vi.mocked(api.updateProfile).mockResolvedValue({
      token: 'new-tok',
      userId: 'u1',
      displayName: 'Jerry',
    });
    const user = userEvent.setup();
    renderPage();
    const emailInput = screen.getByLabelText(/email address/i);
    await user.clear(emailInput);
    await user.type(emailInput, 'new@example.com');
    await user.click(screen.getByRole('button', { name: /save changes/i }));
    await waitFor(() => {
      expect(vi.mocked(api.updateProfile)).toHaveBeenCalledWith('Jerry', null);
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
    mockUser = {
      token: 'some.jwt.token',
      userId: 'u1',
      displayName: 'Jerry',
      email: 'j@j.com',
      iRacingCustomerId: null,
      role: 'Standard',
    };
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
    mockUser = {
      token: 'tok',
      userId: 'u1',
      displayName: '',
      email: 'j@j.com',
      iRacingCustomerId: null,
      role: 'Standard',
    };
    vi.mocked(api.updateProfile).mockRejectedValue(new Error('Server error'));
    const user = userEvent.setup();
    renderPage();
    await user.click(screen.getByRole('button', { name: /save changes/i }));
    await waitFor(() => expect(screen.getByText('Server error')).toBeInTheDocument());
  });

  it('shows fallback message when saveProfile rejects with a non-Error value', async () => {
    mockUser = {
      token: 'tok',
      userId: 'u1',
      displayName: '',
      email: 'j@j.com',
      iRacingCustomerId: null,
      role: 'Standard',
    };
    vi.mocked(api.updateProfile).mockRejectedValue('oops');
    const user = userEvent.setup();
    renderPage();
    await user.click(screen.getByRole('button', { name: /save changes/i }));
    await waitFor(() => expect(screen.getByText('Failed to save profile.')).toBeInTheDocument());
  });

  // ── Access Tier ─────────────────────────────────────────────────────────────

  it('shows Access Tier section for Standard users', () => {
    mockUser = {
      token: 't',
      userId: 'u1',
      displayName: 'Jerry',
      email: 'j@j.com',
      iRacingCustomerId: null,
      role: 'Standard',
    };
    renderPage();
    expect(screen.getByText(/access tier/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /standard/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /beta/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /alpha/i })).toBeInTheDocument();
  });

  it('hides Access Tier section for Admin users', () => {
    mockUser = {
      token: 't',
      userId: 'u1',
      displayName: 'Jerry',
      email: 'j@j.com',
      iRacingCustomerId: null,
      role: 'Admin',
    };
    renderPage();
    expect(screen.queryByText(/access tier/i)).not.toBeInTheDocument();
  });

  it('disables the currently active tier button', () => {
    mockUser = {
      token: 't',
      userId: 'u1',
      displayName: 'Jerry',
      email: 'j@j.com',
      iRacingCustomerId: null,
      role: 'Beta',
    };
    renderPage();
    const betaButton = screen.getByRole('button', { name: /^beta/i });
    expect(betaButton).toBeDisabled();
    expect(screen.getByRole('button', { name: /^standard/i })).not.toBeDisabled();
  });

  it('calls api.updateRole and updateSession when a tier is selected', async () => {
    mockUser = {
      token: 't',
      userId: 'u1',
      displayName: 'Jerry',
      email: 'j@j.com',
      iRacingCustomerId: null,
      role: 'Standard',
    };
    vi.mocked(api.updateRole).mockResolvedValue({
      token: 'new-tok',
      userId: 'u1',
      displayName: 'Jerry',
    });
    const user = userEvent.setup();
    renderPage();
    await user.click(screen.getByRole('button', { name: /^beta/i }));
    await waitFor(() => {
      expect(vi.mocked(api.updateRole)).toHaveBeenCalledWith('Beta');
      expect(mockUpdateSession).toHaveBeenCalledWith({
        token: 'new-tok',
        userId: 'u1',
        displayName: 'Jerry',
      });
    });
  });

  it('shows tier error message when updateRole fails', async () => {
    mockUser = {
      token: 't',
      userId: 'u1',
      displayName: 'Jerry',
      email: 'j@j.com',
      iRacingCustomerId: null,
      role: 'Standard',
    };
    vi.mocked(api.updateRole).mockRejectedValue(new Error('Not allowed'));
    const user = userEvent.setup();
    renderPage();
    await user.click(screen.getByRole('button', { name: /^beta/i }));
    await waitFor(() => expect(screen.getByText('Not allowed')).toBeInTheDocument());
  });

  it('calls api.changePassword and clears all fields on success', async () => {
    vi.mocked(api.changePassword).mockResolvedValue(undefined);
    const user = userEvent.setup();
    renderPage();
    const currentInput = screen.getByLabelText(/current password/i);
    const newInput = screen.getByLabelText(/new password/i);
    const confirmInput = screen.getByLabelText(/confirm password/i);
    await user.type(currentInput, 'oldpass1');
    await user.type(newInput, 'newpass2');
    await user.type(confirmInput, 'newpass2');
    await user.click(screen.getByRole('button', { name: /update password/i }));
    await waitFor(() => {
      expect(vi.mocked(api.changePassword)).toHaveBeenCalledWith('oldpass1', 'newpass2');
      expect(currentInput).toHaveValue('');
      expect(newInput).toHaveValue('');
      expect(confirmInput).toHaveValue('');
    });
  });

  it('shows a mismatch error without calling api.changePassword when passwords differ', async () => {
    const user = userEvent.setup();
    renderPage();
    await user.type(screen.getByLabelText(/current password/i), 'oldpass1');
    await user.type(screen.getByLabelText(/new password/i), 'newpass2');
    await user.type(screen.getByLabelText(/confirm password/i), 'different');
    await user.click(screen.getByRole('button', { name: /update password/i }));
    await waitFor(() => expect(screen.getByText(/do not match/i)).toBeInTheDocument());
    expect(vi.mocked(api.changePassword)).not.toHaveBeenCalled();
  });

  it('shows a server error when api.changePassword fails', async () => {
    vi.mocked(api.changePassword).mockRejectedValue(new Error('Incorrect password.'));
    const user = userEvent.setup();
    renderPage();
    await user.type(screen.getByLabelText(/current password/i), 'wrongpass');
    await user.type(screen.getByLabelText(/new password/i), 'newpass2');
    await user.type(screen.getByLabelText(/confirm password/i), 'newpass2');
    await user.click(screen.getByRole('button', { name: /update password/i }));
    await waitFor(() => expect(screen.getByText('Incorrect password.')).toBeInTheDocument());
  });

  it('shows fallback message when changePassword rejects with a non-Error value', async () => {
    vi.mocked(api.changePassword).mockRejectedValue('oops');
    const user = userEvent.setup();
    renderPage();
    await user.type(screen.getByLabelText(/current password/i), 'oldpass1');
    await user.type(screen.getByLabelText(/new password/i), 'newpass2');
    await user.type(screen.getByLabelText(/confirm password/i), 'newpass2');
    await user.click(screen.getByRole('button', { name: /update password/i }));
    await waitFor(() => expect(screen.getByText('Failed to change password.')).toBeInTheDocument());
  });

  it('clicking Disconnect calls logout', async () => {
    mockUser = {
      token: 'some.jwt.token',
      userId: 'u1',
      displayName: 'Jerry',
      email: 'j@j.com',
      iRacingCustomerId: null,
      role: 'Standard',
    };
    const user = userEvent.setup();
    renderPage();
    const disconnectBtn = screen.getByRole('button', { name: /disconnect/i });
    await user.click(disconnectBtn);
    expect(mockLogout).toHaveBeenCalledTimes(1);
  });

  it('shows Saved ✓ on the save button after a successful profile save', async () => {
    mockUser = {
      token: 'tok',
      userId: 'u1',
      displayName: 'Jerry',
      email: 'j@j.com',
      iRacingCustomerId: null,
      role: 'Standard',
    };
    vi.mocked(api.updateProfile).mockResolvedValue({
      token: 'new-tok',
      userId: 'u1',
      displayName: 'Jerry',
    });
    const user = userEvent.setup();
    renderPage();
    await user.click(screen.getByRole('button', { name: /save changes/i }));
    await waitFor(() => expect(screen.getByRole('button', { name: /saved/i })).toBeInTheDocument());
  });

  it('clicking the active tier button does not call api.updateRole', async () => {
    mockUser = {
      token: 't',
      userId: 'u1',
      displayName: 'Jerry',
      email: 'j@j.com',
      iRacingCustomerId: null,
      role: 'Beta',
    };
    const user = userEvent.setup();
    renderPage();
    const betaButton = screen.getByRole('button', { name: /^beta/i });
    // The active tier button is disabled — clicking it must not invoke the API
    await user.click(betaButton);
    expect(vi.mocked(api.updateRole)).not.toHaveBeenCalled();
  });

  it('typing into password fields updates their values', async () => {
    const user = userEvent.setup();
    renderPage();
    const currentInput = screen.getByLabelText(/current password/i);
    const newInput = screen.getByLabelText(/new password/i);
    const confirmInput = screen.getByLabelText(/confirm password/i);
    await user.type(currentInput, 'secret1');
    await user.type(newInput, 'secret2');
    await user.type(confirmInput, 'secret2');
    expect(currentInput).toHaveValue('secret1');
    expect(newInput).toHaveValue('secret2');
    expect(confirmInput).toHaveValue('secret2');
  });
});
