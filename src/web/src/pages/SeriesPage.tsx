import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { api, type Series } from '../services/api';

const cardGradients = [
  'from-[#FFD700]/10 to-black',
  'from-primary-fixed/10 to-black',
  'from-[#FF9500]/10 to-black',
  'from-secondary-container/10 to-black',
  'from-gray-700/30 to-black',
];

const cardHoverBorders = [
  'group-hover:border-[#FFD700]/50',
  'group-hover:border-primary-fixed-dim/50',
  'group-hover:border-[#FF9500]/50',
  'group-hover:border-secondary-container/50',
  'group-hover:border-white/20',
];

function SeriesCard({ s, index }: { s: Series; index: number }) {
  const gradient = cardGradients[index % cardGradients.length];
  const hoverBorder = cardHoverBorders[index % cardHoverBorders.length];
  const active = s.currentWeekId != null;

  const inner = (
    <div
      className={`bg-surface border border-white/10 rounded-xl overflow-hidden flex flex-col transition-all duration-300 relative h-full group ${hoverBorder}`}
    >
      {/* Image placeholder */}
      <div className="h-32 w-full bg-surface-container-high relative overflow-hidden">
        <div className={`absolute inset-0 bg-gradient-to-br ${gradient}`} />
        <div className="absolute inset-0 bg-gradient-to-b from-transparent to-black/60" />
        {!active && (
          <div className="absolute top-3 left-3 bg-surface-container-highest border border-white/20 text-on-surface font-label-caps text-[10px] px-2 py-1 rounded-sm">
            OFF SEASON
          </div>
        )}
      </div>

      {/* Content */}
      <div className="p-4 flex flex-col gap-3 flex-1 relative z-10">
        <div>
          <h3 className="font-headline-sm text-headline-sm text-on-surface leading-tight">
            {s.name}
          </h3>
          <p className="font-body-sm text-[12px] text-on-surface-variant mt-1">
            Season {s.seasonId}
            {active ? ` · Week ${s.currentWeekId}` : ''}
          </p>
        </div>

        <div className="flex items-center gap-2 mt-auto pt-4 border-t border-white/5">
          <div className="flex items-center gap-1 text-on-surface-variant bg-surface-container-lowest px-2 py-1 rounded-md border border-white/5">
            <span className="material-symbols-outlined text-[14px]" aria-hidden="true">
              {active ? 'speed' : 'pause_circle'}
            </span>
            <span className="font-label-caps text-[10px]">
              {active ? `Week ${s.currentWeekId}` : 'No active week'}
            </span>
          </div>
          {active && (
            <span className="ml-auto font-label-caps text-[10px] text-primary-fixed-dim uppercase tracking-wider opacity-0 group-hover:opacity-100 transition-opacity">
              View data →
            </span>
          )}
        </div>
      </div>
    </div>
  );

  if (active) {
    return (
      <Link
        to={`/series/${s.id}/weeks/${s.currentWeekId}`}
        aria-label={s.name}
        className="col-span-4"
      >
        {inner}
      </Link>
    );
  }

  return <div className="col-span-4">{inner}</div>;
}

export default function SeriesPage() {
  const [series, setSeries] = useState<Series[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [search, setSearch] = useState('');

  useEffect(() => {
    api.getSeries()
      .then(setSeries)
      .catch((err: unknown) =>
        setError(err instanceof Error ? err.message : 'Failed to load series.'))
      .finally(() => setLoading(false));
  }, []);

  if (loading) {
    return (
      <main className="px-6 pt-8 pb-20 max-w-[1440px] mx-auto w-full">
        <p className="text-on-surface-variant font-body-sm animate-pulse">Loading&hellip;</p>
      </main>
    );
  }

  if (error) {
    return (
      <main className="px-6 pt-8 pb-20 max-w-[1440px] mx-auto w-full">
        <div className="glass-panel rounded-lg p-6 font-body-sm text-error">{error}</div>
      </main>
    );
  }

  if (series.length === 0) {
    return (
      <main className="px-6 pt-8 pb-20 max-w-[1440px] mx-auto w-full">
        <p className="font-body-sm text-on-surface-variant">
          No active series found. Check back after the ingestion worker has run.
        </p>
      </main>
    );
  }

  const filtered = search.trim()
    ? series.filter(s => s.name.toLowerCase().includes(search.toLowerCase()))
    : series;

  return (
    <main className="px-6 pt-8 pb-20 max-w-[1440px] mx-auto w-full flex flex-col gap-8">
      {/* Header */}
      <header className="flex flex-col md:flex-row md:items-center justify-between gap-4">
        <div>
          <h2 className="font-headline-md text-[48px] leading-none font-extrabold tracking-tighter text-on-surface">
            Active Series
          </h2>
          <p className="font-body-sm text-body-sm text-on-surface-variant mt-1">
            {series.length} series available
          </p>
        </div>

        {/* Search */}
        <div className="flex items-center gap-3 bg-surface-container border border-white/10 rounded-lg p-1 w-full md:w-auto">
          <div className="relative w-full md:w-64">
            <span
              className="material-symbols-outlined absolute left-3 top-1/2 -translate-y-1/2 text-on-surface-variant text-[18px] pointer-events-none"
              aria-hidden="true"
            >
              search
            </span>
            <input
              type="text"
              value={search}
              onChange={e => setSearch(e.target.value)}
              placeholder="Search series…"
              className="w-full bg-transparent border-none focus:ring-0 text-on-surface font-body-sm text-body-sm pl-10 py-2 placeholder:text-on-surface-variant/50 outline-none"
            />
          </div>
        </div>
      </header>

      {/* Grid */}
      {filtered.length === 0 ? (
        <p className="font-body-sm text-on-surface-variant">No series match your search.</p>
      ) : (
        <div className="grid grid-cols-4 md:grid-cols-12 gap-4">
          {filtered.map((s, i) => (
            <SeriesCard key={s.id} s={s} index={i} />
          ))}
        </div>
      )}
    </main>
  );
}
