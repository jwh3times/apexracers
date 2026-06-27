import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { api, type Series } from '../../services/api';

const scanTexture: React.CSSProperties = {
  backgroundImage:
    'repeating-linear-gradient(115deg, rgba(255,255,255,0.04) 0 1px, transparent 1px 9px)',
};

const cardStyle: React.CSSProperties = {
  boxShadow: '0 1px 0 rgba(255,255,255,.03) inset, 0 18px 40px -24px rgba(0,0,0,.8)',
};

function SeriesCard({ s }: { s: Series }) {
  const active = s.currentWeekNumber != null;

  const inner = (
    <div
      className="card-r border border-line-2 bg-surface overflow-hidden cursor-pointer hover:border-primary-container/30 transition-colors flex flex-col h-full"
      style={cardStyle}
    >
      {/* Header */}
      <div
        className="px-[18px] pt-[16px] pb-[16px] border-b border-line-2"
        style={{
          ...scanTexture,
          background:
            'linear-gradient(120deg, var(--md-sys-color-surface-container-high, rgba(255,255,255,0.03)), transparent)',
        }}
      >
        <div className="flex items-center justify-between">
          {s.category ? (
            <span
              className="text-[11px] font-mono font-semibold uppercase tracking-wider px-2 py-0.5 rounded-full"
              style={{
                background: 'color-mix(in srgb, var(--color-primary-container) 15%, transparent)',
                color: 'var(--color-primary-container)',
                border:
                  '1px solid color-mix(in srgb, var(--color-primary-container) 30%, transparent)',
              }}
            >
              {s.category}
            </span>
          ) : (
            <span />
          )}
          <span className="text-[11px] font-mono text-on-surface-variant/50">
            S{s.seasonId}
            {active ? ` · WK ${s.currentWeekNumber}` : ''}
          </span>
        </div>
        <h3
          className="mt-[14px] text-on-surface leading-snug"
          style={{ fontSize: '16.5px', letterSpacing: '-0.01em', lineHeight: 1.15 }}
        >
          {s.name}
        </h3>
      </div>

      {/* Body */}
      <div className="px-[18px] pt-[15px] pb-[16px] flex flex-col gap-3 flex-1">
        {/* Track row */}
        {s.trackName && (
          <div className="flex items-center gap-[9px] text-small-fluid text-on-surface-variant">
            <span
              className="material-symbols-outlined shrink-0"
              style={{ fontSize: 15, color: 'var(--color-primary-container)' }}
              aria-hidden="true"
            >
              flag
            </span>
            <span className="font-semibold text-on-surface">{s.trackName}</span>
            {s.trackConfigName && s.trackConfigName !== s.trackName && (
              <span className="text-on-surface-variant/50">· {s.trackConfigName}</span>
            )}
          </div>
        )}

        {/* Stats + action row */}
        <div className="mt-auto pt-2 flex items-end justify-between">
          <div className="flex gap-4">
            {s.carCount > 0 && (
              <div>
                <div
                  className="font-mono uppercase tracking-wider text-on-surface-variant/60"
                  style={{ fontSize: '10.5px' }}
                >
                  Cars
                </div>
                <div className="text-mono-fluid font-bold">{s.carCount}</div>
              </div>
            )}
            {s.driverCount > 0 && (
              <div>
                <div
                  className="font-mono uppercase tracking-wider text-on-surface-variant/60"
                  style={{ fontSize: '10.5px' }}
                >
                  Drivers
                </div>
                <div className="text-mono-fluid font-bold">{s.driverCount.toLocaleString()}</div>
              </div>
            )}
          </div>
          {active && (
            <span className="text-small-fluid text-primary-container font-semibold">
              View week →
            </span>
          )}
        </div>
      </div>
    </div>
  );

  if (active) {
    return (
      <Link to={`/series/${s.id}/weeks/${s.currentWeekNumber}`} aria-label={s.name}>
        {inner}
      </Link>
    );
  }

  return <div>{inner}</div>;
}

export default function SeriesPage() {
  const [series, setSeries] = useState<Series[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [search, setSearch] = useState('');
  const [activeCategory, setActiveCategory] = useState<string | null>(null);

  useEffect(() => {
    api
      .getSeries()
      .then(setSeries)
      .catch((err: unknown) =>
        setError(err instanceof Error ? err.message : 'Failed to load series.')
      )
      .finally(() => setLoading(false));
  }, []);

  if (loading) {
    return (
      <main className="page-wrap">
        <p className="text-on-surface-variant text-body-fluid animate-pulse">Loading&hellip;</p>
      </main>
    );
  }

  if (error) {
    return (
      <main className="page-wrap">
        <div className="card-r p-6 text-body-fluid text-error bg-surface border border-line-2">
          {error}
        </div>
      </main>
    );
  }

  if (series.length === 0) {
    return (
      <main className="page-wrap">
        <p className="text-body-fluid text-on-surface-variant">
          No active series found. Check back after the ingestion worker has run.
        </p>
      </main>
    );
  }

  const categories = Array.from(
    new Set(series.map(s => s.category).filter((c): c is string => !!c))
  ).sort();

  const searchFiltered = search.trim()
    ? series.filter(s => s.name.toLowerCase().includes(search.toLowerCase()))
    : series;

  const filtered = activeCategory
    ? searchFiltered.filter(s => s.category === activeCategory)
    : searchFiltered;

  const firstActive = series.find(s => s.currentWeekNumber != null);
  const subtitle = firstActive
    ? `Season ${firstActive.seasonId} · Week ${firstActive.currentWeekNumber} Data`
    : `${series.length} series available`;

  return (
    <main className="page-wrap">
      {/* Page head */}
      <div className="flex items-start justify-between mb-6">
        <div>
          <p className="text-eyebrow text-primary-container">BROWSE SERIES</p>
          <h1 className="text-page-title text-on-surface mt-2 mb-1">Active series</h1>
          <p className="text-body-fluid text-on-surface-variant">{subtitle}</p>
        </div>

        {/* Search */}
        <div className="relative mt-1">
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
            placeholder="Search series..."
            className="btn-fluid pl-10 pr-4 border border-line-2 bg-surface-container text-on-surface text-body-fluid placeholder:text-on-surface-variant/50 focus:outline-none focus:ring-1 focus:ring-primary-container/40 w-64"
          />
        </div>
      </div>

      {/* Category filter chips */}
      {categories.length > 1 && (
        <div className="flex items-center gap-2 mb-6 flex-wrap">
          <button
            onClick={() => setActiveCategory(null)}
            className={`btn-fluid-sm border font-mono uppercase tracking-wider transition-colors ${
              activeCategory === null
                ? 'border-primary-container bg-primary-container/10 text-primary-container'
                : 'border-line-2 text-on-surface-variant hover:border-primary-container/40 hover:text-on-surface'
            }`}
          >
            All
          </button>
          {categories.map(cat => (
            <button
              key={cat}
              onClick={() => setActiveCategory(cat === activeCategory ? null : cat)}
              className={`btn-fluid-sm border font-mono uppercase tracking-wider transition-colors ${
                activeCategory === cat
                  ? 'border-primary-container bg-primary-container/10 text-primary-container'
                  : 'border-line-2 text-on-surface-variant hover:border-primary-container/40 hover:text-on-surface'
              }`}
            >
              {cat}
            </button>
          ))}
        </div>
      )}

      {filtered.length === 0 ? (
        <p className="text-body-fluid text-on-surface-variant">No series match your search.</p>
      ) : (
        <div className="grid-cards">
          {filtered.map(s => (
            <SeriesCard key={s.id} s={s} />
          ))}
        </div>
      )}
    </main>
  );
}
