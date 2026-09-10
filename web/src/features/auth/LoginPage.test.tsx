import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { vi, describe, it, expect, beforeEach } from 'vitest';
import LoginPage from './LoginPage';
import { api } from '../../services/api';

const mockNavigate = vi.fn();
const mockLogin = vi.fn().mockResolvedValue(undefined);

vi.mock('react-router', async importOriginal => {
  const actual = await importOriginal<typeof import('react-router')>();
  return { ...actual, useNavigate: () => mockNavigate };
});

vi.mock('../../services/api', async importOriginal => {
  const { mockApiModule } = await import('../../test/apiMock');
  return mockApiModule(importOriginal);
});

vi.mock('../../context/AuthContext', () => ({
  useAuth: () => ({ login: mockLogin }),
}));

function renderPage() {
  render(<LoginPage />);
}

describe('LoginPage', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    mockLogin.mockResolvedValue(undefined);
  });

  it('renders Sign In tab active by default with submit button', () => {
    renderPage();
    expect(screen.getByRole('tab', { name: 'Sign In' })).toHaveAttribute('aria-selected', 'true');
    expect(screen.getByRole('button', { name: /access telemetry/i })).toBeInTheDocument();
  });

  it('iRacing OAuth button is disabled', () => {
    renderPage();
    expect(screen.getByRole('button', { name: /sign in with iracing/i })).toBeDisabled();
  });

  it('shows Forgot Password button on sign in tab but not on register tab', async () => {
    const user = userEvent.setup();
    renderPage();
    expect(screen.getByRole('button', { name: /forgot password/i })).toBeInTheDocument();
    await user.click(screen.getByRole('tab', { name: 'Create Account' }));
    expect(screen.queryByRole('button', { name: /forgot password/i })).not.toBeInTheDocument();
  });

  it('navigates to the forgot-password page when Forgot Password is clicked', async () => {
    const user = userEvent.setup();
    renderPage();
    await user.click(screen.getByRole('button', { name: /forgot password/i }));
    expect(mockNavigate).toHaveBeenCalledWith('/forgot-password');
  });

  it('switching to Create Account tab shows confirm password field and hides it when switching back', async () => {
    const user = userEvent.setup();
    renderPage();
    expect(screen.queryByLabelText(/confirm password/i)).not.toBeInTheDocument();
    await user.click(screen.getByRole('tab', { name: 'Create Account' }));
    expect(screen.getByLabelText(/confirm password/i)).toBeInTheDocument();
    await user.click(screen.getByRole('tab', { name: 'Sign In' }));
    expect(screen.queryByLabelText(/confirm password/i)).not.toBeInTheDocument();
  });

  it('clears error and form fields when switching tabs', async () => {
    const user = userEvent.setup();
    vi.mocked(api.login).mockRejectedValue(new Error('Bad credentials'));
    renderPage();
    await user.type(screen.getByLabelText(/email address/i), 'a@b.com');
    await user.type(screen.getByLabelText(/^password$/i), 'wrongpass');
    await user.click(screen.getByRole('button', { name: /access telemetry/i }));
    await waitFor(() => expect(screen.getByText(/bad credentials/i)).toBeInTheDocument());
    await user.click(screen.getByRole('tab', { name: 'Create Account' }));
    expect(screen.queryByText(/bad credentials/i)).not.toBeInTheDocument();
    expect(screen.getByLabelText(/email address/i)).toHaveValue('');
  });

  it('toggles password input type when visibility button is clicked', async () => {
    const user = userEvent.setup();
    renderPage();
    const passwordInput = screen.getByLabelText(/^password$/i);
    expect(passwordInput).toHaveAttribute('type', 'password');
    const visibilityBtn = passwordInput.parentElement!.querySelector('button') as HTMLButtonElement;
    await user.click(visibilityBtn);
    expect(passwordInput).toHaveAttribute('type', 'text');
    await user.click(visibilityBtn);
    expect(passwordInput).toHaveAttribute('type', 'password');
  });

  it('calls auth.login and navigates to dashboard on successful sign in', async () => {
    const user = userEvent.setup();
    vi.mocked(api.login).mockResolvedValue({
      token: 'jwt-abc',
      userId: 'u1',
      displayName: 'Jerry',
    });
    renderPage();
    await user.type(screen.getByLabelText(/email address/i), 'jerry@example.com');
    await user.type(screen.getByLabelText(/^password$/i), 'mypassword');
    await user.click(screen.getByRole('button', { name: /access telemetry/i }));
    await waitFor(() => {
      expect(vi.mocked(api.login)).toHaveBeenCalledWith('jerry@example.com', 'mypassword');
      expect(mockLogin).toHaveBeenCalledWith(
        { token: 'jwt-abc', userId: 'u1', displayName: 'Jerry' },
        'jerry@example.com'
      );
      expect(mockNavigate).toHaveBeenCalledWith('/dashboard');
    });
  });

  it('shows error message and does not navigate on failed sign in', async () => {
    const user = userEvent.setup();
    vi.mocked(api.login).mockRejectedValue(new Error('Invalid credentials'));
    renderPage();
    await user.type(screen.getByLabelText(/email address/i), 'bad@example.com');
    await user.type(screen.getByLabelText(/^password$/i), 'wrongpass');
    await user.click(screen.getByRole('button', { name: /access telemetry/i }));
    await waitFor(() => expect(screen.getByText(/invalid credentials/i)).toBeInTheDocument());
    expect(mockNavigate).not.toHaveBeenCalled();
    expect(mockLogin).not.toHaveBeenCalled();
  });

  it('shows loading state while sign in is in progress', async () => {
    const user = userEvent.setup();
    vi.mocked(api.login).mockReturnValue(new Promise(() => {}));
    renderPage();
    await user.type(screen.getByLabelText(/email address/i), 'a@b.com');
    await user.type(screen.getByLabelText(/^password$/i), 'pass');
    await user.click(screen.getByRole('button', { name: /access telemetry/i }));
    expect(screen.getByRole('button', { name: /please wait/i })).toBeInTheDocument();
  });

  it('shows the acknowledgement and returns to sign in after registering, without signing in', async () => {
    const user = userEvent.setup();
    vi.mocked(api.register).mockResolvedValue({
      message: 'If that address can be registered, a confirmation link has been sent to it.',
    });
    renderPage();
    await user.click(screen.getByRole('tab', { name: 'Create Account' }));
    await user.type(screen.getByLabelText(/email address/i), 'new@example.com');
    await user.type(screen.getByLabelText(/^password$/i), 'secret123');
    await user.type(screen.getByLabelText(/confirm password/i), 'secret123');
    await user.click(screen.getByRole('button', { name: /^create account$/i }));

    await waitFor(() =>
      expect(screen.getByRole('status')).toHaveTextContent(/confirmation link has been sent/i)
    );
    expect(vi.mocked(api.register)).toHaveBeenCalledWith('new@example.com', 'secret123');
    // Registration hands back no token, so nothing can sign in here — the account is not usable
    // until the emailed link is followed.
    expect(mockLogin).not.toHaveBeenCalled();
    expect(mockNavigate).not.toHaveBeenCalled();
    expect(screen.getByRole('tab', { name: 'Sign In' })).toHaveAttribute('aria-selected', 'true');
  });

  it('keeps the email filled in after registering so sign in is one password away', async () => {
    const user = userEvent.setup();
    vi.mocked(api.register).mockResolvedValue({ message: 'Check your email.' });
    renderPage();
    await user.click(screen.getByRole('tab', { name: 'Create Account' }));
    await user.type(screen.getByLabelText(/email address/i), 'new@example.com');
    await user.type(screen.getByLabelText(/^password$/i), 'secret123');
    await user.type(screen.getByLabelText(/confirm password/i), 'secret123');
    await user.click(screen.getByRole('button', { name: /^create account$/i }));

    await waitFor(() => expect(screen.getByRole('status')).toBeInTheDocument());
    expect(screen.getByLabelText(/email address/i)).toHaveValue('new@example.com');
    expect(screen.getByLabelText(/^password$/i)).toHaveValue('');
  });

  it('shows password mismatch error without calling api.register', async () => {
    const user = userEvent.setup();
    renderPage();
    await user.click(screen.getByRole('tab', { name: 'Create Account' }));
    await user.type(screen.getByLabelText(/email address/i), 'a@b.com');
    await user.type(screen.getByLabelText(/^password$/i), 'pass1');
    await user.type(screen.getByLabelText(/confirm password/i), 'pass2');
    await user.click(screen.getByRole('button', { name: /^create account$/i }));
    await waitFor(() => expect(screen.getByText(/passwords do not match/i)).toBeInTheDocument());
    expect(vi.mocked(api.register)).not.toHaveBeenCalled();
  });

  it('shows error message on failed registration', async () => {
    const user = userEvent.setup();
    vi.mocked(api.register).mockRejectedValue(new Error('Passwords must have at least one digit.'));
    renderPage();
    await user.click(screen.getByRole('tab', { name: 'Create Account' }));
    await user.type(screen.getByLabelText(/email address/i), 'dup@example.com');
    await user.type(screen.getByLabelText(/^password$/i), 'secret');
    await user.type(screen.getByLabelText(/confirm password/i), 'secret');
    await user.click(screen.getByRole('button', { name: /^create account$/i }));
    await waitFor(() =>
      expect(screen.getByText(/must have at least one digit/i)).toBeInTheDocument()
    );
  });

  it('hints about email confirmation on a failed sign in, for every account alike', async () => {
    const user = userEvent.setup();
    vi.mocked(api.login).mockRejectedValue(new Error('Invalid email or password.'));
    renderPage();
    await user.type(screen.getByLabelText(/email address/i), 'a@b.com');
    await user.type(screen.getByLabelText(/^password$/i), 'nope');
    await user.click(screen.getByRole('button', { name: /access telemetry/i }));

    // Unconditional: a hint shown only for an unconfirmed account would hand back exactly the
    // answer the generic failure withholds.
    await waitFor(() =>
      expect(screen.getByText(/unconfirmed account can't sign in/i)).toBeInTheDocument()
    );
  });

  it('shows loading state while registration is in progress', async () => {
    const user = userEvent.setup();
    vi.mocked(api.register).mockReturnValue(new Promise(() => {}));
    renderPage();
    await user.click(screen.getByRole('tab', { name: 'Create Account' }));
    await user.type(screen.getByLabelText(/email address/i), 'a@b.com');
    await user.type(screen.getByLabelText(/^password$/i), 'pass');
    await user.type(screen.getByLabelText(/confirm password/i), 'pass');
    await user.click(screen.getByRole('button', { name: /^create account$/i }));
    expect(screen.getByRole('button', { name: /please wait/i })).toBeInTheDocument();
  });
});
