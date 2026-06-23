import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { api, type RaceGuideEntry, type CarAnalytics } from '../services/api';
import { useAuth } from '../context/AuthContext';
import { useFeatureFlag } from '../context/FeatureFlagContext';
import { deriveAlerts, type Alert } from '../utils/alerts';

/**
 * Bell + dropdown of client-side-derived notifications (races starting soon, percentile
 * improvements). Driven by the Settings "Alerts" toggle (`alertsEnabled`): when off the bell
 * is dormant (no badge, off-state dropdown); when on it fetches the race guide + analytics and
 * derives alerts via the pure `deriveAlerts` helper. MVP — no persistence or server push.
 */
export default function NotificationsBell() {
  const { alertsEnabled } = useAuth();
  const liveFlag = useFeatureFlag('iracing-live');
  const demoFlag = useFeatureFlag('iracing-demo');
  // Show iRacing panels when real (live) OR synthetic demo data is available.
  const showIracing = liveFlag || demoFlag;
  const [open, setOpen] = useState(false);
  const [alerts, setAlerts] = useState<Alert[]>([]);

  useEffect(() => {
    // Only fetch when alerts are on AND the iracing-live flag is enabled.
    // Without the flag the race-guide endpoint is iRacing-creds-backed and will 503 in production.
    if (!alertsEnabled || !showIracing) return;
    let active = true;
    Promise.all([
      api.getRaceGuide().catch((): RaceGuideEntry[] => []),
      api.getMyAnalytics().catch((): CarAnalytics[] => []),
    ]).then(([raceGuide, analytics]) => {
      if (active) setAlerts(deriveAlerts({ raceGuide, analytics }));
    });
    return () => {
      active = false;
    };
  }, [alertsEnabled, showIracing]);

  const count = alerts.length;

  return (
    <div className="relative">
      <button
        type="button"
        aria-label="Notifications"
        onClick={() => setOpen(o => !o)}
        className="relative flex items-center justify-center w-9 h-9 rounded-full text-on-surface-variant hover:text-on-surface hover:bg-surface-container-highest transition-colors"
      >
        <span className="material-symbols-outlined text-[20px]" aria-hidden="true">
          notifications
        </span>
        {alertsEnabled && count > 0 && (
          <span className="absolute -top-0.5 -right-0.5 min-w-[16px] h-[16px] px-1 rounded-full bg-primary-container text-on-primary-fixed text-[10px] font-bold flex items-center justify-center">
            {count}
          </span>
        )}
      </button>

      {open && (
        <div
          className="absolute right-0 mt-2 w-72 card-r border border-line-2 bg-surface shadow-lg z-50 overflow-hidden"
          role="menu"
        >
          <div className="card-hp border-b border-line-2 text-section-head text-on-surface">
            Notifications
          </div>
          {!alertsEnabled ? (
            <div className="card-p text-small-fluid text-on-surface-variant">
              Notifications are off.{' '}
              <Link to="/settings" className="text-primary-container hover:underline">
                Enable in Settings
              </Link>
              .
            </div>
          ) : count === 0 ? (
            <div className="card-p text-small-fluid text-on-surface-variant">
              No new notifications.
            </div>
          ) : (
            <ul className="max-h-80 overflow-y-auto">
              {alerts.map(a => (
                <li
                  key={a.id}
                  className="flex items-start gap-2 card-hp border-b border-line-2 last:border-b-0"
                >
                  <span
                    className="material-symbols-outlined text-[16px] text-primary-container mt-0.5"
                    aria-hidden="true"
                  >
                    {a.kind === 'race' ? 'flag' : 'trending_up'}
                  </span>
                  <span className="text-small-fluid text-on-surface">{a.message}</span>
                </li>
              ))}
            </ul>
          )}
        </div>
      )}
    </div>
  );
}
