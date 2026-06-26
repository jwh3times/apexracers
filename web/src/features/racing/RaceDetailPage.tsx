import { useEffect, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import {
  api,
  type DriverLaps,
  type SubsessionDetail,
  type SubsessionResultRow,
} from '../../services/api';
import { useAuth } from '../../context/AuthContext';
import { formatLapTime } from '../../utils/lapTime';
import LapTraceChart from '../../components/LapTraceChart';

type FetchState =
  | { status: 'loading' }
  | { status: 'ok'; data: SubsessionDetail }
  | { status: 'error'; message: string };

const cardStyle: React.CSSProperties = {
  boxShadow: '0 1px 0 rgba(255,255,255,.03) inset, 0 18px 40px -24px rgba(0,0,0,.8)',
};

const scanTexture: React.CSSProperties = {
  backgroundImage:
    'repeating-linear-gradient(115deg, rgba(255,255,255,0.04) 0 1px, transparent 1px 9px)',
};

const SKIES = ['Clear', 'Partly Cloudy', 'Mostly Cloudy', 'Overcast'];

function lap(seconds: number): string {
  return seconds > 0 ? formatLapTime(seconds) : '—';
}

function deltaClass(n: number): string {
  return n >= 0 ? 'text-primary-container' : 'text-red-400';
}

function Kpi({ label, value }: { label: string; value: React.ReactNode }) {
  return (
    <div className="kpi-p card-r border border-line-2 bg-surface-container">
      <p className="text-th text-on-surface-variant mb-1">{label}</p>
      <p className="text-kpi-value text-on-surface">{value}</p>
    </div>
  );
}

function ClassifiedTable({ rows, meCustId }: { rows: SubsessionResultRow[]; meCustId?: number }) {
  return (
    <div className="overflow-x-auto">
      <table className="w-full border-collapse">
        <thead>
          <tr className="border-b border-line-2" style={scanTexture}>
            <th className="th-p text-th text-on-surface-variant text-right w-12">Pos</th>
            <th className="th-p text-th text-on-surface-variant text-left">Driver</th>
            <th className="th-p text-th text-on-surface-variant text-right">Start</th>
            <th className="th-p text-th text-on-surface-variant text-right">Best Lap</th>
            <th className="th-p text-th text-on-surface-variant text-right">Avg Lap</th>
            <th className="th-p text-th text-on-surface-variant text-right">Interval</th>
            <th className="th-p text-th text-on-surface-variant text-right">Led</th>
            <th className="th-p text-th text-on-surface-variant text-right">Inc</th>
            <th className="th-p text-th text-on-surface-variant text-right">iR &Delta;</th>
            <th className="th-p text-th text-on-surface-variant text-right">SR &Delta;</th>
          </tr>
        </thead>
        <tbody>
          {rows.map(r => {
            const isMe = meCustId != null && r.custId === meCustId;
            return (
              <tr
                key={r.custId}
                className={`border-b border-line-2 last:border-b-0 ${
                  isMe ? 'bg-primary-container/10' : 'hover:bg-surface-container'
                } transition-colors`}
              >
                <td className="td-p text-mono-fluid text-on-surface text-right">
                  {r.finishPosition}
                </td>
                <td className="td-p text-body-fluid text-on-surface max-w-0">
                  <span className="block truncate">
                    {r.driverName || `#${r.custId}`}
                    {isMe && <span className="text-primary-container"> (you)</span>}
                  </span>
                </td>
                <td className="td-p text-mono-fluid text-on-surface-variant text-right">
                  {r.startPosition}
                </td>
                <td className="td-p text-mono-fluid text-on-surface text-right">
                  {lap(r.bestLapSeconds)}
                </td>
                <td className="td-p text-mono-fluid text-on-surface-variant text-right">
                  {lap(r.averageLapSeconds)}
                </td>
                <td className="td-p text-mono-fluid text-on-surface-variant text-right">
                  {r.interval > 0 ? `+${r.interval.toFixed(2)}s` : '—'}
                </td>
                <td className="td-p text-mono-fluid text-on-surface-variant text-right">
                  {r.lapsLead}
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
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}

function PaceCard({ subsessionId, custId }: { subsessionId: number; custId: number }) {
  const [data, setData] = useState<DriverLaps | null>(null);

  useEffect(() => {
    let active = true;
    api
      .getDriverLaps(subsessionId, custId)
      .then(d => {
        if (active) setData(d);
      })
      .catch(() => {
        if (active) setData(null);
      });
    return () => {
      active = false;
    };
  }, [subsessionId, custId]);

  if (!data || data.laps.filter(l => l.valid).length < 2) return null;

  const deg = data.degSlopeSecondsPerLap;
  return (
    <div className="card-r border border-line-2 bg-surface overflow-hidden mb-6" style={cardStyle}>
      <div
        className="card-hp border-b border-line-2 flex items-center justify-between gap-3"
        style={scanTexture}
      >
        <h2 className="text-section-head text-on-surface">Your Race Pace</h2>
        <span className="text-eyebrow text-primary-container">Automatic telemetry — no upload</span>
      </div>
      <div className="card-p flex flex-col gap-fluid">
        <div className="grid grid-kpi gap-fluid">
          <Kpi label="Fastest Lap" value={lap(data.fastestLapSeconds)} />
          <Kpi label="Avg (clean)" value={lap(data.meanSeconds)} />
          <Kpi label="Consistency" value={`±${data.stdDevSeconds.toFixed(2)}s`} />
          <Kpi label="Degradation" value={`${deg >= 0 ? '+' : ''}${deg.toFixed(3)}s/lap`} />
        </div>
        <LapTraceChart
          laps={data.laps}
          meanSeconds={data.meanSeconds}
          stdDevSeconds={data.stdDevSeconds}
        />
      </div>
    </div>
  );
}

export default function RaceDetailPage() {
  const { subsessionId } = useParams<{ subsessionId: string }>();
  const id = Number(subsessionId);
  const { user } = useAuth();
  const [state, setState] = useState<FetchState>({ status: 'loading' });

  useEffect(() => {
    let active = true;
    api
      .getSubsession(id)
      .then(data => {
        if (active) setState({ status: 'ok', data });
      })
      .catch((err: unknown) => {
        if (!active) return;
        setState({
          status: 'error',
          message: err instanceof Error ? err.message : 'Failed to load race.',
        });
      });
    return () => {
      active = false;
    };
  }, [id]);

  return (
    <main className="page-wrap">
      <Link
        to="/races"
        className="inline-flex items-center gap-1 text-small-fluid text-on-surface-variant hover:text-primary-container transition-colors mb-4"
      >
        <span className="material-symbols-outlined text-[16px]" aria-hidden="true">
          arrow_back
        </span>
        Race History
      </Link>

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
          <div className="mb-6">
            <p className="text-eyebrow text-primary-container">{state.data.seriesName}</p>
            <h1 className="text-page-title text-on-surface mt-2">
              {state.data.trackName}
              {state.data.trackConfigName ? ` — ${state.data.trackConfigName}` : ''}
            </h1>
          </div>

          <div className="grid grid-kpi gap-fluid mb-6">
            <Kpi label="Strength of Field" value={state.data.strengthOfField.toLocaleString()} />
            <Kpi label="Cautions" value={state.data.numCautions} />
            <Kpi label="Lead Changes" value={state.data.numLeadChanges} />
            <Kpi label="Fastest Lap" value={lap(state.data.eventBestLapSeconds)} />
            {state.data.weather && (
              <>
                <Kpi label="Track Temp" value={`${state.data.weather.tempCelsius.toFixed(0)}°C`} />
                <Kpi
                  label="Precip Chance"
                  value={`${state.data.weather.precipChance.toFixed(0)}%`}
                />
                <Kpi
                  label="Skies"
                  value={SKIES[state.data.weather.skies] ?? `#${state.data.weather.skies}`}
                />
              </>
            )}
          </div>

          {user?.iRacingCustomerId != null &&
            state.data.results.some(r => r.custId === user.iRacingCustomerId) && (
              <PaceCard subsessionId={state.data.subsessionId} custId={user.iRacingCustomerId} />
            )}

          <div className="card-r border border-line-2 bg-surface overflow-hidden" style={cardStyle}>
            <div className="card-hp border-b border-line-2" style={scanTexture}>
              <h2 className="text-section-head text-on-surface">
                Results ({state.data.results.length})
              </h2>
            </div>
            {state.data.results.length > 0 ? (
              <ClassifiedTable
                rows={state.data.results}
                meCustId={user?.iRacingCustomerId ?? undefined}
              />
            ) : (
              <p className="card-p text-body-fluid text-on-surface-variant">
                No classified results recorded for this session.
              </p>
            )}
          </div>
        </>
      )}
    </main>
  );
}
