import { useEffect, useReducer, useState } from 'react';
import { Link, useLocation, useParams } from 'react-router';
import { api, ApiError, type DistributionBin, type PercentileResult } from '../../services/api';
import { useAuth } from '../../context/AuthContext';
import PercentileBadge from '../../components/PercentileBadge';
import CalculationSource from '../../components/CalculationSource';
import { usePaceSource } from '../../context/PaceSourceContext';
import { formatLapTime } from '../../utils/lapTime';
import { fieldSizeMessage } from '../../utils/fieldSize';
import { lapEvidenceDescription, lapEvidenceLabel } from '../../utils/lapEvidence';
import { raceWeekNumber } from '../../utils/raceWeek';

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

/** Day only — the excluded lap's date is context, not a timestamp to reconcile. */
function formatDay(iso: string): string {
  return new Date(iso).toLocaleDateString(undefined, { dateStyle: 'medium' });
}

function formatGap(yourBest: number, fieldBest: number): string {
  const diff = yourBest - fieldBest;
  return diff <= 0 ? 'You hold P1' : `+${diff.toFixed(3)}s`;
}

function DistributionChart({ bins }: { bins: DistributionBin[] }) {
  if (bins.length === 0) return null;
  const maxCount = Math.max(...bins.map(b => b.count), 1);
  const w = 460;
  const h = 80;
  const barW = w / bins.length;
  const gap = 1.5;

  return (
    <svg viewBox={`0 0 ${w} ${h}`} className="w-full" preserveAspectRatio="none" aria-hidden="true">
      {bins.map((bin, i) => {
        const barH = Math.max(2, (bin.count / maxCount) * (h - 2));
        const x = i * barW + gap;
        const y = h - barH;
        return (
          <rect
            key={i}
            x={x}
            y={y}
            width={barW - gap * 2}
            height={barH}
            rx={2}
            fill={bin.containsUser ? 'var(--color-primary-container)' : 'rgba(255,255,255,0.12)'}
          />
        );
      })}
    </svg>
  );
}

export default function PercentileCarPage() {
  const { seriesId, weekNumber, carId } = useParams<{
    seriesId: string;
    weekNumber: string;
    carId: string;
  }>();
  const location = useLocation();
  const { user } = useAuth();
  const carName = (location.state as { carName?: string } | null)?.carName ?? `Car ${carId}`;

  const [customerId, setCustomerId] = useState('');
  const [lookedUpId, setLookedUpId] = useState<number | null>(null);
  const { value: paceSource, setValue: setPaceSource, evidenceOptions } = usePaceSource();
  const [{ loading, result, error, notFound }, dispatch] = useReducer(fetchReducer, {
    loading: false,
    result: null,
    error: null,
    notFound: false,
  });

  const profileId = user?.iRacingCustomerId ?? null;
  const effectiveId = profileId ?? lookedUpId;

  useEffect(() => {
    if (!effectiveId || !seriesId || !weekNumber || !carId) return;
    let active = true;
    dispatch({ type: 'start' });
    api
      .getPercentile(
        Number(seriesId),
        Number(weekNumber),
        Number(carId),
        effectiveId,
        evidenceOptions
      )
      .then(data => {
        if (active) dispatch({ type: 'success', result: data });
      })
      .catch((err: unknown) => {
        if (!active) return;
        // Branch on the status, not the message: a bare NotFound() is filled in by ASP.NET as
        // ProblemDetails, so the thrown message is its title ("Not Found"), never a status line.
        if (err instanceof ApiError && err.status === 404) dispatch({ type: 'not_found' });
        else
          dispatch({
            type: 'error',
            message: (err instanceof Error ? err.message : '') || 'Failed to load percentile.',
          });
      });
    return () => {
      active = false;
    };
  }, [effectiveId, seriesId, weekNumber, carId, evidenceOptions]);

  function handleLookup(e: React.FormEvent<HTMLFormElement>) {
    e.preventDefault();
    const id = Number(customerId.trim());
    if (!id) return;
    setLookedUpId(id);
  }

  const trackSubtitle = result
    ? [
        result.seriesName,
        result.trackName,
        result.trackConfigName && result.trackConfigName !== result.trackName
          ? `· ${result.trackConfigName}`
          : null,
      ]
        .filter(Boolean)
        .join(' — ')
    : null;

  return (
    <main className="page-wrap">
      {/* Breadcrumb */}
      <Link
        to={`/series/${seriesId}/weeks/${weekNumber}`}
        className="inline-flex items-center gap-2 text-small-fluid text-on-surface-variant hover:text-on-surface transition-colors mb-[10px]"
      >
        <span className="material-symbols-outlined text-[15px]" aria-hidden="true">
          arrow_back
        </span>
        Car breakdown
      </Link>

      {/* Header */}
      <div className="mb-6">
        <p className="text-eyebrow text-primary-container">
          WEEK {raceWeekNumber(Number(weekNumber))} · PERCENTILE
        </p>
        <h1 className="text-page-title text-on-surface mt-2 mb-1">{carName}</h1>
        {trackSubtitle && (
          <p className="text-body-fluid text-on-surface-variant">{trackSubtitle}</p>
        )}
      </div>

      {/* Loading */}
      {loading && (
        <p className="text-body-fluid text-on-surface-variant animate-pulse mb-4">
          Loading&hellip;
        </p>
      )}

      {/* Manual lookup form — only shown when no iRacing ID is saved in the profile */}
      {!profileId && !loading && (
        <div className="card-r card-shadow border border-line-2 bg-surface p-6 flex flex-col gap-4 mb-6">
          <div className="flex items-start gap-3 p-3 bg-surface-container rounded-lg border border-line">
            <span
              className="material-symbols-outlined text-on-surface-variant text-[18px] mt-0.5 shrink-0"
              aria-hidden="true"
            >
              info
            </span>
            <p className="text-body-fluid text-on-surface-variant">
              Set your iRacing Customer ID in your{' '}
              <Link to="/profile" className="text-primary-container underline hover:opacity-80">
                profile
              </Link>{' '}
              to skip this step.
            </p>
          </div>

          <form onSubmit={handleLookup} className="flex flex-col gap-4">
            <div className="flex flex-col gap-1.5">
              <label htmlFor="customer-id" className="text-body-fluid text-on-surface">
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
              style={{
                boxShadow: customerId ? '0 0 26px -8px var(--color-primary-container)' : undefined,
              }}
            >
              Look up my percentile
            </button>
          </form>
        </div>
      )}

      {/* Pace source selector — shown once we have an ID to fetch for */}
      {effectiveId !== null && !loading && (
        <CalculationSource value={paceSource} onChange={setPaceSource} className="mb-6" />
      )}

      {/* Percentile result */}
      {result &&
        (() => {
          // Both come from the server, computed over the same field as the percentile — deriving
          // them from the rank here cannot work, since the rank splits ties and counts the driver
          // in its own denominator.
          const driversAhead = result.fieldPosition - 1;
          const rank = result.fieldPosition;
          const gapToP1 = formatGap(result.yourBestLapSeconds, result.fieldBestLapSeconds);

          return (
            <div className="card-r card-shadow border border-line-2 bg-surface overflow-hidden">
              <div className="grid md:grid-cols-2 gap-0">
                {/* Left: badge + headline + stat grid */}
                <div className="scan-texture flex flex-col items-center justify-center gap-[22px] p-8 border-b md:border-b-0 md:border-r border-line-2">
                  {result.isPercentilePresentable ? (
                    <>
                      <PercentileBadge topSharePercent={result.topSharePercent} size="lg" />
                      <div className="text-center">
                        <p className="text-section-head text-on-surface">
                          You're faster than{' '}
                          <span className="text-primary-container">
                            {result.percentileRank.toFixed(1)}%
                          </span>{' '}
                          of the field
                        </p>
                        <p className="text-body-fluid text-on-surface-variant mt-1">
                          P{rank} of {result.sampleSize.toLocaleString()} drivers
                        </p>
                      </div>
                    </>
                  ) : (
                    <div className="text-center">
                      <p className="text-section-head text-on-surface">Not enough times yet</p>
                      <p className="text-body-fluid text-on-surface-variant mt-1">
                        {fieldSizeMessage(result.sampleSize)}
                      </p>
                    </div>
                  )}

                  {/* 2×2 stat grid */}
                  <div className="w-full grid grid-cols-2 gap-px bg-line-2 border border-line-2 card-r overflow-hidden">
                    {[
                      {
                        label: 'Your best',
                        value: formatLapTime(result.yourBestLapSeconds),
                        note: lapEvidenceLabel(result.yourBestLapEvidence),
                        noteTitle: lapEvidenceDescription(result.yourBestLapEvidence),
                      },
                      { label: 'Field best', value: formatLapTime(result.fieldBestLapSeconds) },
                      {
                        label: 'Field median',
                        value: formatLapTime(result.fieldMedianLapSeconds),
                      },
                      { label: 'Gap to P1', value: gapToP1 },
                      ...(result.worldRecordLapSeconds != null
                        ? [
                            {
                              label: 'World record',
                              value: formatLapTime(result.worldRecordLapSeconds),
                            },
                            {
                              label: 'Gap to WR',
                              value:
                                result.worldRecordGapSeconds != null
                                  ? `+${result.worldRecordGapSeconds.toFixed(3)}`
                                  : '—',
                            },
                          ]
                        : []),
                    ].map(stat => (
                      <div key={stat.label} className="bg-surface p-4 flex flex-col gap-1">
                        <span className="text-th text-on-surface-variant">{stat.label}</span>
                        <span className="text-mono-fluid text-on-surface font-semibold">
                          {stat.value}
                        </span>
                        {'note' in stat && stat.note && (
                          <span
                            className="text-small-fluid text-on-surface-variant"
                            title={'noteTitle' in stat ? stat.noteTitle : undefined}
                          >
                            {stat.note}
                          </span>
                        )}
                      </div>
                    ))}
                  </div>
                </div>

                {/* Right: distribution + extra stats */}
                <div className="flex flex-col justify-between gap-6 p-8">
                  {/* Histogram */}
                  <div>
                    <p className="text-th text-on-surface-variant mb-3">Lap time distribution</p>
                    <DistributionChart bins={result.distribution} />
                    <div className="flex items-center gap-2 mt-2">
                      <span className="w-3 h-3 rounded-sm bg-primary-container shrink-0" />
                      <span className="text-small-fluid text-on-surface-variant">
                        Your position
                      </span>
                    </div>
                  </div>

                  {/* Three quick stats */}
                  <div className="flex flex-col gap-4">
                    {(
                      [
                        ...(result.isPercentilePresentable
                          ? [
                              {
                                label: 'Percentile rank',
                                value: `${result.percentileRank.toFixed(1)}%`,
                              },
                              { label: 'Drivers ahead', value: driversAhead.toLocaleString() },
                            ]
                          : []),
                        { label: 'Drivers in field', value: result.sampleSize.toLocaleString() },
                      ] as const
                    ).map(stat => (
                      <div key={stat.label} className="flex items-baseline justify-between gap-4">
                        <span className="text-body-fluid text-on-surface-variant">
                          {stat.label}
                        </span>
                        <span className="text-mono-fluid text-on-surface font-semibold">
                          {stat.value}
                        </span>
                      </div>
                    ))}
                  </div>

                  {/* What the race week's bound left out */}
                  {result.uploadedBestOutsideWeek && (
                    <div className="flex items-start gap-2 border-t border-line-2 pt-4">
                      <span
                        className="material-symbols-outlined text-on-surface-variant text-[18px] mt-0.5 shrink-0"
                        aria-hidden="true"
                      >
                        info
                      </span>
                      <p className="text-small-fluid text-on-surface-variant">
                        Your fastest uploaded lap here —{' '}
                        <span className="font-mono text-on-surface">
                          {formatLapTime(result.uploadedBestOutsideWeek.lapSeconds)}
                        </span>{' '}
                        on {formatDay(result.uploadedBestOutsideWeek.recordedAt)} — was set outside
                        this race week, so it isn't counted here. The field is this week's race
                        laps, and a lap from another week was driven on a different track state,
                        weather and setup.
                      </p>
                    </div>
                  )}

                  {/* Cache note */}
                  <p className="text-small-fluid text-on-surface-variant border-t border-line-2 pt-4">
                    Computed {formatDate(result.computedAt)}
                  </p>
                </div>
              </div>
            </div>
          );
        })()}

      {/* Not found */}
      {notFound && (
        <div className="card-r card-shadow border border-line-2 bg-surface p-6 flex items-start gap-3 mb-4">
          <span
            className="material-symbols-outlined text-on-surface-variant mt-0.5"
            aria-hidden="true"
          >
            search_off
          </span>
          <div className="flex flex-col gap-1">
            <p className="text-body-fluid text-on-surface">
              No race lap found for the <span className="font-medium">{carName}</span> this week.
            </p>
            <p className="text-small-fluid text-on-surface-variant">
              Upload your telemetry to record a lap, then check back here.
            </p>
          </div>
        </div>
      )}

      {/* Error */}
      {error && (
        <div className="card-r card-shadow border border-error/20 bg-surface p-6 mb-4">
          <p className="text-body-fluid text-error">{error}</p>
        </div>
      )}
    </main>
  );
}
