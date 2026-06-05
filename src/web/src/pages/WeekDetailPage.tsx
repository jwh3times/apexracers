import { useEffect, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { api, type WeekCar } from '../services/api';
import { formatLapTime } from '../utils/lapTime';

const scanTexture: React.CSSProperties = {
  backgroundImage:
    'repeating-linear-gradient(115deg, rgba(255,255,255,0.04) 0 1px, transparent 1px 9px)',
};

const cardStyle: React.CSSProperties = {
  boxShadow: '0 1px 0 rgba(255,255,255,.03) inset, 0 18px 40px -24px rgba(0,0,0,.8)',
};

export default function WeekDetailPage() {
  const { seriesId, weekNumber } = useParams<{ seriesId: string; weekNumber: string }>();
  const navigate = useNavigate();
  const [cars, setCars] = useState<WeekCar[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!seriesId || !weekNumber) return;
    api.getCarsForWeek(Number(seriesId), Number(weekNumber))
      .then(setCars)
      .catch((err: unknown) =>
        setError(err instanceof Error ? err.message : 'Failed to load week data.'))
      .finally(() => setLoading(false));
  }, [seriesId, weekNumber]);

  const ranked = [...cars].sort((a, b) => {
    if (a.fastestLapSeconds == null) return 1;
    if (b.fastestLapSeconds == null) return -1;
    return a.fastestLapSeconds - b.fastestLapSeconds;
  });

  const totalEntries = cars.reduce((sum, c) => sum + c.entryCount, 0);

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

  return (
    <main className="page-wrap">
      {/* Page head */}
      <div className="flex items-start justify-between mb-6">
        <div>
          <Link
            to="/series"
            className="inline-flex items-center gap-2 text-body-fluid text-on-surface-variant hover:text-on-surface transition-colors mb-[10px]"
          >
            <span className="material-symbols-outlined text-[16px]" aria-hidden="true">arrow_back</span>
            Back to Series
          </Link>

          <p className="text-eyebrow text-primary-container">
            WEEK {weekNumber} · SERIES {seriesId}
          </p>
          <h1 className="text-page-title text-on-surface mt-2 mb-1">
            Car Performance Breakdown
          </h1>
        </div>

        {weekNumber && (
          <Link
            to={`/recommendations?seriesId=${seriesId}&weekNumber=${weekNumber}`}
            className="inline-flex items-center gap-2 text-body-fluid text-on-surface-variant hover:text-on-surface transition-colors mt-8"
          >
            See my car recommendations
          </Link>
        )}
      </div>

      {/* KPI strip */}
      <div className="grid-kpi mb-6">
        <div
          className="bg-surface border border-line-2 card-r kpi-p relative overflow-hidden"
          style={cardStyle}
        >
          <div className="text-small-fluid text-on-surface-variant font-medium">Total entries</div>
          <div className="text-kpi-value mt-2 text-on-surface">
            {totalEntries.toLocaleString()}
          </div>
        </div>
        <div
          className="bg-surface border border-line-2 card-r kpi-p relative overflow-hidden"
          style={cardStyle}
        >
          <div className="text-small-fluid text-on-surface-variant font-medium flex items-center gap-[7px]">
            <span className="w-1.5 h-1.5 rounded-full bg-primary-container animate-pulse" />
            Cars running
          </div>
          <div className="text-kpi-value mt-2 text-on-surface">
            {cars.length}
          </div>
        </div>
      </div>

      {/* Car breakdown card */}
      {cars.length === 0 ? (
        <p className="text-body-fluid text-on-surface-variant">
          No lap time data yet for this week.
        </p>
      ) : (
        <div
          className="card-r border border-line-2 bg-surface overflow-hidden"
          style={cardStyle}
        >
          {/* Card header */}
          <div
            className="flex items-center justify-between card-hp border-b border-line-2"
            style={scanTexture}
          >
            <div>
              <h3 className="text-section-head text-on-surface">Car breakdown</h3>
              <p className="text-small-fluid text-on-surface-variant mt-0.5">all cars running this week</p>
            </div>
          </div>

          <div className="overflow-x-auto">
            <table className="w-full text-left whitespace-nowrap">
              <thead>
                <tr>
                  <th className="text-th text-on-surface-variant th-p border-b border-line-2 whitespace-nowrap w-12 text-center">
                    #
                  </th>
                  <th className="text-th text-on-surface-variant th-p border-b border-line-2 whitespace-nowrap text-left">
                    Car
                  </th>
                  <th className="text-th text-on-surface-variant th-p border-b border-line-2 whitespace-nowrap text-right">
                    Entries
                  </th>
                  <th className="text-th text-on-surface-variant th-p border-b border-line-2 whitespace-nowrap text-right">
                    Fastest Lap
                  </th>
                  <th className="text-th text-on-surface-variant th-p border-b border-line-2 whitespace-nowrap text-right">
                    Median Lap
                  </th>
                  <th className="text-th text-on-surface-variant th-p border-b border-line-2 whitespace-nowrap w-8" />
                </tr>
              </thead>
              <tbody>
                {ranked.map((car, i) => {
                  const rank = i + 1;
                  const isTop = rank === 1;
                  return (
                    <tr
                      key={car.carId}
                      className="cursor-pointer hover:bg-surface-container transition-colors"
                      onClick={() =>
                        navigate(
                          `/series/${seriesId}/weeks/${weekNumber}/cars/${car.carId}/percentile`,
                          { state: { carName: car.carName } },
                        )
                      }
                    >
                      <td className="td-p border-b border-line-2 last:border-b-0 text-body-fluid text-center">
                        <span
                          className={
                            isTop
                              ? 'font-mono font-bold text-primary-container'
                              : 'font-mono text-on-surface-variant'
                          }
                        >
                          {rank}
                        </span>
                      </td>
                      <td className="td-p border-b border-line-2 last:border-b-0 text-body-fluid">
                        <div className="flex items-center gap-3">
                          <span
                            className="w-2 h-2 rounded-full shrink-0"
                            style={{ backgroundColor: isTop ? 'var(--color-primary-container)' : 'var(--color-surface-container-highest)' }}
                          />
                          <span className={isTop ? 'font-semibold text-on-surface' : 'text-on-surface'}>
                            {car.carName}
                          </span>
                        </div>
                      </td>
                      <td className="td-p border-b border-line-2 last:border-b-0 text-body-fluid text-right text-on-surface-variant font-mono">
                        {car.entryCount.toLocaleString()}
                      </td>
                      <td className={`td-p border-b border-line-2 last:border-b-0 text-body-fluid text-right font-mono ${isTop ? 'text-primary-container font-semibold' : 'text-on-surface'}`}>
                        {car.fastestLapSeconds != null
                          ? formatLapTime(car.fastestLapSeconds)
                          : '—'}
                      </td>
                      <td className="td-p border-b border-line-2 last:border-b-0 text-body-fluid text-right font-mono text-on-surface">
                        {car.medianLapSeconds != null
                          ? formatLapTime(car.medianLapSeconds)
                          : '—'}
                      </td>
                      <td className="td-p border-b border-line-2 last:border-b-0 text-body-fluid text-on-surface-variant">
                        <span className="material-symbols-outlined text-[16px]" aria-hidden="true">chevron_right</span>
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
