import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router';
import { vi, describe, it, expect, beforeEach } from 'vitest';
import ForgotPasswordPage from './ForgotPasswordPage';
import { api } from '../../services/api';

vi.mock('../../services/api', () => ({
  api: { forgotPassword: vi.fn() },
}));

function renderPage() {
  render(
    <MemoryRouter>
      <ForgotPasswordPage />
    </MemoryRouter>
  );
}

describe('ForgotPasswordPage', () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  it('renders the email field and submit button', () => {
    renderPage();
    expect(screen.getByLabelText(/email address/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /send reset link/i })).toBeInTheDocument();
  });

  it('calls api.forgotPassword and shows the acknowledgement on submit', async () => {
    vi.mocked(api.forgotPassword).mockResolvedValue({
      message: 'If an account exists, a reset link was sent.',
      resetToken: null,
    });
    const user = userEvent.setup();
    renderPage();
    await user.type(screen.getByLabelText(/email address/i), 'driver@example.com');
    await user.click(screen.getByRole('button', { name: /send reset link/i }));
    await waitFor(() => {
      expect(vi.mocked(api.forgotPassword)).toHaveBeenCalledWith('driver@example.com');
      expect(screen.getByText(/if an account exists/i)).toBeInTheDocument();
    });
  });

  it('shows a dev reset link when the response includes a reset token', async () => {
    vi.mocked(api.forgotPassword).mockResolvedValue({
      message: 'Sent.',
      resetToken: 'dev-token-xyz',
    });
    const user = userEvent.setup();
    renderPage();
    await user.type(screen.getByLabelText(/email address/i), 'driver@example.com');
    await user.click(screen.getByRole('button', { name: /send reset link/i }));
    await waitFor(() => {
      const link = screen.getByRole('link', { name: /continue to reset/i });
      expect(link).toHaveAttribute('href', expect.stringContaining('token=dev-token-xyz'));
      expect(link).toHaveAttribute('href', expect.stringContaining('email=driver%40example.com'));
    });
  });

  it('does not show a reset link when no token is returned', async () => {
    vi.mocked(api.forgotPassword).mockResolvedValue({ message: 'Sent.', resetToken: null });
    const user = userEvent.setup();
    renderPage();
    await user.type(screen.getByLabelText(/email address/i), 'driver@example.com');
    await user.click(screen.getByRole('button', { name: /send reset link/i }));
    await waitFor(() => expect(screen.getByText('Sent.')).toBeInTheDocument());
    expect(screen.queryByRole('link', { name: /continue to reset/i })).not.toBeInTheDocument();
  });

  it('shows an error message when the request fails', async () => {
    vi.mocked(api.forgotPassword).mockRejectedValue(new Error('Server unavailable.'));
    const user = userEvent.setup();
    renderPage();
    await user.type(screen.getByLabelText(/email address/i), 'driver@example.com');
    await user.click(screen.getByRole('button', { name: /send reset link/i }));
    await waitFor(() => expect(screen.getByText('Server unavailable.')).toBeInTheDocument());
  });

  it('shows a fallback message when the request rejects with a non-Error value', async () => {
    vi.mocked(api.forgotPassword).mockRejectedValue('oops');
    const user = userEvent.setup();
    renderPage();
    await user.type(screen.getByLabelText(/email address/i), 'driver@example.com');
    await user.click(screen.getByRole('button', { name: /send reset link/i }));
    await waitFor(() => expect(screen.getByText(/something went wrong/i)).toBeInTheDocument());
  });

  it('has a link back to sign in', () => {
    renderPage();
    expect(screen.getByRole('link', { name: /back to sign in/i })).toHaveAttribute(
      'href',
      '/login'
    );
  });
});
