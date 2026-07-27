import { useEffect, useState } from 'react';
import { Link, useParams } from 'react-router';
import { api, type TrackCatalogDetail } from '../../services/api';
import { formatLapTime } from '../../utils/lapTime';

type FetchState =
  | { status: 'loading' }
  | { status: 'ok'; track: TrackCatalogDetail }
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

export default function TrackDetailPage() {
  const { trackId } = useParams<{ trackId: string }>();
  const [state, setState] = useState<FetchState>({ status: 'loading' });

  useEffect(() => {
    let active = true;
    api
      .getTrack(Number(trackId))
      .then(track => {
        if (active) setState({ status: 'ok', track });
      })
      .catch((err: unknown) => {
        if (!active) return;
        setState({
          status: 'error',
          message: err instanceof Error ? err.message : 'Failed to load the track.',
        });
      });
    return () => {
      active = false;
    };
  }, [trackId]);

  return (
    <main className="page-wrap">
      <Link
        to="/tracks"
        className="text-small-fluid text-primary-container hover:opacity-80 transition-opacity"
      >
        ← All tracks
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
            <p className="text-eyebrow text-primary-container">
              {state.track.configName || state.track.category}
            </p>
            <h1 className="text-page-title text-on-surface mt-2">{state.track.name}</h1>
            {state.track.location && (
              <p className="text-small-fluid text-on-surface-variant mt-1">
                {state.track.location}
              </p>
            )}
          </div>

          {(state.track.largeImageUrl ?? state.track.smallImageUrl) && (
            <img
              src={state.track.largeImageUrl ?? state.track.smallImageUrl ?? undefined}
              alt={state.track.name}
              className="card-r border border-line-2 w-full max-w-3xl object-cover"
            />
          )}

          <div className="grid grid-kpi gap-fluid">
            {state.track.lengthMiles != null && (
              <Spec label="Length (mi)" value={state.track.lengthMiles.toFixed(2)} />
            )}
            {state.track.cornersPerLap != null && (
              <Spec label="Corners" value={state.track.cornersPerLap} />
            )}
            {state.track.numberPitstalls != null && (
              <Spec label="Pit stalls" value={state.track.numberPitstalls} />
            )}
            {state.track.pitRoadSpeedLimit != null && (
              <Spec label="Pit limit (kph)" value={state.track.pitRoadSpeedLimit} />
            )}
            <Spec label="Night lighting" value={state.track.nightLighting ? 'Yes' : 'No'} />
          </div>

          {state.track.trackMapUrl && (
            <a
              href={state.track.trackMapUrl}
              target="_blank"
              rel="noopener noreferrer"
              className="btn-fluid-sm self-start rounded-[7px] border border-primary-container text-primary-container"
            >
              Interactive track map ↗
            </a>
          )}

          {state.track.yourBestLaps.length > 0 && (
            <div
              className="card-r border border-line-2 bg-surface overflow-hidden"
              style={cardStyle}
            >
              <div className="card-hp border-b border-line-2" style={scanTexture}>
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
                    {state.track.yourBestLaps.map(l => (
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
