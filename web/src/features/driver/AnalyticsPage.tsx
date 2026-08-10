import { useState } from 'react';
import { Link } from 'react-router';
import { api, type Series, type CarAnalytics } from '../../services/api';
import { useAuth } from '../../context/AuthContext';
import { formatLapTime } from '../../utils/lapTime';
import { topPercentLabel } from '../../utils/percentile';
import Sparkline from '../../components/Sparkline';
import ResourceView from '../../components/ResourceView';
import { useResource } from '../../hooks/useResource';

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

function isImproving(history: { percentileRank: number }[]): boolean {
  return (
    history.length >= 2 && history[history.length - 1].percentileRank > history[0].percentileRank
  );
}

type ViewMode = 'series' | 'car';

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
      className={`card-r card-shadow border bg-surface overflow-hidden ${
        isGold ? 'border-gold/40' : 'border-line-2'
      }`}
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
              isGold ? 'text-gold' : 'text-primary-container'
            }`}
          >
            {topPercentLabel(data.latestPercentileRank)}
          </div>
          {isGold && (
            <span className="mb-1 px-2 py-0.5 rounded-[6px] bg-gold text-black text-[11px] font-bold">
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
    <div className="card-r card-shadow border border-line-2 bg-surface overflow-hidden">
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
  const [viewMode, setViewMode] = useState<ViewMode>('series');
  const [seriesSelection, setSeriesSelection] = useState<number | null>(null);
  const [carSelection, setCarSelection] = useState<number | null>(null);
  const [refreshVersion, setRefreshVersion] = useState(0);
  const [computing, setComputing] = useState(false);
  const [computeError, setComputeError] = useState<string | null>(null);

  const seriesResource = useResource(signal => api.getSeries(signal), []);
  const series = seriesResource.status === 'ok' ? seriesResource.data : [];
  const selectedSeriesId = seriesSelection ?? series[0]?.id ?? null;
  const analyticsResource = useResource(
    signal => api.getMyAnalytics(selectedSeriesId!, signal),
    [user, viewMode, selectedSeriesId, refreshVersion],
    {
      enabled: !!user && viewMode === 'series' && selectedSeriesId !== null,
      fallbackMessage: 'Failed to load analytics.',
    }
  );
  const allAnalyticsResource = useResource(
    signal => api.getMyAnalytics(undefined, signal),
    [user, viewMode],
    {
      enabled: !!user && viewMode === 'car',
      fallbackMessage: 'Failed to load analytics.',
    }
  );
  const analytics = analyticsResource.status === 'ok' ? analyticsResource.data : [];
  const allAnalytics = allAnalyticsResource.status === 'ok' ? allAnalyticsResource.data : [];

  // Computing recommendations upserts the CarPercentileResult rows analytics reads,
  // so one call populates a first-visit-empty view (works in demo and live modes).
  const computePercentiles = async (sel: Series) => {
    if (sel.currentWeekNumber == null) return;
    setComputing(true);
    setComputeError(null);
    try {
      await api.getRecommendations(sel.id, sel.currentWeekNumber);
      setRefreshVersion(version => version + 1);
    } catch {
      setComputeError('Could not compute percentiles — try the Recommendations page.');
    } finally {
      setComputing(false);
    }
  };

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
  const seriesLoading = seriesResource.status === 'loading';
  const currentResource = viewMode === 'series' ? analyticsResource : allAnalyticsResource;
  const resourceEnabled = viewMode === 'car' || selectedSeriesId !== null;
  const isLoading = resourceEnabled && currentResource.status === 'loading';
  const resourceReady = currentResource.status === 'ok';
  const uniqueCars = allAnalytics.reduce<{ id: number; name: string }[]>((acc, a) => {
    if (!acc.some(c => c.id === a.carId)) acc.push({ id: a.carId, name: a.carName });
    return acc;
  }, []);
  const selectedCarId = carSelection ?? uniqueCars[0]?.id ?? null;
  const displayAnalytics =
    viewMode === 'series' ? analytics : allAnalytics.filter(a => a.carId === selectedCarId);
  const [featuredCard, ...otherCards] = displayAnalytics;

  const selectedSeries = series.find(s => s.id === selectedSeriesId) ?? null;

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
            onClick={() => setViewMode(mode)}
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
            onChange={e => setSeriesSelection(Number(e.target.value))}
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
      {viewMode === 'car' && allAnalyticsResource.status === 'ok' && uniqueCars.length > 0 && (
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
            onChange={e => setCarSelection(Number(e.target.value))}
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

      {resourceEnabled && !isLoading && (
        <ResourceView
          resource={currentResource}
          notLinkedReason="Link your iRacing account to view personalized analytics."
        />
      )}

      {/* Empty state — series mode */}
      {viewMode === 'series' &&
        !isLoading &&
        resourceReady &&
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
              <Link to="/series" className="text-primary-container underline hover:opacity-80">
                Browse series
              </Link>{' '}
              and compute your percentile to start tracking trends.
            </p>
            {selectedSeries && selectedSeries.currentWeekNumber != null && (
              <>
                <p className="text-body-fluid text-on-surface-variant">
                  Analytics builds from your computed percentiles — nothing here yet.
                </p>
                <button
                  onClick={() => void computePercentiles(selectedSeries)}
                  disabled={computing}
                  className="btn-fluid bg-primary-container text-on-primary-container rounded-lg font-semibold disabled:opacity-60"
                >
                  {computing ? 'Computing…' : 'Compute my percentiles'}
                </button>
                {computeError && <p className="text-small-fluid text-error">{computeError}</p>}
                <Link
                  to="/recommendations"
                  className="text-small-fluid text-primary-container underline"
                >
                  Or open Recommendations
                </Link>
              </>
            )}
          </div>
        )}

      {/* Empty state — car mode */}
      {viewMode === 'car' && resourceReady && allAnalytics.length === 0 && (
        <div className="flex flex-col items-center gap-3 py-20 text-center">
          <span
            className="material-symbols-outlined text-4xl text-on-surface-variant"
            aria-hidden="true"
          >
            query_stats
          </span>
          <p className="text-body-fluid text-on-surface-variant max-w-xs">
            No percentile data yet.{' '}
            <Link to="/series" className="text-primary-container underline hover:opacity-80">
              Browse series
            </Link>{' '}
            and compute your percentile to start tracking trends.
          </p>
          <Link to="/recommendations" className="text-small-fluid text-primary-container underline">
            Or open Recommendations
          </Link>
        </div>
      )}

      {/* Analytics grid */}
      {resourceReady && displayAnalytics.length > 0 && (
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
