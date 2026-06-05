import { useEffect, useReducer } from 'react';
import { Link } from 'react-router-dom';
import { api, type Series, type CarAnalytics } from '../services/api';
import { useAuth } from '../context/AuthContext';
import { formatLapTime } from '../utils/lapTime';
import Sparkline from '../components/Sparkline';

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

// ── Shared styles ─────────────────────────────────────────────────────────────

const cardStyle: React.CSSProperties = {
  boxShadow: '0 1px 0 rgba(255,255,255,.03) inset, 0 18px 40px -24px rgba(0,0,0,.8)',
};

// ── Sub-components ────────────────────────────────────────────────────────────

function FeaturedCarCard({ data }: { data: CarAnalytics }) {
  const improving = isImproving(data.percentileHistory);
  const lapDelta =
    data.personalBestLapSeconds != null && data.medianLapSeconds != null
      ? data.personalBestLapSeconds - data.medianLapSeconds
      : null;
  const isGold = data.bestPercentileRank >= 95;
  const sparkData = data.percentileHistory.map(h => h.percentileRank);

  return (
    <div
      className={`card-r border bg-surface overflow-hidden ${
        isGold ? 'border-[#FFD700]/40' : 'border-line-2'
      }`}
      style={cardStyle}
    >
      <div className="card-p flex flex-col gap-4">
        {/* Top: car name + series + improving */}
        <div className="flex items-start justify-between gap-3">
          <div>
            <h3 className="text-[18px] font-bold text-on-surface leading-tight">{data.carName}</h3>
            <p className="text-small-fluid text-on-surface-variant mt-1">{data.seriesName}</p>
          </div>
          {improving && (
            <span className="inline-flex items-center gap-1 px-[10px] h-[24px] rounded-[7px] border border-primary-container/30 bg-primary-container/10 text-primary-container text-[11px] font-semibold shrink-0">
              <span className="material-symbols-outlined text-[12px]" aria-hidden="true">trending_up</span>
              Improving
            </span>
          )}
        </div>

        {/* Percentile */}
        <div className="flex items-end gap-3">
          <div
            className={`font-mono text-[32px] font-bold leading-none tracking-[-0.02em] ${
              isGold ? 'text-[#FFD700]' : 'text-primary-container'
            }`}
          >
            {topPercentLabel(data.latestPercentileRank)}
          </div>
          {isGold && (
            <span className="mb-1 px-2 py-0.5 rounded-[6px] bg-[#FFD700] text-black text-[11px] font-bold">
              ELITE
            </span>
          )}
          {!isGold && data.bestPercentileRank >= 80 && (
            <span className="mb-1 px-2 py-0.5 rounded-[6px] border border-primary-container/30 bg-primary-container/10 text-primary-container text-[11px] font-bold">
              PRO TIER
            </span>
          )}
        </div>

        {/* Sparkline — only rendered when there are at least 2 data points */}
        {sparkData.length >= 2 && (
          <div className="w-full">
            <Sparkline data={sparkData} w={460} h={76} />
          </div>
        )}

        {/* Stats row */}
        <div className="flex items-center gap-6 pt-2 border-t border-line-2 flex-wrap">
          <div>
            <p className="text-th text-on-surface-variant">Total Weeks</p>
            <p className="font-mono text-mono-fluid font-semibold text-on-surface mt-0.5">{data.totalWeeks}</p>
          </div>
          {data.personalBestLapSeconds != null && (
            <div>
              <p className="text-th text-on-surface-variant">Best Lap</p>
              <p className="font-mono text-mono-fluid font-semibold text-primary-container mt-0.5">
                {formatLapTime(data.personalBestLapSeconds)}
              </p>
            </div>
          )}
          {lapDelta != null && (
            <div>
              <p className="text-th text-on-surface-variant">Best vs Median</p>
              <p className={`font-mono text-mono-fluid font-semibold mt-0.5 ${lapDelta < 0 ? 'text-primary-container' : 'text-error'}`}>
                {lapDelta < 0 ? '' : '+'}{lapDelta.toFixed(3)}s
              </p>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

function SecondaryCarCard({ data }: { data: CarAnalytics }) {
  const improving = isImproving(data.percentileHistory);
  const sparkData = data.percentileHistory.map(h => h.percentileRank);

  return (
    <div
      className="card-r border border-line-2 bg-surface overflow-hidden"
      style={cardStyle}
    >
      <div className="card-p flex flex-col gap-3">
        {/* Car name + improving */}
        <div className="flex items-start justify-between gap-3">
          <h3 className="text-section-head text-on-surface leading-tight">{data.carName}</h3>
          {improving && (
            <span className="inline-flex items-center gap-1 px-[10px] h-[22px] rounded-[7px] border border-primary-container/30 bg-primary-container/10 text-primary-container text-[10.5px] font-semibold shrink-0">
              IMPROVING
            </span>
          )}
        </div>

        {/* Percentile label */}
        <div className="font-mono text-[22px] font-bold text-primary-container leading-none">
          {topPercentLabel(data.latestPercentileRank)}
        </div>

        {/* Sparkline — only rendered when there are at least 2 data points */}
        {sparkData.length >= 2 && <Sparkline data={sparkData} w={240} h={52} />}

        {/* Stats */}
        <div className="flex items-center gap-5 pt-2 border-t border-line-2 text-small-fluid flex-wrap">
          <div>
            <span className="text-on-surface-variant">Weeks: </span>
            <span className="text-on-surface font-semibold">{data.percentileHistory.length}</span>
          </div>
          {data.personalBestLapSeconds != null && (
            <div>
              <span className="text-on-surface-variant">Best: </span>
              <span className="text-primary-container font-semibold font-mono">
                {formatLapTime(data.personalBestLapSeconds)}
              </span>
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
      <main className="page-wrap">
        <div className="flex flex-col items-center justify-center gap-4 py-24 text-center">
          <span
            className="material-symbols-outlined text-5xl text-on-surface-variant"
            aria-hidden="true"
          >
            analytics
          </span>
          <h2 className="text-page-title text-on-surface">
            Sign in to view analytics
          </h2>
          <p className="text-body-fluid text-on-surface-variant">
            Track your percentile trends and car performance over time.
          </p>
          <Link
            to="/login"
            className="inline-flex items-center gap-2 btn-fluid border-transparent bg-primary-container text-on-primary-fixed font-semibold transition-all"
            style={{ boxShadow: '0 0 26px -8px var(--color-primary-container)' }}
          >
            Sign In
          </Link>
        </div>
      </main>
    );
  }

  const [featuredCar, ...otherCars] = analytics;

  return (
    <main className="page-wrap">
      {/* Header */}
      <div className="mb-6">
        <p className="text-eyebrow text-primary-container">
          ANALYTICS
        </p>
        <h1 className="text-page-title text-on-surface mt-2 mb-1">
          Performance Deep Dive
        </h1>
      </div>

      {/* Series chip filters */}
      {!seriesLoading && series.length > 0 && (
        <div className="mb-6 overflow-x-auto">
          <div className="flex gap-2 pb-2 min-w-max">
            {series.map(s => (
              <button
                key={s.id}
                onClick={() => dispatch({ type: 'SELECT_SERIES', seriesId: s.id })}
                className={`inline-flex items-center gap-[7px] h-[32px] px-[14px] rounded-[9px] border font-semibold text-small-fluid cursor-pointer transition-all ${
                  selectedSeriesId === s.id
                    ? 'bg-primary-container text-on-primary-fixed border-transparent'
                    : 'border-white/[0.12] bg-transparent text-on-surface-variant hover:text-on-surface'
                }`}
              >
                {s.name}
              </button>
            ))}
          </div>
        </div>
      )}

      {!seriesLoading && series.length === 0 && (
        <p className="text-body-fluid text-on-surface-variant mb-6">No active series found.</p>
      )}

      {/* Loading state */}
      {analyticsLoading && (
        <p className="text-body-fluid text-on-surface-variant animate-pulse">Loading analytics&hellip;</p>
      )}

      {/* Error state */}
      {!analyticsLoading && error && (
        <p className="text-body-fluid text-error">{error}</p>
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
          <p className="text-body-fluid text-on-surface-variant max-w-xs">
            No percentile data for this series yet.{' '}
            <Link to="/series" className="text-primary-container hover:opacity-80">
              Browse series
            </Link>{' '}
            and compute your percentile to start tracking trends.
          </p>
        </div>
      )}

      {/* Car analytics grid */}
      {!analyticsLoading && !error && analytics.length > 0 && (
        <div className="grid grid-cols-1 xl:grid-cols-3 gap-fluid">
          {featuredCar && (
            <div className="xl:col-span-3">
              <FeaturedCarCard data={featuredCar} />
            </div>
          )}
          {otherCars.map(car => (
            <SecondaryCarCard key={`${car.carId}-${car.seriesId}`} data={car} />
          ))}
        </div>
      )}
    </main>
  );
}
