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

function percentileTextColor(p: number): string {
  if (p >= 70) return 'text-primary-container';
  if (p >= 50) return 'text-[#FF9500]';
  return 'text-error';
}

function percentileBarColor(p: number): string {
  if (p >= 70) return 'bg-primary-container';
  if (p >= 50) return 'bg-[#FF9500]';
  return 'bg-error';
}

function ProjectedBadge() {
  return (
    <span className="inline-flex items-center gap-0.5 px-1.5 py-0.5 rounded text-[10px] font-label-caps tracking-wide bg-surface-container text-on-surface-variant border border-white/10">
      <span className="material-symbols-outlined text-[10px]" aria-hidden="true">calculate</span>
      Projected
    </span>
  );
}

function HeroCard({ rec }: { rec: CarRecommendation }) {
  const pColor = percentileTextColor(rec.percentileRank);
  const barColor = percentileBarColor(rec.percentileRank);
  return (
    <div className="glass-panel glow-gold gradient-border-gold rounded-2xl p-6 flex flex-col gap-6 relative overflow-hidden">
      <div className="absolute inset-0 bg-gradient-to-br from-[#FFD700]/5 via-transparent to-transparent pointer-events-none" />

      <div className="flex items-start justify-between gap-4 relative">
        <div className="flex flex-col gap-2">
          <div className="flex items-center gap-2">
            <span className="tier-badge-gold px-2 py-0.5 rounded font-label-caps text-[10px] uppercase tracking-widest font-bold">
              Top Match
            </span>
            <span className="font-label-caps text-label-caps text-on-surface-variant">
              #{rec.rank}
            </span>
          </div>
          <h2 className="font-headline-md text-headline-md text-on-surface tracking-tight">
            {rec.carName}
          </h2>
        </div>

        <div className="w-28 h-16 bg-surface-container-highest rounded-lg border border-white/10 overflow-hidden shrink-0">
          <div className="w-full h-full bg-gradient-to-br from-[#FFD700]/20 to-black" />
        </div>
      </div>

      <div className="flex gap-6 relative flex-wrap">
        <div className="flex flex-col gap-1">
          <span className="font-label-caps text-label-caps text-on-surface-variant">
            {rec.isProjected ? 'Projected Lap' : 'Best Lap'}
          </span>
          <div className="flex items-center gap-2">
            <span className="font-data-lg text-data-lg text-on-surface">
              {formatLap(rec.estimatedLapSeconds)}
            </span>
            {rec.isProjected && <ProjectedBadge />}
          </div>
        </div>
        <div className="flex flex-col gap-1">
          <span className="font-label-caps text-label-caps text-on-surface-variant">
            Your Percentile
          </span>
          <span className={`font-data-lg text-data-lg ${pColor}`}>
            {ordinal(rec.percentileRank)}
          </span>
        </div>
        <div className="flex flex-col gap-1">
          <span className="font-label-caps text-label-caps text-on-surface-variant">
            Sample Size
          </span>
          <span className="font-data-lg text-data-lg text-on-surface">
            {rec.sampleSize.toLocaleString()}
          </span>
        </div>
      </div>

      <div className="relative h-1.5 bg-surface-variant/40 rounded-full overflow-hidden">
        <div
          className={`absolute inset-y-0 left-0 ${barColor} rounded-full`}
          style={{ width: `${rec.percentileRank}%` }}
        />
      </div>

      <Link
        to="/series"
        className="inline-flex items-center gap-2 self-start px-4 py-2 rounded-lg bg-[#FFD700] text-black font-label-caps text-label-caps font-bold hover:bg-[#FFD700]/90 transition-colors"
      >
        Race this car
        <span className="material-symbols-outlined text-sm" aria-hidden="true">
          arrow_forward
        </span>
      </Link>
    </div>
  );
}

function RecommendationRow({ rec }: { rec: CarRecommendation }) {
  const pColor = percentileTextColor(rec.percentileRank);
  const barColor = percentileBarColor(rec.percentileRank);
  return (
    <div className="glass-panel rounded-xl p-4 flex items-center gap-4 hover:bg-white/[0.03] transition-colors group">
      <span className="font-data-md text-data-md text-on-surface-variant w-6 text-center shrink-0">
        #{rec.rank}
      </span>

      <div className="flex-1 min-w-0 flex flex-col gap-2">
        <div className="flex items-center justify-between gap-2 flex-wrap">
          <span className="font-body-sm text-body-sm text-on-surface font-medium truncate group-hover:text-primary-fixed-dim transition-colors">
            {rec.carName}
          </span>
          <div className="flex items-center gap-2 shrink-0">
            <span className="font-data-md text-data-md text-on-surface">
              {formatLap(rec.estimatedLapSeconds)}
            </span>
            {rec.isProjected && <ProjectedBadge />}
            <span className={`font-data-md text-data-md ${pColor}`}>
              {ordinal(rec.percentileRank)}
            </span>
          </div>
        </div>
        <div className="relative h-1 bg-surface-variant/40 rounded-full overflow-hidden">
          <div
            className={`absolute inset-y-0 left-0 ${barColor} rounded-full`}
            style={{ width: `${rec.percentileRank}%` }}
          />
        </div>
      </div>

      <div className="flex flex-col items-end gap-0.5 shrink-0">
        <span className="font-label-caps text-label-caps text-on-surface-variant">
          {rec.sampleSize.toLocaleString()}
        </span>
        <span className="font-label-caps text-label-caps text-on-surface-variant/60">
          entries
        </span>
      </div>

      <Link
        to="/series"
        className="shrink-0 px-3 py-1.5 rounded-lg border border-white/10 font-label-caps text-label-caps text-on-surface-variant hover:text-on-surface hover:border-white/20 transition-colors"
      >
        Race
      </Link>
    </div>
  );
}

export default function RecommendationsPage() {
  const [searchParams] = useSearchParams();
  const weekIdParam = searchParams.get('weekId');
  const weekId = weekIdParam != null ? Number(weekIdParam) : null;

  const [recs, setRecs] = useState<CarRecommendation[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (weekId == null) return;
    setLoading(true);
    setRecs([]);
    setError(null);
    api.getRecommendations(weekId)
      .then(setRecs)
      .catch((err: unknown) =>
        setError(err instanceof Error ? err.message : 'Failed to load recommendations.'))
      .finally(() => setLoading(false));
  }, [weekId]);

  if (weekId == null) {
    return (
      <main className="px-6 pt-8 pb-20 max-w-[1440px] mx-auto w-full flex flex-col gap-8">
        <header>
          <h1 className="font-headline-md text-headline-md text-on-surface tracking-tight mb-2">
            My Car Recommendations
          </h1>
          <p className="font-body-sm text-body-sm text-on-surface-variant max-w-prose">
            Navigate to a week from the{' '}
            <Link to="/series" className="text-primary-fixed-dim hover:text-primary transition-colors">
              Series page
            </Link>{' '}
            and click &ldquo;See my car recommendations&rdquo; to view your personalized rankings.
          </p>
        </header>
      </main>
    );
  }

  return (
    <main className="px-6 pt-8 pb-20 max-w-[1440px] mx-auto w-full flex flex-col gap-8">
      <header className="flex flex-col gap-2">
        <Link
          to="/series"
          className="inline-flex items-center gap-1 font-label-caps text-label-caps text-on-surface-variant hover:text-primary-fixed-dim transition-colors group w-fit"
        >
          <span
            className="material-symbols-outlined text-sm group-hover:-translate-x-1 transition-transform"
            aria-hidden="true"
          >
            arrow_back
          </span>
          Back to Series
        </Link>
        <h1 className="font-headline-md text-headline-md text-on-surface tracking-tight">
          My Car Recommendations
        </h1>
        <p className="font-body-sm text-body-sm text-on-surface-variant">
          Week {weekId} &mdash; ranked by your fastest estimated lap. Cars you&apos;ve driven use your
          actual best time; others are projected from your historical percentile.
        </p>
      </header>

      {loading && (
        <p className="text-on-surface-variant font-body-sm animate-pulse">Loading&hellip;</p>
      )}

      {error && (
        <div className="glass-panel rounded-lg p-6 font-body-sm text-error">{error}</div>
      )}

      {!loading && !error && recs.length === 0 && (
        <div className="glass-panel rounded-xl p-8 flex flex-col items-center gap-4 text-center max-w-md">
          <span className="material-symbols-outlined text-4xl text-on-surface-variant" aria-hidden="true">
            person_off
          </span>
          <p className="font-body-sm text-body-sm text-on-surface-variant">
            No recommendations available. Set your iRacing Customer ID in your{' '}
            <Link to="/profile" className="text-primary-fixed-dim hover:text-primary transition-colors">
              profile
            </Link>{' '}
            and upload a lap for at least one car in this series.
          </p>
        </div>
      )}

      {recs.length > 0 && (
        <div className="flex flex-col gap-4">
          <HeroCard rec={recs[0]} />

          {recs.length > 1 && (
            <div className="flex flex-col gap-3 mt-2">
              <h2 className="font-label-caps text-label-caps text-on-surface-variant uppercase tracking-widest">
                Other Options
              </h2>
              {recs.slice(1).map(r => (
                <RecommendationRow key={r.carId} rec={r} />
              ))}
            </div>
          )}
        </div>
      )}
    </main>
  );
}
