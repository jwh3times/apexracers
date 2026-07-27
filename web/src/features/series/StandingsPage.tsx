import { useEffect, useState } from 'react';
import { Link, useParams } from 'react-router';
import {
  api,
  type SeasonStandings,
  type SeasonTtStandings,
  type SeasonQualifyResults,
} from '../../services/api';
import { useAuth } from '../../context/AuthContext';
import { formatLapTime } from '../../utils/lapTime';

type View = 'championship' | 'tt' | 'qualifying';

type Payload =
  | { view: 'championship'; data: SeasonStandings }
  | { view: 'tt'; data: SeasonTtStandings }
  | { view: 'qualifying'; data: SeasonQualifyResults };

type FetchState =
  { status: 'loading' } | { status: 'ok'; payload: Payload } | { status: 'error'; message: string };

const cardStyle: React.CSSProperties = {
  boxShadow: '0 1px 0 rgba(255,255,255,.03) inset, 0 18px 40px -24px rgba(0,0,0,.8)',
};

const scanTexture: React.CSSProperties = {
  backgroundImage:
    'repeating-linear-gradient(115deg, rgba(255,255,255,0.04) 0 1px, transparent 1px 9px)',
};

const VIEWS: { id: View; label: string }[] = [
  { id: 'championship', label: 'Championship' },
  { id: 'tt', label: 'Time Trial' },
  { id: 'qualifying', label: 'Qualifying' },
];

const chipClass = (active: boolean) =>
  `btn-fluid-sm border transition-colors ${
    active
      ? 'border-primary-container bg-primary-container/10 text-primary-container'
      : 'border-line-2 bg-surface-container text-on-surface-variant hover:bg-surface-container-high'
  }`;

export default function StandingsPage() {
  const { seriesId } = useParams<{ seriesId: string }>();
  const id = Number(seriesId);
  const { user } = useAuth();
  const [view, setView] = useState<View>('championship');
  const [carClassId, setCarClassId] = useState<number | null>(null);
  const [week, setWeek] = useState<number | null>(null);
  const [state, setState] = useState<FetchState>({ status: 'loading' });

  // Reset the class/week selection when switching views so a stale week doesn't leak across tabs.
  function selectView(next: View) {
    if (next === view) return;
    setView(next);
    setCarClassId(null);
    setWeek(null);
    setState({ status: 'loading' });
  }

  useEffect(() => {
    let active = true;
    const cls = carClassId ?? undefined;
    const load: Promise<Payload> =
      view === 'championship'
        ? api.getStandings(id, cls).then(data => ({ view: 'championship', data }) as const)
        : view === 'tt'
          ? api.getTtStandings(id, cls).then(data => ({ view: 'tt', data }) as const)
          : api
              .getQualifyResults(id, cls, week ?? undefined)
              .then(data => ({ view: 'qualifying', data }) as const);

    load
      .then(payload => {
        if (active) setState({ status: 'ok', payload });
      })
      .catch((err: unknown) => {
        if (!active) return;
        setState({
          status: 'error',
          message: err instanceof Error ? err.message : 'Failed to load standings.',
        });
      });
    return () => {
      active = false;
    };
  }, [id, view, carClassId, week]);

  const myCustId = user?.iRacingCustomerId ?? null;
  const myDivision = state.status === 'ok' ? callerDivision(state.payload, myCustId) : null;

  return (
    <main className="page-wrap">
      <Link
        to="/series"
        className="inline-flex items-center gap-1 text-small-fluid text-on-surface-variant hover:text-primary-container transition-colors mb-4"
      >
        <span className="material-symbols-outlined text-[16px]" aria-hidden="true">
          arrow_back
        </span>
        Series
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
          <div className="mb-6 flex flex-wrap items-end justify-between gap-3">
            <div>
              <p className="text-eyebrow text-primary-container">CHAMPIONSHIP STANDINGS</p>
              <h1 className="text-page-title text-on-surface mt-2">
                {state.payload.data.seriesName}
              </h1>
            </div>
            {myDivision != null && (
              <span
                className="btn-fluid-sm border border-primary-container bg-primary-container/10 text-primary-container"
                data-testid="your-division"
              >
                Your division: {myDivision}
              </span>
            )}
          </div>

          <div className="flex flex-wrap gap-2 mb-4">
            {VIEWS.map(v => (
              <button
                key={v.id}
                type="button"
                onClick={() => selectView(v.id)}
                className={chipClass(v.id === view)}
              >
                {v.label}
              </button>
            ))}
          </div>

          {state.payload.data.carClasses.length > 1 && (
            <div className="flex flex-wrap gap-2 mb-4">
              {state.payload.data.carClasses.map(c => (
                <button
                  key={c.carClassId}
                  type="button"
                  onClick={() => setCarClassId(c.carClassId)}
                  className={chipClass(c.carClassId === state.payload.data.carClassId)}
                >
                  {c.carClassName}
                </button>
              ))}
            </div>
          )}

          {state.payload.view === 'qualifying' && (
            <WeekSelector data={state.payload.data} onSelect={setWeek} />
          )}

          {renderBody(state.payload, myCustId)}
        </>
      )}
    </main>
  );
}

function WeekSelector({
  data,
  onSelect,
}: {
  data: SeasonQualifyResults;
  onSelect: (week: number) => void;
}) {
  if (data.availableWeeks.length <= 1) return null;
  return (
    <div className="flex flex-wrap gap-2 mb-6">
      {data.availableWeeks.map(w => (
        <button
          key={w}
          type="button"
          onClick={() => onSelect(w)}
          className={chipClass(w === data.raceWeekNum)}
        >
          Week {w + 1}
        </button>
      ))}
    </div>
  );
}

function callerDivision(payload: Payload, custId: number | null): number | null {
  if (custId == null) return null;
  const row =
    payload.view === 'qualifying'
      ? payload.data.results.find(r => r.custId === custId)
      : payload.data.standings.find(s => s.custId === custId);
  return row ? row.division : null;
}

function renderBody(payload: Payload, custId: number | null) {
  const isEmpty =
    payload.view === 'qualifying'
      ? payload.data.results.length === 0
      : payload.data.standings.length === 0;

  if (isEmpty) {
    return (
      <p className="text-body-fluid text-on-surface-variant">
        No {payload.view === 'qualifying' ? 'qualifying results' : 'standings'} available for this
        class yet.
      </p>
    );
  }

  return (
    <div className="card-r border border-line-2 bg-surface overflow-hidden" style={cardStyle}>
      <div className="overflow-x-auto">
        {payload.view === 'championship' && (
          <ChampionshipTable data={payload.data} custId={custId} />
        )}
        {payload.view === 'tt' && <TimeTrialTable data={payload.data} custId={custId} />}
        {payload.view === 'qualifying' && <QualifyingTable data={payload.data} custId={custId} />}
      </div>
    </div>
  );
}

const th = 'th-p text-th text-on-surface-variant text-right';
const thLeft = 'th-p text-th text-on-surface-variant text-left';
const tdNum = 'td-p text-mono-fluid text-on-surface-variant text-right';

function rowClass(isMe: boolean) {
  return `border-b border-line-2 last:border-b-0 ${
    isMe ? 'bg-primary-container/10' : 'hover:bg-surface-container'
  } transition-colors`;
}

function DriverCell({ name, isMe }: { name: string; isMe: boolean }) {
  return (
    <td className="td-p text-body-fluid text-on-surface max-w-0">
      <span className="block truncate">
        {name}
        {isMe && <span className="text-primary-container"> (you)</span>}
      </span>
    </td>
  );
}

function ChampionshipTable({ data, custId }: { data: SeasonStandings; custId: number | null }) {
  return (
    <table className="w-full border-collapse">
      <thead>
        <tr className="border-b border-line-2" style={scanTexture}>
          <th className={`${th} w-12`}>#</th>
          <th className={thLeft}>Driver</th>
          <th className={th}>Pts</th>
          <th className={th}>Starts</th>
          <th className={th}>Wins</th>
          <th className={th}>Top 5</th>
          <th className={th}>Poles</th>
          <th className={th}>Avg Fin</th>
          <th className={th}>Inc</th>
        </tr>
      </thead>
      <tbody>
        {data.standings.map(s => {
          const isMe = custId != null && s.custId === custId;
          return (
            <tr key={s.custId} className={rowClass(isMe)}>
              <td className={tdNum}>{s.rank}</td>
              <DriverCell name={s.driverName} isMe={isMe} />
              <td className="td-p text-mono-fluid text-primary-container font-semibold text-right">
                {s.points.toLocaleString()}
              </td>
              <td className={tdNum}>{s.starts}</td>
              <td className={tdNum}>{s.wins}</td>
              <td className={tdNum}>{s.top5}</td>
              <td className={tdNum}>{s.poles}</td>
              <td className={tdNum}>{s.avgFinishPosition.toFixed(1)}</td>
              <td className={tdNum}>{s.incidents}</td>
            </tr>
          );
        })}
      </tbody>
    </table>
  );
}

function TimeTrialTable({ data, custId }: { data: SeasonTtStandings; custId: number | null }) {
  return (
    <table className="w-full border-collapse">
      <thead>
        <tr className="border-b border-line-2" style={scanTexture}>
          <th className={`${th} w-12`}>#</th>
          <th className={thLeft}>Driver</th>
          <th className={th}>Pts</th>
          <th className={th}>TT Rating</th>
          <th className={th}>Starts</th>
          <th className={th}>Wins</th>
          <th className={th}>Avg Fin</th>
          <th className={th}>Inc</th>
        </tr>
      </thead>
      <tbody>
        {data.standings.map(s => {
          const isMe = custId != null && s.custId === custId;
          return (
            <tr key={s.custId} className={rowClass(isMe)}>
              <td className={tdNum}>{s.rank}</td>
              <DriverCell name={s.driverName} isMe={isMe} />
              <td className="td-p text-mono-fluid text-primary-container font-semibold text-right">
                {s.points.toLocaleString()}
              </td>
              <td className={tdNum}>{s.ttRating != null ? s.ttRating.toLocaleString() : '—'}</td>
              <td className={tdNum}>{s.starts}</td>
              <td className={tdNum}>{s.wins}</td>
              <td className={tdNum}>{s.avgFinishPosition.toFixed(1)}</td>
              <td className={tdNum}>{s.incidents}</td>
            </tr>
          );
        })}
      </tbody>
    </table>
  );
}

function QualifyingTable({ data, custId }: { data: SeasonQualifyResults; custId: number | null }) {
  return (
    <table className="w-full border-collapse">
      <thead>
        <tr className="border-b border-line-2" style={scanTexture}>
          <th className={`${th} w-12`}>#</th>
          <th className={thLeft}>Driver</th>
          <th className={th}>Best Qual</th>
          <th className={th}>iRating</th>
          <th className={th}>Div</th>
        </tr>
      </thead>
      <tbody>
        {data.results.map(r => {
          const isMe = custId != null && r.custId === custId;
          return (
            <tr key={r.custId} className={rowClass(isMe)}>
              <td className={tdNum}>{r.rank}</td>
              <DriverCell name={r.driverName} isMe={isMe} />
              <td className="td-p text-mono-fluid text-primary-container font-semibold text-right">
                {r.bestQualLapSeconds > 0 ? formatLapTime(r.bestQualLapSeconds) : '—'}
              </td>
              <td className={tdNum}>{r.iRating != null ? r.iRating.toLocaleString() : '—'}</td>
              <td className={tdNum}>{r.division}</td>
            </tr>
          );
        })}
      </tbody>
    </table>
  );
}
