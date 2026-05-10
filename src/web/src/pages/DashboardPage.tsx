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
    <main className="px-6 pt-8 pb-20 max-w-[1440px] mx-auto w-full">
      {/* Welcome header */}
      <div className="mb-8 grid grid-cols-1 lg:grid-cols-3 gap-4">
        <div className="lg:col-span-2 flex flex-col justify-center">
          <h2 className="font-display-lg text-[48px] leading-none font-extrabold tracking-tighter text-on-surface mb-2">
            Race Center
          </h2>
          <p className="font-body-lg text-body-lg text-on-surface-variant">
            Welcome back,{' '}
            <span className="text-on-surface font-semibold">{displayName}</span>
            . Telemetry systems online.
          </p>
        </div>

        {/* Stat cards */}
        <div className="flex gap-4">
          <div className="glass-panel p-4 rounded-xl flex-1 flex flex-col justify-between relative overflow-hidden border border-[#FFD700]/30 shadow-[0_0_20px_rgba(255,215,0,0.1)]">
            <div className="absolute -right-4 -top-4 w-16 h-16 bg-[#FFD700]/10 rounded-full blur-xl pointer-events-none" />
            <span className="font-label-caps text-label-caps text-on-surface-variant relative z-10">
              Active Series
            </span>
            <span className="font-data-lg text-[32px] font-bold text-on-surface relative z-10 mt-2 block">
              {seriesLoading ? '—' : series.length}
            </span>
          </div>
          <div className="glass-panel p-4 rounded-xl flex-1 flex flex-col justify-between relative overflow-hidden">
            <div className="absolute -right-4 -top-4 w-16 h-16 bg-primary-fixed-dim/10 rounded-full blur-xl pointer-events-none" />
            <span className="font-label-caps text-label-caps text-on-surface-variant relative z-10">
              Laps Recorded
            </span>
            <span className="font-data-lg text-[32px] font-bold text-on-surface relative z-10 mt-2 block">
              {lapsLoading ? '—' : totalLaps}
            </span>
          </div>
        </div>
      </div>

      {/* Main grid */}
      <div className="grid grid-cols-1 xl:grid-cols-12 gap-4">
        {/* Left column */}
        <div className="xl:col-span-8 flex flex-col gap-4">
          {/* This Week's Series */}
          <section className="glass-panel rounded-xl p-6">
            <div className="flex justify-between items-center mb-6">
              <h3 className="font-headline-md text-headline-md text-on-surface flex items-center gap-2">
                <span
                  className="material-symbols-outlined text-primary-fixed-dim"
                  style={{ fontVariationSettings: "'FILL' 1" }}
                  aria-hidden="true"
                >
                  timer
                </span>
                This Week
              </h3>
              <Link
                to="/series"
                className="font-label-caps text-label-caps text-primary-fixed-dim hover:text-primary transition-colors"
              >
                Browse All
              </Link>
            </div>

            {seriesLoading && (
              <p className="font-body-sm text-body-sm text-on-surface-variant animate-pulse">
                Loading&hellip;
              </p>
            )}
            {!seriesLoading && series.length === 0 && (
              <p className="font-body-sm text-body-sm text-on-surface-variant">
                No active series available.
              </p>
            )}
            {!seriesLoading && series.length > 0 && (
              <div className="flex flex-col divide-y divide-white/5">
                {series.slice(0, 4).map(s => (
                  <div
                    key={s.id}
                    className="flex flex-col md:flex-row md:items-center justify-between py-4 first:pt-0 last:pb-0 hover:bg-white/5 transition-colors rounded-lg group -mx-3 px-3"
                  >
                    <div className="flex items-center gap-4 mb-3 md:mb-0">
                      <div className="w-12 h-12 bg-surface-container-high rounded flex items-center justify-center border border-white/10 shrink-0">
                        <span
                          className="material-symbols-outlined text-primary-fixed-dim text-[20px]"
                          aria-hidden="true"
                        >
                          sports_motorsports
                        </span>
                      </div>
                      <div>
                        <h4 className="font-headline-sm text-[16px] text-on-surface">{s.name}</h4>
                        <p className="font-body-sm text-body-sm text-on-surface-variant">
                          {s.currentWeekId != null ? `Week ${s.currentWeekId}` : 'Season upcoming'}
                        </p>
                      </div>
                    </div>
                    {s.currentWeekId != null && (
                      <Link
                        to={`/series/${s.id}/weeks/${s.currentWeekId}`}
                        className="self-start md:self-auto bg-surface-container-lowest border border-white/10 px-4 py-2 rounded-lg font-label-caps text-label-caps text-primary-fixed-dim hover:border-primary-fixed-dim/50 transition-colors"
                      >
                        View Week
                      </Link>
                    )}
                  </div>
                ))}
              </div>
            )}
          </section>

          {/* Personal Bests */}
          <section className="glass-panel rounded-xl p-6 relative overflow-hidden">
            <div className="absolute inset-0 bg-gradient-to-t from-surface-container-highest/20 to-transparent pointer-events-none" />
            <div className="flex justify-between items-start mb-6 relative z-10">
              <div>
                <h3 className="font-headline-md text-headline-md text-on-surface mb-1">
                  Personal Bests
                </h3>
                <p className="font-body-sm text-body-sm text-on-surface-variant">
                  Your fastest lap per car and track
                </p>
              </div>
              <Link
                to="/my-laps"
                className="font-label-caps text-label-caps text-primary-fixed-dim hover:text-primary transition-colors"
              >
                View All
              </Link>
            </div>

            {lapsLoading && (
              <p className="font-body-sm text-body-sm text-on-surface-variant animate-pulse relative z-10">
                Loading&hellip;
              </p>
            )}

            {!lapsLoading && recentLaps.length === 0 && (
              <div className="relative z-10 flex flex-col items-center gap-3 py-8 text-center">
                <span
                  className="material-symbols-outlined text-3xl text-on-surface-variant"
                  aria-hidden="true"
                >
                  timer_off
                </span>
                <p className="font-body-sm text-body-sm text-on-surface-variant">
                  No laps yet.{' '}
                  <Link to="/telemetry" className="text-primary-fixed-dim hover:text-primary">
                    Upload a telemetry file
                  </Link>{' '}
                  to get started.
                </p>
              </div>
            )}

            {!lapsLoading && recentLaps.length > 0 && (
              <div className="relative z-10 overflow-x-auto">
                <table className="w-full text-left">
                  <thead>
                    <tr className="border-b border-white/10">
                      <th className="pb-3 font-label-caps text-label-caps text-on-surface-variant font-semibold">
                        Car
                      </th>
                      <th className="pb-3 font-label-caps text-label-caps text-on-surface-variant font-semibold">
                        Track
                      </th>
                      <th className="pb-3 font-label-caps text-label-caps text-on-surface-variant font-semibold text-right">
                        Best Lap
                      </th>
                    </tr>
                  </thead>
                  <tbody className="font-body-sm text-body-sm divide-y divide-white/5">
                    {recentLaps.map((lap, i) => (
                      <tr key={i} className="hover:bg-white/5 transition-colors">
                        <td className="py-3 text-on-surface font-medium pr-4">{lap.carName}</td>
                        <td className="py-3 text-on-surface-variant pr-4">{trackLabel(lap)}</td>
                        <td className="py-3 font-data-md text-data-md text-primary-fixed-dim text-right">
                          {formatLapTime(lap.bestLapSeconds)}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>

                {bestLap && (
                  <div className="mt-4 pt-4 border-t border-white/10 flex items-center justify-between">
                    <div>
                      <p className="font-label-caps text-label-caps text-on-surface-variant">
                        Overall Best
                      </p>
                      <p className="font-label-caps text-label-caps text-on-surface-variant mt-0.5">
                        {bestLap.carName} &middot; {trackLabel(bestLap)}
                      </p>
                    </div>
                    <span className="font-data-lg text-[24px] text-primary-fixed-dim">
                      {formatLapTime(bestLap.bestLapSeconds)}
                    </span>
                  </div>
                )}
              </div>
            )}
          </section>
        </div>

        {/* Right column: Active Series cards */}
        <div className="xl:col-span-4">
          <section className="glass-panel rounded-xl p-6 h-full flex flex-col">
            <div className="flex justify-between items-center mb-6">
              <h3 className="font-headline-md text-headline-md text-on-surface">Active Series</h3>
              <Link
                to="/series"
                className="font-label-caps text-label-caps text-on-surface-variant hover:text-primary-fixed-dim transition-colors"
              >
                All
              </Link>
            </div>

            {seriesLoading && (
              <p className="font-body-sm text-body-sm text-on-surface-variant animate-pulse">
                Loading&hellip;
              </p>
            )}

            {!seriesLoading && (
              <div className="flex flex-col gap-4 flex-1 overflow-y-auto">
                {series.length === 0 && (
                  <p className="font-body-sm text-body-sm text-on-surface-variant">
                    No active series.
                  </p>
                )}
                {series.map(s => {
                  const weekNum = s.currentWeekId ?? 0;
                  const progressPct = Math.min(100, (weekNum / 12) * 100);
                  return (
                    <Link
                      key={s.id}
                      to={s.currentWeekId != null ? `/series/${s.id}/weeks/${s.currentWeekId}` : '/series'}
                      className="block bg-surface-container-low border border-white/5 p-4 rounded-lg hover:border-primary-fixed-dim/30 transition-colors group"
                    >
                      <div className="flex justify-between items-start mb-3">
                        <div className="bg-primary-fixed-dim/10 text-primary-fixed-dim font-label-caps text-[10px] px-2 py-1 rounded border border-primary-fixed-dim/20">
                          {s.currentWeekId != null ? `Week ${s.currentWeekId}` : 'Upcoming'}
                        </div>
                        <span
                          className="material-symbols-outlined text-on-surface-variant group-hover:text-primary-fixed-dim text-[18px] transition-colors"
                          aria-hidden="true"
                        >
                          chevron_right
                        </span>
                      </div>
                      <h4 className="font-headline-sm text-[16px] text-on-surface mb-1 truncate">
                        {s.name}
                      </h4>
                      <div className="w-full bg-surface-container-highest h-1 rounded-full mt-4 mb-3 overflow-hidden">
                        <div
                          className="bg-primary-fixed-dim h-full transition-all"
                          style={{ width: `${progressPct}%` }}
                        />
                      </div>
                      <div className="flex justify-between font-data-md text-[11px] text-on-surface-variant">
                        <span>Week {weekNum} / 12</span>
                        <span>Season {s.seasonId}</span>
                      </div>
                    </Link>
                  );
                })}
              </div>
            )}
          </section>
        </div>
      </div>
    </main>
  );
}
