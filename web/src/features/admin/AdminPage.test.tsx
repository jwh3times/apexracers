import { render, screen, waitFor, act } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { vi, describe, it, expect, beforeEach } from 'vitest';
import AdminPage from './AdminPage';
import { api } from '../../services/api';
import type { AdminUser, FeatureFlag } from '../../services/api';

vi.mock('../../services/api', () => ({
  api: {
    getAdminUsers: vi.fn(),
    setAdminUserRole: vi.fn(),
    getAdminFeatureFlags: vi.fn(),
    createFeatureFlag: vi.fn(),
    updateFeatureFlag: vi.fn(),
    deleteFeatureFlag: vi.fn(),
  },
}));

const mockUsers: AdminUser[] = [
  { userId: 'u1', email: 'alice@example.com', displayName: 'Alice', role: 'Standard' },
  { userId: 'u2', email: 'bob@example.com', displayName: 'Bob', role: 'Beta' },
];

const mockFlag: FeatureFlag = {
  id: 1,
  key: 'new.ui',
  name: 'New UI',
  description: 'A redesigned UI',
  isEnabled: true,
  minimumRole: 'Beta',
  createdAt: '2024-01-01T00:00:00Z',
  updatedAt: '2024-01-01T00:00:00Z',
};

function renderPage() {
  return render(<AdminPage />);
}

describe('AdminPage', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(api.getAdminUsers).mockResolvedValue(mockUsers);
    vi.mocked(api.getAdminFeatureFlags).mockResolvedValue([mockFlag]);
  });

  // ── Layout ──────────────────────────────────────────────────────────────────

  it('renders the Admin Panel heading', async () => {
    await act(async () => {
      renderPage();
    });
    expect(screen.getByRole('heading', { name: /admin panel/i })).toBeInTheDocument();
  });

  it('renders Users and Feature Flags tabs', async () => {
    await act(async () => {
      renderPage();
    });
    expect(screen.getByRole('button', { name: /^users$/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /feature flags/i })).toBeInTheDocument();
  });

  // ── Users tab ───────────────────────────────────────────────────────────────

  it('loads and displays users in the users tab', async () => {
    renderPage();
    await waitFor(() => {
      expect(screen.getByText('alice@example.com')).toBeInTheDocument();
      expect(screen.getByText('bob@example.com')).toBeInTheDocument();
    });
  });

  it('shows a Save button when the role dropdown is changed', async () => {
    const user = userEvent.setup();
    renderPage();
    await waitFor(() => screen.getByText('alice@example.com'));

    const selects = screen.getAllByRole('combobox');
    await user.selectOptions(selects[0], 'Alpha');
    expect(screen.getByRole('button', { name: /^save$/i })).toBeInTheDocument();
  });

  it('calls setAdminUserRole and removes Save button after successful save', async () => {
    const user = userEvent.setup();
    vi.mocked(api.setAdminUserRole).mockResolvedValue({
      userId: 'u1',
      email: 'alice@example.com',
      displayName: 'Alice',
      role: 'Alpha',
    });
    renderPage();
    await waitFor(() => screen.getByText('alice@example.com'));

    const selects = screen.getAllByRole('combobox');
    await user.selectOptions(selects[0], 'Alpha');
    await user.click(screen.getByRole('button', { name: /^save$/i }));

    await waitFor(() =>
      expect(vi.mocked(api.setAdminUserRole)).toHaveBeenCalledWith('u1', 'Alpha')
    );
    await waitFor(() =>
      expect(screen.queryByRole('button', { name: /^save$/i })).not.toBeInTheDocument()
    );
  });

  it('shows an error message when setAdminUserRole fails', async () => {
    const user = userEvent.setup();
    vi.mocked(api.setAdminUserRole).mockRejectedValue(new Error('Forbidden'));
    renderPage();
    await waitFor(() => screen.getByText('alice@example.com'));

    const selects = screen.getAllByRole('combobox');
    await user.selectOptions(selects[0], 'Alpha');
    await user.click(screen.getByRole('button', { name: /^save$/i }));

    await waitFor(() => expect(screen.getByText(/failed to update role/i)).toBeInTheDocument());
  });

  it('shows an error message when user load fails', async () => {
    vi.mocked(api.getAdminUsers).mockRejectedValue(new Error('Network error'));
    renderPage();
    await waitFor(() => expect(screen.getByText(/failed to load users/i)).toBeInTheDocument());
  });

  // ── Feature Flags tab ───────────────────────────────────────────────────────

  async function switchToFlagsTab() {
    const user = userEvent.setup();
    renderPage();
    await waitFor(() => screen.getByText('alice@example.com'));
    await user.click(screen.getByRole('button', { name: /feature flags/i }));
    return user;
  }

  it('switches to Feature Flags tab and shows existing flag', async () => {
    await switchToFlagsTab();
    await waitFor(() => expect(screen.getByText('new.ui')).toBeInTheDocument());
    expect(screen.getByText('New UI')).toBeInTheDocument();
  });

  it('shows error when flag load fails', async () => {
    vi.mocked(api.getAdminFeatureFlags).mockRejectedValue(new Error('fail'));
    const user = userEvent.setup();
    renderPage();
    await waitFor(() => screen.getByText('alice@example.com'));
    await user.click(screen.getByRole('button', { name: /feature flags/i }));
    await waitFor(() =>
      expect(screen.getByText(/failed to load feature flags/i)).toBeInTheDocument()
    );
  });

  it('creates a new flag when the create form is submitted', async () => {
    const newFlag: FeatureFlag = {
      id: 2,
      key: 'dark.mode',
      name: 'Dark Mode',
      description: null,
      isEnabled: false,
      minimumRole: 'Standard',
      createdAt: '',
      updatedAt: '',
    };
    vi.mocked(api.createFeatureFlag).mockResolvedValue(newFlag);
    const user = await switchToFlagsTab();
    await waitFor(() => screen.getByText('new.ui'));

    const inputs = screen.getAllByRole('textbox');
    await user.type(inputs[0], 'dark.mode');
    await user.type(inputs[1], 'Dark Mode');

    await user.click(screen.getByRole('button', { name: /create flag/i }));

    await waitFor(() =>
      expect(vi.mocked(api.createFeatureFlag)).toHaveBeenCalledWith(
        expect.objectContaining({ key: 'dark.mode', name: 'Dark Mode' })
      )
    );
    await waitFor(() => expect(screen.getByText('dark.mode')).toBeInTheDocument());
  });

  it('shows create form error when createFeatureFlag fails', async () => {
    vi.mocked(api.createFeatureFlag).mockRejectedValue(new Error('Key exists'));
    const user = await switchToFlagsTab();
    await waitFor(() => screen.getByText('new.ui'));

    const inputs = screen.getAllByRole('textbox');
    await user.type(inputs[0], 'new.ui');
    await user.type(inputs[1], 'New UI');
    await user.click(screen.getByRole('button', { name: /create flag/i }));

    await waitFor(() => expect(screen.getByText('Key exists')).toBeInTheDocument());
  });

  it('enters inline edit mode when Edit is clicked', async () => {
    const user = await switchToFlagsTab();
    await waitFor(() => screen.getByText('new.ui'));

    await user.click(screen.getByRole('button', { name: /^edit$/i }));
    expect(screen.getByRole('button', { name: /^save$/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /cancel/i })).toBeInTheDocument();
  });

  it('cancels edit and restores normal row', async () => {
    const user = await switchToFlagsTab();
    await waitFor(() => screen.getByText('new.ui'));

    await user.click(screen.getByRole('button', { name: /^edit$/i }));
    await user.click(screen.getByRole('button', { name: /cancel/i }));

    expect(screen.queryByRole('button', { name: /cancel/i })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: /^edit$/i })).toBeInTheDocument();
  });

  it('saves updated flag via updateFeatureFlag', async () => {
    const updated = { ...mockFlag, name: 'Updated Name' };
    vi.mocked(api.updateFeatureFlag).mockResolvedValue(updated);
    const user = await switchToFlagsTab();
    await waitFor(() => screen.getByText('new.ui'));

    await user.click(screen.getByRole('button', { name: /^edit$/i }));
    const nameInput = screen
      .getAllByRole('textbox')
      .find(el => (el as HTMLInputElement).value === 'New UI')!;
    await user.clear(nameInput);
    await user.type(nameInput, 'Updated Name');

    await user.click(screen.getByRole('button', { name: /^save$/i }));
    await waitFor(() =>
      expect(vi.mocked(api.updateFeatureFlag)).toHaveBeenCalledWith(
        1,
        expect.objectContaining({ name: 'Updated Name' })
      )
    );
    await waitFor(() => expect(screen.getByText('Updated Name')).toBeInTheDocument());
  });

  it('deletes a flag when Delete is clicked and confirmed', async () => {
    vi.mocked(api.deleteFeatureFlag).mockResolvedValue();
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    const user = await switchToFlagsTab();
    await waitFor(() => screen.getByText('new.ui'));

    await user.click(screen.getByRole('button', { name: /^delete$/i }));
    await waitFor(() => expect(vi.mocked(api.deleteFeatureFlag)).toHaveBeenCalledWith(1));
    await waitFor(() => expect(screen.queryByText('new.ui')).not.toBeInTheDocument());
  });

  it('does not delete when confirm returns false', async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(false);
    const user = await switchToFlagsTab();
    await waitFor(() => screen.getByText('new.ui'));

    await user.click(screen.getByRole('button', { name: /^delete$/i }));
    expect(vi.mocked(api.deleteFeatureFlag)).not.toHaveBeenCalled();
    expect(screen.getByText('new.ui')).toBeInTheDocument();
  });
});
