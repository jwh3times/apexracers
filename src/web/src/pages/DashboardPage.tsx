import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { api, type Series, type PersonalLap } from '../services/api';
import { useAuth } from '../context/AuthContext';

function formatLapTime(seconds: number): string {
  const mins = Math.floor(seconds / 60);
  const secs = (seconds % 60).toFixed(3).padStart(6, '0');
  return `${mins}:${secs}`;
}

function trackLabel(lap: PersonalLap): string {
  return lap.configName ? `${lap.trackName} — ${lap.configName}` : lap.trackName;
}

const scanTexture: React.CSSProperties = {
  backgroundImage:
    'repeating-linear-gradient(115deg, rgba(255,255,255,0.04) 0 1px, transparent 1px 9px)',
};

const cardStyle: React.CSSProperties = {
  boxShadow: '0 1px 0 rgba(255,255,255,.03) inset, 0 18px 40px -24px rgba(0,0,0,.8)',
};

export default function DashboardPage() {
  const { user } = useAuth();
  const displayName = user?.displayName ?? 'Driver';

  const [series, setSeries] = useState<Series[]>([]);
  const [laps, setLaps] = useState<PersonalLap[]>([]);
  const [seriesLoading, setSeriesLoading] = useState(true);
  const [lapsLoading, setLapsLoading] = useState(true);

  useEffect(() => {
    api.getSeries()
      .then(setSeries)
      .catch(() => {})
      .finally(() => setSeriesLoading(false));

    api.getMyLaps()
      .then(setLaps)
      .catch(() => {})
      .finally(() => setLapsLoading(false));
  }, []);

  const recentLaps = laps.slice(0, 5);
  const totalLaps = laps.reduce((sum, l) => sum + l.lapCount, 0);
  const bestLap = laps.length > 0
    ? laps.reduce((best, l) => l.bestLapSeconds < best.bestLapSeconds ? l : best)
    : null;

  return (
    <main className="page-wrap">
      {/* Page head */}
      <div className="flex items-start justify-between mb-6">
        <div>
          <p className="text-eyebrow text-primary-container">
            Race Center
          </p>
          <h1 className="text-page-title text-on-surface mt-2 mb-1">
            Welcome back,{' '}
            <span className="text-on-surface font-bold">{displayName}</span>
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
            <span className="material-symbols-outlined text-[17px]" aria-hidden="true">upload_file</span>
            Upload telemetry
          </Link>
          <Link
            to="/recommendations"
            className="inline-flex items-center gap-2 btn-fluid border-transparent bg-primary-container text-on-primary-fixed font-semibold transition-all"
            style={{ boxShadow: '0 0 26px -8px var(--color-primary-container)' }}
          >
            <span className="material-symbols-outlined text-[17px]" aria-hidden="true">auto_awesome</span>
            Find my edge
          </Link>
        </div>
      </div>

      {/* KPI row — 2 primary loading tiles + 1 derived tile (no — placeholder) */}
      <div className="grid-kpi mb-4">
        {/* Active series */}
        <div
          className="bg-surface border border-line-2 card-r kpi-p relative overflow-hidden"
          style={cardStyle}
        >
          <div className="text-small-fluid text-on-surface-variant font-medium flex items-center gap-[7px]">
            <span className="material-symbols-outlined text-[15px]" aria-hidden="true">sports_motorsports</span>
            Active series
          </div>
          <div className="text-kpi-value mt-2 text-on-surface">
            {seriesLoading ? '—' : series.length}
          </div>
        </div>

        {/* Laps recorded */}
        <div
          className="bg-surface border border-line-2 card-r kpi-p relative overflow-hidden"
          style={cardStyle}
        >
          <div className="text-small-fluid text-on-surface-variant font-medium flex items-center gap-[7px]">
            <span className="material-symbols-outlined text-[15px]" aria-hidden="true">timer</span>
            Laps recorded
          </div>
          <div className="text-kpi-value mt-2 text-on-surface">
            {lapsLoading ? '—' : totalLaps}
          </div>
        </div>

        {/* Cars tracked — distinct car count, not car+track combos */}
        <div
          className="bg-surface border border-line-2 card-r kpi-p relative overflow-hidden"
          style={cardStyle}
        >
          <div className="text-small-fluid text-on-surface-variant font-medium flex items-center gap-[7px]">
            <span className="material-symbols-outlined text-[15px]" aria-hidden="true">directions_car</span>
            Cars tracked
          </div>
          <div className="text-kpi-value mt-2 text-on-surface">
            {new Set(laps.map(l => l.carId)).size}
          </div>
        </div>
      </div>

      {/* Main content — 2 col on lg+, single col on mobile */}
      <div className="grid grid-cols-1 lg:grid-cols-[1.55fr_1fr] gap-fluid">
        {/* Left column */}
        <div className="flex flex-col gap-fluid">
          {/* This week */}
          <div
            className="card-r border border-line-2 bg-surface overflow-hidden"
            style={cardStyle}
          >
            <div className="flex items-center justify-between card-hp border-b border-line-2" style={scanTexture}>
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
                  <div
                    key={s.id}
                    className="flex items-center justify-between px-5 py-[13px]"
                  >
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

          {/* Personal bests */}
          <div
            className="card-r border border-line-2 bg-surface overflow-hidden"
            style={cardStyle}
          >
            <div className="flex items-center justify-between card-hp border-b border-line-2" style={scanTexture}>
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
                <span className="material-symbols-outlined text-3xl text-on-surface-variant" aria-hidden="true">
                  timer_off
                </span>
                <p className="text-body-fluid text-on-surface-variant">
                  No laps yet.{' '}
                  <Link to="/telemetry" className="text-primary-container hover:opacity-80">
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
                      <p className="text-th text-on-surface-variant">
                        Overall Best
                      </p>
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
        <div>
          <div
            className="card-r border border-line-2 bg-surface overflow-hidden h-full"
            style={cardStyle}
          >
            <div className="flex items-center justify-between card-hp border-b border-line-2" style={scanTexture}>
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
                      to={s.currentWeekNumber != null ? `/series/${s.id}/weeks/${s.currentWeekNumber}` : '/series'}
                      className="block px-5 py-4 hover:bg-surface-container transition-colors"
                    >
                      <div className="flex items-center justify-between mb-2">
                        <span className="text-small-fluid font-mono text-primary-container font-semibold">
                          {s.currentWeekNumber != null ? `Week ${s.currentWeekNumber}` : 'Upcoming'}
                        </span>
                        <span className="material-symbols-outlined text-[16px] text-on-surface-variant" aria-hidden="true">
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
      </div>
    </main>
  );
}
