import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import {
  api,
  IRacingNotLinkedError,
  type CategoryProgression,
  type MemberProgression,
} from '../../services/api';
import Sparkline from '../../components/Sparkline';

type FetchState =
  | { status: 'loading' }
  | { status: 'ok'; data: MemberProgression }
  | { status: 'not-linked' }
  | { status: 'error'; message: string };

const cardStyle: React.CSSProperties = {
  boxShadow: '0 1px 0 rgba(255,255,255,.03) inset, 0 18px 40px -24px rgba(0,0,0,.8)',
};

const scanTexture: React.CSSProperties = {
  backgroundImage:
    'repeating-linear-gradient(115deg, rgba(255,255,255,0.04) 0 1px, transparent 1px 9px)',
};

function Kpi({
  label,
  value,
  valueClassName,
}: {
  label: string;
  value: React.ReactNode;
  valueClassName?: string;
}) {
  return (
    <div className="kpi-p card-r border border-line-2 bg-surface-container">
      <p className="text-th text-on-surface-variant mb-1">{label}</p>
      <p className={`text-kpi-value ${valueClassName ?? 'text-on-surface'}`}>{value}</p>
    </div>
  );
}

function CategoryCard({ cat }: { cat: CategoryProgression }) {
  const series = cat.iRatingHistory.map(p => p.value);
  const delta = series.length >= 2 ? series[series.length - 1] - series[0] : null;

  return (
    <div className="card-r border border-line-2 bg-surface overflow-hidden" style={cardStyle}>
      <div
        className="card-hp border-b border-line-2 flex items-center justify-between gap-3"
        style={scanTexture}
      >
        <h3 className="text-section-head text-on-surface">{cat.categoryName}</h3>
        <span
          className="text-eyebrow px-2 py-1 rounded-[7px] border"
          style={{ color: `#${cat.color}`, borderColor: `#${cat.color}55` }}
        >
          {cat.groupName} · L{cat.licenseLevel}
        </span>
      </div>

      <div className="card-p flex flex-col gap-fluid">
        <div className="grid grid-kpi gap-fluid">
          <Kpi label="iRating" value={cat.iRating.toLocaleString()} />
          <Kpi
            label="Safety Rating"
            value={cat.safetyRating.toFixed(2)}
            valueClassName="text-primary-container"
          />
          <Kpi label="CPI" value={cat.cpi.toFixed(1)} />
          <Kpi label="TT Rating" value={cat.ttRating.toLocaleString()} />
        </div>

        {series.length >= 2 ? (
          <div className="flex flex-col gap-2">
            <div className="flex items-baseline justify-between">
              <span className="text-th text-on-surface-variant">
                iRating history ({series.length})
              </span>
              {delta !== null && (
                <span
                  className={`text-mono-fluid font-semibold ${
                    delta >= 0 ? 'text-primary-container' : 'text-red-400'
                  }`}
                >
                  {`${delta >= 0 ? '+' : ''}${delta.toLocaleString()} iR`}
                </span>
              )}
            </div>
            <div className="w-full">
              <Sparkline data={series} h={64} />
            </div>
          </div>
        ) : (
          <p className="text-small-fluid text-on-surface-variant">
            Not enough history to chart yet.
          </p>
        )}
      </div>
    </div>
  );
}

export default function ProgressionPage() {
  const [state, setState] = useState<FetchState>({ status: 'loading' });

  useEffect(() => {
    let active = true;
    api
      .getProgression()
      .then(data => {
        if (active) setState({ status: 'ok', data });
      })
      .catch((err: unknown) => {
        if (!active) return;
        if (err instanceof IRacingNotLinkedError) {
          setState({ status: 'not-linked' });
          return;
        }
        setState({
          status: 'error',
          message: err instanceof Error ? err.message : 'Failed to load progression.',
        });
      });
    return () => {
      active = false;
    };
  }, []);

  return (
    <main className="page-wrap">
      <div className="mb-6">
        <p className="text-eyebrow text-primary-container">PROGRESSION</p>
        <h1 className="text-page-title text-on-surface mt-2">iRating &amp; Safety Rating</h1>
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

      {state.status === 'not-linked' && (
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
            Link your iRacing account to see your progression. Add your iRacing customer ID in{' '}
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

      {state.status === 'ok' && state.data.categories.length === 0 && (
        <p className="text-body-fluid text-on-surface-variant">
          No license categories found for this account yet.
        </p>
      )}

      {state.status === 'ok' && state.data.categories.length > 0 && (
        <div className="grid grid-cards gap-fluid">
          {state.data.categories.map(cat => (
            <CategoryCard key={cat.categoryId} cat={cat} />
          ))}
        </div>
      )}
    </main>
  );
}
