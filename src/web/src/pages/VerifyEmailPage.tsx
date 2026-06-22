import { useEffect, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { api } from '../services/api';

export default function VerifyEmailPage() {
  const [params] = useSearchParams();
  const userId = params.get('userId') ?? '';
  const email = params.get('email') ?? '';
  const token = params.get('token') ?? '';
  const linkValid = userId !== '' && email !== '' && token !== '';

  const [status, setStatus] = useState<'pending' | 'done' | 'error'>(
    linkValid ? 'pending' : 'error'
  );
  const [error, setError] = useState<string | null>(
    linkValid ? null : 'This email verification link is invalid or has expired.'
  );

  useEffect(() => {
    if (!linkValid) return;
    let cancelled = false;
    (async () => {
      try {
        await api.confirmEmailChange(userId, email, token);
        if (!cancelled) setStatus('done');
      } catch (err) {
        if (!cancelled) {
          setError(err instanceof Error ? err.message : 'This link is invalid or has expired.');
          setStatus('error');
        }
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [linkValid, userId, email, token]);

  return (
    <div className="bg-background text-on-background antialiased min-h-screen flex items-center justify-center p-4">
      <main className="relative z-10 w-full max-w-md bg-surface border border-line-2 rounded-xl shadow-2xl p-8 md:p-10">
        <h1 className="font-headline-md text-headline-md text-primary-fixed-dim font-extrabold tracking-tighter mb-2">
          Confirm Email Change
        </h1>

        {status === 'pending' ? (
          <p className="font-body-sm text-body-sm text-on-surface-variant mt-4">
            Confirming your new email…
          </p>
        ) : status === 'done' ? (
          <div className="space-y-6 mt-4">
            <div className="p-4 bg-surface-container-high border border-line rounded-lg font-body-sm text-body-sm text-on-surface">
              Your account email has been updated to{' '}
              <span className="text-on-surface font-semibold">{email}</span>. For security,
              you&apos;ve been signed out everywhere — please sign in again.
            </div>
            <Link
              to="/login"
              className="block text-center w-full bg-primary-fixed-dim text-on-primary-fixed font-headline-sm text-headline-sm py-3 rounded-lg hover:bg-primary-fixed transition-all"
            >
              Continue to Sign In
            </Link>
          </div>
        ) : (
          <div className="space-y-6 mt-4">
            <div className="p-4 bg-error-container rounded-lg font-body-sm text-body-sm text-on-error-container">
              {error}
            </div>
            <Link
              to="/settings"
              className="block text-center w-full bg-primary-fixed-dim text-on-primary-fixed font-headline-sm text-headline-sm py-3 rounded-lg hover:bg-primary-fixed transition-all"
            >
              Back to Settings
            </Link>
          </div>
        )}
      </main>
    </div>
  );
}
