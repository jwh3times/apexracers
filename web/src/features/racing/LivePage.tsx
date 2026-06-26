import { useEffect, useState } from 'react';
import { api, type RaceGuideEntry } from '../../services/api';

type FetchState =
  | { status: 'loading' }
  | { status: 'ok'; rows: RaceGuideEntry[] }
  | { status: 'error'; message: string };

const cardStyle: React.CSSProperties = {
  boxShadow: '0 1px 0 rgba(255,255,255,.03) inset, 0 18px 40px -24px rgba(0,0,0,.8)',
};

const scanTexture: React.CSSProperties = {
  backgroundImage:
    'repeating-linear-gradient(115deg, rgba(255,255,255,0.04) 0 1px, transparent 1px 9px)',
};

function startLabel(iso: string): string {
  const d = new Date(iso);
  return Number.isNaN(d.getTime())
    ? iso
    : d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
}

function countdown(startMs: number, now: number): string {
  const diff = Math.max(0, startMs - now);
  const mins = Math.floor(diff / 60000);
  if (mins >= 60) return `in ${Math.floor(mins / 60)}h ${mins % 60}m`;
  if (mins >= 1) return `in ${mins}m`;
  return `in ${Math.floor(diff / 1000)}s`;
}

export default function LivePage() {
  const [state, setState] = useState<FetchState>({ status: 'loading' });
  const [now, setNow] = useState(() => Date.now());

  useEffect(() => {
    let active = true;
    api
      .getRaceGuide()
      .then(rows => {
        if (active) setState({ status: 'ok', rows });
      })
      .catch((err: unknown) => {
        if (!active) return;
        setState({
          status: 'error',
          message: err instanceof Error ? err.message : 'Failed to load the race guide.',
        });
      });
    return () => {
      active = false;
    };
  }, []);

  // Tick once a second so the countdowns stay live.
  useEffect(() => {
    const t = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(t);
  }, []);

  return (
    <main className="page-wrap">
      <div className="mb-6">
        <p className="text-eyebrow text-primary-container">RACE NOW</p>
        <h1 className="text-page-title text-on-surface mt-2">Sessions starting soon</h1>
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

      {state.status === 'ok' && state.rows.length === 0 && (
        <p className="text-body-fluid text-on-surface-variant">
          No official sessions starting in the next few hours.
        </p>
      )}

      {state.status === 'ok' && state.rows.length > 0 && (
        <div className="card-r border border-line-2 bg-surface overflow-hidden" style={cardStyle}>
          <div className="overflow-x-auto">
            <table className="w-full border-collapse">
              <thead>
                <tr className="border-b border-line-2" style={scanTexture}>
                  <th className="th-p text-th text-on-surface-variant text-left">Start</th>
                  <th className="th-p text-th text-on-surface-variant text-left">Series</th>
                  <th className="th-p text-th text-on-surface-variant text-left">Status</th>
                  <th className="th-p text-th text-on-surface-variant text-right">Week</th>
                  <th className="th-p text-th text-on-surface-variant text-right">Entries</th>
                </tr>
              </thead>
              <tbody>
                {state.rows.map(r => {
                  const startMs = new Date(r.startTime).getTime();
                  const endMs = new Date(r.endTime).getTime();
                  const live = startMs <= now && now < endMs;
                  return (
                    <tr
                      key={`${r.seriesId}-${r.startTime}`}
                      className="border-b border-line-2 last:border-b-0 hover:bg-surface-container transition-colors"
                    >
                      <td className="td-p text-mono-fluid text-on-surface whitespace-nowrap">
                        {startLabel(r.startTime)}
                      </td>
                      <td className="td-p text-body-fluid text-on-surface max-w-0">
                        <span className="block truncate">{r.seriesName}</span>
                      </td>
                      <td className="td-p whitespace-nowrap">
                        {live ? (
                          <span className="text-eyebrow px-2 py-1 rounded-[7px] bg-primary-container/15 text-primary-container">
                            Live
                          </span>
                        ) : (
                          <span className="text-small-fluid text-on-surface-variant">
                            {countdown(startMs, now)}
                          </span>
                        )}
                      </td>
                      <td className="td-p text-mono-fluid text-on-surface-variant text-right">
                        {r.raceWeekNum}
                      </td>
                      <td className="td-p text-mono-fluid text-on-surface-variant text-right">
                        {r.entryCount.toLocaleString()}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        </div>
      )}
    </main>
  );
}
