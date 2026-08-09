import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router';
import { vi, describe, it, expect, beforeEach } from 'vitest';
import ResetPasswordPage from './ResetPasswordPage';
import { api } from '../../services/api';

vi.mock('../../services/api', async importOriginal => {
  const { mockApiModule } = await import('../../test/apiMock');
  return mockApiModule(importOriginal);
});

function renderPage(search = '?email=driver%40example.com&token=tok-1') {
  render(
    <MemoryRouter initialEntries={[`/reset-password${search}`]}>
      <ResetPasswordPage />
    </MemoryRouter>
  );
}

describe('ResetPasswordPage', () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  it('shows an invalid-link message when the token is missing', () => {
    renderPage('?email=driver%40example.com');
    expect(screen.getByText(/invalid or has expired/i)).toBeInTheDocument();
    expect(screen.queryByLabelText(/new password/i)).not.toBeInTheDocument();
  });

  it('renders the password fields when the link is valid', () => {
    renderPage();
    expect(screen.getByLabelText(/new password/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/confirm password/i)).toBeInTheDocument();
  });

  it('calls api.resetPassword with the email, token, and new password on submit', async () => {
    vi.mocked(api.resetPassword).mockResolvedValue(undefined);
    const user = userEvent.setup();
    renderPage();
    await user.type(screen.getByLabelText(/new password/i), 'NewPass99');
    await user.type(screen.getByLabelText(/confirm password/i), 'NewPass99');
    await user.click(screen.getByRole('button', { name: /reset password/i }));
    await waitFor(() => {
      expect(vi.mocked(api.resetPassword)).toHaveBeenCalledWith(
        'driver@example.com',
        'tok-1',
        'NewPass99'
      );
      expect(screen.getByText(/password has been reset/i)).toBeInTheDocument();
    });
  });

  it('shows a mismatch error without calling api when passwords differ', async () => {
    const user = userEvent.setup();
    renderPage();
    await user.type(screen.getByLabelText(/new password/i), 'NewPass99');
    await user.type(screen.getByLabelText(/confirm password/i), 'different');
    await user.click(screen.getByRole('button', { name: /reset password/i }));
    await waitFor(() => expect(screen.getByText(/do not match/i)).toBeInTheDocument());
    expect(vi.mocked(api.resetPassword)).not.toHaveBeenCalled();
  });

  it('shows a server error when the reset fails', async () => {
    vi.mocked(api.resetPassword).mockRejectedValue(new Error('Invalid token.'));
    const user = userEvent.setup();
    renderPage();
    await user.type(screen.getByLabelText(/new password/i), 'NewPass99');
    await user.type(screen.getByLabelText(/confirm password/i), 'NewPass99');
    await user.click(screen.getByRole('button', { name: /reset password/i }));
    await waitFor(() => expect(screen.getByText('Invalid token.')).toBeInTheDocument());
  });

  it('shows a fallback message when the reset rejects with a non-Error value', async () => {
    vi.mocked(api.resetPassword).mockRejectedValue('oops');
    const user = userEvent.setup();
    renderPage();
    await user.type(screen.getByLabelText(/new password/i), 'NewPass99');
    await user.type(screen.getByLabelText(/confirm password/i), 'NewPass99');
    await user.click(screen.getByRole('button', { name: /reset password/i }));
    await waitFor(() => expect(screen.getByText(/something went wrong/i)).toBeInTheDocument());
  });

  it('links to sign in after a successful reset', async () => {
    vi.mocked(api.resetPassword).mockResolvedValue(undefined);
    const user = userEvent.setup();
    renderPage();
    await user.type(screen.getByLabelText(/new password/i), 'NewPass99');
    await user.type(screen.getByLabelText(/confirm password/i), 'NewPass99');
    await user.click(screen.getByRole('button', { name: /reset password/i }));
    await waitFor(() =>
      expect(screen.getByRole('link', { name: /continue to sign in/i })).toHaveAttribute(
        'href',
        '/login'
      )
    );
  });
});
