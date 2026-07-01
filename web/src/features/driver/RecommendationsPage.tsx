import { useEffect, useReducer, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import {
  api,
  IRacingNotLinkedError,
  type CarRecommendation,
  type Series,
} from '../../services/api';
import { formatLapTime } from '../../utils/lapTime';
import CalculationSource, { type PaceSourceValue } from '../../components/CalculationSource';

type FetchState =
  | { status: 'idle' }
  | { status: 'loading' }
  | { status: 'ok'; recs: CarRecommendation[] }
  | { status: 'not-linked' }
  | { status: 'error'; message: string };

type FetchAction =
  | { type: 'FETCH_START' }
  | { type: 'FETCH_OK'; recs: CarRecommendation[] }
  | { type: 'FETCH_NOT_LINKED' }
  | { type: 'FETCH_ERROR'; message: string }
  | { type: 'RESET' };

function fetchReducer(_state: FetchState, action: FetchAction): FetchState {
  switch (action.type) {
    case 'FETCH_START':
      return { status: 'loading' };
    case 'FETCH_OK':
      return { status: 'ok', recs: action.recs };
    case 'FETCH_NOT_LINKED':
      return { status: 'not-linked' };
    case 'FETCH_ERROR':
      return { status: 'error', message: action.message };
    case 'RESET':
      return { status: 'idle' };
  }
}

function ordinal(p: number): string {
  return `${p.toFixed(1)}th`;
}

const cardStyle: React.CSSProperties = {
  boxShadow: '0 1px 0 rgba(255,255,255,.03) inset, 0 18px 40px -24px rgba(0,0,0,.8)',
};

const scanTexture: React.CSSProperties = {
  backgroundImage:
    'repeating-linear-gradient(115deg, rgba(255,255,255,0.04) 0 1px, transparent 1px 9px)',
};

function HeroCard({
  rec,
  seriesId,
  weekNumber,
}: {
  rec: CarRecommendation;
  seriesId: number;
  weekNumber: number;
}) {
  return (
    <div
      className="card-r border border-line-2 bg-surface overflow-hidden"
      style={{ ...cardStyle, ...scanTexture }}
    >
      <div className="card-p flex flex-col gap-5">
        {/* Header */}
        <div className="flex items-start justify-between gap-4">
          <div className="flex flex-col gap-2">
            <span className="text-eyebrow text-primary-container">TOP MATCH</span>
            <h2 className="text-[22px] font-bold tracking-[-0.025em] text-on-surface leading-tight">
              {rec.carName}
            </h2>
          </div>
          <div className="flex items-center justify-center w-9 h-9 rounded-[10px] border border-primary-container/30 bg-primary-container/10 text-primary-container font-mono font-bold text-[14px] shrink-0">
            #{rec.rank}
          </div>
        </div>

        {/* Stats row */}
        <div className="flex gap-6 flex-wrap">
          <div>
            <p className="text-th text-on-surface-variant mb-1">Best Lap</p>
            <span className="font-mono text-[22px] font-bold text-on-surface leading-none">
              {rec.bestLapSeconds != null ? formatLapTime(rec.bestLapSeconds) : '—'}
            </span>
          </div>
          <div>
            <p className="text-th text-on-surface-variant mb-1">Projected Lap</p>
            <span className="font-mono text-[22px] font-bold text-on-surface leading-none">
              {formatLapTime(rec.projectedLapSeconds)}
            </span>
          </div>
          <div>
            <p className="text-th text-on-surface-variant mb-1">Your Percentile</p>
            <span className="font-mono text-[22px] font-bold text-primary-container leading-none">
              {ordinal(rec.percentileRank)}
            </span>
          </div>
          <div>
            <p className="text-th text-on-surface-variant mb-1">Sample Size</p>
            <span className="font-mono text-[22px] font-bold text-on-surface leading-none">
              {rec.sampleSize.toLocaleString()}
            </span>
          </div>
        </div>

        {/* Progress bar */}
        <div className="h-[3px] bg-surface-container-highest rounded-full overflow-hidden">
          <div
            className="h-full bg-primary-container rounded-full"
            style={{ width: `${rec.percentileRank}%` }}
          />
        </div>

        {/* CTA */}
        <Link
          to={`/series/${seriesId}/weeks/${weekNumber}`}
          className="self-start inline-flex items-center gap-2 btn-fluid border-transparent bg-primary-container text-on-primary-fixed font-semibold transition-all"
          style={{ boxShadow: '0 0 26px -8px var(--color-primary-container)' }}
        >
          Race this car
          <span className="material-symbols-outlined text-[17px]" aria-hidden="true">
            arrow_forward
          </span>
        </Link>
      </div>
    </div>
  );
}

function RecommendationTable({
  recs,
  seriesId,
  weekNumber,
}: {
  recs: CarRecommendation[];
  seriesId: number;
  weekNumber: number;
}) {
  return (
    <table className="w-full border-collapse">
      <thead>
        <tr className="border-b border-line-2" style={scanTexture}>
          <th className="th-p text-th text-on-surface-variant text-left w-10">#</th>
          <th className="th-p text-th text-on-surface-variant text-left">Car</th>
          <th className="th-p text-th text-on-surface-variant text-right">Best Lap</th>
          <th className="th-p text-th text-on-surface-variant text-right">Projected Lap</th>
          <th className="th-p text-th text-on-surface-variant text-right w-28">Percentile</th>
          <th className="th-p text-th text-on-surface-variant text-right w-24">Entries</th>
          <th className="th-p w-20" />
        </tr>
      </thead>
      <tbody>
        {recs.map(r => (
          <tr
            key={r.carId}
            className="border-b border-line-2 last:border-b-0 hover:bg-surface-container transition-colors"
          >
            <td className="td-p font-mono text-body-fluid text-on-surface-variant text-center">
              #{r.rank}
            </td>
            <td className="td-p text-body-fluid font-medium text-on-surface max-w-0">
              <span className="block truncate">{r.carName}</span>
            </td>
            <td className="td-p font-mono text-mono-fluid text-on-surface text-right">
              {r.bestLapSeconds != null ? formatLapTime(r.bestLapSeconds) : '—'}
            </td>
            <td className="td-p font-mono text-mono-fluid text-on-surface text-right">
              {formatLapTime(r.projectedLapSeconds)}
            </td>
            <td className="td-p font-mono text-mono-fluid text-primary-container font-semibold text-right">
              {ordinal(r.percentileRank)}
            </td>
            <td className="td-p text-small-fluid text-on-surface-variant text-right tabular-nums">
              {r.sampleSize.toLocaleString()}
            </td>
            <td className="td-p text-right">
              <Link
                to={`/series/${seriesId}/weeks/${weekNumber}`}
                className="inline-flex items-center gap-2 btn-fluid-sm border border-line-2 bg-surface-container text-on-surface font-semibold transition-all hover:bg-surface-container-high"
              >
                Race
              </Link>
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}

export default function RecommendationsPage() {
  const [searchParams, setSearchParams] = useSearchParams();
  const seriesIdParam = searchParams.get('seriesId');

  const [allSeries, setAllSeries] = useState<Series[]>([]);
  const [seriesLoading, setSeriesLoading] = useState(true);
  const [paceSource, setPaceSource] = useState<PaceSourceValue>({ mode: 'official', sessions: [] });

  const [fetchState, dispatch] = useReducer(fetchReducer, { status: 'idle' } as FetchState);

  useEffect(() => {
    api
      .getSeries()
      .then(data => {
        const sorted = [...data].sort((a, b) => a.name.localeCompare(b.name));
        setAllSeries(sorted);
      })
      .catch(() => setAllSeries([]))
      .finally(() => setSeriesLoading(false));
  }, []);

  const selectedSeriesId =
    seriesIdParam != null ? Number(seriesIdParam) : (allSeries[0]?.id ?? null);
  const selectedSeries = allSeries.find(s => s.id === selectedSeriesId) ?? null;
  const weekNumber = selectedSeries?.currentWeekNumber ?? null;

  useEffect(() => {
    if (selectedSeriesId == null || weekNumber == null) {
      dispatch({ type: 'RESET' });
      return;
    }
    dispatch({ type: 'FETCH_START' });
    const blended = paceSource.mode === 'blend';
    api
      .getRecommendations(selectedSeriesId, weekNumber, {
        includePersonalLaps: blended,
        personalLapTypes:
          blended && paceSource.sessions.length > 0 ? paceSource.sessions : undefined,
      })
      .then(recs => dispatch({ type: 'FETCH_OK', recs }))
      .catch((err: unknown) => {
        if (err instanceof IRacingNotLinkedError) {
          dispatch({ type: 'FETCH_NOT_LINKED' });
          return;
        }
        dispatch({
          type: 'FETCH_ERROR',
          message: err instanceof Error ? err.message : 'Failed to load recommendations.',
        });
      });
  }, [selectedSeriesId, weekNumber, paceSource]);

  return (
    <main className="page-wrap">
      <div className="mb-6">
        <p className="text-eyebrow text-primary-container">RECOMMENDATIONS</p>
        <h1 className="text-page-title text-on-surface mt-2 mb-4">My Car Recommendations</h1>

        {seriesLoading && (
          <p className="text-body-fluid text-on-surface-variant animate-pulse">
            Loading series&hellip;
          </p>
        )}

        {!seriesLoading && allSeries.length === 0 && (
          <p className="text-body-fluid text-on-surface-variant">No active series found.</p>
        )}

        {!seriesLoading && allSeries.length > 0 && (
          <div className="flex items-start justify-between gap-4 flex-wrap">
            {/* Left: week description */}
            <p className="text-body-fluid text-on-surface-variant max-w-prose">
              {weekNumber != null ? (
                <>
                  Week {weekNumber} &mdash; ranked by your fastest estimated lap. Cars you&apos;ve
                  driven use your actual best time; others are projected from your historical
                  percentile.
                </>
              ) : (
                <>This series does not have an active week.</>
              )}
            </p>

            {/* Right: series selector */}
            <div className="flex items-center gap-3 shrink-0">
              <label htmlFor="series-select" className="text-body-fluid text-on-surface-variant">
                Series:
              </label>
              <select
                id="series-select"
                value={selectedSeriesId ?? ''}
                onChange={e => {
                  if (e.target.value) setSearchParams({ seriesId: e.target.value });
                }}
                className="text-body-fluid text-on-surface bg-surface-container border border-line-2 rounded-[9px] px-3 py-[7px] cursor-pointer focus:outline-none focus:border-primary-container/50 transition-colors"
              >
                {allSeries.map(s => (
                  <option key={s.id} value={s.id}>
                    {s.name}
                  </option>
                ))}
              </select>
            </div>
          </div>
        )}
      </div>

      <div className="flex flex-col gap-fluid">
        {/* Pace source selector */}
        {selectedSeries && weekNumber != null && (
          <CalculationSource value={paceSource} onChange={setPaceSource} />
        )}

        {fetchState.status === 'loading' && (
          <p className="text-body-fluid text-on-surface-variant animate-pulse">Loading&hellip;</p>
        )}

        {fetchState.status === 'error' && (
          <div
            className="card-r border border-line-2 bg-surface p-6 text-body-fluid text-error"
            style={cardStyle}
          >
            {fetchState.message}
          </div>
        )}

        {fetchState.status === 'not-linked' && (
          <div
            className="card-r border border-line-2 bg-surface p-8 flex flex-col items-center gap-4 text-center w-full max-w-md"
            style={cardStyle}
          >
            <span
              className="material-symbols-outlined text-4xl text-primary-container"
              aria-hidden="true"
            >
              link_off
            </span>
            <p className="text-body-fluid text-on-surface-variant">
              Link your iRacing account to see personalized recommendations. Add your iRacing
              customer ID in{' '}
              <Link
                to="/settings"
                className="text-primary-container underline hover:opacity-80 transition-opacity"
              >
                Settings
              </Link>
              .
            </p>
          </div>
        )}

        {fetchState.status === 'ok' && fetchState.recs.length === 0 && (
          <div
            className="card-r border border-line-2 bg-surface p-8 flex flex-col items-center gap-4 text-center w-full max-w-md"
            style={cardStyle}
          >
            <span
              className="material-symbols-outlined text-4xl text-on-surface-variant"
              aria-hidden="true"
            >
              person_off
            </span>
            <p className="text-body-fluid text-on-surface-variant">
              No recommendations available. Set your iRacing Customer ID in your{' '}
              <Link
                to="/profile"
                className="text-primary-container underline hover:opacity-80 transition-opacity"
              >
                profile
              </Link>{' '}
              and upload a lap for at least one car in this series.
            </p>
          </div>
        )}

        {fetchState.status === 'ok' &&
          fetchState.recs.length > 0 &&
          selectedSeriesId != null &&
          weekNumber != null && (
            <>
              <HeroCard
                rec={fetchState.recs[0]}
                seriesId={selectedSeriesId}
                weekNumber={weekNumber}
              />

              {fetchState.recs.length > 1 && (
                <div
                  className="card-r border border-line-2 bg-surface overflow-hidden"
                  style={cardStyle}
                >
                  <div
                    className="flex items-center justify-between card-hp border-b border-line-2"
                    style={scanTexture}
                  >
                    <h2 className="text-section-head text-on-surface">Other Options</h2>
                  </div>
                  <RecommendationTable
                    recs={fetchState.recs.slice(1)}
                    seriesId={selectedSeriesId}
                    weekNumber={weekNumber}
                  />
                </div>
              )}
            </>
          )}
      </div>
    </main>
  );
}
