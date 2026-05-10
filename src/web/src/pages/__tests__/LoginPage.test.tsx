import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { vi, describe, it, expect, beforeEach } from 'vitest';
import LoginPage from '../LoginPage';
import { api } from '../../services/api';

const mockNavigate = vi.fn();

vi.mock('react-router-dom', async (importOriginal) => {
  const actual = await importOriginal<typeof import('react-router-dom')>();
  return { ...actual, useNavigate: () => mockNavigate };
});

vi.mock('../../services/api', () => ({
  api: { login: vi.fn(), register: vi.fn() },
}));

function renderPage() {
  render(<LoginPage />);
}

describe('LoginPage', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    localStorage.clear();
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
    // The visibility toggle button contains a material symbol span — find it by its container position
    const visibilityBtn = passwordInput.parentElement!.querySelector('button') as HTMLButtonElement;
    await user.click(visibilityBtn);
    expect(passwordInput).toHaveAttribute('type', 'text');
    await user.click(visibilityBtn);
    expect(passwordInput).toHaveAttribute('type', 'password');
  });

  it('calls api.login, stores token in localStorage, and navigates on successful sign in', async () => {
    const user = userEvent.setup();
    vi.mocked(api.login).mockResolvedValue({ token: 'jwt-abc', userId: 'u1', displayName: 'Jerry' });
    renderPage();
    await user.type(screen.getByLabelText(/email address/i), 'jerry@example.com');
    await user.type(screen.getByLabelText(/^password$/i), 'mypassword');
    await user.click(screen.getByRole('button', { name: /access telemetry/i }));
    await waitFor(() => {
      expect(vi.mocked(api.login)).toHaveBeenCalledWith('jerry@example.com', 'mypassword');
      expect(localStorage.getItem('ar_token')).toBe('jwt-abc');
      expect(mockNavigate).toHaveBeenCalledWith('/');
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
    expect(localStorage.getItem('ar_token')).toBeNull();
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

  it('calls api.register, stores token, and navigates on successful registration', async () => {
    const user = userEvent.setup();
    vi.mocked(api.register).mockResolvedValue({ token: 'jwt-xyz', userId: 'u2', displayName: 'New User' });
    renderPage();
    await user.click(screen.getByRole('tab', { name: 'Create Account' }));
    await user.type(screen.getByLabelText(/email address/i), 'new@example.com');
    await user.type(screen.getByLabelText(/^password$/i), 'secret123');
    await user.type(screen.getByLabelText(/confirm password/i), 'secret123');
    await user.click(screen.getByRole('button', { name: /^create account$/i }));
    await waitFor(() => {
      expect(vi.mocked(api.register)).toHaveBeenCalledWith('new@example.com', 'secret123');
      expect(localStorage.getItem('ar_token')).toBe('jwt-xyz');
      expect(mockNavigate).toHaveBeenCalledWith('/');
    });
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
    vi.mocked(api.register).mockRejectedValue(new Error('Email already taken'));
    renderPage();
    await user.click(screen.getByRole('tab', { name: 'Create Account' }));
    await user.type(screen.getByLabelText(/email address/i), 'dup@example.com');
    await user.type(screen.getByLabelText(/^password$/i), 'secret');
    await user.type(screen.getByLabelText(/confirm password/i), 'secret');
    await user.click(screen.getByRole('button', { name: /^create account$/i }));
    await waitFor(() => expect(screen.getByText(/email already taken/i)).toBeInTheDocument());
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
