import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { api, type PersonalLap, type Series } from '../services/api';
import { useAuth } from '../context/AuthContext';
import { formatLapTime } from '../utils/lapTime';

function trackLabel(lap: PersonalLap): string {
  return lap.configName ? `${lap.trackName} — ${lap.configName}` : lap.trackName;
}

function SeriesCard({ s }: { s: Series }) {
  const active = s.currentWeekNumber != null;
  return (
    <div className="glass-panel p-5 rounded-xl border border-line-2 relative overflow-hidden hover:bg-surface-container-highest transition-all hover:border-primary-fixed-dim/30 group">
      <div className="absolute top-0 right-0 p-3 opacity-10 pointer-events-none">
        <span className="material-symbols-outlined text-[64px]" aria-hidden="true">
          sports_score
        </span>
      </div>
      <div className="flex justify-between items-start mb-4">
        <h4 className="font-body-lg font-bold text-on-surface pr-2 truncate">{s.name}</h4>
        {active && (
          <span className="bg-primary-container text-on-primary-container font-label-caps text-label-caps px-2 py-1 rounded shrink-0">
            WK {s.currentWeekNumber}
          </span>
        )}
      </div>
      <div className="grid grid-cols-2 gap-4 mb-4">
        <div>
          <span className="block font-label-caps text-label-caps text-on-surface-variant mb-1">
            SEASON
          </span>
          <span className="font-data-md text-data-md text-on-surface">{s.seasonId}</span>
        </div>
        <div>
          <span className="block font-label-caps text-label-caps text-on-surface-variant mb-1">
            STATUS
          </span>
          <span
            className={`font-data-md text-data-md ${active ? 'text-primary-fixed-dim' : 'text-on-surface-variant'}`}
          >
            {active ? 'Active' : 'Off Season'}
          </span>
        </div>
      </div>
      {active ? (
        <Link
          to={`/series/${s.id}/weeks/${s.currentWeekNumber}`}
          className="w-full py-2 bg-surface-container-highest hover:bg-surface-container-high border border-line-2 rounded font-body-sm text-body-sm text-on-surface transition-colors flex items-center justify-center gap-2"
        >
          View Week Details
          <span className="material-symbols-outlined text-[16px]" aria-hidden="true">
            arrow_forward
          </span>
        </Link>
      ) : (
        <div className="w-full py-2 border border-line rounded font-body-sm text-body-sm text-on-surface-variant/40 flex items-center justify-center">
          No Active Week
        </div>
      )}
    </div>
  );
}

export default function ProfilePage() {
  const { user } = useAuth();
  const displayName = user?.displayName ?? 'Driver';

  const [laps, setLaps] = useState<PersonalLap[]>([]);
  const [series, setSeries] = useState<Series[]>([]);
  const [lapsLoading, setLapsLoading] = useState(true);
  const [seriesLoading, setSeriesLoading] = useState(true);

  useEffect(() => {
    api
      .getMyLaps()
      .then(setLaps)
      .catch(() => {})
      .finally(() => setLapsLoading(false));

    api
      .getSeries()
      .then(setSeries)
      .catch(() => {})
      .finally(() => setSeriesLoading(false));
  }, []);

  const totalLaps = laps.reduce((sum, l) => sum + l.lapCount, 0);
  const uniqueCars = new Set(laps.map(l => l.carId)).size;
  const bestLap =
    laps.length > 0
      ? laps.reduce((best, l) => (l.bestLapSeconds < best.bestLapSeconds ? l : best))
      : null;

  // Best lap per car, sorted fastest first
  const carBests = Object.values(
    laps.reduce<Record<number, PersonalLap>>((acc, lap) => {
      if (!acc[lap.carId] || lap.bestLapSeconds < acc[lap.carId].bestLapSeconds) {
        acc[lap.carId] = lap;
      }
      return acc;
    }, {})
  ).sort((a, b) => a.bestLapSeconds - b.bestLapSeconds);

  return (
    <main className="px-6 pt-8 pb-20 max-w-[1440px] mx-auto w-full flex flex-col gap-8">
      {/* Driver overview */}
      <div className="flex flex-col md:flex-row justify-between items-start md:items-center gap-6">
        <div className="flex items-center gap-6">
          <div className="relative">
            <div className="w-20 h-20 md:w-24 md:h-24 rounded-xl border-2 border-primary-fixed-dim p-1 bg-surface-container-highest flex items-center justify-center">
              <span
                className="material-symbols-outlined text-4xl text-primary-fixed-dim"
                aria-hidden="true"
              >
                person
              </span>
            </div>
            <div className="absolute -bottom-2 -right-2 bg-[#FFD700] text-black font-label-caps text-label-caps px-2 py-1 rounded shadow-[0_0_10px_rgba(255,215,0,0.5)]">
              PRO TIER
            </div>
          </div>
          <div>
            <h2 className="font-headline-md text-headline-md text-on-surface mb-1">
              {displayName}
            </h2>
            <div className="flex items-center gap-3 text-on-surface-variant font-body-sm flex-wrap">
              {user?.iRacingCustomerId && (
                <span className="flex items-center gap-1">
                  <span className="material-symbols-outlined text-[16px]" aria-hidden="true">
                    badge
                  </span>
                  ID {user.iRacingCustomerId}
                </span>
              )}
              <span className="flex items-center gap-1">
                <span className="material-symbols-outlined text-[16px]" aria-hidden="true">
                  timer
                </span>
                {lapsLoading ? '—' : `${totalLaps} laps recorded`}
              </span>
            </div>
          </div>
        </div>

        {/* Quick stat chips */}
        <div className="flex gap-4 w-full md:w-auto">
          <div className="glass-panel p-4 rounded-xl flex-1 md:w-36 flex flex-col items-center justify-center">
            <span className="font-label-caps text-label-caps text-on-surface-variant mb-1">
              CARS DRIVEN
            </span>
            <span className="font-data-lg text-data-lg text-primary-fixed-dim">
              {lapsLoading ? '—' : uniqueCars}
            </span>
          </div>
          <div className="glass-panel p-4 rounded-xl flex-1 md:w-36 flex flex-col items-center justify-center relative overflow-hidden">
            <div className="absolute inset-0 bg-gradient-to-br from-[#FFD700]/5 to-transparent pointer-events-none" />
            <span className="font-label-caps text-label-caps text-on-surface-variant mb-1">
              PERSONAL BEST
            </span>
            <span className="font-data-lg text-data-lg text-[#FFD700]">
              {lapsLoading ? '—' : bestLap ? formatLapTime(bestLap.bestLapSeconds) : '—'}
            </span>
          </div>
        </div>
      </div>

      {/* Active series */}
      <section>
        <h3 className="font-headline-sm text-headline-sm text-on-surface mb-4">Active Series</h3>
        {seriesLoading && (
          <p className="font-body-sm text-body-sm text-on-surface-variant animate-pulse">
            Loading&hellip;
          </p>
        )}
        {!seriesLoading && series.length === 0 && (
          <p className="font-body-sm text-body-sm text-on-surface-variant">No active series.</p>
        )}
        {!seriesLoading && series.length > 0 && (
          <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
            {series.slice(0, 6).map(s => (
              <SeriesCard key={s.id} s={s} />
            ))}
          </div>
        )}
      </section>

      {/* Car performance table */}
      <section>
        <div className="glass-panel rounded-xl overflow-hidden border border-primary-fixed-dim/20 shadow-[0_4px_30px_rgba(0,0,0,0.5)]">
          <div className="bg-surface-container-high px-6 py-4 border-b border-line-2 flex justify-between items-center">
            <h3 className="font-headline-sm text-headline-sm text-primary-fixed-dim flex items-center gap-2">
              <span className="material-symbols-outlined" aria-hidden="true">
                data_table
              </span>
              Personal Best by Car
            </h3>
            <Link
              to="/my-laps"
              className="font-label-caps text-label-caps text-on-surface-variant hover:text-primary-fixed-dim transition-colors flex items-center gap-1"
            >
              Full History
              <span className="material-symbols-outlined text-[14px]" aria-hidden="true">
                arrow_forward
              </span>
            </Link>
          </div>

          {lapsLoading && (
            <p className="px-6 py-8 font-body-sm text-body-sm text-on-surface-variant animate-pulse">
              Loading&hellip;
            </p>
          )}

          {!lapsLoading && carBests.length === 0 && (
            <div className="px-6 py-8 flex flex-col items-center gap-3 text-center">
              <span
                className="material-symbols-outlined text-3xl text-on-surface-variant"
                aria-hidden="true"
              >
                timer_off
              </span>
              <p className="font-body-sm text-body-sm text-on-surface-variant">
                No lap data yet.{' '}
                <Link
                  to="/telemetry"
                  className="text-primary-fixed-dim hover:text-primary transition-colors"
                >
                  Upload a telemetry file
                </Link>{' '}
                to populate your profile.
              </p>
            </div>
          )}

          {!lapsLoading && carBests.length > 0 && (
            <div className="overflow-x-auto">
              <table className="w-full text-left border-collapse">
                <thead>
                  <tr className="bg-surface-container/50 border-b border-line-2">
                    <th className="p-4 font-label-caps text-label-caps text-on-surface-variant">
                      CAR MODEL
                    </th>
                    <th className="p-4 font-label-caps text-label-caps text-on-surface-variant">
                      BEST TRACK
                    </th>
                    <th className="p-4 font-label-caps text-label-caps text-on-surface-variant text-right">
                      PERSONAL BEST
                    </th>
                    <th className="p-4 font-label-caps text-label-caps text-on-surface-variant text-right">
                      LAPS
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {carBests.map(lap => (
                    <tr
                      key={lap.carId}
                      className="border-b border-surface-container-high hover:bg-surface-container-highest transition-colors last:border-b-0"
                    >
                      <td className="p-4">
                        <div className="flex items-center gap-3">
                          <div className="w-10 h-6 bg-surface-container-high rounded flex items-center justify-center shrink-0">
                            <span
                              className="material-symbols-outlined text-[12px] text-on-surface-variant"
                              aria-hidden="true"
                            >
                              directions_car
                            </span>
                          </div>
                          <span className="font-body-sm text-body-sm text-on-surface">
                            {lap.carName}
                          </span>
                        </div>
                      </td>
                      <td className="p-4 font-body-sm text-body-sm text-on-surface-variant">
                        {trackLabel(lap)}
                      </td>
                      <td className="p-4 font-data-md text-data-md text-primary-fixed-dim text-right">
                        {formatLapTime(lap.bestLapSeconds)}
                      </td>
                      <td className="p-4 font-data-md text-data-md text-on-surface-variant text-right">
                        {lap.lapCount}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      </section>
    </main>
  );
}
