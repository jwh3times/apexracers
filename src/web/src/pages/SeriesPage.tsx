import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { api, type Series } from '../services/api';

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
      {/* Header with scan texture */}
      <div
        className="px-[18px] pt-[16px] pb-[16px] border-b border-line-2"
        style={{
          ...scanTexture,
          background: 'linear-gradient(135deg, rgba(0,224,255,0.04) 0%, transparent 60%)',
        }}
      >
        <div className="flex items-center justify-between">
          <span className="text-eyebrow text-primary-container">
            {active ? `Week ${s.currentWeekNumber}` : 'Off Season'}
          </span>
          <span className="text-small-fluid font-mono text-on-surface-variant/60">
            S{s.seasonId}
          </span>
        </div>
        <h3 className="mt-[14px] text-section-head text-on-surface leading-snug">
          {s.name}
        </h3>
      </div>

      {/* Body */}
      <div className="px-[18px] py-[15px] flex flex-col gap-3 flex-1">
        <div className="text-small-fluid text-on-surface-variant">
          Season {s.seasonId}
          {active ? ` · Week ${s.currentWeekNumber}` : ''}
        </div>
        {active && (
          <div className="mt-auto flex items-center justify-end">
            <span className="text-small-fluid text-primary-container font-semibold">View week →</span>
          </div>
        )}
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

  const filtered = search.trim()
    ? series.filter(s => s.name.toLowerCase().includes(search.toLowerCase()))
    : series;

  const firstActive = series.find(s => s.currentWeekNumber != null);
  const subtitle = firstActive
    ? `Season ${firstActive.seasonId} · Week ${firstActive.currentWeekNumber} Data`
    : `${series.length} series available`;

  return (
    <main className="page-wrap">
      {/* Page head */}
      <div className="flex items-start justify-between mb-8">
        <div>
          <p className="text-eyebrow text-primary-container">
            BROWSE SERIES
          </p>
          <h1 className="text-page-title text-on-surface mt-2 mb-1">
            Active series
          </h1>
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
