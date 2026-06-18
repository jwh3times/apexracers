import { useEffect, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { api, type SeasonStandings } from '../services/api';
import { useAuth } from '../context/AuthContext';

type FetchState =
  | { status: 'loading' }
  | { status: 'ok'; data: SeasonStandings }
  | { status: 'error'; message: string };

const cardStyle: React.CSSProperties = {
  boxShadow: '0 1px 0 rgba(255,255,255,.03) inset, 0 18px 40px -24px rgba(0,0,0,.8)',
};

const scanTexture: React.CSSProperties = {
  backgroundImage:
    'repeating-linear-gradient(115deg, rgba(255,255,255,0.04) 0 1px, transparent 1px 9px)',
};

export default function StandingsPage() {
  const { seriesId } = useParams<{ seriesId: string }>();
  const id = Number(seriesId);
  const { user } = useAuth();
  const [carClassId, setCarClassId] = useState<number | null>(null);
  const [state, setState] = useState<FetchState>({ status: 'loading' });

  useEffect(() => {
    let active = true;
    api
      .getStandings(id, carClassId ?? undefined)
      .then(data => {
        if (active) setState({ status: 'ok', data });
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
  }, [id, carClassId]);

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
            <p className="text-eyebrow text-primary-container">CHAMPIONSHIP STANDINGS</p>
            <h1 className="text-page-title text-on-surface mt-2">{state.data.seriesName}</h1>
          </div>

          {state.data.carClasses.length > 1 && (
            <div className="flex flex-wrap gap-2 mb-6">
              {state.data.carClasses.map(c => (
                <button
                  key={c.carClassId}
                  type="button"
                  onClick={() => setCarClassId(c.carClassId)}
                  className={`btn-fluid-sm border transition-colors ${
                    c.carClassId === state.data.carClassId
                      ? 'border-primary-container bg-primary-container/10 text-primary-container'
                      : 'border-line-2 bg-surface-container text-on-surface-variant hover:bg-surface-container-high'
                  }`}
                >
                  {c.carClassName}
                </button>
              ))}
            </div>
          )}

          {state.data.standings.length === 0 ? (
            <p className="text-body-fluid text-on-surface-variant">
              No standings available for this class yet.
            </p>
          ) : (
            <div
              className="card-r border border-line-2 bg-surface overflow-hidden"
              style={cardStyle}
            >
              <div className="overflow-x-auto">
                <table className="w-full border-collapse">
                  <thead>
                    <tr className="border-b border-line-2" style={scanTexture}>
                      <th className="th-p text-th text-on-surface-variant text-right w-12">#</th>
                      <th className="th-p text-th text-on-surface-variant text-left">Driver</th>
                      <th className="th-p text-th text-on-surface-variant text-right">Pts</th>
                      <th className="th-p text-th text-on-surface-variant text-right">Starts</th>
                      <th className="th-p text-th text-on-surface-variant text-right">Wins</th>
                      <th className="th-p text-th text-on-surface-variant text-right">Top 5</th>
                      <th className="th-p text-th text-on-surface-variant text-right">Poles</th>
                      <th className="th-p text-th text-on-surface-variant text-right">Avg Fin</th>
                      <th className="th-p text-th text-on-surface-variant text-right">Inc</th>
                    </tr>
                  </thead>
                  <tbody>
                    {state.data.standings.map(s => {
                      const isMe =
                        user?.iRacingCustomerId != null && s.custId === user.iRacingCustomerId;
                      return (
                        <tr
                          key={s.custId}
                          className={`border-b border-line-2 last:border-b-0 ${
                            isMe ? 'bg-primary-container/10' : 'hover:bg-surface-container'
                          } transition-colors`}
                        >
                          <td className="td-p text-mono-fluid text-on-surface-variant text-right">
                            {s.rank}
                          </td>
                          <td className="td-p text-body-fluid text-on-surface max-w-0">
                            <span className="block truncate">
                              {s.driverName}
                              {isMe && <span className="text-primary-container"> (you)</span>}
                            </span>
                          </td>
                          <td className="td-p text-mono-fluid text-primary-container font-semibold text-right">
                            {s.points.toLocaleString()}
                          </td>
                          <td className="td-p text-mono-fluid text-on-surface-variant text-right">
                            {s.starts}
                          </td>
                          <td className="td-p text-mono-fluid text-on-surface-variant text-right">
                            {s.wins}
                          </td>
                          <td className="td-p text-mono-fluid text-on-surface-variant text-right">
                            {s.top5}
                          </td>
                          <td className="td-p text-mono-fluid text-on-surface-variant text-right">
                            {s.poles}
                          </td>
                          <td className="td-p text-mono-fluid text-on-surface-variant text-right">
                            {s.avgFinishPosition.toFixed(1)}
                          </td>
                          <td className="td-p text-mono-fluid text-on-surface-variant text-right">
                            {s.incidents}
                          </td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            </div>
          )}
        </>
      )}
    </main>
  );
}
