import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, it, expect, beforeEach } from 'vitest';
import ProfilePage from '../ProfilePage';

function renderPage() {
  return render(<ProfilePage />);
}

describe('ProfilePage', () => {
  beforeEach(() => {
    localStorage.clear();
  });

  it('renders the Account Settings heading', () => {
    renderPage();
    expect(screen.getByRole('heading', { name: /account settings/i })).toBeInTheDocument();
  });

  it('shows empty display name field when localStorage has no stored name', () => {
    renderPage();
    expect(screen.getByLabelText(/display name/i)).toHaveValue('');
  });

  it('pre-fills display name from localStorage', () => {
    localStorage.setItem('ar_display_name', 'Jerry Holland');
    renderPage();
    expect(screen.getByLabelText(/display name/i)).toHaveValue('Jerry Holland');
  });

  it('saves display name to localStorage when Save Changes is clicked', async () => {
    const user = userEvent.setup();
    renderPage();
    const input = screen.getByLabelText(/display name/i);
    await user.clear(input);
    await user.type(input, 'Speed Demon');
    await user.click(screen.getByRole('button', { name: /save changes/i }));
    expect(localStorage.getItem('ar_display_name')).toBe('Speed Demon');
  });

  it('renders the Connections section with iRacing label', () => {
    renderPage();
    expect(screen.getByText(/connections/i)).toBeInTheDocument();
    expect(screen.getByText(/iracing account/i)).toBeInTheDocument();
  });

  it('shows Not connected when no ar_token in localStorage', () => {
    renderPage();
    expect(screen.getByText(/not connected/i)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /disconnect/i })).not.toBeInTheDocument();
  });

  it('shows Connected and Disconnect button when ar_token is present', () => {
    localStorage.setItem('ar_token', 'some.jwt.token');
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

  it('toggles the alerts preference', async () => {
    const user = userEvent.setup();
    renderPage();
    const toggle = screen.getByRole('checkbox');
    // default is on (ar_alerts not set → !== 'false' → true)
    expect(toggle).toBeChecked();
    await user.click(toggle);
    expect(toggle).not.toBeChecked();
    expect(localStorage.getItem('ar_alerts')).toBe('false');
  });
});
