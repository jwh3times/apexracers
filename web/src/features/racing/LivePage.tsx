import { useEffect, useState } from 'react';
import { api } from '../../services/api';
import ResourceView from '../../components/ResourceView';
import { useResource } from '../../hooks/useResource';

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
  const [now, setNow] = useState(() => Date.now());
  const resource = useResource(signal => api.getRaceGuide(signal), [], {
    fallbackMessage: 'Failed to load the race guide.',
  });

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

      <ResourceView resource={resource} />

      {resource.status === 'ok' && resource.data.length === 0 && (
        <p className="text-body-fluid text-on-surface-variant">
          No official sessions starting in the next few hours.
        </p>
      )}

      {resource.status === 'ok' && resource.data.length > 0 && (
        <div className="card-r card-shadow border border-line-2 bg-surface overflow-hidden">
          <div className="overflow-x-auto">
            <table className="w-full border-collapse">
              <thead>
                <tr className="scan-texture border-b border-line-2">
                  <th className="th-p text-th text-on-surface-variant text-left">Start</th>
                  <th className="th-p text-th text-on-surface-variant text-left">Series</th>
                  <th className="th-p text-th text-on-surface-variant text-left">Status</th>
                  <th className="th-p text-th text-on-surface-variant text-right">Week</th>
                  <th className="th-p text-th text-on-surface-variant text-right">Entries</th>
                </tr>
              </thead>
              <tbody>
                {resource.data.map(r => {
                  const startMs = new Date(r.startTime).getTime();
                  const endMs = new Date(r.endTime).getTime();
                  const live = startMs <= now && now < endMs;
                  // Sentinel/stale guard: a session "live" for over a day has a bogus
                  // start (the demo board's fixed window, or garbage data) — don't
                  // render a misleading absolute start time for it.
                  const staleStart = live && now - startMs > 24 * 60 * 60 * 1000;
                  return (
                    <tr
                      key={`${r.seriesId}-${r.startTime}`}
                      className="border-b border-line-2 last:border-b-0 hover:bg-surface-container transition-colors"
                    >
                      <td className="td-p text-mono-fluid text-on-surface whitespace-nowrap">
                        {staleStart ? '—' : startLabel(r.startTime)}
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
