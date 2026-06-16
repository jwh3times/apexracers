import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { api, type PersonalLap } from '../services/api';
import { formatLapTime } from '../utils/lapTime';

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString('en-US', {
    month: 'short',
    day: 'numeric',
    year: 'numeric',
  });
}

function trackLabel(lap: PersonalLap): string {
  return lap.configName ? `${lap.trackName} — ${lap.configName}` : lap.trackName;
}

function StatCard({
  label,
  value,
  icon,
  sub,
  subIcon,
  subColor = 'text-primary-fixed-dim',
  gold = false,
}: {
  label: string;
  value: string;
  icon: string;
  sub?: string;
  subIcon?: string;
  subColor?: string;
  gold?: boolean;
}) {
  return (
    <div
      className={`glass-panel rounded-xl p-6 relative overflow-hidden ${
        gold ? 'glow-gold gradient-border-gold' : ''
      }`}
    >
      <div className="absolute top-0 right-0 p-4 opacity-10">
        <span className="material-symbols-outlined text-[64px]" aria-hidden="true">
          {icon}
        </span>
      </div>
      <p className="font-label-caps text-label-caps text-on-surface-variant mb-2">{label}</p>
      <p
        className={`font-data-lg text-[48px] leading-none font-extrabold tracking-tighter ${gold ? 'text-tertiary-fixed-dim' : 'text-primary-fixed-dim'}`}
      >
        {value}
      </p>
      {sub && (
        <div className={`mt-4 flex items-center gap-2 font-body-sm text-body-sm ${subColor}`}>
          {subIcon && (
            <span className="material-symbols-outlined text-[16px]" aria-hidden="true">
              {subIcon}
            </span>
          )}
          <span>{sub}</span>
        </div>
      )}
    </div>
  );
}

export default function MyLapsPage() {
  const [laps, setLaps] = useState<PersonalLap[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [search, setSearch] = useState('');

  useEffect(() => {
    setLoading(true);
    api
      .getMyLaps()
      .then(setLaps)
      .catch((err: unknown) =>
        setError(err instanceof Error ? err.message : 'Failed to load laps.')
      )
      .finally(() => setLoading(false));
  }, []);

  const uniqueCars = new Set(laps.map(l => l.carId)).size;
  const uniqueTracks = new Set(laps.map(l => l.trackName)).size;
  const bestLap =
    laps.length > 0
      ? laps.reduce((best, l) => (l.bestLapSeconds < best.bestLapSeconds ? l : best))
      : null;

  const filtered = search.trim()
    ? laps.filter(
        l =>
          l.carName.toLowerCase().includes(search.toLowerCase()) ||
          l.trackName.toLowerCase().includes(search.toLowerCase()) ||
          (l.configName ?? '').toLowerCase().includes(search.toLowerCase())
      )
    : laps;

  return (
    <main className="px-6 pt-8 pb-20 max-w-[1440px] mx-auto w-full flex flex-col gap-8">
      {/* Page header */}
      <header>
        <h2 className="font-headline-md text-[48px] leading-none font-extrabold tracking-tighter text-on-surface mb-2">
          My Laps
        </h2>
        <p className="font-body-sm text-body-sm text-on-surface-variant max-w-2xl">
          Your personal telemetry record book. Review your fastest times and track improvements
          across all recorded sessions.
        </p>
      </header>

      {loading && (
        <p className="text-on-surface-variant font-body-sm animate-pulse">Loading&hellip;</p>
      )}

      {error && <div className="glass-panel rounded-lg p-6 font-body-sm text-error">{error}</div>}

      {!loading && !error && laps.length === 0 && (
        <div className="glass-panel rounded-xl p-8 flex flex-col items-center gap-4 text-center max-w-md">
          <span
            className="material-symbols-outlined text-4xl text-on-surface-variant"
            aria-hidden="true"
          >
            timer_off
          </span>
          <p className="font-body-sm text-body-sm text-on-surface-variant">
            No laps recorded yet.{' '}
            <Link
              to="/telemetry"
              className="text-primary-fixed-dim hover:text-primary transition-colors"
            >
              Upload a telemetry file
            </Link>{' '}
            to get started.
          </p>
        </div>
      )}

      {laps.length > 0 && (
        <>
          {/* Stat cards */}
          <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
            <StatCard
              label="Sessions"
              value={laps.length.toLocaleString()}
              icon="database"
              sub={`${uniqueCars} car${uniqueCars !== 1 ? 's' : ''} tracked`}
              subIcon="directions_car"
            />
            <StatCard
              label="Best Recorded Time"
              value={bestLap ? formatLapTime(bestLap.bestLapSeconds) : '—'}
              icon="workspace_premium"
              sub={bestLap ? 'Personal best' : undefined}
              subIcon={bestLap ? 'star' : undefined}
              gold
            />
            <StatCard
              label="Unique Tracks"
              value={uniqueTracks.toLocaleString()}
              icon="map"
              sub={`${uniqueCars} car${uniqueCars !== 1 ? 's' : ''} in data`}
              subIcon="track_changes"
              subColor="text-secondary-fixed-dim"
            />
          </div>

          {/* Filter bar */}
          <div className="glass-panel rounded-xl border border-outline-variant/30 flex flex-col md:flex-row gap-4 p-4 items-center justify-between">
            <div className="flex flex-wrap gap-3 w-full md:w-auto">
              <div className="relative w-full md:w-64">
                <span
                  className="material-symbols-outlined absolute left-3 top-1/2 -translate-y-1/2 text-on-surface-variant text-[18px] pointer-events-none"
                  aria-hidden="true"
                >
                  search
                </span>
                <input
                  type="text"
                  value={search}
                  onChange={e => setSearch(e.target.value)}
                  placeholder="Search track or car…"
                  className="w-full bg-surface-container border border-surface-variant rounded py-1.5 pl-9 pr-3 font-body-sm text-body-sm text-on-surface focus:border-primary-fixed-dim focus:ring-1 focus:ring-primary-fixed-dim transition-colors placeholder:text-on-surface-variant outline-none"
                />
              </div>
            </div>
            <span className="font-label-caps text-label-caps text-on-surface-variant self-center">
              {filtered.length} of {laps.length} records
            </span>
          </div>

          {/* Table */}
          <div className="bg-surface border border-outline-variant rounded-xl overflow-hidden shadow-lg">
            <div className="overflow-x-auto">
              <table className="w-full text-left border-collapse min-w-[640px]">
                <thead>
                  <tr className="bg-surface-container-high border-b border-surface-variant">
                    <th className="py-3 px-4 font-label-caps text-label-caps text-on-surface-variant font-semibold">
                      Car
                    </th>
                    <th className="py-3 px-4 font-label-caps text-label-caps text-on-surface-variant font-semibold">
                      Track
                    </th>
                    <th className="py-3 px-4 font-label-caps text-label-caps text-on-surface-variant font-semibold text-right">
                      Best Lap
                    </th>
                    <th className="py-3 px-4 font-label-caps text-label-caps text-on-surface-variant font-semibold text-right">
                      Laps
                    </th>
                    <th className="py-3 px-4 font-label-caps text-label-caps text-on-surface-variant font-semibold">
                      Date
                    </th>
                  </tr>
                </thead>
                <tbody className="font-body-sm text-body-sm divide-y divide-surface-variant/50">
                  {filtered.map((lap, i) => (
                    <tr
                      key={i}
                      className="hover:bg-surface-container-highest transition-colors group cursor-pointer"
                    >
                      <td className="py-3 px-4">
                        <div className="flex items-center gap-3">
                          <div className="w-8 h-8 bg-surface-container-highest rounded flex items-center justify-center shrink-0">
                            <span
                              className="material-symbols-outlined text-[16px] text-on-surface-variant"
                              aria-hidden="true"
                            >
                              directions_car
                            </span>
                          </div>
                          <span className="text-on-surface font-medium group-hover:text-primary-fixed-dim transition-colors">
                            {lap.carName}
                          </span>
                        </div>
                      </td>
                      <td className="py-3 px-4 text-on-surface-variant">{trackLabel(lap)}</td>
                      <td className="py-3 px-4 font-data-md text-data-md text-right text-primary-fixed-dim">
                        {formatLapTime(lap.bestLapSeconds)}
                      </td>
                      <td className="py-3 px-4 font-data-md text-data-md text-right text-on-surface-variant">
                        {lap.lapCount}
                      </td>
                      <td className="py-3 px-4 text-on-surface-variant">
                        {formatDate(lap.lastRecordedAt)}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <div className="bg-surface-container-low border-t border-surface-variant p-4 flex justify-between items-center font-body-sm text-body-sm text-on-surface-variant">
              <span>
                {filtered.length} record{filtered.length !== 1 ? 's' : ''}
              </span>
            </div>
          </div>
        </>
      )}
    </main>
  );
}
