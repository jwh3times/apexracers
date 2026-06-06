import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { api } from '../services/api';
import { useAuth } from '../context/AuthContext';

type Tab = 'signin' | 'register';

const BG_STEERING_WHEEL =
  'https://lh3.googleusercontent.com/aida-public/AB6AXuCfE9XCfxUwXzjRnyo5pyQD6yht0Pg4nfW7OKx6_iNRwee9CUFw4IH2LB0vkFYo9b8tGvlZaf8SKrjwQzMkIFBv_UlGfxoanNFNB7qqSJ48aOOoFJT1P5iRvjNt3RhFqAIhy8BcuMGxvyH2NpLRgbvdROuFtJUIznyucti-mJpCAJ2TWfDsKM89Q2RQU4-R1FsE9PL6KPdJ_SrlzJQ1kB5wWI3g-uIHKFShhheb3U3U-Vku1N0DTBhHTGAW5xoCglAYnFxHxi13Ivc';

const BG_TELEMETRY =
  'https://lh3.googleusercontent.com/aida-public/AB6AXuCI4n2qoVOlelbevOQIuuMo6Z-2UAqRv123lnT9qZIi59i7ZB7JIbUW34m4Jk9zhY-UGJVp_S7REUYa_Y1kxF-zwjm7gCWVUNPdhtSvxmKJxGZcs91vdQtokzja3NLAfFADjKpsyttIu5kJIk3ih4dbuZb6w-p9PytVnBaWiI5DFzZAiOfFIqQA9puG7pANcNF3Iw28VtL5sH_znf2g34Pd2mKW6TCSAEwXdOmukWgSHkYgbVZ-czCvBuyr6UwbXIjXwlNt5JMu-cg';

export default function LoginPage() {
  const [tab, setTab] = useState<Tab>('signin');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [showPassword, setShowPassword] = useState(false);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const navigate = useNavigate();
  const { login } = useAuth();

  function switchTab(next: Tab) {
    setTab(next);
    setError(null);
    setEmail('');
    setPassword('');
    setConfirmPassword('');
  }

  async function handleSubmit(e: React.SubmitEvent<HTMLFormElement>) {
    e.preventDefault();
    setError(null);
    if (tab === 'register' && password !== confirmPassword) {
      setError('Passwords do not match.');
      return;
    }
    setLoading(true);
    try {
      const result =
        tab === 'signin' ? await api.login(email, password) : await api.register(email, password);
      await login(result, email);
      navigate('/dashboard');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Authentication failed.');
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="bg-background text-on-background antialiased min-h-screen relative overflow-hidden flex items-center justify-center p-4 md:p-margin">
      {/* Atmospheric background image */}
      <div
        className="absolute inset-0 z-0 bg-cover bg-center opacity-30"
        style={{ backgroundImage: `url('${BG_STEERING_WHEEL}')` }}
      />
      {/* Gradient overlay */}
      <div className="absolute inset-0 z-0 bg-gradient-to-t from-background via-background/80 to-background/40" />

      <main className="relative z-10 w-full max-w-5xl bg-surface/80 backdrop-blur-xl border border-line-2 rounded-xl shadow-2xl flex flex-col md:flex-row overflow-hidden">
        {/* ── Left panel: branding ── */}
        <div className="hidden md:flex md:w-5/12 bg-surface-container-highest relative flex-col justify-between p-12 overflow-hidden border-r border-line">
          <div
            className="absolute inset-0 opacity-20 pointer-events-none bg-cover bg-center"
            style={{ backgroundImage: `url('${BG_TELEMETRY}')` }}
          />
          <div className="relative z-10">
            <h1 className="font-display-lg text-display-lg text-primary-fixed-dim font-extrabold tracking-tighter mb-2">
              ApexRacers
            </h1>
            <p className="font-headline-sm text-headline-sm text-on-surface-variant max-w-[250px]">
              Elite Telemetry &amp; Performance Analytics.
            </p>
          </div>
          <div className="relative z-10 mt-auto">
            <div className="flex items-center gap-3 text-secondary-fixed-dim mb-4">
              <span className="material-symbols-outlined fill">speed</span>
              <span className="font-label-caps text-label-caps tracking-widest">System Online</span>
            </div>
            <div className="h-1 w-full bg-surface-container-low rounded-full overflow-hidden">
              <div className="h-full bg-primary-fixed-dim w-3/4 shadow-[0_0_10px_rgba(0,228,121,0.5)]" />
            </div>
          </div>
        </div>

        {/* ── Right panel: auth form ── */}
        <div className="w-full md:w-7/12 p-8 md:p-12 flex flex-col justify-center bg-surface">
          {/* Mobile header */}
          <div className="md:hidden mb-8 text-center">
            <h1 className="font-headline-md text-headline-md text-primary-fixed-dim font-extrabold tracking-tighter">
              ApexRacers
            </h1>
          </div>

          {/* Tabs */}
          <div className="flex border-b border-line-2 mb-8" role="tablist">
            {(['signin', 'register'] as const).map(t => (
              <button
                key={t}
                role="tab"
                aria-selected={tab === t}
                onClick={() => switchTab(t)}
                className={`flex-1 pb-4 text-center border-b-2 font-label-caps text-label-caps transition-colors ${
                  tab === t
                    ? 'border-primary-fixed-dim text-primary-fixed-dim'
                    : 'border-transparent text-on-surface-variant hover:text-on-surface'
                }`}
              >
                {t === 'signin' ? 'Sign In' : 'Create Account'}
              </button>
            ))}
          </div>

          <div className="max-w-md w-full mx-auto">
            {/* iRacing OAuth — blocked until client registration completes */}
            <button
              disabled
              title="iRacing OAuth coming soon"
              className="w-full bg-gradient-to-r from-surface-container-high to-surface-container-lowest border border-secondary-container/40 text-on-surface flex items-center justify-center gap-4 py-4 rounded-lg opacity-50 cursor-not-allowed mb-6"
            >
              <span className="material-symbols-outlined text-secondary-fixed-dim">
                sports_esports
              </span>
              <span className="font-body-lg text-body-lg font-semibold tracking-wide">
                {tab === 'signin' ? 'Sign in' : 'Sign up'} with iRacing (Coming Soon)
              </span>
            </button>

            {/* Divider */}
            <div className="flex items-center gap-4 mb-6">
              <div className="h-px bg-surface-container-high flex-1" />
              <span className="font-label-caps text-label-caps text-on-surface-variant">OR</span>
              <div className="h-px bg-surface-container-high flex-1" />
            </div>

            {/* Error banner */}
            {error && (
              <div className="mb-5 p-3 bg-error-container rounded-lg font-body-sm text-body-sm text-on-error-container">
                {error}
              </div>
            )}

            <form onSubmit={handleSubmit} className="space-y-5">
              {/* Email */}
              <div>
                <label
                  htmlFor="email"
                  className="block font-label-caps text-label-caps text-on-surface-variant mb-2"
                >
                  Email Address
                </label>
                <div className="relative">
                  <span className="material-symbols-outlined absolute left-4 top-1/2 -translate-y-1/2 text-on-surface-variant">
                    mail
                  </span>
                  <input
                    id="email"
                    type="email"
                    required
                    value={email}
                    onChange={e => setEmail(e.target.value)}
                    placeholder="driver@example.com"
                    className="w-full bg-surface-container-low border border-line-2 rounded-lg pl-12 pr-4 py-3 font-body-lg text-body-lg text-on-surface focus:outline-none focus:border-primary-fixed-dim focus:ring-1 focus:ring-primary-fixed-dim transition-all placeholder:text-on-surface-variant/50"
                  />
                </div>
              </div>

              {/* Password */}
              <div>
                <div className="flex justify-between items-center mb-2">
                  <label
                    htmlFor="password"
                    className="font-label-caps text-label-caps text-on-surface-variant"
                  >
                    Password
                  </label>
                  {tab === 'signin' && (
                    <button
                      type="button"
                      className="font-body-sm text-body-sm text-secondary-fixed-dim hover:text-secondary-fixed transition-colors"
                    >
                      Forgot Password?
                    </button>
                  )}
                </div>
                <div className="relative">
                  <span className="material-symbols-outlined absolute left-4 top-1/2 -translate-y-1/2 text-on-surface-variant">
                    lock
                  </span>
                  <input
                    id="password"
                    type={showPassword ? 'text' : 'password'}
                    required
                    value={password}
                    onChange={e => setPassword(e.target.value)}
                    placeholder="••••••••"
                    className="w-full bg-surface-container-low border border-line-2 rounded-lg pl-12 pr-12 py-3 font-body-lg text-body-lg text-on-surface focus:outline-none focus:border-primary-fixed-dim focus:ring-1 focus:ring-primary-fixed-dim transition-all placeholder:text-on-surface-variant/50"
                  />
                  <button
                    type="button"
                    onClick={() => setShowPassword(v => !v)}
                    className="absolute right-4 top-1/2 -translate-y-1/2 text-on-surface-variant hover:text-on-surface transition-colors"
                  >
                    <span className="material-symbols-outlined text-[20px]">
                      {showPassword ? 'visibility' : 'visibility_off'}
                    </span>
                  </button>
                </div>
              </div>

              {/* Confirm password (register only) */}
              {tab === 'register' && (
                <div>
                  <label
                    htmlFor="confirm-password"
                    className="block font-label-caps text-label-caps text-on-surface-variant mb-2"
                  >
                    Confirm Password
                  </label>
                  <div className="relative">
                    <span className="material-symbols-outlined absolute left-4 top-1/2 -translate-y-1/2 text-on-surface-variant">
                      lock
                    </span>
                    <input
                      id="confirm-password"
                      type={showPassword ? 'text' : 'password'}
                      required
                      value={confirmPassword}
                      onChange={e => setConfirmPassword(e.target.value)}
                      placeholder="••••••••"
                      className="w-full bg-surface-container-low border border-line-2 rounded-lg pl-12 pr-4 py-3 font-body-lg text-body-lg text-on-surface focus:outline-none focus:border-primary-fixed-dim focus:ring-1 focus:ring-primary-fixed-dim transition-all placeholder:text-on-surface-variant/50"
                    />
                  </div>
                </div>
              )}

              {/* Submit */}
              <button
                type="submit"
                disabled={loading}
                className="w-full bg-primary-fixed-dim text-on-primary-fixed font-headline-sm text-headline-sm py-4 rounded-lg hover:bg-primary-fixed transition-all hover:shadow-[0_0_20px_rgba(0,228,121,0.3)] active:scale-[0.98] mt-4 disabled:opacity-60 disabled:cursor-not-allowed"
              >
                {loading
                  ? 'Please wait…'
                  : tab === 'signin'
                    ? 'Access Telemetry'
                    : 'Create Account'}
              </button>
            </form>
          </div>
        </div>
      </main>
    </div>
  );
}
