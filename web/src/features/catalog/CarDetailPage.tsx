import { useEffect, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { api, type CarCatalogDetail } from '../../services/api';
import { formatLapTime } from '../../utils/lapTime';

type FetchState =
  | { status: 'loading' }
  | { status: 'ok'; car: CarCatalogDetail }
  | { status: 'error'; message: string };

const cardStyle: React.CSSProperties = {
  boxShadow: '0 1px 0 rgba(255,255,255,.03) inset, 0 18px 40px -24px rgba(0,0,0,.8)',
};
const scanTexture: React.CSSProperties = {
  backgroundImage:
    'repeating-linear-gradient(115deg, rgba(255,255,255,0.04) 0 1px, transparent 1px 9px)',
};

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
  const [state, setState] = useState<FetchState>({ status: 'loading' });

  useEffect(() => {
    let active = true;
    const id = Number(carId);
    api
      .getCar(id)
      .then(car => {
        if (active) setState({ status: 'ok', car });
      })
      .catch((err: unknown) => {
        if (!active) return;
        setState({
          status: 'error',
          message: err instanceof Error ? err.message : 'Failed to load the car.',
        });
      });
    return () => {
      active = false;
    };
  }, [carId]);

  return (
    <main className="page-wrap">
      <Link
        to="/cars"
        className="text-small-fluid text-primary-container hover:opacity-80 transition-opacity"
      >
        ← All cars
      </Link>

      {state.status === 'loading' && (
        <p className="text-body-fluid text-on-surface-variant animate-pulse mt-4">
          Loading&hellip;
        </p>
      )}

      {state.status === 'error' && (
        <div
          className="card-r border border-line-2 bg-surface p-6 mt-4 text-body-fluid text-error"
          style={cardStyle}
        >
          {state.message}
        </div>
      )}

      {state.status === 'ok' && (
        <div className="flex flex-col gap-fluid-lg mt-4">
          <div>
            <p className="text-eyebrow text-primary-container">{state.car.make}</p>
            <h1 className="text-page-title text-on-surface mt-2">{state.car.name}</h1>
          </div>

          {(state.car.largeImageUrl ?? state.car.smallImageUrl) && (
            <img
              src={state.car.largeImageUrl ?? state.car.smallImageUrl ?? undefined}
              alt={state.car.name}
              className="card-r border border-line-2 w-full max-w-3xl object-cover"
            />
          )}

          <div className="grid grid-kpi gap-fluid">
            {state.car.hp != null && (
              <Spec label="Horsepower" value={state.car.hp.toLocaleString()} />
            )}
            {state.car.weight != null && (
              <Spec label="Weight (kg)" value={state.car.weight.toLocaleString()} />
            )}
            <Spec label="Rain capable" value={state.car.rainEnabled ? 'Yes' : 'No'} />
            <Spec label="Free with sub" value={state.car.freeWithSubscription ? 'Yes' : 'No'} />
          </div>

          {state.car.carClasses.length > 0 && (
            <div className="flex flex-col gap-2">
              <p className="text-th text-on-surface-variant">Car classes</p>
              <div className="flex flex-wrap gap-2">
                {state.car.carClasses.map(cc => (
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

          {state.car.yourBestLaps.length > 0 && (
            <div
              className="card-r border border-line-2 bg-surface overflow-hidden"
              style={cardStyle}
            >
              <div className="card-hp border-b border-line-2" style={scanTexture}>
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
                    {state.car.yourBestLaps.map(l => (
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
