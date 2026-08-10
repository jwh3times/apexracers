import { Link, useParams } from 'react-router';
import { api } from '../../services/api';
import ResourceView from '../../components/ResourceView';
import { useResource } from '../../hooks/useResource';
import { formatLapTime } from '../../utils/lapTime';

function Spec({ label, value }: { label: string; value: React.ReactNode }) {
  return (
    <div className="kpi-p card-r border border-line-2 bg-surface-container">
      <p className="text-th text-on-surface-variant mb-1">{label}</p>
      <p className="text-kpi-value text-on-surface">{value}</p>
    </div>
  );
}

export default function TrackDetailPage() {
  const { trackId } = useParams<{ trackId: string }>();
  const resource = useResource(signal => api.getTrack(Number(trackId), signal), [trackId], {
    fallbackMessage: 'Failed to load the track.',
  });

  return (
    <main className="page-wrap">
      <Link
        to="/tracks"
        className="text-small-fluid text-primary-container hover:opacity-80 transition-opacity"
      >
        ← All tracks
      </Link>

      <div className="mt-4">
        <ResourceView resource={resource} />
      </div>

      {resource.status === 'ok' && (
        <div className="flex flex-col gap-fluid-lg mt-4">
          <div>
            <p className="text-eyebrow text-primary-container">
              {resource.data.configName || resource.data.category}
            </p>
            <h1 className="text-page-title text-on-surface mt-2">{resource.data.name}</h1>
            {resource.data.location && (
              <p className="text-small-fluid text-on-surface-variant mt-1">
                {resource.data.location}
              </p>
            )}
          </div>

          {(resource.data.largeImageUrl ?? resource.data.smallImageUrl) && (
            <img
              src={resource.data.largeImageUrl ?? resource.data.smallImageUrl ?? undefined}
              alt={resource.data.name}
              className="card-r border border-line-2 w-full max-w-3xl object-cover"
            />
          )}

          <div className="grid grid-kpi gap-fluid">
            {resource.data.lengthMiles != null && (
              <Spec label="Length (mi)" value={resource.data.lengthMiles.toFixed(2)} />
            )}
            {resource.data.cornersPerLap != null && (
              <Spec label="Corners" value={resource.data.cornersPerLap} />
            )}
            {resource.data.numberPitstalls != null && (
              <Spec label="Pit stalls" value={resource.data.numberPitstalls} />
            )}
            {resource.data.pitRoadSpeedLimit != null && (
              <Spec label="Pit limit (kph)" value={resource.data.pitRoadSpeedLimit} />
            )}
            <Spec label="Night lighting" value={resource.data.nightLighting ? 'Yes' : 'No'} />
          </div>

          {resource.data.trackMapUrl && (
            <a
              href={resource.data.trackMapUrl}
              target="_blank"
              rel="noopener noreferrer"
              className="btn-fluid-sm self-start rounded-[7px] border border-primary-container text-primary-container"
            >
              Interactive track map ↗
            </a>
          )}

          {resource.data.yourBestLaps.length > 0 && (
            <div className="card-r card-shadow border border-line-2 bg-surface overflow-hidden">
              <div className="card-hp scan-texture border-b border-line-2">
                <h3 className="text-section-head text-on-surface">Your best laps at this track</h3>
              </div>
              <div className="overflow-x-auto">
                <table className="w-full">
                  <thead>
                    <tr className="text-th text-on-surface-variant text-left border-b border-line-2">
                      <th className="th-p">Car</th>
                      <th className="th-p text-right">Best lap</th>
                      <th className="th-p text-right">Laps</th>
                    </tr>
                  </thead>
                  <tbody>
                    {resource.data.yourBestLaps.map(l => (
                      <tr key={l.carId} className="border-b border-line-2/60 last:border-0">
                        <td className="td-p text-body-fluid text-on-surface">{l.carName}</td>
                        <td className="td-p text-mono-fluid text-primary-container text-right">
                          {formatLapTime(l.bestLapSeconds)}
                        </td>
                        <td className="td-p text-mono-fluid text-on-surface-variant text-right">
                          {l.lapCount}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          )}
        </div>
      )}
    </main>
  );
}
