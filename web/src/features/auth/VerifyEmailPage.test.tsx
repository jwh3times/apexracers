import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import VerifyEmailPage from './VerifyEmailPage';
import { api } from '../../services/api';

vi.mock('../../services/api', async importOriginal => {
  const { mockApiModule } = await import('../../test/apiMock');
  return mockApiModule(importOriginal);
});

function renderAt(search: string) {
  return render(
    <MemoryRouter initialEntries={[`/verify-email${search}`]}>
      <VerifyEmailPage />
    </MemoryRouter>
  );
}

describe('VerifyEmailPage', () => {
  beforeEach(() => vi.clearAllMocks());

  it('confirms the change and shows success', async () => {
    (api.confirmEmailChange as ReturnType<typeof vi.fn>).mockResolvedValue(undefined);
    renderAt('?userId=u1&email=new@example.com&token=tok');
    await waitFor(() =>
      expect(api.confirmEmailChange).toHaveBeenCalledWith('u1', 'new@example.com', 'tok')
    );
    expect(await screen.findByText(/email.*updated/i)).toBeInTheDocument();
  });

  it('shows an error for an invalid link (missing params)', () => {
    renderAt('');
    expect(screen.getByText(/invalid or has expired/i)).toBeInTheDocument();
    expect(api.confirmEmailChange).not.toHaveBeenCalled();
    expect(api.confirmEmail).not.toHaveBeenCalled();
  });

  it('shows an error when confirmation fails', async () => {
    (api.confirmEmailChange as ReturnType<typeof vi.fn>).mockRejectedValue(new Error('expired'));
    renderAt('?userId=u1&email=new@example.com&token=bad');
    expect(await screen.findByText(/expired/i)).toBeInTheDocument();
  });

  // ── New-account confirmation ─────────────────────────────────────────────
  //
  // The same page serves both emailed links. A change link names the address it is moving to; a
  // new-account link has no address to move, only the account it activates.

  it('confirms a new account when the link carries no target address', async () => {
    (api.confirmEmail as ReturnType<typeof vi.fn>).mockResolvedValue(undefined);
    renderAt('?userId=u1&token=tok');

    await waitFor(() => expect(api.confirmEmail).toHaveBeenCalledWith('u1', 'tok'));
    expect(api.confirmEmailChange).not.toHaveBeenCalled();
    expect(await screen.findByText(/your account is active/i)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /continue to sign in/i })).toBeInTheDocument();
  });

  it('titles itself for the account it is confirming, not for an email change', async () => {
    (api.confirmEmail as ReturnType<typeof vi.fn>).mockResolvedValue(undefined);
    renderAt('?userId=u1&token=tok');
    expect(
      await screen.findByRole('heading', { level: 1, name: /confirm your email/i })
    ).toBeInTheDocument();
  });

  it('sends a failed new-account confirmation back to sign in, not to settings', async () => {
    (api.confirmEmail as ReturnType<typeof vi.fn>).mockRejectedValue(new Error('link expired'));
    renderAt('?userId=u1&token=stale');

    expect(await screen.findByText(/link expired/i)).toBeInTheDocument();
    // Settings is behind the sign-in this person cannot complete yet.
    const back = screen.getByRole('link', { name: /back to sign in/i });
    expect(back).toHaveAttribute('href', '/login');
  });
});
