import { Link } from 'react-router';
import { api, type DriverProfile, type PersonalLap } from '../../services/api';
import { useAuth } from '../../context/AuthContext';
import { useIracingSurface } from '../../context/FeatureFlagContext';
import { NotLinkedCard } from '../../components/ResourceView';
import { useResource } from '../../hooks/useResource';
import { formatLapTime } from '../../utils/lapTime';
import { topPercentLabel } from '../../utils/percentile';

function trackLabel(lap: PersonalLap): string {
  return lap.configName ? `${lap.trackName} — ${lap.configName}` : lap.trackName;
}

export default function DashboardPage() {
  const { user } = useAuth();
  const displayName = user?.displayName ?? 'Driver';
  const { enabled: showIracing } = useIracingSurface();

  const lapsResource = useResource(signal => api.getMyLaps(signal), [], {
    onError: { fallback: [] },
  });
  const seriesResource = useResource(signal => api.getSeries(signal), [showIracing], {
    enabled: showIracing,
    onError: { fallback: [] },
  });
  const profileResource = useResource<DriverProfile | null>(
    signal => api.getProfileStats(signal),
    [showIracing],
    {
      enabled: showIracing,
      onError: { fallback: null },
    }
  );
  const analyticsResource = useResource(
    signal => api.getMyAnalytics(undefined, signal),
    [showIracing],
    {
      enabled: showIracing,
      onError: { fallback: [] },
    }
  );

  const laps = lapsResource.status === 'ok' ? lapsResource.data : [];
  const series = seriesResource.status === 'ok' ? seriesResource.data : [];
  const profile = profileResource.status === 'ok' ? profileResource.data : null;
  const analytics = analyticsResource.status === 'ok' ? analyticsResource.data : [];
  const lapsLoading = lapsResource.status === 'loading';
  const seriesLoading = seriesResource.status === 'loading';
  const profileLoading = profileResource.status === 'loading';
  const analyticsLoading = analyticsResource.status === 'loading';
  const notLinked =
    profileResource.status === 'not-linked' || analyticsResource.status === 'not-linked';

  const recentLaps = laps.slice(0, 5);
  const totalLaps = laps.reduce((sum, l) => sum + l.lapCount, 0);
  const bestLap =
    laps.length > 0
      ? laps.reduce((best, l) => (l.bestLapSeconds < best.bestLapSeconds ? l : best))
      : null;

  // Headline driver stats come from the category the driver has the highest iRating in.
  const topLicense =
    profile && profile.licenses.length > 0
      ? profile.licenses.reduce((best, l) => (l.iRating > best.iRating ? l : best))
      : null;
  const topCareer = topLicense
    ? (profile!.career.find(c => c.categoryId === topLicense.categoryId) ?? null)
    : null;

  // Best percentile = the strongest (highest) rank across all the driver's cars.
  const bestPercentileRank =
    analytics.length > 0 ? Math.max(...analytics.map(a => a.bestPercentileRank)) : null;

  return (
    <main className="page-wrap">
      {/* Page head */}
      <div className="flex items-start justify-between mb-6">
        <div>
          <p className="text-eyebrow text-primary-container">Race Center</p>
          <h1 className="text-page-title text-on-surface mt-2 mb-1">
            Welcome back, <span className="text-on-surface font-bold">{displayName}</span>
          </h1>
          <p className="text-body-fluid text-on-surface-variant">
            Here&rsquo;s where your pace stands right now.
          </p>
        </div>
        <div className="flex items-center gap-3 mt-1">
          <Link
            to="/telemetry"
            className="inline-flex items-center gap-2 btn-fluid border border-line-2 bg-surface-container text-on-surface font-semibold transition-all hover:bg-surface-container-high"
          >
            <span className="material-symbols-outlined text-[17px]" aria-hidden="true">
              upload_file
            </span>
            Upload telemetry
          </Link>
          <Link
            to="/recommendations"
            className="inline-flex items-center gap-2 btn-fluid border-transparent bg-primary-container text-on-primary-fixed font-semibold transition-all"
            style={{ boxShadow: '0 0 26px -8px var(--color-primary-container)' }}
          >
            <span className="material-symbols-outlined text-[17px]" aria-hidden="true">
              auto_awesome
            </span>
            Find my edge
          </Link>
        </div>
      </div>

      {showIracing && notLinked && (
        <div className="mb-6">
          <NotLinkedCard reason="Link your iRacing account to personalize the Race Center." />
        </div>
      )}

      {/* KPI row — lap/series tiles + driver-stat tiles (iRating / SR / avg finish) */}
      <div className="grid-kpi mb-4">
        {/* Active series */}
        {showIracing && (
          <div className="bg-surface border border-line-2 card-r card-shadow kpi-p relative overflow-hidden">
            <div className="text-small-fluid text-on-surface-variant font-medium flex items-center gap-[7px]">
              <span className="material-symbols-outlined text-[15px]" aria-hidden="true">
                sports_motorsports
              </span>
              Active series
            </div>
            <div className="text-kpi-value mt-2 text-on-surface">
              {seriesLoading ? '—' : series.length}
            </div>
          </div>
        )}

        {/* Laps recorded */}
        <div className="bg-surface border border-line-2 card-r card-shadow kpi-p relative overflow-hidden">
          <div className="text-small-fluid text-on-surface-variant font-medium flex items-center gap-[7px]">
            <span className="material-symbols-outlined text-[15px]" aria-hidden="true">
              timer
            </span>
            Laps recorded
          </div>
          <div className="text-kpi-value mt-2 text-on-surface">{lapsLoading ? '—' : totalLaps}</div>
        </div>

        {/* Cars tracked — distinct car count, not car+track combos */}
        <div className="bg-surface border border-line-2 card-r card-shadow kpi-p relative overflow-hidden">
          <div className="text-small-fluid text-on-surface-variant font-medium flex items-center gap-[7px]">
            <span className="material-symbols-outlined text-[15px]" aria-hidden="true">
              directions_car
            </span>
            Cars tracked
          </div>
          <div className="text-kpi-value mt-2 text-on-surface">
            {new Set(laps.map(l => l.carId)).size}
          </div>
        </div>

        {/* Best percentile — strongest rank across the driver's cars */}
        {showIracing && (
          <div className="bg-surface border border-line-2 card-r card-shadow kpi-p relative overflow-hidden">
            <div className="text-small-fluid text-on-surface-variant font-medium flex items-center gap-[7px]">
              <span className="material-symbols-outlined text-[15px]" aria-hidden="true">
                social_leaderboard
              </span>
              Best percentile
            </div>
            <div className="text-kpi-value mt-2 text-on-surface">
              {analyticsLoading
                ? '—'
                : bestPercentileRank != null
                  ? topPercentLabel(bestPercentileRank)
                  : '—'}
            </div>
          </div>
        )}

        {/* iRating — headline (highest-iRating category) */}
        {showIracing && (
          <div className="bg-surface border border-line-2 card-r card-shadow kpi-p relative overflow-hidden">
            <div className="text-small-fluid text-on-surface-variant font-medium flex items-center gap-[7px]">
              <span className="material-symbols-outlined text-[15px]" aria-hidden="true">
                trending_up
              </span>
              iRating
            </div>
            <div className="text-kpi-value mt-2 text-on-surface">
              {profileLoading ? '—' : (topLicense?.iRating ?? '—')}
            </div>
          </div>
        )}

        {/* Safety Rating — same category */}
        {showIracing && (
          <div className="bg-surface border border-line-2 card-r card-shadow kpi-p relative overflow-hidden">
            <div className="text-small-fluid text-on-surface-variant font-medium flex items-center gap-[7px]">
              <span className="material-symbols-outlined text-[15px]" aria-hidden="true">
                shield
              </span>
              Safety Rating
            </div>
            <div className="text-kpi-value mt-2 text-on-surface">
              {profileLoading ? '—' : topLicense ? topLicense.safetyRating.toFixed(2) : '—'}
            </div>
          </div>
        )}

        {/* Average finish — same category career */}
        {showIracing && (
          <div className="bg-surface border border-line-2 card-r card-shadow kpi-p relative overflow-hidden">
            <div className="text-small-fluid text-on-surface-variant font-medium flex items-center gap-[7px]">
              <span className="material-symbols-outlined text-[15px]" aria-hidden="true">
                sports_score
              </span>
              Avg finish
            </div>
            <div className="text-kpi-value mt-2 text-on-surface">
              {profileLoading ? '—' : topCareer ? topCareer.avgFinishPosition.toFixed(1) : '—'}
            </div>
          </div>
        )}
      </div>

      {/* Main content — 2 col on lg+ when showIracing, single col otherwise */}
      <div
        className={`grid grid-cols-1 ${showIracing ? 'lg:grid-cols-[1.55fr_1fr]' : ''} gap-fluid`}
      >
        {/* Left column */}
        <div className="flex flex-col gap-fluid">
          {/* This week */}
          {showIracing && (
            <div className="card-r card-shadow border border-line-2 bg-surface overflow-hidden">
              <div className="scan-texture flex items-center justify-between card-hp border-b border-line-2">
                <h3 className="text-section-head text-on-surface">This week</h3>
                <Link
                  to="/series"
                  className="text-small-fluid text-primary-container font-semibold hover:opacity-80 transition-opacity"
                >
                  Browse all →
                </Link>
              </div>

              {seriesLoading && (
                <p className="px-5 py-4 text-body-fluid text-on-surface-variant animate-pulse">
                  Loading&hellip;
                </p>
              )}
              {!seriesLoading && series.length === 0 && (
                <p className="px-5 py-4 text-body-fluid text-on-surface-variant">
                  No active series available.
                </p>
              )}
              {!seriesLoading && series.length > 0 && (
                <div className="divide-y divide-white/[0.06]">
                  {series.slice(0, 5).map(s => (
                    <div key={s.id} className="flex items-center justify-between px-5 py-[13px]">
                      <div>
                        <div className="text-body-fluid font-medium text-on-surface">{s.name}</div>
                        <div className="text-small-fluid text-on-surface-variant mt-0.5">
                          {s.currentWeekNumber != null
                            ? `Season ${s.seasonId} · Week ${s.currentWeekNumber}`
                            : 'Season upcoming'}
                        </div>
                      </div>
                      {s.currentWeekNumber != null && (
                        <Link
                          to={`/series/${s.id}/weeks/${s.currentWeekNumber}`}
                          className="inline-flex items-center gap-2 btn-fluid-sm border border-line-2 bg-surface-container text-on-surface font-semibold transition-all hover:bg-surface-container-high whitespace-nowrap"
                        >
                          View Week
                        </Link>
                      )}
                    </div>
                  ))}
                </div>
              )}
            </div>
          )}

          {/* Personal bests */}
          <div className="card-r card-shadow border border-line-2 bg-surface overflow-hidden">
            <div className="scan-texture flex items-center justify-between card-hp border-b border-line-2">
              <h3 className="text-section-head text-on-surface">Personal bests</h3>
              <Link
                to="/my-laps"
                className="text-small-fluid text-primary-container font-semibold hover:opacity-80 transition-opacity"
              >
                View all →
              </Link>
            </div>

            {lapsLoading && (
              <p className="px-5 py-4 text-body-fluid text-on-surface-variant animate-pulse">
                Loading&hellip;
              </p>
            )}

            {!lapsLoading && recentLaps.length === 0 && (
              <div className="flex flex-col items-center gap-3 py-10 text-center px-5">
                <span
                  className="material-symbols-outlined text-3xl text-on-surface-variant"
                  aria-hidden="true"
                >
                  timer_off
                </span>
                <p className="text-body-fluid text-on-surface-variant">
                  No laps yet.{' '}
                  <Link
                    to="/telemetry"
                    className="text-primary-container underline hover:opacity-80"
                  >
                    Upload a telemetry file
                  </Link>{' '}
                  to get started.
                </p>
              </div>
            )}

            {!lapsLoading && recentLaps.length > 0 && (
              <div className="overflow-x-auto">
                <table className="w-full text-left">
                  <thead>
                    <tr>
                      <th className="text-th text-on-surface-variant th-p border-b border-line-2 whitespace-nowrap text-left">
                        Car
                      </th>
                      <th className="text-th text-on-surface-variant th-p border-b border-line-2 whitespace-nowrap text-left">
                        Track
                      </th>
                      <th className="text-th text-on-surface-variant th-p border-b border-line-2 whitespace-nowrap text-right">
                        Best Lap
                      </th>
                    </tr>
                  </thead>
                  <tbody>
                    {recentLaps.map((lap, i) => (
                      <tr key={i}>
                        <td className="td-p border-b border-line-2 last:border-b-0 text-body-fluid text-on-surface font-medium">
                          {lap.carName}
                        </td>
                        <td className="td-p border-b border-line-2 last:border-b-0 text-body-fluid text-on-surface-variant">
                          {trackLabel(lap)}
                        </td>
                        <td className="td-p border-b border-line-2 last:border-b-0 text-body-fluid text-primary-container font-mono text-right">
                          {formatLapTime(lap.bestLapSeconds)}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>

                {bestLap && (
                  <div className="px-4 py-3 border-t border-line-2 flex items-center justify-between">
                    <div>
                      <p className="text-th text-on-surface-variant">Overall Best</p>
                      <p className="text-small-fluid text-on-surface-variant mt-0.5">
                        {bestLap.carName} &middot; {trackLabel(bestLap)}
                      </p>
                    </div>
                    <span className="font-mono text-[22px] font-bold text-primary-container">
                      {formatLapTime(bestLap.bestLapSeconds)}
                    </span>
                  </div>
                )}
              </div>
            )}
          </div>
        </div>

        {/* Right column: Active series list */}
        {showIracing && (
          <div>
            <div className="card-r card-shadow border border-line-2 bg-surface overflow-hidden h-full">
              <div className="scan-texture flex items-center justify-between card-hp border-b border-line-2">
                <h3 className="text-section-head text-on-surface">Active series</h3>
                <Link
                  to="/series"
                  className="text-small-fluid text-on-surface-variant hover:text-on-surface transition-colors"
                >
                  All
                </Link>
              </div>

              {seriesLoading && (
                <p className="px-5 py-4 text-body-fluid text-on-surface-variant animate-pulse">
                  Loading&hellip;
                </p>
              )}

              {!seriesLoading && (
                <div className="flex flex-col divide-y divide-white/[0.06]">
                  {series.length === 0 && (
                    <p className="px-5 py-4 text-body-fluid text-on-surface-variant">
                      No active series.
                    </p>
                  )}
                  {series.map(s => {
                    const weekNum = s.currentWeekNumber ?? 0;
                    const progressPct = Math.min(100, (weekNum / 12) * 100);
                    return (
                      <Link
                        key={s.id}
                        to={
                          s.currentWeekNumber != null
                            ? `/series/${s.id}/weeks/${s.currentWeekNumber}`
                            : '/series'
                        }
                        className="block px-5 py-4 hover:bg-surface-container transition-colors"
                      >
                        <div className="flex items-center justify-between mb-2">
                          <span className="text-small-fluid font-mono text-primary-container font-semibold">
                            {s.currentWeekNumber != null
                              ? `Week ${s.currentWeekNumber}`
                              : 'Upcoming'}
                          </span>
                          <span
                            className="material-symbols-outlined text-[16px] text-on-surface-variant"
                            aria-hidden="true"
                          >
                            chevron_right
                          </span>
                        </div>
                        <p className="text-body-fluid font-medium text-on-surface truncate mb-3">
                          {s.name}
                        </p>
                        <div className="w-full bg-surface-container-highest h-[3px] rounded-full overflow-hidden">
                          <div
                            className="bg-primary-container h-full transition-all"
                            style={{ width: `${progressPct}%` }}
                          />
                        </div>
                        <div className="flex justify-between text-small-fluid text-on-surface-variant mt-1.5">
                          <span>Week {weekNum} / 12</span>
                          <span>S{s.seasonId}</span>
                        </div>
                      </Link>
                    );
                  })}
                </div>
              )}
            </div>
          </div>
        )}
      </div>
    </main>
  );
}
