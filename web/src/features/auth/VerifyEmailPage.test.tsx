import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import VerifyEmailPage from './VerifyEmailPage';
import { api } from '../../services/api';

vi.mock('../../services/api', () => ({ api: { confirmEmailChange: vi.fn() } }));

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
  });

  it('shows an error when confirmation fails', async () => {
    (api.confirmEmailChange as ReturnType<typeof vi.fn>).mockRejectedValue(new Error('expired'));
    renderAt('?userId=u1&email=new@example.com&token=bad');
    expect(await screen.findByText(/expired/i)).toBeInTheDocument();
  });
});
