import { useEffect, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { api, type CarRecommendation } from '../services/api';

function formatLap(totalSeconds: number): string {
  const m = Math.floor(totalSeconds / 60);
  const s = totalSeconds - m * 60;
  return `${m}:${s.toFixed(3).padStart(6, '0')}`;
}

function ordinal(p: number): string {
  return `${p.toFixed(1)}th`;
}

function ProjectedBadge() {
  return (
    <span className="inline-flex items-center gap-0.5 px-1.5 py-0.5 rounded-[5px] text-[10px] font-semibold tracking-wide bg-surface-container text-on-surface-variant border border-line-2">
      <span className="material-symbols-outlined text-[10px]" aria-hidden="true">calculate</span>
      Projected
    </span>
  );
}

const cardStyle: React.CSSProperties = {
  boxShadow: '0 1px 0 rgba(255,255,255,.03) inset, 0 18px 40px -24px rgba(0,0,0,.8)',
};

const scanTexture: React.CSSProperties = {
  backgroundImage:
    'repeating-linear-gradient(115deg, rgba(255,255,255,0.04) 0 1px, transparent 1px 9px)',
};

function HeroCard({ rec, seriesId, weekNumber }: { rec: CarRecommendation; seriesId: number; weekNumber: number }) {
  return (
    <div
      className="card-r border border-line-2 bg-surface overflow-hidden"
      style={{ ...cardStyle, ...scanTexture }}
    >
      <div className="card-p flex flex-col gap-5">
        {/* Header */}
        <div className="flex items-start justify-between gap-4">
          <div className="flex flex-col gap-2">
            <span className="text-eyebrow text-primary-container">
              TOP MATCH
            </span>
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
            <p className="text-th text-on-surface-variant mb-1">
              {rec.isProjected ? 'Projected Lap' : 'Best Lap'}
            </p>
            <div className="flex items-center gap-2">
              <span className="font-mono text-[22px] font-bold text-on-surface leading-none">
                {formatLap(rec.estimatedLapSeconds)}
              </span>
              {rec.isProjected && <ProjectedBadge />}
            </div>
          </div>
          <div>
            <p className="text-th text-on-surface-variant mb-1">
              Your Percentile
            </p>
            <span className="font-mono text-[22px] font-bold text-primary-container leading-none">
              {ordinal(rec.percentileRank)}
            </span>
          </div>
          <div>
            <p className="text-th text-on-surface-variant mb-1">
              Sample Size
            </p>
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
          <span className="material-symbols-outlined text-[17px]" aria-hidden="true">arrow_forward</span>
        </Link>
      </div>
    </div>
  );
}

function RecommendationRow({ rec, seriesId, weekNumber }: { rec: CarRecommendation; seriesId: number; weekNumber: number }) {
  return (
    <div className="flex items-center justify-between gap-4 td-p border-b border-line-2 last:border-b-0 hover:bg-surface-container transition-colors">
      {/* Rank + name */}
      <div className="flex items-center gap-4 min-w-0">
        <span className="font-mono text-body-fluid font-bold w-7 text-center shrink-0 text-on-surface-variant">
          #{rec.rank}
        </span>
        <span className="text-body-fluid font-medium text-on-surface truncate">
          {rec.carName}
        </span>
      </div>

      {/* Lap + projected */}
      <div className="flex items-center gap-2 shrink-0">
        <span className="font-mono text-mono-fluid text-on-surface">
          {formatLap(rec.estimatedLapSeconds)}
        </span>
        {rec.isProjected && <ProjectedBadge />}
      </div>

      {/* Percentile + sample size */}
      <div className="flex items-center gap-4 shrink-0">
        <span className="font-mono text-mono-fluid text-primary-container font-semibold">
          {ordinal(rec.percentileRank)}
        </span>
        <span className="text-small-fluid text-on-surface-variant">
          {rec.sampleSize.toLocaleString()} entries
        </span>
        <Link
          to={`/series/${seriesId}/weeks/${weekNumber}`}
          className="inline-flex items-center gap-2 btn-fluid-sm border border-line-2 bg-surface-container text-on-surface font-semibold transition-all hover:bg-surface-container-high"
        >
          Race
        </Link>
      </div>
    </div>
  );
}

export default function RecommendationsPage() {
  const [searchParams] = useSearchParams();
  const weekNumberParam = searchParams.get('weekNumber');
  const seriesIdParam = searchParams.get('seriesId');
  const weekNumber = weekNumberParam != null ? Number(weekNumberParam) : null;
  const seriesId = seriesIdParam != null ? Number(seriesIdParam) : null;

  const [recs, setRecs] = useState<CarRecommendation[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (weekNumber == null || seriesId == null) return;
    setLoading(true);
    setRecs([]);
    setError(null);
    api.getRecommendations(seriesId, weekNumber)
      .then(setRecs)
      .catch((err: unknown) =>
        setError(err instanceof Error ? err.message : 'Failed to load recommendations.'))
      .finally(() => setLoading(false));
  }, [weekNumber, seriesId]);

  if (weekNumber == null || seriesId == null) {
    return (
      <main className="page-wrap">
        <div>
          <p className="text-eyebrow text-primary-container">
            RECOMMENDATIONS
          </p>
          <h1 className="text-page-title text-on-surface mt-2 mb-1">
            My Car Recommendations
          </h1>
          <p className="text-body-fluid text-on-surface-variant max-w-prose mt-2">
            Navigate to a week from the{' '}
            <Link to="/series" className="text-primary-container hover:opacity-80 transition-opacity">
              Series page
            </Link>{' '}
            and click &ldquo;See my car recommendations&rdquo; to view your personalized rankings.
          </p>
        </div>
      </main>
    );
  }

  return (
    <main className="page-wrap">
      <div className="mb-6">
        <Link
          to="/series"
          className="inline-flex items-center gap-2 text-body-fluid text-on-surface-variant hover:text-on-surface transition-colors mb-[10px]"
        >
          <span className="material-symbols-outlined text-[16px]" aria-hidden="true">arrow_back</span>
          Back to Series
        </Link>
        <p className="text-eyebrow text-primary-container">
          RECOMMENDATIONS
        </p>
        <h1 className="text-page-title text-on-surface mt-2 mb-1">
          My Car Recommendations
        </h1>
        <p className="text-body-fluid text-on-surface-variant">
          Week {weekNumber} &mdash; ranked by your fastest estimated lap. Cars you&apos;ve driven use your
          actual best time; others are projected from your historical percentile.
        </p>
      </div>

      {loading && (
        <p className="text-body-fluid text-on-surface-variant animate-pulse">Loading&hellip;</p>
      )}

      {error && (
        <div className="card-r border border-line-2 bg-surface p-6 text-body-fluid text-error" style={cardStyle}>
          {error}
        </div>
      )}

      {!loading && !error && recs.length === 0 && (
        <div
          className="card-r border border-line-2 bg-surface p-8 flex flex-col items-center gap-4 text-center w-full max-w-md"
          style={cardStyle}
        >
          <span className="material-symbols-outlined text-4xl text-on-surface-variant" aria-hidden="true">
            person_off
          </span>
          <p className="text-body-fluid text-on-surface-variant">
            No recommendations available. Set your iRacing Customer ID in your{' '}
            <Link to="/profile" className="text-primary-container hover:opacity-80 transition-opacity">
              profile
            </Link>{' '}
            and upload a lap for at least one car in this series.
          </p>
        </div>
      )}

      {recs.length > 0 && (
        <div className="flex flex-col gap-fluid">
          <HeroCard rec={recs[0]} seriesId={seriesId} weekNumber={weekNumber} />

          {recs.length > 1 && (
            <div
              className="card-r border border-line-2 bg-surface overflow-hidden"
              style={cardStyle}
            >
              {/* Header */}
              <div
                className="flex items-center justify-between card-hp border-b border-line-2"
                style={scanTexture}
              >
                <h2 className="text-section-head text-on-surface">Other Options</h2>
              </div>
              <div>
                {recs.slice(1).map(r => (
                  <RecommendationRow key={r.carId} rec={r} seriesId={seriesId} weekNumber={weekNumber} />
                ))}
              </div>
            </div>
          )}
        </div>
      )}
    </main>
  );
}
