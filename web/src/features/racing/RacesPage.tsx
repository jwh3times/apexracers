import { useState } from 'react';
import { Link } from 'react-router';
import { api, type RaceHistoryRow } from '../../services/api';
import ResourceView from '../../components/ResourceView';
import { useResource } from '../../hooks/useResource';

function formatDate(iso: string): string {
  const d = new Date(iso);
  return Number.isNaN(d.getTime())
    ? iso
    : d.toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' });
}

function deltaClass(n: number): string {
  return n >= 0 ? 'text-primary-container' : 'text-error';
}

/** A track name names the venue — the configuration is what says which layout was raced. */
function TrackLabel({ row }: { row: RaceHistoryRow }) {
  return (
    <>
      {row.trackName}
      {row.configName && <span className="text-on-surface-variant/70"> · {row.configName}</span>}
    </>
  );
}

function RaceTable({ rows }: { rows: RaceHistoryRow[] }) {
  return (
    <div className="overflow-x-auto">
      <table className="w-full border-collapse">
        <thead>
          <tr className="scan-texture border-b border-line-2">
            <th className="th-p text-th text-on-surface-variant text-left">Date</th>
            <th className="th-p text-th text-on-surface-variant text-left">Series</th>
            <th className="th-p text-th text-on-surface-variant text-left">Track</th>
            <th className="th-p text-th text-on-surface-variant text-left">Car</th>
            <th className="th-p text-th text-on-surface-variant text-right">Start → Finish</th>
            <th className="th-p text-th text-on-surface-variant text-right">Inc</th>
            <th className="th-p text-th text-on-surface-variant text-right">iR &Delta;</th>
            <th className="th-p text-th text-on-surface-variant text-right">SR &Delta;</th>
            <th className="th-p text-th text-on-surface-variant text-right">SOF</th>
            <th className="th-p text-th text-on-surface-variant text-right">Pts</th>
            <th className="th-p w-10" aria-label="Actions" />
          </tr>
        </thead>
        <tbody>
          {rows.map(r => (
            <tr
              key={r.subsessionId}
              className="border-b border-line-2 last:border-b-0 hover:bg-surface-container transition-colors"
            >
              <td className="td-p text-small-fluid text-on-surface-variant whitespace-nowrap">
                {formatDate(r.startTime)}
              </td>
              <td className="td-p text-body-fluid text-on-surface max-w-0">
                <span className="block truncate">{r.seriesName}</span>
              </td>
              <td className="td-p text-small-fluid text-on-surface-variant max-w-0">
                {r.trackId > 0 ? (
                  <Link
                    to={`/tracks/${r.trackId}`}
                    className="block truncate hover:text-primary-container transition-colors"
                  >
                    <TrackLabel row={r} />
                  </Link>
                ) : (
                  <span className="block truncate">
                    <TrackLabel row={r} />
                  </span>
                )}
              </td>
              <td className="td-p text-small-fluid text-on-surface-variant max-w-0">
                <span className="block truncate">{r.carName}</span>
              </td>
              <td className="td-p text-mono-fluid text-on-surface text-right whitespace-nowrap">
                <span className="text-on-surface-variant">P{r.startPosition}</span> &rarr;{' '}
                {r.finishPosition <= 3 ? (
                  <span className="text-primary-container font-semibold">P{r.finishPosition}</span>
                ) : (
                  <span>P{r.finishPosition}</span>
                )}
              </td>
              <td className="td-p text-mono-fluid text-on-surface-variant text-right">
                {r.incidents}x
              </td>
              <td
                className={`td-p text-mono-fluid text-right font-semibold ${deltaClass(r.iRatingDelta)}`}
              >
                {`${r.iRatingDelta >= 0 ? '+' : ''}${r.iRatingDelta}`}
              </td>
              <td className={`td-p text-mono-fluid text-right ${deltaClass(r.srDelta)}`}>
                {`${r.srDelta >= 0 ? '+' : ''}${r.srDelta.toFixed(2)}`}
              </td>
              <td className="td-p text-mono-fluid text-on-surface-variant text-right">
                {r.strengthOfField.toLocaleString()}
              </td>
              <td className="td-p text-mono-fluid text-on-surface text-right">{r.points}</td>
              <td className="td-p text-right">
                <Link
                  to={`/races/${r.subsessionId}`}
                  aria-label={`View race ${r.subsessionId}`}
                  className="inline-flex items-center text-on-surface-variant hover:text-primary-container transition-colors"
                >
                  <span className="material-symbols-outlined text-[18px]" aria-hidden="true">
                    chevron_right
                  </span>
                </Link>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

export default function RacesPage() {
  const [seriesFilter, setSeriesFilter] = useState<string | null>(null);
  const resource = useResource(signal => api.getRaceHistory(signal), [], {
    fallbackMessage: 'Failed to load race history.',
  });

  const allRows = resource.status === 'ok' ? resource.data : [];
  const seriesNames = Array.from(new Set(allRows.map(r => r.seriesName))).sort((a, b) =>
    a.localeCompare(b)
  );
  const visibleRows =
    seriesFilter == null ? allRows : allRows.filter(r => r.seriesName === seriesFilter);

  return (
    <main className="page-wrap">
      <div className="mb-6">
        <p className="text-eyebrow text-primary-container">RACE HISTORY</p>
        <h1 className="text-page-title text-on-surface mt-2">Recent Races</h1>
      </div>

      <ResourceView
        resource={resource}
        notLinkedReason="Link your iRacing account to see your race history."
      />

      {resource.status === 'ok' && allRows.length === 0 && (
        <p className="text-body-fluid text-on-surface-variant">No recent races found.</p>
      )}

      {resource.status === 'ok' && allRows.length > 0 && (
        <div className="flex flex-col gap-fluid">
          {/* Series filter chips */}
          <div className="flex flex-wrap gap-2">
            <button
              type="button"
              onClick={() => setSeriesFilter(null)}
              className={`btn-fluid-sm border transition-colors ${
                seriesFilter == null
                  ? 'border-primary-container bg-primary-container/10 text-primary-container'
                  : 'border-line-2 bg-surface-container text-on-surface-variant hover:bg-surface-container-high'
              }`}
            >
              All
            </button>
            {seriesNames.map(name => (
              <button
                key={name}
                type="button"
                onClick={() => setSeriesFilter(name)}
                className={`btn-fluid-sm border transition-colors ${
                  seriesFilter === name
                    ? 'border-primary-container bg-primary-container/10 text-primary-container'
                    : 'border-line-2 bg-surface-container text-on-surface-variant hover:bg-surface-container-high'
                }`}
              >
                {name}
              </button>
            ))}
          </div>

          <div className="card-r card-shadow border border-line-2 bg-surface overflow-hidden">
            <RaceTable rows={visibleRows} />
          </div>
        </div>
      )}
    </main>
  );
}
