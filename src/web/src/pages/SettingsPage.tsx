import { useState } from 'react';
import { api } from '../services/api';
import { useAuth } from '../context/AuthContext';
import { useTheme } from '../context/ThemeContext';
import type { ThemePreference } from '../context/ThemeContext';

const TIER_DESCRIPTIONS: Record<string, string> = {
  Standard: 'Default access. New features go here once stable.',
  Beta: 'Early access to features that are close to done. Some rough edges possible.',
  Alpha: 'Cutting-edge features that may be incomplete or change without notice.',
  Admin: 'Administrator. Manage users and feature flags via the Admin Panel.',
};

export default function SettingsPage() {
  const { user, logout, updateSession, alertsEnabled, setAlertsEnabled } = useAuth();
  const { theme, setTheme } = useTheme();

  const [displayName, setDisplayName] = useState(user?.displayName ?? '');
  const [email, setEmail] = useState(user?.email ?? '');
  const [iRacingCustomerId, setIRacingCustomerId] = useState(
    user?.iRacingCustomerId?.toString() ?? ''
  );
  const [currentPassword, setCurrentPassword] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [profileSaved, setProfileSaved] = useState(false);
  const [profileError, setProfileError] = useState<string | null>(null);
  const [profileSaving, setProfileSaving] = useState(false);

  const [roleSaving, setRoleSaving] = useState(false);
  const [roleSaved, setRoleSaved] = useState(false);
  const [roleError, setRoleError] = useState<string | null>(null);

  const [pwSaving, setPwSaving] = useState(false);
  const [pwSaved, setPwSaved] = useState(false);
  const [pwError, setPwError] = useState<string | null>(null);

  const connected = !!user;

  async function changePassword(e: React.SubmitEvent<HTMLFormElement>) {
    e.preventDefault();
    setPwError(null);
    if (newPassword !== confirmPassword) {
      setPwError('New passwords do not match.');
      return;
    }
    setPwSaving(true);
    try {
      await api.changePassword(currentPassword, newPassword);
      setCurrentPassword('');
      setNewPassword('');
      setConfirmPassword('');
      setPwSaved(true);
      setTimeout(() => setPwSaved(false), 2500);
    } catch (err) {
      setPwError(err instanceof Error ? err.message : 'Failed to change password.');
    } finally {
      setPwSaving(false);
    }
  }

  async function saveProfile(e: React.SubmitEvent<HTMLFormElement>) {
    e.preventDefault();
    setProfileError(null);
    setProfileSaving(true);
    try {
      const result = await api.updateProfile(
        displayName,
        iRacingCustomerId ? Number(iRacingCustomerId) : null,
        email
      );
      await updateSession(result);
      setProfileSaved(true);
      setTimeout(() => setProfileSaved(false), 2500);
    } catch (err) {
      setProfileError(err instanceof Error ? err.message : 'Failed to save profile.');
    } finally {
      setProfileSaving(false);
    }
  }

  function toggleAlerts() {
    setAlertsEnabled(!alertsEnabled);
  }

  async function selectTier(tier: string) {
    if (tier === user?.role) return;
    setRoleError(null);
    setRoleSaving(true);
    try {
      const result = await api.updateRole(tier);
      await updateSession(result);
      setRoleSaved(true);
      setTimeout(() => setRoleSaved(false), 2500);
    } catch (err) {
      setRoleError(err instanceof Error ? err.message : 'Failed to update tier.');
    } finally {
      setRoleSaving(false);
    }
  }

  return (
    <main className="px-6 pt-8 pb-20 max-w-3xl mx-auto w-full">
      <div className="space-y-8 py-4">
        {/* Page header */}
        <div>
          <h1 className="font-headline-md text-[48px] leading-none font-extrabold tracking-tighter text-on-surface mb-2">
            Account Settings
          </h1>
          <p className="font-body-sm text-body-sm text-on-surface-variant">
            Manage your profile, security preferences, and connected services.
          </p>
        </div>

        {/* Profile header card */}
        <div className="bg-surface rounded-xl border border-line-2 p-6 flex flex-col md:flex-row items-center gap-6 relative overflow-hidden">
          <div className="absolute -top-20 -right-20 w-40 h-40 bg-primary-fixed-dim/10 rounded-full blur-3xl pointer-events-none" />

          {/* Avatar placeholder */}
          <div className="relative shrink-0">
            <div className="w-24 h-24 rounded-full bg-surface-container-highest border-2 border-primary-fixed-dim shadow-[0_0_15px_rgba(0,224,255,0.3)] flex items-center justify-center">
              <span
                className="material-symbols-outlined text-4xl text-primary-fixed-dim fill"
                aria-hidden="true"
              >
                person
              </span>
            </div>
          </div>

          <div className="text-center md:text-left flex-1">
            <h2 className="font-headline-md text-headline-md text-on-surface">
              {displayName || 'ApexRacers Driver'}
            </h2>
            <p className="font-body-sm text-body-sm text-on-surface-variant mt-1">
              Account Settings
            </p>
            <div className="mt-3 inline-flex items-center gap-2 bg-[#FFD700] text-black px-3 py-1 rounded-sm font-label-caps text-label-caps">
              <span className="material-symbols-outlined text-[14px]" aria-hidden="true">
                stars
              </span>
              Pro Tier Driver
            </div>
          </div>
        </div>

        <div className="grid grid-cols-1 md:grid-cols-2 gap-8">
          {/* Personal Information */}
          <div className="bg-surface rounded-xl border border-line-2 p-6 space-y-6">
            <div className="flex items-center gap-3 border-b border-line pb-4">
              <span className="material-symbols-outlined text-primary-fixed-dim" aria-hidden="true">
                person
              </span>
              <h3 className="font-headline-sm text-headline-sm text-on-surface">
                Personal Information
              </h3>
            </div>

            <form onSubmit={saveProfile} className="space-y-4">
              <div>
                <label
                  htmlFor="display-name"
                  className="block font-label-caps text-label-caps text-on-surface-variant mb-2"
                >
                  Display Name
                </label>
                <input
                  id="display-name"
                  type="text"
                  value={displayName}
                  onChange={e => setDisplayName(e.target.value)}
                  className="w-full bg-surface-container-high border border-line-2 rounded text-on-surface font-body-sm text-body-sm px-3 py-2 focus:outline-none focus:border-primary-fixed-dim focus:ring-1 focus:ring-primary-fixed-dim transition-colors"
                />
              </div>

              <div>
                <label
                  htmlFor="profile-email"
                  className="block font-label-caps text-label-caps text-on-surface-variant mb-2"
                >
                  Email Address
                </label>
                <input
                  id="profile-email"
                  type="email"
                  value={email}
                  onChange={e => setEmail(e.target.value)}
                  className="w-full bg-surface-container-high border border-line-2 rounded text-on-surface font-body-sm text-body-sm px-3 py-2 focus:outline-none focus:border-primary-fixed-dim focus:ring-1 focus:ring-primary-fixed-dim transition-colors"
                />
              </div>

              <div>
                <label
                  htmlFor="iracing-customer-id"
                  className="block font-label-caps text-label-caps text-on-surface-variant mb-2"
                >
                  iRacing Customer ID
                </label>
                <input
                  id="iracing-customer-id"
                  type="number"
                  min="1"
                  value={iRacingCustomerId}
                  onChange={e => setIRacingCustomerId(e.target.value)}
                  placeholder="e.g. 100042"
                  className="w-full bg-surface-container-high border border-line-2 rounded text-on-surface font-body-sm text-body-sm px-3 py-2 focus:outline-none focus:border-primary-fixed-dim focus:ring-1 focus:ring-primary-fixed-dim transition-colors"
                />
                <p className="mt-1.5 font-body-sm text-[12px] text-on-surface-variant/60">
                  Used to look up your lap time percentile. Will be set automatically once iRacing
                  OAuth is available.
                </p>
              </div>

              {profileError && (
                <p className="font-body-sm text-body-sm text-error">{profileError}</p>
              )}
              <div className="pt-2">
                <button
                  type="submit"
                  disabled={profileSaving}
                  className="bg-surface-container-highest border border-line-2 text-on-surface px-4 py-2 rounded font-body-sm text-body-sm hover:border-primary-fixed-dim/50 hover:text-primary-fixed-dim transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
                >
                  {profileSaving ? 'Saving…' : profileSaved ? 'Saved ✓' : 'Save Changes'}
                </button>
              </div>
            </form>
          </div>

          {/* Connections + Preferences */}
          <div className="bg-surface rounded-xl border border-line-2 p-6 space-y-6 flex flex-col">
            <div className="flex items-center gap-3 border-b border-line pb-4">
              <span className="material-symbols-outlined text-primary-fixed-dim" aria-hidden="true">
                link
              </span>
              <h3 className="font-headline-sm text-headline-sm text-on-surface">Connections</h3>
            </div>

            <div className="bg-surface-container-high rounded-lg p-4 border border-line flex items-center justify-between">
              <div className="flex items-center gap-4">
                <div className="w-10 h-10 bg-[#00D1FF]/10 rounded flex items-center justify-center border border-[#00D1FF]/20 shrink-0">
                  <span className="font-data-md text-data-md text-[#00D1FF]">iR</span>
                </div>
                <div>
                  <p className="font-body-sm text-body-sm text-on-surface font-semibold">
                    iRacing Account
                  </p>
                  <p
                    className={`font-label-caps text-label-caps flex items-center gap-1 mt-1 ${connected ? 'text-primary-fixed-dim' : 'text-on-surface-variant'}`}
                  >
                    <span className="material-symbols-outlined text-[14px]" aria-hidden="true">
                      {connected ? 'check_circle' : 'radio_button_unchecked'}
                    </span>
                    {connected ? 'Connected' : 'Not connected'}
                  </p>
                </div>
              </div>
              {connected && (
                <button
                  onClick={logout}
                  className="font-body-sm text-body-sm text-on-surface-variant hover:text-error transition-colors underline"
                >
                  Disconnect
                </button>
              )}
            </div>

            {/* Preferences */}
            <div className="mt-auto pt-6 border-t border-line">
              <div className="flex items-center gap-3 mb-4">
                <span
                  className="material-symbols-outlined text-primary-fixed-dim"
                  aria-hidden="true"
                >
                  tune
                </span>
                <h3 className="font-headline-sm text-headline-sm text-on-surface">Preferences</h3>
              </div>

              {/* Theme */}
              <div className="mb-5">
                <p className="font-label-caps text-label-caps text-on-surface-variant mb-2">
                  Theme
                </p>
                <div className="flex gap-2">
                  {(['auto', 'light', 'dark'] as ThemePreference[]).map(opt => {
                    const icons: Record<ThemePreference, string> = {
                      auto: 'brightness_auto',
                      light: 'light_mode',
                      dark: 'dark_mode',
                    };
                    const labels: Record<ThemePreference, string> = {
                      auto: 'Auto',
                      light: 'Light',
                      dark: 'Dark',
                    };
                    const active = theme === opt;
                    return (
                      <button
                        key={opt}
                        onClick={() => setTheme(opt)}
                        className={`flex items-center gap-1.5 btn-fluid-sm border transition-all ${
                          active
                            ? 'border-primary-fixed-dim bg-primary-container/10 text-primary-fixed-dim'
                            : 'border-line-2 text-on-surface-variant hover:border-line-2 hover:text-on-surface'
                        }`}
                      >
                        <span className="material-symbols-outlined text-[15px]" aria-hidden="true">
                          {icons[opt]}
                        </span>
                        {labels[opt]}
                      </button>
                    );
                  })}
                </div>
              </div>

              <label className="flex items-center justify-between cursor-pointer group">
                <span className="font-body-sm text-body-sm text-on-surface-variant group-hover:text-on-surface transition-colors">
                  New series data alerts
                </span>
                <div className="relative">
                  <input
                    type="checkbox"
                    checked={alertsEnabled}
                    onChange={toggleAlerts}
                    className="sr-only peer"
                  />
                  <div className="w-11 h-6 bg-surface-container-highest rounded-full peer peer-checked:after:translate-x-full peer-checked:after:border-white after:content-[''] after:absolute after:top-[2px] after:left-[2px] after:bg-white after:border after:border-gray-300 after:rounded-full after:h-5 after:w-5 after:transition-all peer-checked:bg-primary-fixed-dim" />
                </div>
              </label>
            </div>
          </div>

          {/* Security */}
          <div className="bg-surface rounded-xl border border-line-2 p-6 space-y-6 md:col-span-2">
            <div className="flex items-center gap-3 border-b border-line pb-4">
              <span className="material-symbols-outlined text-primary-fixed-dim" aria-hidden="true">
                lock
              </span>
              <h3 className="font-headline-sm text-headline-sm text-on-surface">Security</h3>
            </div>

            <form onSubmit={changePassword} className="grid grid-cols-1 md:grid-cols-3 gap-4">
              <div>
                <label
                  htmlFor="current-password"
                  className="block font-label-caps text-label-caps text-on-surface-variant mb-2"
                >
                  Current Password
                </label>
                <input
                  id="current-password"
                  type="password"
                  value={currentPassword}
                  onChange={e => setCurrentPassword(e.target.value)}
                  className="w-full bg-surface-container-high border border-line-2 rounded text-on-surface font-body-sm text-body-sm px-3 py-2 focus:outline-none focus:border-primary-fixed-dim focus:ring-1 focus:ring-primary-fixed-dim transition-colors"
                />
              </div>
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
                  value={newPassword}
                  onChange={e => setNewPassword(e.target.value)}
                  className="w-full bg-surface-container-high border border-line-2 rounded text-on-surface font-body-sm text-body-sm px-3 py-2 focus:outline-none focus:border-primary-fixed-dim focus:ring-1 focus:ring-primary-fixed-dim transition-colors"
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
                  value={confirmPassword}
                  onChange={e => setConfirmPassword(e.target.value)}
                  className="w-full bg-surface-container-high border border-line-2 rounded text-on-surface font-body-sm text-body-sm px-3 py-2 focus:outline-none focus:border-primary-fixed-dim focus:ring-1 focus:ring-primary-fixed-dim transition-colors"
                />
              </div>
              {pwError && (
                <p className="md:col-span-3 font-body-sm text-body-sm text-error">{pwError}</p>
              )}
              <div className="md:col-span-3 pt-2">
                <button
                  type="submit"
                  disabled={pwSaving}
                  className="bg-surface-container-highest border border-line-2 text-on-surface px-4 py-2 rounded font-body-sm text-body-sm hover:border-primary-fixed-dim/50 hover:text-primary-fixed-dim transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
                >
                  {pwSaving ? 'Updating…' : pwSaved ? 'Updated ✓' : 'Update Password'}
                </button>
              </div>
            </form>
          </div>
          {/* Access Tier */}
          {user?.role !== 'Admin' && (
            <div className="bg-surface rounded-xl border border-line-2 p-6 space-y-6 md:col-span-2">
              <div className="flex items-center gap-3 border-b border-line pb-4">
                <span
                  className="material-symbols-outlined text-primary-fixed-dim"
                  aria-hidden="true"
                >
                  experiment
                </span>
                <div>
                  <h3 className="font-headline-sm text-headline-sm text-on-surface">Access Tier</h3>
                  <p className="font-body-sm text-[12px] text-on-surface-variant mt-0.5">
                    Opt into early access features. You can change back to Standard at any time.
                  </p>
                </div>
              </div>

              <div className="grid grid-cols-1 md:grid-cols-3 gap-3">
                {(['Standard', 'Beta', 'Alpha'] as const).map(tier => {
                  const isActive = (user?.role ?? 'Standard') === tier;
                  return (
                    <button
                      key={tier}
                      onClick={() => selectTier(tier)}
                      disabled={roleSaving || isActive}
                      className={`rounded-lg border p-4 text-left transition-all ${
                        isActive
                          ? 'border-primary-fixed-dim bg-primary-container/10 cursor-default'
                          : 'border-line-2 hover:border-line-2 hover:bg-surface-container-highest cursor-pointer'
                      }`}
                    >
                      <div className="flex items-center justify-between mb-2">
                        <span className="font-body-sm text-body-sm text-on-surface font-semibold">
                          {tier}
                        </span>
                        {isActive && (
                          <span
                            className="material-symbols-outlined text-[16px] text-primary-fixed-dim"
                            aria-hidden="true"
                          >
                            check_circle
                          </span>
                        )}
                      </div>
                      <p className="font-body-sm text-[12px] text-on-surface-variant leading-relaxed">
                        {TIER_DESCRIPTIONS[tier]}
                      </p>
                    </button>
                  );
                })}
              </div>

              {roleError && <p className="font-body-sm text-body-sm text-error">{roleError}</p>}
              {roleSaved && (
                <p className="font-body-sm text-body-sm text-primary-fixed-dim">
                  Access tier updated.
                </p>
              )}
            </div>
          )}
        </div>
      </div>
    </main>
  );
}
