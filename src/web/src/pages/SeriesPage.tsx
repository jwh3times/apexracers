import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { api, type Series } from '../services/api';

const tierConfig = [
  {
    badge: 'CLASS A · PRO',
    badgeClass: 'bg-[#FFD700] text-black shadow-[0_0_10px_rgba(255,215,0,0.5)]',
    hoverBorder: 'group-hover:border-[#FFD700]/50',
    glowFrom: 'from-[#FFD700]/10',
    gradientFrom: 'from-yellow-900/40',
    icon: 'emoji_events',
  },
  {
    badge: 'CLASS C · POPULAR',
    badgeClass: 'bg-[#00FF88] text-[#003919] shadow-[0_0_10px_rgba(0,255,136,0.3)]',
    hoverBorder: 'group-hover:border-[#00FF88]/50',
    glowFrom: null,
    gradientFrom: 'from-emerald-900/40',
    icon: 'directions_car',
  },
  {
    badge: 'CLASS D · FIXED',
    badgeClass: 'bg-[#FF9500] text-white',
    hoverBorder: 'group-hover:border-[#FF9500]/50',
    glowFrom: null,
    gradientFrom: 'from-orange-900/40',
    icon: 'speed',
  },
  {
    badge: 'ROOKIE',
    badgeClass: 'bg-surface-container-highest border border-white/20 text-on-surface',
    hoverBorder: 'group-hover:border-white/20',
    glowFrom: null,
    gradientFrom: 'from-slate-800/40',
    icon: 'directions_car',
  },
];

function SeriesCard({ s, index }: { s: Series; index: number }) {
  const tier = tierConfig[index % tierConfig.length];
  const active = s.currentWeekNumber != null;

  const inner = (
    <div
      className={`bg-surface border border-white/10 rounded-xl overflow-hidden flex flex-col transition-all duration-300 relative h-full group ${tier.hoverBorder}`}
    >
      {tier.glowFrom && (
        <div
          className={`absolute inset-0 bg-gradient-to-b ${tier.glowFrom} to-transparent opacity-0 group-hover:opacity-100 transition-opacity pointer-events-none`}
        />
      )}

      {/* Image area */}
      <div className="h-32 w-full bg-surface-container-high relative overflow-hidden">
        <div
          className={`absolute inset-0 bg-gradient-to-br ${tier.gradientFrom} to-surface-container-high opacity-60 mix-blend-luminosity group-hover:mix-blend-normal transition-all duration-500`}
        />
        <div className="absolute inset-0 bg-gradient-to-b from-transparent to-black/60" />

        <div
          className={`absolute top-3 left-3 font-label-caps text-[10px] px-2 py-1 rounded-sm ${
            active ? tier.badgeClass : 'bg-surface-container-highest border border-white/20 text-on-surface'
          }`}
        >
          {active ? tier.badge : 'OFF SEASON'}
        </div>
      </div>

      {/* Content */}
      <div className="p-4 flex flex-col gap-3 flex-1 bg-surface relative z-10">
        <div>
          <h3 className="font-headline-sm text-headline-sm text-on-surface leading-tight">{s.name}</h3>
          <p className="font-body-sm text-[12px] text-on-surface-variant mt-1">
            Season {s.seasonId}
            {active ? ` · Week ${s.currentWeekNumber}` : ''}
          </p>
        </div>

        <div className="flex items-center gap-2 mt-auto pt-4 border-t border-white/5">
          <div className="flex items-center gap-1 text-on-surface-variant bg-surface-container-lowest px-2 py-1 rounded-md border border-white/5">
            <span className="material-symbols-outlined text-[14px]" aria-hidden="true">
              {active ? tier.icon : 'pause_circle'}
            </span>
            <span className="font-label-caps text-[10px]">
              {active ? `Week ${s.currentWeekNumber}` : 'No active week'}
            </span>
          </div>
          {active && (
            <div className="ml-auto text-right">
              <div className="font-label-caps text-[10px] text-on-surface-variant uppercase tracking-wider mb-1">
                Season
              </div>
              <div className="font-data-md text-data-md text-primary-fixed-dim">{s.seasonId}</div>
            </div>
          )}
        </div>
      </div>
    </div>
  );

  if (active) {
    return (
      <Link to={`/series/${s.id}/weeks/${s.currentWeekNumber}`} aria-label={s.name} className="col-span-4">
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
    api
      .getSeries()
      .then(setSeries)
      .catch((err: unknown) =>
        setError(err instanceof Error ? err.message : 'Failed to load series.'),
      )
      .finally(() => setLoading(false));
  }, []);

  if (loading) {
    return (
      <main className="px-margin pt-8 pb-20 max-w-container-max mx-auto w-full">
        <p className="text-on-surface-variant font-body-sm animate-pulse">Loading&hellip;</p>
      </main>
    );
  }

  if (error) {
    return (
      <main className="px-margin pt-8 pb-20 max-w-container-max mx-auto w-full">
        <div className="rounded-lg p-6 font-body-sm text-error bg-surface-container border border-white/10">
          {error}
        </div>
      </main>
    );
  }

  if (series.length === 0) {
    return (
      <main className="px-margin pt-8 pb-20 max-w-container-max mx-auto w-full">
        <p className="font-body-sm text-on-surface-variant">
          No active series found. Check back after the ingestion worker has run.
        </p>
      </main>
    );
  }

  const filtered = search.trim()
    ? series.filter(s => s.name.toLowerCase().includes(search.toLowerCase()))
    : series;

  const firstActive = series.find(s => s.currentWeekNumber != null);
  const subtitle = firstActive
    ? `Season ${firstActive.seasonId} · Week ${firstActive.currentWeekNumber} Data`
    : `${series.length} series available`;

  return (
    <main className="px-margin pt-8 pb-24 md:pb-12 max-w-container-max mx-auto w-full flex flex-col gap-8">
      <header className="flex flex-col md:flex-row md:items-center justify-between gap-4">
        <div>
          <h2 className="font-display-lg text-display-lg text-on-surface">Active Series</h2>
          <p className="font-body-sm text-body-sm text-on-surface-variant mt-1">{subtitle}</p>
        </div>

        <div className="flex items-center gap-3 bg-surface-container border border-white/10 rounded-lg p-1 backdrop-blur-md w-full md:w-auto">
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
              placeholder="Search series or cars..."
              className="w-full bg-transparent border-none focus:ring-0 text-on-surface font-body-sm text-body-sm pl-10 py-2 placeholder:text-on-surface-variant/50 outline-none"
            />
          </div>
          <div className="h-6 w-px bg-white/10" />
          <button
            type="button"
            className="p-2 text-on-surface-variant hover:text-primary-fixed-dim transition-colors rounded-md hover:bg-white/5"
            aria-label="Filter series"
          >
            <span className="material-symbols-outlined text-[20px]" aria-hidden="true">tune</span>
          </button>
        </div>
      </header>

      {filtered.length === 0 ? (
        <p className="font-body-sm text-on-surface-variant">No series match your search.</p>
      ) : (
        <div className="grid grid-cols-4 md:grid-cols-12 gap-gutter">
          {filtered.map((s, i) => (
            <SeriesCard key={s.id} s={s} index={i} />
          ))}
        </div>
      )}
    </main>
  );
}
