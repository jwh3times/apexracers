import { useEffect, useReducer, useState } from 'react';
import { Link, useLocation, useParams } from 'react-router-dom';
import { api, type PercentileResult } from '../services/api';
import { useAuth } from '../context/AuthContext';

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

// 270° arc gauge. Starts at ~7:30, ends at ~4:30 (gap at bottom).
function PercentileGauge({ value }: { value: number }) {
  const r = 80;
  const cx = 100;
  const cy = 100;
  const C = 2 * Math.PI * r;
  const arcLen = C * 0.75;

  const clamped = Math.min(Math.max(value, 0), 100);
  const filled = arcLen * (clamped / 100);

  return (
    <svg width="200" height="200" viewBox="0 0 200 200" aria-hidden="true">
      <circle
        cx={cx} cy={cy} r={r}
        fill="none"
        strokeWidth={10}
        stroke="rgba(255,255,255,0.06)"
        strokeDasharray={`${arcLen} ${C - arcLen}`}
        transform={`rotate(135 ${cx} ${cy})`}
        strokeLinecap="round"
      />
      <circle
        cx={cx} cy={cy} r={r}
        fill="none"
        strokeWidth={10}
        stroke="#00e479"
        strokeDasharray={`${filled} ${C - filled}`}
        transform={`rotate(135 ${cx} ${cy})`}
        strokeLinecap="round"
        style={{ filter: 'drop-shadow(0 0 6px rgba(0,228,121,0.5))' }}
      />
    </svg>
  );
}

function rankLabel(p: number): string {
  if (p >= 90) return 'Elite';
  if (p >= 75) return 'Fast';
  if (p >= 50) return 'Above average';
  if (p >= 25) return 'Below average';
  return 'Still learning';
}

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
    <main className="px-6 pt-8 pb-20 max-w-[800px] mx-auto w-full flex flex-col gap-8">
      {/* Breadcrumb */}
      <Link
        to={`/series/${seriesId}/weeks/${weekNumber}`}
        className="inline-flex items-center gap-1 font-label-caps text-label-caps text-on-surface-variant hover:text-primary-fixed-dim transition-colors group w-fit"
      >
        <span
          className="material-symbols-outlined text-sm group-hover:-translate-x-1 transition-transform"
          aria-hidden="true"
        >
          arrow_back
        </span>
        Car breakdown
      </Link>

      {/* Header */}
      <header className="flex flex-col gap-2">
        <h1 className="font-headline-md text-headline-md text-on-surface tracking-tight">
          {carName}
        </h1>
        <p className="font-body-sm text-body-sm text-on-surface-variant">
          Week {weekNumber} &mdash; lap time percentile
        </p>
      </header>

      {/* Loading */}
      {loading && (
        <p className="font-body-sm text-body-sm text-on-surface-variant animate-pulse">
          Loading&hellip;
        </p>
      )}

      {/* Manual lookup form — only shown when no iRacing ID is saved in the profile */}
      {!profileId && !loading && (
        <div className="glass-panel rounded-xl p-6 flex flex-col gap-4">
          <div className="flex items-start gap-3 p-3 bg-surface-container rounded-lg border border-white/5">
            <span
              className="material-symbols-outlined text-on-surface-variant text-[18px] mt-0.5 shrink-0"
              aria-hidden="true"
            >
              info
            </span>
            <p className="font-body-sm text-body-sm text-on-surface-variant">
              Set your iRacing Customer ID in your{' '}
              <Link
                to="/profile"
                className="text-primary-fixed-dim hover:underline"
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
                className="font-body-sm text-body-sm text-on-surface"
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
                className="bg-surface-container border border-white/10 rounded-lg px-4 py-2.5 font-data-md text-data-md text-on-surface placeholder:text-on-surface-variant/40 focus:outline-none focus:ring-1 focus:ring-primary-fixed-dim w-full max-w-xs"
              />
            </div>
            <button
              type="submit"
              disabled={!customerId}
              className="self-start px-5 py-2.5 rounded-lg bg-primary-fixed-dim text-on-primary font-label-caps text-label-caps disabled:opacity-40 disabled:cursor-not-allowed hover:brightness-110 transition-all active:scale-95"
            >
              Look up my percentile
            </button>
          </form>
        </div>
      )}

      {/* Percentile result */}
      {result && (
        <div className="glass-panel rounded-xl p-8 flex flex-col items-center gap-6">
          <div className="relative w-[200px] h-[200px]">
            <PercentileGauge value={result.percentileRank} />
            <div className="absolute inset-0 flex flex-col items-center justify-center pb-3">
              <span
                className="font-data-lg text-on-surface leading-none"
                style={{ fontSize: '38px' }}
              >
                {result.percentileRank.toFixed(1)}
              </span>
              <span className="font-label-caps text-label-caps text-on-surface-variant mt-1">
                PERCENTILE
              </span>
            </div>
          </div>

          <div className="text-center flex flex-col gap-1">
            <p className="font-headline-md text-headline-md text-on-surface">
              You beat{' '}
              <span className="text-primary-fixed-dim">
                {result.percentileRank.toFixed(1)}%
              </span>{' '}
              of the field
            </p>
            <p className="font-body-sm text-body-sm text-on-surface-variant">
              {rankLabel(result.percentileRank)}
            </p>
          </div>

          <div className="flex gap-8 border-t border-white/10 pt-5 w-full justify-center">
            <div className="flex flex-col items-center gap-1">
              <span className="font-data-lg text-data-lg text-on-surface">
                {result.sampleSize.toLocaleString()}
              </span>
              <span className="font-label-caps text-label-caps text-on-surface-variant">
                Drivers in field
              </span>
            </div>
            <div className="w-px bg-white/10" />
            <div className="flex flex-col items-center gap-1">
              <span className="font-data-md text-data-md text-on-surface">
                {formatDate(result.computedAt)}
              </span>
              <span className="font-label-caps text-label-caps text-on-surface-variant">
                Computed
              </span>
            </div>
          </div>
        </div>
      )}

      {/* Not found */}
      {notFound && (
        <div className="glass-panel rounded-xl p-6 flex items-start gap-3">
          <span
            className="material-symbols-outlined text-on-surface-variant mt-0.5"
            aria-hidden="true"
          >
            search_off
          </span>
          <div className="flex flex-col gap-1">
            <p className="font-body-sm text-body-sm text-on-surface">
              No race lap found for the{' '}
              <span className="font-medium">{carName}</span> this week.
            </p>
            <p className="font-body-sm text-body-sm text-on-surface-variant text-[12px]">
              Upload your telemetry to record a lap, then check back here.
            </p>
          </div>
        </div>
      )}

      {/* Error */}
      {error && (
        <div className="glass-panel rounded-xl p-6 border border-error/20">
          <p className="font-body-sm text-body-sm text-error">{error}</p>
        </div>
      )}
    </main>
  );
}
