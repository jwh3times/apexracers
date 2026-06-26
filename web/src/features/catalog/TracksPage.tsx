import { useEffect, useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { api, type TrackCatalogItem } from '../../services/api';

type FetchState =
  | { status: 'loading' }
  | { status: 'ok'; tracks: TrackCatalogItem[] }
  | { status: 'error'; message: string };

const cardStyle: React.CSSProperties = {
  boxShadow: '0 1px 0 rgba(255,255,255,.03) inset, 0 18px 40px -24px rgba(0,0,0,.8)',
};

function pretty(slug: string): string {
  return slug
    .split('_')
    .filter(Boolean)
    .map(w => w[0].toUpperCase() + w.slice(1))
    .join(' ');
}

export default function TracksPage() {
  const [state, setState] = useState<FetchState>({ status: 'loading' });
  const [search, setSearch] = useState('');
  const [category, setCategory] = useState<string | null>(null);

  useEffect(() => {
    let active = true;
    api
      .getTracks()
      .then(tracks => {
        if (active) setState({ status: 'ok', tracks });
      })
      .catch((err: unknown) => {
        if (!active) return;
        setState({
          status: 'error',
          message: err instanceof Error ? err.message : 'Failed to load the track catalog.',
        });
      });
    return () => {
      active = false;
    };
  }, []);

  const tracks = useMemo(() => (state.status === 'ok' ? state.tracks : []), [state]);

  const categories = useMemo(
    () => [...new Set(tracks.map(t => t.category).filter((c): c is string => !!c))].sort(),
    [tracks]
  );

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    return tracks.filter(
      t =>
        (q === '' || t.name.toLowerCase().includes(q)) &&
        (category === null || t.category === category)
    );
  }, [tracks, search, category]);

  return (
    <main className="page-wrap">
      <div className="mb-6">
        <p className="text-eyebrow text-primary-container">CATALOG</p>
        <h1 className="text-page-title text-on-surface mt-2">Tracks</h1>
      </div>

      {state.status === 'loading' && (
        <p className="text-body-fluid text-on-surface-variant animate-pulse">Loading&hellip;</p>
      )}

      {state.status === 'error' && (
        <div
          className="card-r border border-line-2 bg-surface p-6 text-body-fluid text-error"
          style={cardStyle}
        >
          {state.message}
        </div>
      )}

      {state.status === 'ok' && (
        <>
          <div className="flex flex-col gap-3 mb-6">
            <input
              type="text"
              value={search}
              onChange={e => setSearch(e.target.value)}
              placeholder="Search tracks by name…"
              aria-label="Search tracks"
              className="w-full max-w-sm btn-fluid-sm rounded-[7px] border border-line-2 bg-surface-container text-on-surface px-3"
            />
            {categories.length > 1 && (
              <div className="flex flex-wrap gap-2">
                <button
                  type="button"
                  onClick={() => setCategory(null)}
                  className={`btn-fluid-sm border transition-colors ${
                    category === null
                      ? 'border-primary-container bg-primary-container/10 text-primary-container'
                      : 'border-line-2 bg-surface-container text-on-surface-variant'
                  }`}
                >
                  All
                </button>
                {categories.map(cat => (
                  <button
                    key={cat}
                    type="button"
                    onClick={() => setCategory(cat)}
                    className={`btn-fluid-sm border transition-colors ${
                      category === cat
                        ? 'border-primary-container bg-primary-container/10 text-primary-container'
                        : 'border-line-2 bg-surface-container text-on-surface-variant'
                    }`}
                  >
                    {pretty(cat)}
                  </button>
                ))}
              </div>
            )}
          </div>

          {filtered.length === 0 ? (
            <p className="text-body-fluid text-on-surface-variant">No tracks match your filters.</p>
          ) : (
            <div className="grid grid-cards gap-fluid">
              {filtered.map(t => (
                <Link
                  key={t.trackId}
                  to={`/tracks/${t.trackId}`}
                  className="card-r border border-line-2 bg-surface overflow-hidden hover:border-primary-container transition-colors"
                  style={cardStyle}
                >
                  <div className="aspect-[16/9] bg-surface-container overflow-hidden">
                    {t.smallImageUrl && (
                      <img
                        src={t.smallImageUrl}
                        alt={t.name}
                        loading="lazy"
                        className="w-full h-full object-cover"
                      />
                    )}
                  </div>
                  <div className="card-p flex flex-col gap-1">
                    <h3 className="text-section-head text-on-surface">{t.name}</h3>
                    {t.configName && (
                      <p className="text-small-fluid text-primary-container">{t.configName}</p>
                    )}
                    <p className="text-small-fluid text-on-surface-variant">
                      {[
                        t.location,
                        t.lengthMiles ? `${t.lengthMiles.toFixed(2)} mi` : null,
                        t.cornersPerLap ? `${t.cornersPerLap} turns` : null,
                      ]
                        .filter(Boolean)
                        .join(' · ')}
                    </p>
                    {t.nightLighting && (
                      <span className="text-eyebrow px-2 py-0.5 mt-1 self-start rounded-[7px] border border-primary-container text-primary-container">
                        Night
                      </span>
                    )}
                  </div>
                </Link>
              ))}
            </div>
          )}
        </>
      )}
    </main>
  );
}
