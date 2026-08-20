import { useMemo } from 'react';
import { Link, useSearchParams } from 'react-router';
import { api, type CarRecommendation } from '../../services/api';
import { formatLapTime } from '../../utils/lapTime';
import { lapEvidenceDescription, lapEvidenceLabel } from '../../utils/lapEvidence';
import { raceWeekLabel } from '../../utils/raceWeek';
import CalculationSource from '../../components/CalculationSource';
import { usePaceSource } from '../../context/PaceSourceContext';
import ResourceView from '../../components/ResourceView';
import { useResource } from '../../hooks/useResource';

// A percentile is a share, not a placement — rendering 92.3 as "92.3th" read as an ordinal the
// driver never held. The placement is carried separately as topSharePercent.
function percentile(p: number): string {
  return `${p.toFixed(1)}%`;
}

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
    <div className="card-r card-shadow scan-texture border border-line-2 bg-surface overflow-hidden">
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
            {rec.bestLapEvidence != null && (
              <p
                className="text-small-fluid text-on-surface-variant mt-1"
                title={lapEvidenceDescription(rec.bestLapEvidence)}
              >
                {lapEvidenceLabel(rec.bestLapEvidence)}
              </p>
            )}
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
              {percentile(rec.percentileRank)}
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
        <tr className="scan-texture border-b border-line-2">
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
              {r.bestLapEvidence != null && (
                <span
                  className="block font-sans text-small-fluid text-on-surface-variant"
                  title={lapEvidenceDescription(r.bestLapEvidence)}
                >
                  {lapEvidenceLabel(r.bestLapEvidence)}
                </span>
              )}
            </td>
            <td className="td-p font-mono text-mono-fluid text-on-surface text-right">
              {formatLapTime(r.projectedLapSeconds)}
            </td>
            <td className="td-p font-mono text-mono-fluid text-primary-container font-semibold text-right">
              {percentile(r.percentileRank)}
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

  const { value: paceSource, setValue: setPaceSource, evidenceOptions } = usePaceSource();
  const seriesResource = useResource(signal => api.getSeries(signal), []);
  const allSeries = useMemo(
    () =>
      seriesResource.status === 'ok'
        ? [...seriesResource.data].sort((a, b) => a.name.localeCompare(b.name))
        : [],
    [seriesResource]
  );

  const selectedSeriesId =
    seriesIdParam != null ? Number(seriesIdParam) : (allSeries[0]?.id ?? null);
  const selectedSeries = allSeries.find(s => s.id === selectedSeriesId) ?? null;
  const weekNumber = selectedSeries?.currentWeekNumber ?? null;

  const recommendations = useResource(
    signal => api.getRecommendations(selectedSeriesId, weekNumber!, evidenceOptions, signal),
    [selectedSeriesId, weekNumber, evidenceOptions],
    {
      enabled: selectedSeriesId != null && weekNumber != null,
      fallbackMessage: 'Failed to load recommendations.',
    }
  );

  return (
    <main className="page-wrap">
      <div className="mb-6">
        <p className="text-eyebrow text-primary-container">RECOMMENDATIONS</p>
        <h1 className="text-page-title text-on-surface mt-2 mb-4">My Car Recommendations</h1>

        <ResourceView resource={seriesResource} />

        {seriesResource.status === 'ok' && allSeries.length === 0 && (
          <p className="text-body-fluid text-on-surface-variant">No active series found.</p>
        )}

        {seriesResource.status === 'ok' && allSeries.length > 0 && (
          <div className="flex items-start justify-between gap-4 flex-wrap">
            {/* Left: week description */}
            <p className="text-body-fluid text-on-surface-variant max-w-prose">
              {weekNumber != null ? (
                <>
                  {raceWeekLabel(weekNumber)} &mdash; ranked by your fastest estimated lap. Cars
                  you&apos;ve driven use your actual best time; others are projected from your
                  historical percentile.
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

        {selectedSeriesId != null && weekNumber != null && (
          <ResourceView
            resource={recommendations}
            notLinkedReason="Link your iRacing account to see personalized recommendations."
          />
        )}

        {recommendations.status === 'ok' && recommendations.data.length === 0 && (
          <div className="card-r card-shadow border border-line-2 bg-surface p-8 flex flex-col items-center gap-4 text-center w-full max-w-md">
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

        {recommendations.status === 'ok' &&
          recommendations.data.length > 0 &&
          selectedSeriesId != null &&
          weekNumber != null && (
            <>
              <HeroCard
                rec={recommendations.data[0]}
                seriesId={selectedSeriesId}
                weekNumber={weekNumber}
              />

              {recommendations.data.length > 1 && (
                <div className="card-r card-shadow border border-line-2 bg-surface overflow-hidden">
                  <div className="scan-texture flex items-center justify-between card-hp border-b border-line-2">
                    <h2 className="text-section-head text-on-surface">Other Options</h2>
                  </div>
                  <RecommendationTable
                    recs={recommendations.data.slice(1)}
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
