import { useEffect, useReducer } from 'react';
import { Link } from 'react-router-dom';
import { api, type Series, type CarAnalytics } from '../services/api';
import { useAuth } from '../context/AuthContext';
import { formatLapTime } from '../utils/lapTime';
import Sparkline from '../components/Sparkline';

// ── Helpers ───────────────────────────────────────────────────────────────────

function trendAxisLabels(history: { computedAt: string }[]): [string, string] {
  const start = new Date(history[0].computedAt);
  const end = new Date(history[history.length - 1].computedAt);
  if (start.getFullYear() !== end.getFullYear()) {
    return [String(start.getFullYear()), String(end.getFullYear())];
  }
  const fmt = (d: Date) => d.toLocaleDateString('en-US', { month: 'short', day: 'numeric' });
  return [fmt(start), fmt(end)];
}

function topPercentLabel(rank: number): string {
  return `TOP ${Math.max(1, Math.ceil(100 - rank))}%`;
}

function isImproving(history: { percentileRank: number }[]): boolean {
  return (
    history.length >= 2 && history[history.length - 1].percentileRank > history[0].percentileRank
  );
}

// ── Reducer ───────────────────────────────────────────────────────────────────

type ViewMode = 'series' | 'car';

type State = {
  viewMode: ViewMode;
  // Series mode
  series: Series[];
  selectedSeriesId: number | null;
  analytics: CarAnalytics[];
  seriesLoading: boolean;
  analyticsLoading: boolean;
  // Car mode
  allAnalytics: CarAnalytics[];
  allAnalyticsLoading: boolean;
  selectedCarId: number | null;
  // Shared
  error: string | null;
};

type Action =
  | { type: 'SET_VIEW_MODE'; mode: ViewMode }
  | { type: 'SERIES_LOADED'; series: Series[] }
  | { type: 'SELECT_SERIES'; seriesId: number }
  | { type: 'ANALYTICS_LOADED'; analytics: CarAnalytics[] }
  | { type: 'ALL_ANALYTICS_LOADED'; analytics: CarAnalytics[] }
  | { type: 'SELECT_CAR'; carId: number }
  | { type: 'ANALYTICS_ERROR'; message: string };

function reducer(state: State, action: Action): State {
  switch (action.type) {
    case 'SET_VIEW_MODE':
      return {
        ...state,
        viewMode: action.mode,
        error: null,
        allAnalyticsLoading: action.mode === 'car' && state.allAnalytics.length === 0,
      };
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
    case 'ALL_ANALYTICS_LOADED': {
      const uniqueCarIds = [...new Set(action.analytics.map(a => a.carId))];
      return {
        ...state,
        allAnalytics: action.analytics,
        allAnalyticsLoading: false,
        selectedCarId: uniqueCarIds[0] ?? null,
      };
    }
    case 'SELECT_CAR':
      return { ...state, selectedCarId: action.carId };
    case 'ANALYTICS_ERROR':
      return {
        ...state,
        error: action.message,
        analyticsLoading: false,
        allAnalyticsLoading: false,
      };
    default:
      return state;
  }
}

const initialState: State = {
  viewMode: 'series',
  series: [],
  selectedSeriesId: null,
  analytics: [],
  seriesLoading: true,
  analyticsLoading: false,
  allAnalytics: [],
  allAnalyticsLoading: false,
  selectedCarId: null,
  error: null,
};

// ── Shared styles ─────────────────────────────────────────────────────────────

const cardStyle: React.CSSProperties = {
  boxShadow: '0 1px 0 rgba(255,255,255,.03) inset, 0 18px 40px -24px rgba(0,0,0,.8)',
};

// ── Sub-components ────────────────────────────────────────────────────────────

function FeaturedCarCard({
  data,
  flipLabels = false,
}: {
  data: CarAnalytics;
  flipLabels?: boolean;
}) {
  const improving = isImproving(data.percentileHistory);
  const lapDelta =
    data.personalBestLapSeconds != null && data.medianLapSeconds != null
      ? data.personalBestLapSeconds - data.medianLapSeconds
      : null;
  const isGold = data.bestPercentileRank >= 95;
  const sparkData = data.percentileHistory.map(h => h.percentileRank);
  const [trendStart, trendEnd] =
    data.percentileHistory.length >= 2 ? trendAxisLabels(data.percentileHistory) : ['', ''];
  const primaryLabel = flipLabels ? data.seriesName : data.carName;
  const secondaryLabel = flipLabels ? data.carName : data.seriesName;

  return (
    <div
      className={`card-r border bg-surface overflow-hidden ${
        isGold ? 'border-[#FFD700]/40' : 'border-line-2'
      }`}
      style={cardStyle}
    >
      <div className="card-p flex flex-col gap-4">
        <div className="flex items-start justify-between gap-3">
          <div>
            <h3 className="text-[18px] font-bold text-on-surface leading-tight">{primaryLabel}</h3>
            <p className="text-small-fluid text-on-surface-variant mt-1">{secondaryLabel}</p>
          </div>
          {improving && (
            <span className="inline-flex items-center gap-1 px-[10px] h-[24px] rounded-[7px] border border-primary-container/30 bg-primary-container/10 text-primary-container text-[11px] font-semibold shrink-0">
              <span className="material-symbols-outlined text-[12px]" aria-hidden="true">
                trending_up
              </span>
              Improving
            </span>
          )}
        </div>

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

        {sparkData.length >= 2 && (
          <div className="w-full flex flex-col gap-1.5">
            <p className="text-th text-on-surface-variant">Percentile Trend</p>
            <Sparkline data={sparkData} h={76} />
            <div className="flex justify-between">
              <span className="text-[10px] font-mono text-on-surface-variant">{trendStart}</span>
              <span className="text-[10px] font-mono text-on-surface-variant">{trendEnd}</span>
            </div>
          </div>
        )}

        <div className="flex items-center gap-6 pt-2 border-t border-line-2 flex-wrap">
          <div>
            <p className="text-th text-on-surface-variant">Total Weeks</p>
            <p className="font-mono text-mono-fluid font-semibold text-on-surface mt-0.5">
              {data.totalWeeks}
            </p>
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
              <p
                className={`font-mono text-mono-fluid font-semibold mt-0.5 ${lapDelta < 0 ? 'text-primary-container' : 'text-error'}`}
              >
                {lapDelta < 0 ? '' : '+'}
                {lapDelta.toFixed(3)}s
              </p>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

function SecondaryCarCard({
  data,
  flipLabels = false,
}: {
  data: CarAnalytics;
  flipLabels?: boolean;
}) {
  const improving = isImproving(data.percentileHistory);
  const sparkData = data.percentileHistory.map(h => h.percentileRank);
  const [trendStart, trendEnd] =
    data.percentileHistory.length >= 2 ? trendAxisLabels(data.percentileHistory) : ['', ''];
  const primaryLabel = flipLabels ? data.seriesName : data.carName;
  const secondaryLabel = flipLabels ? data.carName : data.seriesName;

  return (
    <div className="card-r border border-line-2 bg-surface overflow-hidden" style={cardStyle}>
      <div className="card-p flex flex-col gap-3">
        <div className="flex items-start justify-between gap-3">
          <div>
            <h3 className="text-section-head text-on-surface leading-tight">{primaryLabel}</h3>
            <p className="text-small-fluid text-on-surface-variant mt-0.5">{secondaryLabel}</p>
          </div>
          {improving && (
            <span className="inline-flex items-center gap-1 px-[10px] h-[22px] rounded-[7px] border border-primary-container/30 bg-primary-container/10 text-primary-container text-[10.5px] font-semibold shrink-0">
              IMPROVING
            </span>
          )}
        </div>

        <div className="font-mono text-[22px] font-bold text-primary-container leading-none">
          {topPercentLabel(data.latestPercentileRank)}
        </div>

        {sparkData.length >= 2 && (
          <div className="w-full flex flex-col gap-1">
            <p className="text-th text-on-surface-variant">Percentile Trend</p>
            <Sparkline data={sparkData} h={52} />
            <div className="flex justify-between">
              <span className="text-[10px] font-mono text-on-surface-variant">{trendStart}</span>
              <span className="text-[10px] font-mono text-on-surface-variant">{trendEnd}</span>
            </div>
          </div>
        )}

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
  const {
    viewMode,
    series,
    selectedSeriesId,
    analytics,
    seriesLoading,
    analyticsLoading,
    allAnalytics,
    allAnalyticsLoading,
    selectedCarId,
    error,
  } = state;

  // Series list (for series mode chips)
  useEffect(() => {
    api
      .getSeries()
      .then(s => dispatch({ type: 'SERIES_LOADED', series: s }))
      .catch(() => dispatch({ type: 'SERIES_LOADED', series: [] }));
  }, []);

  // Per-series analytics (series mode)
  useEffect(() => {
    if (!user || viewMode !== 'series' || selectedSeriesId === null) return;
    api
      .getMyAnalytics(selectedSeriesId)
      .then(a => dispatch({ type: 'ANALYTICS_LOADED', analytics: a }))
      .catch((e: Error) => dispatch({ type: 'ANALYTICS_ERROR', message: e.message }));
  }, [user, viewMode, selectedSeriesId]);

  // All analytics (car mode) — fetched once on demand
  useEffect(() => {
    if (!user || !allAnalyticsLoading) return;
    api
      .getMyAnalytics()
      .then(a => dispatch({ type: 'ALL_ANALYTICS_LOADED', analytics: a }))
      .catch((e: Error) => dispatch({ type: 'ANALYTICS_ERROR', message: e.message }));
  }, [user, allAnalyticsLoading]);

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
          <h2 className="text-page-title text-on-surface">Sign in to view analytics</h2>
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

  // Derived display data
  const isLoading = viewMode === 'series' ? analyticsLoading : allAnalyticsLoading;
  const displayAnalytics =
    viewMode === 'series' ? analytics : allAnalytics.filter(a => a.carId === selectedCarId);
  const [featuredCard, ...otherCards] = displayAnalytics;

  const uniqueCars = allAnalytics.reduce<{ id: number; name: string }[]>((acc, a) => {
    if (!acc.some(c => c.id === a.carId)) acc.push({ id: a.carId, name: a.carName });
    return acc;
  }, []);

  return (
    <main className="page-wrap">
      {/* Header */}
      <div className="mb-6">
        <p className="text-eyebrow text-primary-container">ANALYTICS</p>
        <h1 className="text-page-title text-on-surface mt-2 mb-1">Performance Deep Dive</h1>
      </div>

      {/* View mode toggle */}
      <div className="flex gap-1 mb-4 p-1 rounded-[10px] bg-white/[0.04] border border-white/[0.08] w-fit">
        {(['series', 'car'] as const).map(mode => (
          <button
            key={mode}
            onClick={() => dispatch({ type: 'SET_VIEW_MODE', mode })}
            className={`h-[28px] px-[14px] rounded-[7px] text-small-fluid font-semibold cursor-pointer transition-all ${
              viewMode === mode
                ? 'bg-primary-container text-on-primary-fixed'
                : 'text-on-surface-variant hover:text-on-surface'
            }`}
          >
            {mode === 'series' ? 'By Series' : 'By Car'}
          </button>
        ))}
      </div>

      {/* Series selector (series mode) */}
      {viewMode === 'series' && !seriesLoading && series.length > 0 && (
        <div className="flex items-center gap-3 mb-6">
          <label
            htmlFor="analytics-series-select"
            className="text-body-fluid text-on-surface-variant shrink-0"
          >
            Series:
          </label>
          <select
            id="analytics-series-select"
            value={selectedSeriesId ?? ''}
            onChange={e => dispatch({ type: 'SELECT_SERIES', seriesId: Number(e.target.value) })}
            className="text-body-fluid text-on-surface bg-surface-container border border-line-2 rounded-[9px] px-3 py-[7px] cursor-pointer focus:outline-none focus:border-primary-container/50 transition-colors"
          >
            {series.map(s => (
              <option key={s.id} value={s.id}>
                {s.name}
              </option>
            ))}
          </select>
        </div>
      )}

      {viewMode === 'series' && !seriesLoading && series.length === 0 && (
        <p className="text-body-fluid text-on-surface-variant mb-6">No active series found.</p>
      )}

      {/* Car selector (car mode) */}
      {viewMode === 'car' && !allAnalyticsLoading && uniqueCars.length > 0 && (
        <div className="flex items-center gap-3 mb-6">
          <label
            htmlFor="analytics-car-select"
            className="text-body-fluid text-on-surface-variant shrink-0"
          >
            Car:
          </label>
          <select
            id="analytics-car-select"
            value={selectedCarId ?? ''}
            onChange={e => dispatch({ type: 'SELECT_CAR', carId: Number(e.target.value) })}
            className="text-body-fluid text-on-surface bg-surface-container border border-line-2 rounded-[9px] px-3 py-[7px] cursor-pointer focus:outline-none focus:border-primary-container/50 transition-colors"
          >
            {uniqueCars.map(c => (
              <option key={c.id} value={c.id}>
                {c.name}
              </option>
            ))}
          </select>
        </div>
      )}

      {/* Loading */}
      {isLoading && (
        <p className="text-body-fluid text-on-surface-variant animate-pulse">
          Loading analytics&hellip;
        </p>
      )}

      {/* Error */}
      {!isLoading && error && <p className="text-body-fluid text-error">{error}</p>}

      {/* Empty state — series mode */}
      {viewMode === 'series' &&
        !isLoading &&
        !error &&
        analytics.length === 0 &&
        selectedSeriesId !== null && (
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

      {/* Empty state — car mode */}
      {viewMode === 'car' && !isLoading && !error && allAnalytics.length === 0 && (
        <div className="flex flex-col items-center gap-3 py-20 text-center">
          <span
            className="material-symbols-outlined text-4xl text-on-surface-variant"
            aria-hidden="true"
          >
            query_stats
          </span>
          <p className="text-body-fluid text-on-surface-variant max-w-xs">
            No percentile data yet.{' '}
            <Link to="/series" className="text-primary-container hover:opacity-80">
              Browse series
            </Link>{' '}
            and compute your percentile to start tracking trends.
          </p>
        </div>
      )}

      {/* Analytics grid */}
      {!isLoading && !error && displayAnalytics.length > 0 && (
        <div className="grid grid-cols-1 xl:grid-cols-3 gap-fluid">
          {featuredCard && (
            <div className="xl:col-span-3">
              <FeaturedCarCard data={featuredCard} flipLabels={viewMode === 'car'} />
            </div>
          )}
          {otherCards.map(card => (
            <SecondaryCarCard
              key={`${card.carId}-${card.seriesId}`}
              data={card}
              flipLabels={viewMode === 'car'}
            />
          ))}
        </div>
      )}
    </main>
  );
}
