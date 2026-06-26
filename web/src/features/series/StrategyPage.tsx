import { useEffect, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { api, type CarStrategy, type WeekStrategy } from '../../services/api';
import { formatLapTime } from '../../utils/lapTime';
import { topPercentLabel } from '../../utils/percentile';

type FetchState =
  | { status: 'loading' }
  | { status: 'ok'; data: WeekStrategy }
  | { status: 'error'; message: string };

const cardStyle: React.CSSProperties = {
  boxShadow: '0 1px 0 rgba(255,255,255,.03) inset, 0 18px 40px -24px rgba(0,0,0,.8)',
};

const scanTexture: React.CSSProperties = {
  backgroundImage:
    'repeating-linear-gradient(115deg, rgba(255,255,255,0.04) 0 1px, transparent 1px 9px)',
};

const SKIES = ['Clear', 'Partly Cloudy', 'Mostly Cloudy', 'Overcast'];

// Amber is a data-accent color (not the cyan brand accent) — used for "caution" states.
const AMBER = '#f59e0b';

/** Risk-banner styling per weather-risk level. High → error red, Medium → amber, Low → cyan. */
function riskStyle(level: string): { className: string; style?: React.CSSProperties } {
  if (level === 'High') return { className: 'border-error/40 bg-error/10 text-error' };
  if (level === 'Medium')
    return {
      className: 'border',
      style: {
        color: AMBER,
        borderColor: 'rgba(245,158,11,0.4)',
        backgroundColor: 'rgba(245,158,11,0.1)',
      },
    };
  return {
    className: 'border-primary-container/40 bg-primary-container/10 text-primary-container',
  };
}

/** BoP trend pill color: a nerf is bad (red), a buff is good (cyan), mixed is amber. */
function trendStyle(trend: string): { className: string; style?: React.CSSProperties } {
  if (trend === 'Nerfed') return { className: 'text-error border-error/40' };
  if (trend === 'Buffed')
    return { className: 'text-primary-container border-primary-container/40' };
  if (trend === 'Mixed')
    return {
      className: 'border',
      style: { color: AMBER, borderColor: 'rgba(245,158,11,0.4)' },
    };
  return { className: 'text-on-surface-variant border-line-2' };
}

function signed(value: number, unit: string): string {
  return `${value > 0 ? '+' : ''}${value}${unit}`;
}

function CarStrategyCard({ car, personalized }: { car: CarStrategy; personalized: boolean }) {
  const trend = trendStyle(car.bopTrend);
  return (
    <div className="card-r border border-line-2 bg-surface overflow-hidden" style={cardStyle}>
      <div
        className="card-hp border-b border-line-2 flex items-center justify-between gap-3 flex-wrap"
        style={scanTexture}
      >
        <div className="flex items-center gap-3 flex-wrap">
          {car.optimalRank != null && (
            <span className="text-eyebrow px-2 py-1 rounded-[7px] bg-primary-container/15 text-primary-container">
              #{car.optimalRank} for you
            </span>
          )}
          <h3 className="text-section-head text-on-surface">{car.carName}</h3>
          {car.rainEnabled && (
            <span
              className="text-eyebrow px-2 py-1 rounded-[7px] border border-line-2 text-on-surface-variant"
              title="Rain-capable"
            >
              Rain OK
            </span>
          )}
        </div>
        {car.bopTrend !== '—' && (
          <span
            className={`text-eyebrow px-2 py-1 rounded-[7px] border ${trend.className}`}
            style={trend.style}
          >
            {car.bopTrend}
          </span>
        )}
      </div>

      <div className="card-p flex flex-col gap-fluid">
        {/* Personal overlay */}
        {car.percentileRank != null && (
          <div className="flex flex-wrap gap-x-6 gap-y-2">
            <span className="text-small-fluid text-on-surface-variant">
              <span className="text-primary-container font-mono">
                {topPercentLabel(car.percentileRank)}
              </span>{' '}
              your pace
            </span>
            {car.projectedLapSeconds != null && (
              <span className="text-small-fluid text-on-surface-variant">
                <span className="text-on-surface font-mono">
                  {formatLapTime(car.projectedLapSeconds)}
                </span>{' '}
                projected
              </span>
            )}
          </div>
        )}

        {/* BoP grid */}
        <div className="grid-kpi">
          <BopStat
            label="Weight"
            value={`${car.weightPenaltyKg}kg`}
            delta={
              car.bopTrend !== '—' && car.weightDeltaKg !== 0
                ? signed(car.weightDeltaKg, 'kg')
                : null
            }
            deltaBad={car.weightDeltaKg > 0}
          />
          <BopStat
            label="Power"
            value={`${car.powerAdjustPct > 0 ? '+' : ''}${car.powerAdjustPct}%`}
            delta={
              car.bopTrend !== '—' && car.powerDeltaPct !== 0
                ? signed(car.powerDeltaPct, '%')
                : null
            }
            deltaBad={car.powerDeltaPct < 0}
          />
          <BopStat label="Max fuel" value={`${car.maxPctFuelFill}%`} />
          <BopStat
            label="Tire sets"
            value={car.maxDryTireSets > 0 ? String(car.maxDryTireSets) : '∞'}
          />
        </div>

        {/* Strategy notes */}
        <ul className="flex flex-col gap-1.5">
          <NoteRow icon="local_gas_station" active={car.fuelCapped} text={car.fuelNote} />
          <NoteRow icon="tire_repair" active={car.limitedTireSets} text={car.tireNote} />
        </ul>
      </div>

      {!personalized && car.percentileRank == null && (
        <div className="card-hp border-t border-line-2 text-small-fluid text-on-surface-variant">
          <Link to="/settings" className="text-primary-container hover:underline">
            Link your iRacing account
          </Link>{' '}
          to rank this car for your pace.
        </div>
      )}
    </div>
  );
}

function BopStat({
  label,
  value,
  delta,
  deltaBad,
}: {
  label: string;
  value: string;
  delta?: string | null;
  deltaBad?: boolean;
}) {
  return (
    <div className="bg-surface border border-line-2 card-r kpi-p" style={cardStyle}>
      <div className="text-small-fluid text-on-surface-variant font-medium">{label}</div>
      <div className="mt-1 flex items-baseline gap-2">
        <span className="text-kpi-value text-on-surface">{value}</span>
        {delta != null && (
          <span
            className={`text-small-fluid font-mono ${deltaBad ? 'text-error' : 'text-primary-container'}`}
          >
            {delta}
          </span>
        )}
      </div>
    </div>
  );
}

function NoteRow({ icon, active, text }: { icon: string; active: boolean; text: string }) {
  return (
    <li className="flex items-center gap-2 text-small-fluid text-on-surface-variant">
      <span
        className={`material-symbols-outlined text-[16px] ${active ? 'text-on-surface' : 'text-on-surface-variant/50'}`}
        aria-hidden="true"
      >
        {icon}
      </span>
      {text}
    </li>
  );
}

export default function StrategyPage() {
  const { seriesId, weekNumber } = useParams<{ seriesId: string; weekNumber: string }>();
  const id = Number(seriesId);
  const week = Number(weekNumber);
  const [state, setState] = useState<FetchState>({ status: 'loading' });

  useEffect(() => {
    let active = true;
    api
      .getWeekStrategy(id, week)
      .then(data => {
        if (active) setState({ status: 'ok', data });
      })
      .catch((err: unknown) => {
        if (!active) return;
        setState({
          status: 'error',
          message: err instanceof Error ? err.message : 'Failed to load strategy.',
        });
      });
    return () => {
      active = false;
    };
  }, [id, week]);

  const data = state.status === 'ok' ? state.data : null;
  const risk = data ? riskStyle(data.weatherRisk.level) : null;

  const contextChips = data
    ? ([
        data.trackLengthMiles != null ? `${data.trackLengthMiles.toFixed(2)} mi` : null,
        data.cornersPerLap != null ? `${data.cornersPerLap} corners` : null,
        data.numberPitstalls != null ? `${data.numberPitstalls} pit stalls` : null,
        data.pitRoadSpeedLimit != null ? `${data.pitRoadSpeedLimit} km/h pit limit` : null,
        data.nightLighting ? 'Night lighting' : null,
      ].filter(Boolean) as string[])
    : [];

  return (
    <main className="page-wrap">
      <Link
        to={`/series/${seriesId}/weeks/${weekNumber}`}
        className="inline-flex items-center gap-1 text-small-fluid text-on-surface-variant hover:text-primary-container transition-colors mb-4"
      >
        <span className="material-symbols-outlined text-[16px]" aria-hidden="true">
          arrow_back
        </span>
        Week detail
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

      {data && risk && (
        <>
          <div className="mb-6">
            <p className="text-eyebrow text-primary-container">STRATEGY · WEEK {data.weekNumber}</p>
            <h1 className="text-page-title text-on-surface mt-2">{data.seriesName}</h1>
            <p className="text-body-fluid text-on-surface-variant mt-1">
              {data.trackName}
              {data.configName ? ` — ${data.configName}` : ''}
            </p>
            {contextChips.length > 0 && (
              <div className="flex flex-wrap gap-x-5 gap-y-1 mt-3">
                {contextChips.map(c => (
                  <span key={c} className="text-small-fluid text-on-surface-variant font-mono">
                    {c}
                  </span>
                ))}
              </div>
            )}
          </div>

          {/* Weather-risk banner */}
          <div
            className={`card-r p-4 mb-6 flex flex-wrap items-center gap-x-6 gap-y-2 ${risk.className}`}
            style={{ ...cardStyle, ...risk.style }}
          >
            <span className="text-section-head">Weather risk: {data.weatherRisk.level}</span>
            <span className="text-body-fluid">
              {data.weatherRisk.precipChancePct.toFixed(0)}% precip · {data.weatherRisk.note}
            </span>
            {data.weather && (
              <span className="text-small-fluid font-mono opacity-80">
                {data.weather.tempLowC.toFixed(0)}–{data.weather.tempHighC.toFixed(0)}°C ·{' '}
                {data.weather.windLowKph.toFixed(0)}–{data.weather.windHighKph.toFixed(0)} km/h ·{' '}
                {SKIES[data.weather.skies] ?? 'Unknown'}
              </span>
            )}
          </div>

          {data.cars.length === 0 ? (
            <p className="text-body-fluid text-on-surface-variant">
              No Balance of Performance data for this week yet.
            </p>
          ) : (
            <div className="flex flex-col gap-fluid">
              {data.cars.map(car => (
                <CarStrategyCard key={car.carId} car={car} personalized={data.personalized} />
              ))}
            </div>
          )}
        </>
      )}
    </main>
  );
}
