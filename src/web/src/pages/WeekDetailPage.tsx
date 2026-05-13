import { useEffect, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { api, type WeekCar } from '../services/api';

function formatLapTime(seconds: number): string {
  const mins = Math.floor(seconds / 60);
  const secs = (seconds % 60).toFixed(3).padStart(6, '0');
  return `${mins}:${secs}`;
}

// Gradient swatches cycled per rank to stand in for car thumbnails
const rowGradients = [
  'from-gray-800 to-black',
  'from-red-900/40 to-black',
  'from-gray-700/40 to-black',
  'from-blue-900/30 to-black',
  'from-gray-500/30 to-black',
];

function CarThumbnail({ index }: { index: number }) {
  const gradient = rowGradients[index % rowGradients.length];
  return (
    <div className="w-16 h-9 bg-surface-container-highest rounded border border-white/5 overflow-hidden shrink-0 relative">
      <div className="absolute inset-0 bg-gradient-to-t from-black/60 to-transparent z-10" />
      <div className={`w-full h-full bg-gradient-to-br ${gradient}`} />
    </div>
  );
}

function RankBadge({ rank }: { rank: number }) {
  if (rank === 1) {
    return (
      <div className="w-6 h-6 mx-auto bg-[#FFD700] text-black font-data-md text-[11px] flex items-center justify-center rounded-sm font-bold shadow-[0_0_8px_rgba(255,215,0,0.4)]">
        1
      </div>
    );
  }
  return <span className="font-data-md text-on-surface-variant text-sm">{rank}</span>;
}

export default function WeekDetailPage() {
  const { seriesId, weekId } = useParams<{ seriesId: string; weekId: string }>();
  const navigate = useNavigate();
  const [cars, setCars] = useState<WeekCar[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!seriesId || !weekId) return;
    api.getCarsForWeek(Number(seriesId), Number(weekId))
      .then(setCars)
      .catch((err: unknown) =>
        setError(err instanceof Error ? err.message : 'Failed to load week data.'))
      .finally(() => setLoading(false));
  }, [seriesId, weekId]);

  const ranked = [...cars].sort((a, b) => {
    if (a.fastestLapSeconds == null) return 1;
    if (b.fastestLapSeconds == null) return -1;
    return a.fastestLapSeconds - b.fastestLapSeconds;
  });

  const totalEntries = cars.reduce((sum, c) => sum + c.entryCount, 0);

  if (loading) {
    return (
      <main className="px-6 pt-8 pb-20 max-w-[1440px] mx-auto w-full">
        <p className="text-on-surface-variant font-body-sm animate-pulse">Loading&hellip;</p>
      </main>
    );
  }

  if (error) {
    return (
      <main className="px-6 pt-8 pb-20 max-w-[1440px] mx-auto w-full">
        <div className="glass-panel rounded-lg p-6 font-body-sm text-error">{error}</div>
      </main>
    );
  }

  return (
    <main className="px-6 pt-8 pb-20 max-w-[1440px] mx-auto w-full flex flex-col gap-8">
      {/* Header */}
      <header className="flex flex-col md:flex-row md:items-end justify-between gap-6">
        <div>
          <Link
            to="/series"
            className="inline-flex items-center gap-1 font-label-caps text-label-caps text-on-surface-variant hover:text-primary-fixed-dim transition-colors mb-4 group"
          >
            <span
              className="material-symbols-outlined text-sm group-hover:-translate-x-1 transition-transform"
              aria-hidden="true"
            >
              arrow_back
            </span>
            Back to Series
          </Link>

          <div className="flex items-center gap-3 mb-2">
            <div className="px-2 py-0.5 bg-tertiary-container text-on-tertiary-container font-label-caps text-[10px] rounded uppercase tracking-widest font-bold">
              Week {weekId}
            </div>
            <span className="font-body-sm text-body-sm text-on-surface-variant">
              Series {seriesId}
            </span>
          </div>

          <h1 className="font-headline-md text-headline-md text-on-surface tracking-tight mb-2">
            Car Performance Breakdown
          </h1>

          {weekId && (
            <Link
              to={`/recommendations?weekId=${weekId}`}
              className="inline-flex items-center gap-1 font-body-sm text-body-sm text-primary-fixed-dim hover:text-primary transition-colors"
            >
              See my car recommendations
              <span className="material-symbols-outlined text-sm" aria-hidden="true">
                arrow_forward
              </span>
            </Link>
          )}
        </div>

        {/* Stats cards */}
        <div className="flex gap-4 self-start md:self-end">
          <div className="glass-panel rounded-lg p-4 min-w-[140px] flex flex-col justify-between relative overflow-hidden">
            <div className="absolute top-0 right-0 w-16 h-16 bg-secondary-container/10 rounded-full blur-xl" />
            <span className="font-label-caps text-label-caps text-on-surface-variant mb-2">
              Total Entries
            </span>
            <span className="font-data-lg text-data-lg text-on-surface">
              {totalEntries.toLocaleString()}
            </span>
          </div>
          <div className="glass-panel rounded-lg p-4 min-w-[140px] flex flex-col justify-between relative overflow-hidden">
            <div className="absolute top-0 right-0 w-16 h-16 bg-primary-fixed/10 rounded-full blur-xl" />
            <span className="font-label-caps text-label-caps text-on-surface-variant mb-2 flex items-center gap-1">
              <span className="w-1.5 h-1.5 rounded-full bg-primary-container animate-pulse" />
              Cars
            </span>
            <span className="font-data-lg text-data-lg text-on-surface">{cars.length}</span>
          </div>
        </div>
      </header>

      {/* Table */}
      {cars.length === 0 ? (
        <p className="text-on-surface-variant font-body-sm">
          No lap time data yet for this week.
        </p>
      ) : (
        <div className="bg-surface rounded-lg border border-white/10 overflow-x-auto shadow-xl shadow-black/50">
          <table className="w-full text-left whitespace-nowrap">
            <thead className="bg-surface-variant/20 border-b border-white/10">
              <tr>
                <th className="px-4 py-3 font-label-caps text-label-caps text-on-surface-variant font-medium w-12 text-center">
                  Rank
                </th>
                <th className="px-4 py-3 font-label-caps text-label-caps text-on-surface-variant font-medium">
                  Car Model
                </th>
                <th className="px-4 py-3 font-label-caps text-label-caps text-on-surface-variant font-medium text-right">
                  Entries
                </th>
                <th className="px-4 py-3 font-label-caps text-label-caps text-on-surface-variant font-medium text-right">
                  Fastest Lap
                </th>
                <th className="px-4 py-3 font-label-caps text-label-caps text-on-surface-variant font-medium text-right">
                  Median Lap
                </th>
              </tr>
            </thead>
            <tbody className="font-body-sm text-body-sm text-on-surface divide-y divide-[#1A1A1C]">
              {ranked.map((car, i) => {
                const rank = i + 1;
                const isTop = rank === 1;
                return (
                  <tr
                    key={car.carId}
                    className="hover:bg-white/[0.03] transition-colors cursor-pointer group"
                    onClick={() =>
                      navigate(
                        `/series/${seriesId}/weeks/${weekId}/cars/${car.carId}/percentile`,
                        { state: { carName: car.carName } },
                      )
                    }
                  >
                    <td className="px-4 py-3 text-center">
                      <RankBadge rank={rank} />
                    </td>
                    <td className="px-4 py-3">
                      <div className="flex items-center gap-3">
                        <CarThumbnail index={i} />
                        <span
                          className={`group-hover:text-primary-fixed-dim transition-colors ${
                            isTop ? 'font-semibold' : 'font-medium'
                          }`}
                        >
                          {car.carName}
                        </span>
                      </div>
                    </td>
                    <td className="px-4 py-3 font-data-md text-data-md text-right text-on-surface-variant">
                      {car.entryCount.toLocaleString()}
                    </td>
                    <td
                      className={`px-4 py-3 font-data-md text-data-md text-right ${
                        isTop
                          ? 'text-primary-container drop-shadow-[0_0_4px_rgba(0,255,136,0.3)]'
                          : 'text-on-surface'
                      }`}
                    >
                      {car.fastestLapSeconds != null
                        ? formatLapTime(car.fastestLapSeconds)
                        : '—'}
                    </td>
                    <td className="px-4 py-3 font-data-md text-data-md text-right text-on-surface">
                      {car.medianLapSeconds != null
                        ? formatLapTime(car.medianLapSeconds)
                        : '—'}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
    </main>
  );
}
