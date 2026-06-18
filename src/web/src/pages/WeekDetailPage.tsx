import { useEffect, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { api, type WeekDetail, type WeekCar } from '../services/api';
import { formatLapTime } from '../utils/lapTime';

const scanTexture: React.CSSProperties = {
  backgroundImage:
    'repeating-linear-gradient(115deg, rgba(255,255,255,0.04) 0 1px, transparent 1px 9px)',
};

const cardStyle: React.CSSProperties = {
  boxShadow: '0 1px 0 rgba(255,255,255,.03) inset, 0 18px 40px -24px rgba(0,0,0,.8)',
};

type SortMode = 'best' | 'consistency' | 'volume';

function formatDelta(best: number | null, median: number | null): string {
  if (best == null || median == null) return '—';
  return (median - best).toFixed(3);
}

function dotColor(carId: number): string {
  const palette = [
    '#00e0ff',
    '#f59e0b',
    '#10b981',
    '#8b5cf6',
    '#ef4444',
    '#3b82f6',
    '#ec4899',
    '#84cc16',
  ];
  return palette[carId % palette.length];
}

function sortCars(cars: WeekCar[], mode: SortMode): WeekCar[] {
  const copy = [...cars];
  if (mode === 'best') {
    copy.sort((a, b) => {
      if (a.fastestLapSeconds == null) return 1;
      if (b.fastestLapSeconds == null) return -1;
      return a.fastestLapSeconds - b.fastestLapSeconds;
    });
  } else if (mode === 'consistency') {
    copy.sort((a, b) => {
      const da =
        a.fastestLapSeconds != null && a.medianLapSeconds != null
          ? a.medianLapSeconds - a.fastestLapSeconds
          : Infinity;
      const db =
        b.fastestLapSeconds != null && b.medianLapSeconds != null
          ? b.medianLapSeconds - b.fastestLapSeconds
          : Infinity;
      return da - db;
    });
  } else {
    copy.sort((a, b) => b.entryCount - a.entryCount);
  }
  return copy;
}

export default function WeekDetailPage() {
  const { seriesId, weekNumber } = useParams<{ seriesId: string; weekNumber: string }>();
  const navigate = useNavigate();
  const [detail, setDetail] = useState<WeekDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [sort, setSort] = useState<SortMode>('best');

  useEffect(() => {
    if (!seriesId || !weekNumber) return;
    api
      .getWeekDetail(Number(seriesId), Number(weekNumber))
      .then(setDetail)
      .catch((err: unknown) =>
        setError(err instanceof Error ? err.message : 'Failed to load week data.')
      )
      .finally(() => setLoading(false));
  }, [seriesId, weekNumber]);

  if (loading) {
    return (
      <main className="page-wrap">
        <p className="text-on-surface-variant text-body-fluid animate-pulse">Loading&hellip;</p>
      </main>
    );
  }

  if (error) {
    return (
      <main className="page-wrap">
        <div className="card-r p-6 text-body-fluid text-error bg-surface border border-line-2">
          {error}
        </div>
      </main>
    );
  }

  const cars = detail?.cars ?? [];
  const sorted = sortCars(cars, sort);
  const fieldBest = sorted.find(c => c.fastestLapSeconds != null);
  const totalEntries = cars.reduce((sum, c) => sum + c.entryCount, 0);

  const trackSubtitle = detail
    ? [
        detail.trackName,
        detail.trackConfigName && detail.trackConfigName !== detail.trackName
          ? `— ${detail.trackConfigName}`
          : null,
        detail.trackLengthMiles ? `· ${detail.trackLengthMiles.toFixed(2)} mi` : null,
      ]
        .filter(Boolean)
        .join(' ')
    : null;

  return (
    <main className="page-wrap">
      {/* Page head */}
      <div className="flex items-start justify-between mb-6">
        <div>
          <Link
            to="/series"
            className="inline-flex items-center gap-1.5 text-small-fluid text-on-surface-variant hover:text-on-surface transition-colors mb-[10px]"
          >
            <span className="material-symbols-outlined text-[15px]" aria-hidden="true">
              arrow_back
            </span>
            Browse series
          </Link>
          <p className="text-eyebrow text-primary-container">
            {weekNumber ? `WEEK ${weekNumber}` : 'WEEK DETAIL'}
            {detail?.category ? ` · ${detail.category.toUpperCase()}` : ''}
          </p>
          <h1 className="text-page-title text-on-surface mt-2 mb-1">
            {detail?.seriesName ?? 'Week Detail'}
          </h1>
          {trackSubtitle && (
            <p className="text-body-fluid text-on-surface-variant">{trackSubtitle}</p>
          )}
        </div>

        <div className="flex items-center gap-3 mt-8">
          {seriesId && (
            <Link
              to={`/series/${seriesId}/schedule`}
              className="text-small-fluid text-on-surface-variant hover:text-on-surface transition-colors"
            >
              Season schedule
            </Link>
          )}
          {weekNumber && (
            <Link
              to={`/recommendations?seriesId=${seriesId}&weekNumber=${weekNumber}`}
              className="text-small-fluid text-on-surface-variant hover:text-on-surface transition-colors"
            >
              See my car recommendations
            </Link>
          )}
          <Link
            to="/analytics"
            className="inline-flex items-center gap-2 btn-fluid border border-primary-container/40 text-primary-container hover:bg-primary-container/10 transition-colors"
          >
            <span className="material-symbols-outlined text-[17px]" aria-hidden="true">
              analytics
            </span>
            Deep dive
          </Link>
        </div>
      </div>

      {/* KPI strip */}
      <div className="grid-kpi mb-6">
        {(
          [
            {
              label: 'Fastest car',
              value: fieldBest ? fieldBest.carName.split(' ').slice(0, 3).join(' ') : '—',
              mono: false,
              icon: 'emoji_events',
            },
            {
              label: 'Field best',
              value:
                fieldBest?.fastestLapSeconds != null
                  ? formatLapTime(fieldBest.fastestLapSeconds)
                  : '—',
              mono: true,
              icon: 'timer',
            },
            {
              label: 'Cars running',
              value: String(cars.length),
              mono: true,
              icon: 'directions_car',
            },
            {
              label: 'Total entries',
              value: totalEntries.toLocaleString(),
              mono: true,
              icon: 'person',
            },
          ] as const
        ).map(kpi => (
          <div
            key={kpi.label}
            className="bg-surface border border-line-2 card-r kpi-p relative overflow-hidden"
            style={cardStyle}
          >
            <div className="text-small-fluid text-on-surface-variant font-medium flex items-center gap-[7px]">
              <span
                className="material-symbols-outlined text-[14px] text-on-surface-variant/60"
                aria-hidden="true"
              >
                {kpi.icon}
              </span>
              {kpi.label}
            </div>
            <div
              className={`mt-2 text-on-surface ${kpi.mono ? 'text-kpi-value' : 'text-section-head font-semibold'}`}
            >
              {kpi.value}
            </div>
          </div>
        ))}
      </div>

      {/* Car breakdown card */}
      {cars.length === 0 ? (
        <p className="text-body-fluid text-on-surface-variant">
          No lap time data yet for this week.
        </p>
      ) : (
        <div className="card-r border border-line-2 bg-surface overflow-hidden" style={cardStyle}>
          {/* Card header with sort controls */}
          <div
            className="flex items-center justify-between card-hp border-b border-line-2"
            style={scanTexture}
          >
            <div>
              <h3 className="text-section-head text-on-surface">Car breakdown</h3>
              <p className="text-small-fluid text-on-surface-variant mt-0.5">
                all cars running this week
              </p>
            </div>
            <div className="flex items-center gap-2">
              {(
                [
                  ['best', 'Best lap'],
                  ['consistency', 'Consistency'],
                  ['volume', 'Volume'],
                ] as [SortMode, string][]
              ).map(([mode, label]) => (
                <button
                  key={mode}
                  onClick={() => setSort(mode)}
                  className={`btn-fluid-sm border transition-colors font-mono uppercase tracking-wider ${
                    sort === mode
                      ? 'border-primary-container bg-primary-container/10 text-primary-container'
                      : 'border-line-2 text-on-surface-variant hover:border-primary-container/40 hover:text-on-surface'
                  }`}
                >
                  {label}
                </button>
              ))}
            </div>
          </div>

          <div className="overflow-x-auto">
            <table className="w-full text-left whitespace-nowrap">
              <thead>
                <tr>
                  <th className="text-th text-on-surface-variant th-p border-b border-line-2 text-left">
                    Car
                  </th>
                  <th className="text-th text-on-surface-variant th-p border-b border-line-2 text-left">
                    Class
                  </th>
                  <th className="text-th text-on-surface-variant th-p border-b border-line-2 text-right">
                    Best
                  </th>
                  <th className="text-th text-on-surface-variant th-p border-b border-line-2 text-right">
                    Median
                  </th>
                  <th className="text-th text-on-surface-variant th-p border-b border-line-2 text-right">
                    Δ B/M
                  </th>
                  <th className="text-th text-on-surface-variant th-p border-b border-line-2 text-right">
                    Laps
                  </th>
                  <th className="th-p border-b border-line-2 w-8" />
                </tr>
              </thead>
              <tbody>
                {sorted.map((car, i) => {
                  const isFirst = i === 0;
                  return (
                    <tr
                      key={car.carId}
                      className="cursor-pointer hover:bg-surface-container transition-colors"
                      onClick={() =>
                        navigate(
                          `/series/${seriesId}/weeks/${weekNumber}/cars/${car.carId}/percentile`,
                          { state: { carName: car.carName } }
                        )
                      }
                    >
                      {/* Car name + colored dot */}
                      <td className="td-p border-b border-line-2 text-body-fluid">
                        <div className="flex items-center gap-[9px]">
                          <span
                            className="w-2 h-2 rounded-full shrink-0"
                            style={{ backgroundColor: dotColor(car.carId) }}
                          />
                          <span
                            className={`font-semibold ${isFirst ? 'text-on-surface' : 'text-on-surface'}`}
                          >
                            {car.carName}
                          </span>
                        </div>
                      </td>
                      {/* Class pill */}
                      <td className="td-p border-b border-line-2 text-body-fluid">
                        {car.className ? (
                          <span className="text-[11px] font-mono px-[6px] py-[2px] rounded border border-line-2 text-on-surface-variant">
                            {car.className}
                          </span>
                        ) : (
                          <span className="text-on-surface-variant/40">—</span>
                        )}
                      </td>
                      {/* Best lap */}
                      <td
                        className={`td-p border-b border-line-2 text-right font-mono text-body-fluid ${isFirst ? 'text-primary-container font-bold' : 'text-on-surface'}`}
                      >
                        {car.fastestLapSeconds != null ? formatLapTime(car.fastestLapSeconds) : '—'}
                      </td>
                      {/* Median lap */}
                      <td className="td-p border-b border-line-2 text-right font-mono text-body-fluid text-on-surface-variant">
                        {car.medianLapSeconds != null ? formatLapTime(car.medianLapSeconds) : '—'}
                      </td>
                      {/* Δ B/M */}
                      <td className="td-p border-b border-line-2 text-right font-mono text-body-fluid text-on-surface-variant">
                        {formatDelta(car.fastestLapSeconds, car.medianLapSeconds)}
                      </td>
                      {/* Laps (entry count) */}
                      <td className="td-p border-b border-line-2 text-right font-mono text-body-fluid text-on-surface-variant">
                        {car.entryCount.toLocaleString()}
                      </td>
                      {/* Chevron */}
                      <td className="td-p border-b border-line-2 text-on-surface-variant/60">
                        <span className="material-symbols-outlined text-[16px]" aria-hidden="true">
                          chevron_right
                        </span>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        </div>
      )}
    </main>
  );
}
