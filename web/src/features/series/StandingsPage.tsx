import { useState } from 'react';
import { Link, useParams } from 'react-router';
import {
  api,
  type SeasonStandings,
  type SeasonTtStandings,
  type SeasonQualifyResults,
} from '../../services/api';
import { useAuth } from '../../context/AuthContext';
import ResourceView from '../../components/ResourceView';
import { useResource } from '../../hooks/useResource';
import { formatLapTime } from '../../utils/lapTime';
import { raceWeekLabel } from '../../utils/raceWeek';

type View = 'championship' | 'tt' | 'qualifying';

type Payload =
  | { view: 'championship'; data: SeasonStandings }
  | { view: 'tt'; data: SeasonTtStandings }
  | { view: 'qualifying'; data: SeasonQualifyResults };

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

  // Reset the class/week selection when switching views so a stale week doesn't leak across tabs.
  function selectView(next: View) {
    if (next === view) return;
    setView(next);
    setCarClassId(null);
    setWeek(null);
  }

  const resource = useResource<Payload>(
    signal => {
      const cls = carClassId ?? undefined;
      return view === 'championship'
        ? api.getStandings(id, cls, signal).then(data => ({ view: 'championship', data }) as const)
        : view === 'tt'
          ? api.getTtStandings(id, cls, signal).then(data => ({ view: 'tt', data }) as const)
          : api
              .getQualifyResults(id, cls, week ?? undefined, signal)
              .then(data => ({ view: 'qualifying', data }) as const);
    },
    [id, view, carClassId, week],
    { fallbackMessage: 'Failed to load standings.' }
  );

  const myCustId = user?.iRacingCustomerId ?? null;
  const myDivision = resource.status === 'ok' ? callerDivision(resource.data, myCustId) : null;

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

      <ResourceView resource={resource} />

      {resource.status === 'ok' && (
        <>
          <div className="mb-6 flex flex-wrap items-end justify-between gap-3">
            <div>
              <p className="text-eyebrow text-primary-container">CHAMPIONSHIP STANDINGS</p>
              <h1 className="text-page-title text-on-surface mt-2">
                {resource.data.data.seriesName}
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

          {resource.data.data.carClasses.length > 1 && (
            <div className="flex flex-wrap gap-2 mb-4">
              {resource.data.data.carClasses.map(c => (
                <button
                  key={c.carClassId}
                  type="button"
                  onClick={() => setCarClassId(c.carClassId)}
                  className={chipClass(c.carClassId === resource.data.data.carClassId)}
                >
                  {c.carClassName}
                </button>
              ))}
            </div>
          )}

          {resource.data.view === 'qualifying' && (
            <WeekSelector data={resource.data.data} onSelect={setWeek} />
          )}

          {renderBody(resource.data, myCustId)}
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
          {raceWeekLabel(w)}
        </button>
      ))}
    </div>
  );
}

function callerDivision(payload: Payload, customerId: number | null): number | null {
  if (customerId == null) return null;
  const row =
    payload.view === 'qualifying'
      ? payload.data.results.find(r => r.customerId === customerId)
      : payload.data.standings.find(s => s.customerId === customerId);
  return row ? row.division : null;
}

function renderBody(payload: Payload, customerId: number | null) {
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
    <div className="card-r card-shadow border border-line-2 bg-surface overflow-hidden">
      <div className="overflow-x-auto">
        {payload.view === 'championship' && (
          <ChampionshipTable data={payload.data} customerId={customerId} />
        )}
        {payload.view === 'tt' && <TimeTrialTable data={payload.data} customerId={customerId} />}
        {payload.view === 'qualifying' && (
          <QualifyingTable data={payload.data} customerId={customerId} />
        )}
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

function ChampionshipTable({
  data,
  customerId,
}: {
  data: SeasonStandings;
  customerId: number | null;
}) {
  return (
    <table className="w-full border-collapse">
      <thead>
        <tr className="scan-texture border-b border-line-2">
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
          const isMe = customerId != null && s.customerId === customerId;
          return (
            <tr key={s.customerId} className={rowClass(isMe)}>
              <td className={tdNum}>{s.standing}</td>
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

function TimeTrialTable({
  data,
  customerId,
}: {
  data: SeasonTtStandings;
  customerId: number | null;
}) {
  return (
    <table className="w-full border-collapse">
      <thead>
        <tr className="scan-texture border-b border-line-2">
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
          const isMe = customerId != null && s.customerId === customerId;
          return (
            <tr key={s.customerId} className={rowClass(isMe)}>
              <td className={tdNum}>{s.standing}</td>
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

function QualifyingTable({
  data,
  customerId,
}: {
  data: SeasonQualifyResults;
  customerId: number | null;
}) {
  return (
    <table className="w-full border-collapse">
      <thead>
        <tr className="scan-texture border-b border-line-2">
          <th className={`${th} w-12`}>#</th>
          <th className={thLeft}>Driver</th>
          <th className={th}>Best Qual</th>
          <th className={th}>iRating</th>
          <th className={th}>Div</th>
        </tr>
      </thead>
      <tbody>
        {data.results.map(r => {
          const isMe = customerId != null && r.customerId === customerId;
          return (
            <tr key={r.customerId} className={rowClass(isMe)}>
              <td className={tdNum}>{r.standing}</td>
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
