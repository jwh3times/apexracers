import { useEffect, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { api, type ScheduleWeek, type SeasonSchedule } from '../../services/api';

type FetchState =
  | { status: 'loading' }
  | { status: 'ok'; data: SeasonSchedule }
  | { status: 'error'; message: string };

const cardStyle: React.CSSProperties = {
  boxShadow: '0 1px 0 rgba(255,255,255,.03) inset, 0 18px 40px -24px rgba(0,0,0,.8)',
};

const scanTexture: React.CSSProperties = {
  backgroundImage:
    'repeating-linear-gradient(115deg, rgba(255,255,255,0.04) 0 1px, transparent 1px 9px)',
};

const SKIES = ['Clear', 'Partly Cloudy', 'Mostly Cloudy', 'Overcast'];

function fmtDate(iso: string): string {
  const d = new Date(`${iso}T00:00:00`);
  return Number.isNaN(d.getTime())
    ? iso
    : d.toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' });
}

function WeatherChips({ w }: { w: NonNullable<ScheduleWeek['weather']> }) {
  return (
    <div className="flex flex-wrap gap-x-6 gap-y-2">
      <span className="text-small-fluid text-on-surface-variant">
        <span className="text-on-surface font-mono">
          {w.tempLowC.toFixed(0)}–{w.tempHighC.toFixed(0)}°C
        </span>{' '}
        temp
      </span>
      <span className="text-small-fluid text-on-surface-variant">
        <span className="text-on-surface font-mono">{w.precipChancePct.toFixed(0)}%</span> precip
      </span>
      <span className="text-small-fluid text-on-surface-variant">
        <span className="text-on-surface font-mono">
          {w.windLowKph.toFixed(0)}–{w.windHighKph.toFixed(0)} km/h
        </span>{' '}
        wind
      </span>
      <span className="text-small-fluid text-on-surface-variant">
        {SKIES[w.skies] ?? 'Unknown'}
      </span>
    </div>
  );
}

function BopTable({ bop }: { bop: ScheduleWeek['bop'] }) {
  return (
    <div className="overflow-x-auto">
      <table className="w-full border-collapse">
        <thead>
          <tr className="border-b border-line-2" style={scanTexture}>
            <th className="th-p text-th text-on-surface-variant text-left">Car</th>
            <th className="th-p text-th text-on-surface-variant text-right">Weight</th>
            <th className="th-p text-th text-on-surface-variant text-right">Power</th>
            <th className="th-p text-th text-on-surface-variant text-right">Max Fuel</th>
            <th className="th-p text-th text-on-surface-variant text-right">Tire Sets</th>
          </tr>
        </thead>
        <tbody>
          {bop.map(c => (
            <tr key={c.carId} className="border-b border-line-2 last:border-b-0">
              <td className="td-p text-body-fluid text-on-surface max-w-0">
                <span className="block truncate">{c.carName}</span>
              </td>
              <td className="td-p text-mono-fluid text-on-surface-variant text-right">
                {c.weightPenaltyKg > 0 ? `+${c.weightPenaltyKg}kg` : `${c.weightPenaltyKg}kg`}
              </td>
              <td className="td-p text-mono-fluid text-on-surface-variant text-right">
                {`${c.powerAdjustPct > 0 ? '+' : ''}${c.powerAdjustPct}%`}
              </td>
              <td className="td-p text-mono-fluid text-on-surface-variant text-right">
                {c.maxPctFuelFill}%
              </td>
              <td className="td-p text-mono-fluid text-on-surface-variant text-right">
                {c.maxDryTireSets || '∞'}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function WeekCard({ week, tag }: { week: ScheduleWeek; tag: 'this' | 'next' | null }) {
  return (
    <div className="card-r border border-line-2 bg-surface overflow-hidden" style={cardStyle}>
      <div
        className="card-hp border-b border-line-2 flex items-center justify-between gap-3 flex-wrap"
        style={scanTexture}
      >
        <div className="flex items-baseline gap-3 flex-wrap">
          <span className="text-eyebrow text-on-surface-variant">Week {week.weekNumber}</span>
          <h3 className="text-section-head text-on-surface">
            {week.trackName}
            {week.configName ? ` — ${week.configName}` : ''}
          </h3>
          <span className="text-small-fluid text-on-surface-variant">
            {fmtDate(week.startDate)}
          </span>
        </div>
        <div className="flex items-center gap-2">
          {week.hasPersonalBest && (
            <span className="text-eyebrow px-2 py-1 rounded-[7px] border border-primary-container/40 text-primary-container">
              Your PB here
            </span>
          )}
          {tag && (
            <span className="text-eyebrow px-2 py-1 rounded-[7px] bg-primary-container/15 text-primary-container">
              {tag === 'this' ? 'This Week' : 'Next Week'}
            </span>
          )}
        </div>
      </div>
      <div className="card-p flex flex-col gap-fluid">
        {week.weather ? (
          <WeatherChips w={week.weather} />
        ) : (
          <p className="text-small-fluid text-on-surface-variant">No forecast yet.</p>
        )}
        {week.bop.length > 0 && <BopTable bop={week.bop} />}
      </div>
    </div>
  );
}

export default function SchedulePage() {
  const { seriesId } = useParams<{ seriesId: string }>();
  const id = Number(seriesId);
  const [state, setState] = useState<FetchState>({ status: 'loading' });

  useEffect(() => {
    let active = true;
    api
      .getSchedule(id)
      .then(data => {
        if (active) setState({ status: 'ok', data });
      })
      .catch((err: unknown) => {
        if (!active) return;
        setState({
          status: 'error',
          message: err instanceof Error ? err.message : 'Failed to load schedule.',
        });
      });
    return () => {
      active = false;
    };
  }, [id]);

  // The current week is the latest week whose start date is on or before today.
  const today = new Date().toISOString().slice(0, 10);
  const weeks = state.status === 'ok' ? state.data.weeks : [];
  const past = weeks.filter(w => w.startDate <= today);
  const currentWeekNumber = past.length > 0 ? past[past.length - 1].weekNumber : null;

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
          <div className="mb-6">
            <p className="text-eyebrow text-primary-container">SEASON SCHEDULE</p>
            <h1 className="text-page-title text-on-surface mt-2">{state.data.seriesName}</h1>
          </div>

          {weeks.length === 0 ? (
            <p className="text-body-fluid text-on-surface-variant">No schedule available yet.</p>
          ) : (
            <div className="flex flex-col gap-fluid">
              {weeks.map(w => (
                <WeekCard
                  key={w.weekNumber}
                  week={w}
                  tag={
                    w.weekNumber === currentWeekNumber
                      ? 'this'
                      : currentWeekNumber != null && w.weekNumber === currentWeekNumber + 1
                        ? 'next'
                        : null
                  }
                />
              ))}
            </div>
          )}
        </>
      )}
    </main>
  );
}
