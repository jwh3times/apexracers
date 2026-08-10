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

export default function CarDetailPage() {
  const { carId } = useParams<{ carId: string }>();
  const resource = useResource(() => api.getCar(Number(carId)), [carId], {
    fallbackMessage: 'Failed to load the car.',
  });

  return (
    <main className="page-wrap">
      <Link
        to="/cars"
        className="text-small-fluid text-primary-container hover:opacity-80 transition-opacity"
      >
        ← All cars
      </Link>

      <div className="mt-4">
        <ResourceView resource={resource} />
      </div>

      {resource.status === 'ok' && (
        <div className="flex flex-col gap-fluid-lg mt-4">
          <div>
            <p className="text-eyebrow text-primary-container">{resource.data.make}</p>
            <h1 className="text-page-title text-on-surface mt-2">{resource.data.name}</h1>
          </div>

          {(resource.data.largeImageUrl ?? resource.data.smallImageUrl) && (
            <img
              src={resource.data.largeImageUrl ?? resource.data.smallImageUrl ?? undefined}
              alt={resource.data.name}
              className="card-r border border-line-2 w-full max-w-3xl object-cover"
            />
          )}

          <div className="grid grid-kpi gap-fluid">
            {resource.data.hp != null && (
              <Spec label="Horsepower" value={resource.data.hp.toLocaleString()} />
            )}
            {resource.data.weight != null && (
              <Spec label="Weight (kg)" value={resource.data.weight.toLocaleString()} />
            )}
            <Spec label="Rain capable" value={resource.data.rainEnabled ? 'Yes' : 'No'} />
            <Spec label="Free with sub" value={resource.data.freeWithSubscription ? 'Yes' : 'No'} />
          </div>

          {resource.data.carClasses.length > 0 && (
            <div className="flex flex-col gap-2">
              <p className="text-th text-on-surface-variant">Car classes</p>
              <div className="flex flex-wrap gap-2">
                {resource.data.carClasses.map(cc => (
                  <span
                    key={cc.carClassId}
                    className="text-eyebrow px-2 py-1 rounded-[7px] border border-line-2 text-on-surface-variant"
                  >
                    {cc.name}
                  </span>
                ))}
              </div>
            </div>
          )}

          {resource.data.yourBestLaps.length > 0 && (
            <div className="card-r card-shadow border border-line-2 bg-surface overflow-hidden">
              <div className="card-hp scan-texture border-b border-line-2">
                <h3 className="text-section-head text-on-surface">Your best laps in this car</h3>
              </div>
              <div className="overflow-x-auto">
                <table className="w-full">
                  <thead>
                    <tr className="text-th text-on-surface-variant text-left border-b border-line-2">
                      <th className="th-p">Track</th>
                      <th className="th-p text-right">Best lap</th>
                      <th className="th-p text-right">Laps</th>
                    </tr>
                  </thead>
                  <tbody>
                    {resource.data.yourBestLaps.map(l => (
                      <tr
                        key={`${l.trackName}-${l.configName}`}
                        className="border-b border-line-2/60 last:border-0"
                      >
                        <td className="td-p text-body-fluid text-on-surface">
                          {l.trackName}
                          {l.configName ? ` · ${l.configName}` : ''}
                        </td>
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
