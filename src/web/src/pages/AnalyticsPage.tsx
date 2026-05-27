import { useEffect, useReducer } from 'react';
import { Link } from 'react-router-dom';
import { api, type Series, type CarAnalytics } from '../services/api';
import { useAuth } from '../context/AuthContext';
import { formatLapTime } from '../utils/lapTime';

// ── Helpers ───────────────────────────────────────────────────────────────────

function topPercentLabel(rank: number): string {
  return `TOP ${Math.max(1, Math.ceil(100 - rank))}%`;
}

function isImproving(history: { percentileRank: number }[]): boolean {
  return history.length >= 2 && history[history.length - 1].percentileRank > history[0].percentileRank;
}

// ── Reducer ───────────────────────────────────────────────────────────────────

type State = {
  series: Series[];
  selectedSeriesId: number | null;
  analytics: CarAnalytics[];
  seriesLoading: boolean;
  analyticsLoading: boolean;
  error: string | null;
};

type Action =
  | { type: 'SERIES_LOADED'; series: Series[] }
  | { type: 'SELECT_SERIES'; seriesId: number }
  | { type: 'ANALYTICS_LOADED'; analytics: CarAnalytics[] }
  | { type: 'ANALYTICS_ERROR'; message: string };

function reducer(state: State, action: Action): State {
  switch (action.type) {
    case 'SERIES_LOADED':
      return {
        ...state,
        series: action.series,
        seriesLoading: false,
        selectedSeriesId: action.series[0]?.id ?? null,
        analyticsLoading: action.series.length > 0,
        error: null,
      };
    case 'SELECT_SERIES':
      return { ...state, selectedSeriesId: action.seriesId, analyticsLoading: true, error: null };
    case 'ANALYTICS_LOADED':
      return { ...state, analytics: action.analytics, analyticsLoading: false };
    case 'ANALYTICS_ERROR':
      return { ...state, error: action.message, analyticsLoading: false };
    default:
      return state;
  }
}

const initialState: State = {
  series: [],
  selectedSeriesId: null,
  analytics: [],
  seriesLoading: true,
  analyticsLoading: false,
  error: null,
};

// ── Sub-components ────────────────────────────────────────────────────────────

function SparklineBar({
  percentileRank,
  isLatest,
}: {
  percentileRank: number;
  isLatest: boolean;
}) {
  return (
    <div
      className={`flex-1 min-w-0 rounded-sm transition-colors ${
        isLatest
          ? 'bg-primary-fixed-dim shadow-[0_0_6px_rgba(0,228,121,0.4)]'
          : 'bg-surface-container-highest hover:bg-primary-fixed-dim/40'
      }`}
      style={{ height: `${Math.max(5, percentileRank)}%` }}
      aria-label={`${percentileRank.toFixed(1)}th percentile`}
    />
  );
}

function FeaturedCarCard({ data }: { data: CarAnalytics }) {
  const improving = isImproving(data.percentileHistory);
  const lapDelta =
    data.personalBestLapSeconds != null && data.medianLapSeconds != null
      ? data.personalBestLapSeconds - data.medianLapSeconds
      : null;
  const isGold = data.bestPercentileRank >= 95;

  return (
    <div
      className={`col-span-1 xl:col-span-2 glass-panel rounded-xl relative overflow-hidden group ${
        isGold
          ? 'gradient-border-gold glow-gold'
          : 'border border-primary-fixed-dim/20 glow-accent'
      }`}
    >
      {isGold && (
        <div className="absolute top-0 left-0 w-full h-0.5 bg-gradient-to-r from-transparent via-[#FFD700] to-transparent" />
      )}

      <div className="p-6 flex flex-col md:flex-row gap-8 relative z-10">
        {/* Left: car icon + stats */}
        <div className="w-full md:w-1/3 flex flex-col">
          <div className="h-28 bg-surface-container-highest border border-white/10 rounded-lg mb-4 flex items-center justify-center">
            <span
              className="material-symbols-outlined text-5xl text-on-surface-variant"
              style={{ fontVariationSettings: "'FILL' 1" }}
              aria-hidden="true"
            >
              directions_car
            </span>
          </div>

          <h3 className="font-headline text-headline-sm font-bold text-on-surface">{data.carName}</h3>
          <p className="text-xs text-on-surface-variant mt-1">{data.seriesName}</p>

          <div className="mt-auto pt-4 border-t border-white/10 grid grid-cols-2 gap-x-4 gap-y-3">
            <div>
              <p className="text-xs text-on-surface-variant">Total Laps</p>
              <p className="font-label text-on-surface font-semibold">{data.totalLaps}</p>
            </div>
            <div>
              <p className="text-xs text-on-surface-variant">Weeks Tracked</p>
              <p className="font-label text-on-surface font-semibold">{data.percentileHistory.length}</p>
            </div>
            {data.personalBestLapSeconds != null && (
              <div className="col-span-2">
                <p className="text-xs text-on-surface-variant">Personal Best</p>
                <p className="font-label text-primary-fixed-dim font-semibold">
                  {formatLapTime(data.personalBestLapSeconds)}
                </p>
              </div>
            )}
          </div>
        </div>

        {/* Right: percentile + sparkline */}
        <div className="w-full md:w-2/3 flex flex-col justify-between">
          <div className="flex flex-col sm:flex-row sm:justify-between sm:items-start gap-4 mb-6">
            <div>
              <p className="text-xs text-on-surface-variant mb-1">Global Percentile</p>
              <div className="flex items-end gap-3">
                <h4
                  className={`font-headline text-[2.5rem] font-extrabold leading-none ${
                    isGold ? 'text-[#FFD700]' : 'text-primary-fixed-dim'
                  }`}
                >
                  {topPercentLabel(data.latestPercentileRank)}
                </h4>
                {isGold && (
                  <span className="tier-badge-gold px-2 py-0.5 text-xs font-bold font-label rounded-sm mb-1">
                    ELITE
                  </span>
                )}
                {!isGold && data.bestPercentileRank >= 80 && (
                  <span className="px-2 py-0.5 bg-primary-fixed-dim/20 text-primary-fixed-dim text-xs font-bold font-label rounded-sm mb-1 border border-primary-fixed-dim/30">
                    PRO TIER
                  </span>
                )}
              </div>
            </div>

            {lapDelta != null && (
              <div className="text-right">
                <p className="text-xs text-on-surface-variant mb-1">Best vs Median</p>
                <p
                  className={`font-label font-semibold ${
                    lapDelta < 0 ? 'text-primary-fixed-dim' : 'text-error'
                  }`}
                >
                  {lapDelta < 0 ? '' : '+'}
                  {lapDelta.toFixed(3)}s
                </p>
                {data.medianLapSeconds != null && (
                  <p className="text-xs text-on-surface-variant">
                    Median: {formatLapTime(data.medianLapSeconds)}
                  </p>
                )}
              </div>
            )}
          </div>

          {/* Sparkline */}
          <div>
            <div className="flex justify-between items-center mb-2">
              <p className="text-xs text-on-surface-variant">Percentile Trend</p>
              {improving && (
                <span className="flex items-center gap-1 text-primary-fixed-dim text-xs">
                  <span className="material-symbols-outlined text-xs" aria-hidden="true">
                    trending_up
                  </span>
                  Improving
                </span>
              )}
            </div>
            <div className="h-20 flex items-end gap-1 border-b border-white/10 pb-1">
              {data.percentileHistory.map((week, i) => (
                <SparklineBar
                  key={week.weekNumber}
                  percentileRank={week.percentileRank}
                  isLatest={i === data.percentileHistory.length - 1}
                />
              ))}
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

function SecondaryCarCard({ data }: { data: CarAnalytics }) {
  const improving = isImproving(data.percentileHistory);
  const lapDelta =
    data.personalBestLapSeconds != null && data.medianLapSeconds != null
      ? data.personalBestLapSeconds - data.medianLapSeconds
      : null;

  return (
    <div className="glass-panel rounded-xl border border-white/10 hover:border-primary-fixed-dim/30 transition-colors duration-300 flex flex-col">
      <div className="p-6 flex flex-col h-full">
        <div className="flex justify-between items-start mb-4">
          <h3 className="font-headline text-headline-sm font-bold text-on-surface">{data.carName}</h3>
          {improving && (
            <span className="px-2 py-0.5 bg-primary-fixed-dim/20 text-primary-fixed-dim text-xs font-bold font-label rounded-sm border border-primary-fixed-dim/30">
              IMPROVING
            </span>
          )}
        </div>

        <div className="flex justify-between items-end mb-4">
          <div>
            <p className="text-xs text-on-surface-variant mb-1">Global Pct.</p>
            <h4 className="font-headline font-bold text-xl text-primary-fixed-dim">
              {topPercentLabel(data.latestPercentileRank)}
            </h4>
          </div>
          {lapDelta != null && (
            <div className="text-right">
              <p className="text-xs text-on-surface-variant mb-1">Best vs Median</p>
              <p
                className={`font-label text-sm font-semibold ${
                  lapDelta < 0 ? 'text-primary-fixed-dim' : 'text-error'
                }`}
              >
                {lapDelta < 0 ? '' : '+'}
                {lapDelta.toFixed(3)}s
              </p>
            </div>
          )}
        </div>

        <div className="h-16 flex items-end gap-1 border-b border-white/10 pb-1 mb-4">
          {data.percentileHistory.map((week, i) => (
            <SparklineBar
              key={week.weekNumber}
              percentileRank={week.percentileRank}
              isLatest={i === data.percentileHistory.length - 1}
            />
          ))}
        </div>

        <div className="grid grid-cols-2 gap-x-4 gap-y-1 text-xs text-on-surface-variant">
          <div className="flex justify-between">
            <span>Total Laps:</span>
            <span className="text-on-surface">{data.totalLaps}</span>
          </div>
          <div className="flex justify-between">
            <span>Weeks:</span>
            <span className="text-on-surface">{data.percentileHistory.length}</span>
          </div>
          {data.personalBestLapSeconds != null && (
            <div className="col-span-2 flex justify-between">
              <span>Best Lap:</span>
              <span className="text-primary-fixed-dim">{formatLapTime(data.personalBestLapSeconds)}</span>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

// ── Page ──────────────────────────────────────────────────────────────────────

export default function AnalyticsPage() {
  const { user } = useAuth();
  const [state, dispatch] = useReducer(reducer, initialState);
  const { series, selectedSeriesId, analytics, seriesLoading, analyticsLoading, error } = state;

  useEffect(() => {
    api
      .getSeries()
      .then(s => dispatch({ type: 'SERIES_LOADED', series: s }))
      .catch(() => dispatch({ type: 'SERIES_LOADED', series: [] }));
  }, []);

  useEffect(() => {
    if (!user || selectedSeriesId === null) return;
    api
      .getMyAnalytics(selectedSeriesId)
      .then(a => dispatch({ type: 'ANALYTICS_LOADED', analytics: a }))
      .catch((e: Error) => dispatch({ type: 'ANALYTICS_ERROR', message: e.message }));
  }, [user, selectedSeriesId]);

  if (!user) {
    return (
      <main className="px-6 pt-8 pb-20 max-w-[1440px] mx-auto w-full">
        <div className="flex flex-col items-center justify-center gap-4 py-24 text-center">
          <span
            className="material-symbols-outlined text-5xl text-on-surface-variant"
            aria-hidden="true"
          >
            analytics
          </span>
          <h2 className="font-headline text-headline-md text-on-surface">
            Sign in to view analytics
          </h2>
          <p className="text-on-surface-variant text-sm">
            Track your percentile trends and car performance over time.
          </p>
          <Link
            to="/login"
            className="px-6 py-3 bg-primary-fixed-dim text-on-primary-fixed font-semibold rounded-lg text-sm"
          >
            Sign In
          </Link>
        </div>
      </main>
    );
  }

  const [featuredCar, ...otherCars] = analytics;

  return (
    <main className="px-6 pt-8 pb-20 max-w-[1440px] mx-auto w-full">
      {/* Header */}
      <div className="mb-8">
        <div className="flex items-center gap-2 text-on-surface-variant text-xs mb-2">
          <span className="font-label">Analytics</span>
          <span className="material-symbols-outlined text-xs" aria-hidden="true">
            chevron_right
          </span>
          <span className="text-primary-fixed-dim font-label">Performance Percentiles</span>
        </div>
        <h1 className="font-headline text-[2.5rem] font-extrabold tracking-tight text-on-surface leading-tight">
          Performance Deep Dive
        </h1>
      </div>

      {/* Series tabs */}
      {!seriesLoading && series.length > 0 && (
        <div className="mb-8 overflow-x-auto">
          <div className="flex gap-3 pb-2 min-w-max">
            {series.map(s => (
              <button
                key={s.id}
                onClick={() => dispatch({ type: 'SELECT_SERIES', seriesId: s.id })}
                className={`px-5 py-2.5 text-sm font-headline font-semibold transition-colors rounded-sm ${
                  selectedSeriesId === s.id
                    ? 'bg-primary-fixed-dim text-on-primary-fixed border border-primary-fixed-dim'
                    : 'bg-surface-container border border-outline-variant text-on-surface-variant hover:text-on-surface hover:border-primary-fixed-dim/30'
                }`}
              >
                {s.name}
              </button>
            ))}
          </div>
        </div>
      )}

      {!seriesLoading && series.length === 0 && (
        <p className="text-on-surface-variant text-sm mb-8">No active series found.</p>
      )}

      {/* Loading state */}
      {analyticsLoading && (
        <p className="text-on-surface-variant animate-pulse">Loading analytics&hellip;</p>
      )}

      {/* Error state */}
      {!analyticsLoading && error && (
        <p className="text-error text-sm">{error}</p>
      )}

      {/* Empty state */}
      {!analyticsLoading && !error && analytics.length === 0 && selectedSeriesId !== null && (
        <div className="flex flex-col items-center gap-3 py-20 text-center">
          <span
            className="material-symbols-outlined text-4xl text-on-surface-variant"
            aria-hidden="true"
          >
            query_stats
          </span>
          <p className="text-on-surface-variant text-sm max-w-xs">
            No percentile data for this series yet.{' '}
            <Link to="/series" className="text-primary-fixed-dim hover:text-primary">
              Browse series
            </Link>{' '}
            and compute your percentile to start tracking trends.
          </p>
        </div>
      )}

      {/* Car analytics grid */}
      {!analyticsLoading && !error && analytics.length > 0 && (
        <div className="grid grid-cols-1 xl:grid-cols-3 gap-6">
          {featuredCar && <FeaturedCarCard data={featuredCar} />}
          {otherCars.map(car => (
            <SecondaryCarCard key={`${car.carId}-${car.seriesId}`} data={car} />
          ))}
        </div>
      )}
    </main>
  );
}
