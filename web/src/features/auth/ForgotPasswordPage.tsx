import { useState } from 'react';
import { Link } from 'react-router-dom';
import { api, type ForgotPasswordResult } from '../../services/api';

export default function ForgotPasswordPage() {
  const [email, setEmail] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [result, setResult] = useState<ForgotPasswordResult | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function handleSubmit(e: React.SubmitEvent<HTMLFormElement>) {
    e.preventDefault();
    setError(null);
    setSubmitting(true);
    try {
      setResult(await api.forgotPassword(email));
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Something went wrong. Please try again.');
    } finally {
      setSubmitting(false);
    }
  }

  const resetHref = result?.resetToken
    ? `/reset-password?email=${encodeURIComponent(email)}&token=${encodeURIComponent(result.resetToken)}`
    : null;

  return (
    <div className="bg-background text-on-background antialiased min-h-screen flex items-center justify-center p-4">
      <main className="relative z-10 w-full max-w-md bg-surface border border-line-2 rounded-xl shadow-2xl p-8 md:p-10">
        <h1 className="font-headline-md text-headline-md text-primary-fixed-dim font-extrabold tracking-tighter mb-2">
          Reset Password
        </h1>
        <p className="font-body-sm text-body-sm text-on-surface-variant mb-8">
          Enter the email associated with your account and we&rsquo;ll send you a link to reset your
          password.
        </p>

        {result ? (
          <div className="space-y-5">
            <div className="p-4 bg-surface-container-high border border-line rounded-lg font-body-sm text-body-sm text-on-surface">
              {result.message}
            </div>
            {resetHref && (
              <Link
                to={resetHref}
                className="block text-center w-full bg-primary-fixed-dim text-on-primary-fixed font-headline-sm text-headline-sm py-3 rounded-lg hover:bg-primary-fixed transition-all"
              >
                Continue to reset
              </Link>
            )}
          </div>
        ) : (
          <form onSubmit={handleSubmit} className="space-y-5">
            {error && (
              <div className="p-3 bg-error-container rounded-lg font-body-sm text-body-sm text-on-error-container">
                {error}
              </div>
            )}
            <div>
              <label
                htmlFor="email"
                className="block font-label-caps text-label-caps text-on-surface-variant mb-2"
              >
                Email Address
              </label>
              <input
                id="email"
                type="email"
                required
                value={email}
                onChange={e => setEmail(e.target.value)}
                placeholder="driver@example.com"
                className="w-full bg-surface-container-low border border-line-2 rounded-lg px-4 py-3 font-body-lg text-body-lg text-on-surface focus:outline-none focus:border-primary-fixed-dim focus:ring-1 focus:ring-primary-fixed-dim transition-all placeholder:text-on-surface-variant/50"
              />
            </div>
            <button
              type="submit"
              disabled={submitting}
              className="w-full bg-primary-fixed-dim text-on-primary-fixed font-headline-sm text-headline-sm py-3 rounded-lg hover:bg-primary-fixed transition-all disabled:opacity-60 disabled:cursor-not-allowed"
            >
              {submitting ? 'Please wait…' : 'Send Reset Link'}
            </button>
          </form>
        )}

        <div className="mt-8 text-center">
          <Link
            to="/login"
            className="font-body-sm text-body-sm text-secondary-fixed-dim hover:text-secondary-fixed transition-colors"
          >
            Back to Sign In
          </Link>
        </div>
      </main>
    </div>
  );
}
