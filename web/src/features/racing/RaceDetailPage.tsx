import { Link, useParams } from 'react-router';
import {
  api,
  type DriverLaps,
  type SubsessionDetail,
  type SubsessionResultRow,
} from '../../services/api';
import { useAuth } from '../../context/AuthContext';
import { formatLapTime } from '../../utils/lapTime';
import { splitLabel } from '../../utils/split';
import { unrepresentedEntriesNote } from '../../utils/fieldCompleteness';
import LapTraceChart from '../../components/LapTraceChart';
import ResourceView from '../../components/ResourceView';
import { useResource } from '../../hooks/useResource';

const SKIES = ['Clear', 'Partly Cloudy', 'Mostly Cloudy', 'Overcast'];

function lap(seconds: number): string {
  return seconds > 0 ? formatLapTime(seconds) : '—';
}

function deltaClass(n: number): string {
  return n >= 0 ? 'text-primary-container' : 'text-error';
}

function Kpi({ label, value }: { label: string; value: React.ReactNode }) {
  return (
    <div className="kpi-p card-r border border-line-2 bg-surface-container">
      <p className="text-th text-on-surface-variant mb-1">{label}</p>
      <p className="text-kpi-value text-on-surface">{value}</p>
    </div>
  );
}

/**
 * Which Split of its Race Session this race was, shown one-based ("1 of 3"). Renders nothing when
 * the position is unknown — an unknown Split Index must not read as the strongest Split.
 */
function SplitKpi({ index, count }: { index: number | null; count: number | null }) {
  const label = splitLabel(index, count);
  return label === null ? null : <Kpi label="Split" value={label} />;
}

/**
 * Why the listed results are fewer than the race's field, when they are. Renders nothing when the
 * field is complete or was never counted, so a complete race carries no caveat.
 */
function UnrepresentedEntriesNote({
  teamEntryCount,
  aiEntryCount,
}: {
  teamEntryCount: number | null;
  aiEntryCount: number | null;
}) {
  const note = unrepresentedEntriesNote(teamEntryCount, aiEntryCount);
  return note === null ? null : (
    <p className="text-small-fluid text-on-surface-variant mt-2">{note}</p>
  );
}

function ClassifiedTable({ rows, meCustId }: { rows: SubsessionResultRow[]; meCustId?: number }) {
  return (
    <div className="overflow-x-auto">
      <table className="w-full border-collapse">
        <thead>
          <tr className="scan-texture border-b border-line-2">
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
            const isMe = meCustId != null && r.customerId === meCustId;
            return (
              <tr
                key={r.customerId}
                className={`border-b border-line-2 last:border-b-0 ${
                  isMe ? 'bg-primary-container/10' : 'hover:bg-surface-container'
                } transition-colors`}
              >
                <td className="td-p text-mono-fluid text-on-surface text-right">
                  {r.finishPosition}
                </td>
                <td className="td-p text-body-fluid text-on-surface max-w-0">
                  <span className="block truncate">
                    {r.driverName || `#${r.customerId}`}
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

function PaceCard({ subsessionId, customerId }: { subsessionId: number; customerId: number }) {
  const resource = useResource<DriverLaps | null>(
    signal => api.getDriverLaps(subsessionId, customerId, signal),
    [subsessionId, customerId],
    { onNotLinked: { fallback: null }, onError: { fallback: null } }
  );
  const data = resource.status === 'ok' ? resource.data : null;

  if (!data || data.laps.filter(l => l.timed).length < 2) return null;

  const deg = data.degSlopeSecondsPerLap;
  return (
    <div className="card-r card-shadow border border-line-2 bg-surface overflow-hidden mb-6">
      <div className="card-hp scan-texture border-b border-line-2 flex items-center justify-between gap-3">
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
  const resource = useResource<SubsessionDetail>(signal => api.getSubsession(id, signal), [id], {
    fallbackMessage: 'Failed to load race.',
  });

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

      <ResourceView resource={resource} />

      {resource.status === 'ok' && (
        <>
          <div className="mb-6">
            <p className="text-eyebrow text-primary-container">{resource.data.seriesName}</p>
            <h1 className="text-page-title text-on-surface mt-2">
              {resource.data.trackName}
              {resource.data.trackConfigName ? ` — ${resource.data.trackConfigName}` : ''}
            </h1>
          </div>

          <div className="grid grid-kpi gap-fluid mb-6">
            <Kpi label="Strength of Field" value={resource.data.strengthOfField.toLocaleString()} />
            <SplitKpi index={resource.data.splitIndex} count={resource.data.splitCount} />
            <Kpi label="Cautions" value={resource.data.numCautions} />
            <Kpi label="Lead Changes" value={resource.data.numLeadChanges} />
            <Kpi label="Fastest Lap" value={lap(resource.data.eventBestLapSeconds)} />
            {resource.data.weather && (
              <>
                <Kpi
                  label="Track Temp"
                  value={`${resource.data.weather.tempCelsius.toFixed(0)}°C`}
                />
                <Kpi
                  label="Precip Chance"
                  value={`${resource.data.weather.precipChance.toFixed(0)}%`}
                />
                <Kpi
                  label="Skies"
                  value={SKIES[resource.data.weather.skies] ?? `#${resource.data.weather.skies}`}
                />
              </>
            )}
          </div>

          {user?.iRacingCustomerId != null &&
            resource.data.results.some(r => r.customerId === user.iRacingCustomerId) && (
              <PaceCard
                subsessionId={resource.data.subsessionId}
                customerId={user.iRacingCustomerId}
              />
            )}

          <div className="card-r card-shadow border border-line-2 bg-surface overflow-hidden">
            <div className="card-hp scan-texture border-b border-line-2">
              <h2 className="text-section-head text-on-surface">
                Results ({resource.data.results.length})
              </h2>
              <UnrepresentedEntriesNote
                teamEntryCount={resource.data.teamEntryCount}
                aiEntryCount={resource.data.aiEntryCount}
              />
            </div>
            {resource.data.results.length > 0 ? (
              <ClassifiedTable
                rows={resource.data.results}
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
