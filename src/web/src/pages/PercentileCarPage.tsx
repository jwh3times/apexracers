import { useEffect, useReducer, useState } from 'react';
import { Link, useLocation, useParams } from 'react-router-dom';
import { api, type PercentileResult } from '../services/api';
import { useAuth } from '../context/AuthContext';
import PercentileBadge from '../components/PercentileBadge';

type FetchState = {
  loading: boolean;
  result: PercentileResult | null;
  error: string | null;
  notFound: boolean;
};

type FetchAction =
  | { type: 'start' }
  | { type: 'success'; result: PercentileResult }
  | { type: 'not_found' }
  | { type: 'error'; message: string };

function fetchReducer(_: FetchState, action: FetchAction): FetchState {
  switch (action.type) {
    case 'start':
      return { loading: true, result: null, error: null, notFound: false };
    case 'success':
      return { loading: false, result: action.result, error: null, notFound: false };
    case 'not_found':
      return { loading: false, result: null, error: null, notFound: true };
    case 'error':
      return { loading: false, result: null, error: action.message, notFound: false };
  }
}

function formatDate(iso: string): string {
  return new Date(iso).toLocaleString(undefined, {
    dateStyle: 'medium',
    timeStyle: 'short',
  });
}

function rankLabel(p: number): string {
  if (p >= 90) return 'Elite';
  if (p >= 75) return 'Fast';
  if (p >= 50) return 'Above average';
  if (p >= 25) return 'Below average';
  return 'Still learning';
}

const cardStyle: React.CSSProperties = {
  boxShadow: '0 1px 0 rgba(255,255,255,.03) inset, 0 18px 40px -24px rgba(0,0,0,.8)',
};

const scanTexture: React.CSSProperties = {
  backgroundImage:
    'repeating-linear-gradient(115deg, rgba(255,255,255,0.04) 0 1px, transparent 1px 9px)',
};

export default function PercentileCarPage() {
  const { seriesId, weekNumber, carId } = useParams<{
    seriesId: string;
    weekNumber: string;
    carId: string;
  }>();
  const location = useLocation();
  const { user } = useAuth();
  const carName =
    (location.state as { carName?: string } | null)?.carName ?? `Car ${carId}`;

  const [customerId, setCustomerId] = useState('');
  const [{ loading, result, error, notFound }, dispatch] = useReducer(fetchReducer, {
    loading: false,
    result: null,
    error: null,
    notFound: false,
  });

  const profileId = user?.iRacingCustomerId ?? null;

  function runFetch(id: number) {
    dispatch({ type: 'start' });
    api
      .getPercentile(Number(seriesId), Number(weekNumber), Number(carId), id)
      .then(data => dispatch({ type: 'success', result: data }))
      .catch((err: unknown) => {
        const msg = err instanceof Error ? err.message : '';
        if (msg.includes('404')) dispatch({ type: 'not_found' });
        else dispatch({ type: 'error', message: msg || 'Failed to load percentile.' });
      });
  }

  // Auto-fetch when the profile has an iRacing customer ID
  useEffect(() => {
    if (profileId && seriesId && weekNumber && carId) {
      runFetch(profileId);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [profileId, seriesId, weekNumber, carId]);

  function handleLookup(e: React.FormEvent<HTMLFormElement>) {
    e.preventDefault();
    const id = Number(customerId.trim());
    if (!id) return;
    runFetch(id);
  }

  return (
    <main className="page-wrap">
      {/* Breadcrumb */}
      <Link
        to={`/series/${seriesId}/weeks/${weekNumber}`}
        className="inline-flex items-center gap-2 text-body-fluid text-on-surface-variant hover:text-on-surface transition-colors mb-[10px]"
      >
        <span className="material-symbols-outlined text-[16px]" aria-hidden="true">arrow_back</span>
        Car breakdown
      </Link>

      {/* Header */}
      <div className="mb-6">
        <p className="text-eyebrow text-primary-container">
          WEEK {weekNumber} · PERCENTILE
        </p>
        <h1 className="text-page-title text-on-surface mt-2 mb-1">
          {carName}
        </h1>
        <p className="text-body-fluid text-on-surface-variant">
          Week {weekNumber} &mdash; lap time percentile
        </p>
      </div>

      {/* Loading */}
      {loading && (
        <p className="text-body-fluid text-on-surface-variant animate-pulse mb-4">
          Loading&hellip;
        </p>
      )}

      {/* Manual lookup form — only shown when no iRacing ID is saved in the profile */}
      {!profileId && !loading && (
        <div className="glass-panel rounded-xl p-6 flex flex-col gap-4 mb-6">
          <div className="flex items-start gap-3 p-3 bg-surface-container rounded-lg border border-line">
            <span
              className="material-symbols-outlined text-on-surface-variant text-[18px] mt-0.5 shrink-0"
              aria-hidden="true"
            >
              info
            </span>
            <p className="text-body-fluid text-on-surface-variant">
              Set your iRacing Customer ID in your{' '}
              <Link
                to="/profile"
                className="text-primary-container hover:opacity-80"
              >
                profile
              </Link>{' '}
              to skip this step.
            </p>
          </div>

          <form onSubmit={handleLookup} className="flex flex-col gap-4">
            <div className="flex flex-col gap-1.5">
              <label
                htmlFor="customer-id"
                className="text-body-fluid text-on-surface"
              >
                iRacing Customer ID
              </label>
              <input
                id="customer-id"
                type="number"
                min="1"
                value={customerId}
                onChange={e => setCustomerId(e.target.value)}
                placeholder="e.g. 100042"
                className="bg-surface-container border border-line-2 rounded-[10px] px-4 py-2.5 font-mono text-body-fluid text-on-surface placeholder:text-on-surface-variant/40 focus:outline-none focus:ring-1 focus:ring-primary-container/40 w-full max-w-xs"
              />
            </div>
            <button
              type="submit"
              disabled={!customerId}
              className="self-start inline-flex items-center gap-2 btn-fluid border-transparent bg-primary-container text-on-primary-fixed font-semibold disabled:opacity-40 disabled:cursor-not-allowed transition-all"
              style={{ boxShadow: customerId ? '0 0 26px -8px var(--color-primary-container)' : undefined }}
            >
              Look up my percentile
            </button>
          </form>
        </div>
      )}

      {/* Percentile result */}
      {result && (() => {
        const topPct = Math.max(1, Math.ceil(100 - result.percentileRank));
        return (
          <div
            className="card-r border border-line-2 bg-surface overflow-hidden mb-4"
            style={{ ...cardStyle, ...scanTexture }}
          >
            <div className="grid md:grid-cols-2 gap-0">
              {/* Left: badge + summary */}
              <div className="flex flex-col items-center justify-center gap-[18px] p-8 border-b md:border-b-0 md:border-r border-line-2">
                <PercentileBadge pct={topPct} size="lg" />
                <div className="text-center">
                  <p className="text-section-head text-on-surface">
                    You beat{' '}
                    <span className="text-primary-container">{result.percentileRank.toFixed(1)}%</span>
                    {' '}of the field
                  </p>
                  <p className="text-body-fluid text-on-surface-variant mt-1">
                    {rankLabel(result.percentileRank)}
                  </p>
                </div>
              </div>

              {/* Right: stats */}
              <div className="flex flex-col justify-center gap-6 p-8">
                <div className="grid grid-cols-2 gap-x-8 gap-y-6">
                  <div>
                    <p className="text-th text-on-surface-variant mb-1">
                      Drivers in field
                    </p>
                    <p className="font-mono text-[24px] font-bold text-on-surface leading-none">
                      {result.sampleSize.toLocaleString()}
                    </p>
                  </div>
                  <div>
                    <p className="text-th text-on-surface-variant mb-1">
                      Computed
                    </p>
                    <p className="text-body-fluid text-on-surface">
                      {formatDate(result.computedAt)}
                    </p>
                  </div>
                </div>
              </div>
            </div>
          </div>
        );
      })()}

      {/* Not found */}
      {notFound && (
        <div className="card-r border border-line-2 bg-surface p-6 flex items-start gap-3 mb-4" style={cardStyle}>
          <span
            className="material-symbols-outlined text-on-surface-variant mt-0.5"
            aria-hidden="true"
          >
            search_off
          </span>
          <div className="flex flex-col gap-1">
            <p className="text-body-fluid text-on-surface">
              No race lap found for the{' '}
              <span className="font-medium">{carName}</span> this week.
            </p>
            <p className="text-small-fluid text-on-surface-variant">
              Upload your telemetry to record a lap, then check back here.
            </p>
          </div>
        </div>
      )}

      {/* Error */}
      {error && (
        <div className="card-r border border-error/20 bg-surface p-6 mb-4" style={cardStyle}>
          <p className="text-body-fluid text-error">{error}</p>
        </div>
      )}
    </main>
  );
}
