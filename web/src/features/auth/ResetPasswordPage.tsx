import { useState } from 'react';
import { Link, useSearchParams } from 'react-router';
import { api } from '../../services/api';

export default function ResetPasswordPage() {
  const [params] = useSearchParams();
  const email = params.get('email') ?? '';
  const token = params.get('token') ?? '';
  const linkValid = email !== '' && token !== '';

  const [newPassword, setNewPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [done, setDone] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleSubmit(e: React.SubmitEvent<HTMLFormElement>) {
    e.preventDefault();
    setError(null);
    if (newPassword !== confirmPassword) {
      setError('Passwords do not match.');
      return;
    }
    setSubmitting(true);
    try {
      await api.resetPassword(email, token, newPassword);
      setDone(true);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Something went wrong. Please try again.');
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div className="bg-background text-on-background antialiased min-h-screen flex items-center justify-center p-4">
      <main className="relative z-10 w-full max-w-md bg-surface border border-line-2 rounded-xl shadow-2xl p-8 md:p-10">
        <h1 className="font-headline-md text-headline-md text-primary-fixed-dim font-extrabold tracking-tighter mb-2">
          Choose a New Password
        </h1>

        {!linkValid ? (
          <div className="space-y-6">
            <div className="p-4 bg-error-container rounded-lg font-body-sm text-body-sm text-on-error-container">
              This password reset link is invalid or has expired. Request a new one to continue.
            </div>
            <Link
              to="/forgot-password"
              className="block text-center w-full bg-primary-fixed-dim text-on-primary-fixed font-headline-sm text-headline-sm py-3 rounded-lg hover:bg-primary-fixed transition-all"
            >
              Request a new link
            </Link>
          </div>
        ) : done ? (
          <div className="space-y-6">
            <div className="p-4 bg-surface-container-high border border-line rounded-lg font-body-sm text-body-sm text-on-surface">
              Your password has been reset. You can now sign in with your new password.
            </div>
            <Link
              to="/login"
              className="block text-center w-full bg-primary-fixed-dim text-on-primary-fixed font-headline-sm text-headline-sm py-3 rounded-lg hover:bg-primary-fixed transition-all"
            >
              Continue to Sign In
            </Link>
          </div>
        ) : (
          <>
            <p className="font-body-sm text-body-sm text-on-surface-variant mb-8">
              Resetting the password for{' '}
              <span className="text-on-surface font-semibold">{email}</span>.
            </p>
            <form onSubmit={handleSubmit} className="space-y-5">
              {error && (
                <div className="p-3 bg-error-container rounded-lg font-body-sm text-body-sm text-on-error-container">
                  {error}
                </div>
              )}
              <div>
                <label
                  htmlFor="new-password"
                  className="block font-label-caps text-label-caps text-on-surface-variant mb-2"
                >
                  New Password
                </label>
                <input
                  id="new-password"
                  type="password"
                  required
                  value={newPassword}
                  onChange={e => setNewPassword(e.target.value)}
                  placeholder="••••••••"
                  className="w-full bg-surface-container-low border border-line-2 rounded-lg px-4 py-3 font-body-lg text-body-lg text-on-surface focus:outline-none focus:border-primary-fixed-dim focus:ring-1 focus:ring-primary-fixed-dim transition-all placeholder:text-on-surface-variant/50"
                />
              </div>
              <div>
                <label
                  htmlFor="confirm-password"
                  className="block font-label-caps text-label-caps text-on-surface-variant mb-2"
                >
                  Confirm Password
                </label>
                <input
                  id="confirm-password"
                  type="password"
                  required
                  value={confirmPassword}
                  onChange={e => setConfirmPassword(e.target.value)}
                  placeholder="••••••••"
                  className="w-full bg-surface-container-low border border-line-2 rounded-lg px-4 py-3 font-body-lg text-body-lg text-on-surface focus:outline-none focus:border-primary-fixed-dim focus:ring-1 focus:ring-primary-fixed-dim transition-all placeholder:text-on-surface-variant/50"
                />
              </div>
              <button
                type="submit"
                disabled={submitting}
                className="w-full bg-primary-fixed-dim text-on-primary-fixed font-headline-sm text-headline-sm py-3 rounded-lg hover:bg-primary-fixed transition-all disabled:opacity-60 disabled:cursor-not-allowed"
              >
                {submitting ? 'Please wait…' : 'Reset Password'}
              </button>
            </form>
            <div className="mt-8 text-center">
              <Link
                to="/login"
                className="font-body-sm text-body-sm text-secondary-fixed-dim hover:text-secondary-fixed transition-colors"
              >
                Back to Sign In
              </Link>
            </div>
          </>
        )}
      </main>
    </div>
  );
}
