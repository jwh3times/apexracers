import { useState } from 'react';
import { api } from '../../services/api';
import { useAuth } from '../../context/AuthContext';
import ResourceView from '../../components/ResourceView';
import { useResource } from '../../hooks/useResource';

const CATEGORIES = [
  { id: 5, label: 'Sports Car' },
  { id: 6, label: 'Formula Car' },
  { id: 1, label: 'Oval' },
  { id: 3, label: 'Dirt Oval' },
  { id: 4, label: 'Dirt Road' },
  { id: 2, label: 'Road' },
];

export default function LeaderboardsPage() {
  const { user } = useAuth();
  const [categoryId, setCategoryId] = useState(5);
  const resource = useResource(signal => api.getLeaderboard(categoryId, signal), [categoryId], {
    fallbackMessage: 'Failed to load leaderboard.',
  });

  return (
    <main className="page-wrap">
      <div className="mb-6">
        <p className="text-eyebrow text-primary-container">LEADERBOARDS</p>
        <h1 className="text-page-title text-on-surface mt-2">Global iRating Top 200</h1>
      </div>

      <div className="flex flex-wrap gap-2 mb-6">
        {CATEGORIES.map(c => (
          <button
            key={c.id}
            type="button"
            onClick={() => setCategoryId(c.id)}
            className={`btn-fluid-sm border transition-colors ${
              categoryId === c.id
                ? 'border-primary-container bg-primary-container/10 text-primary-container'
                : 'border-line-2 bg-surface-container text-on-surface-variant hover:bg-surface-container-high'
            }`}
          >
            {c.label}
          </button>
        ))}
      </div>

      <ResourceView resource={resource} />

      {resource.status === 'ok' && resource.data.length === 0 && (
        <p className="text-body-fluid text-on-surface-variant">No leaderboard data available.</p>
      )}

      {resource.status === 'ok' && resource.data.length > 0 && (
        <div className="card-r card-shadow border border-line-2 bg-surface overflow-hidden">
          <div className="overflow-x-auto">
            <table className="w-full border-collapse">
              <thead>
                <tr className="scan-texture border-b border-line-2">
                  <th className="th-p text-th text-on-surface-variant text-right w-12">#</th>
                  <th className="th-p text-th text-on-surface-variant text-left">Driver</th>
                  <th className="th-p text-th text-on-surface-variant text-left">Loc</th>
                  <th className="th-p text-th text-on-surface-variant text-right">Starts</th>
                  <th className="th-p text-th text-on-surface-variant text-right">Wins</th>
                  <th className="th-p text-th text-on-surface-variant text-right">iRating</th>
                  <th className="th-p text-th text-on-surface-variant text-right">TT</th>
                  <th className="th-p text-th text-on-surface-variant text-right">Champ Pts</th>
                </tr>
              </thead>
              <tbody>
                {resource.data.map(r => {
                  const isMe =
                    user?.iRacingCustomerId != null && r.customerId === user.iRacingCustomerId;
                  return (
                    <tr
                      key={r.customerId}
                      className={`border-b border-line-2 last:border-b-0 ${
                        isMe ? 'bg-primary-container/10' : 'hover:bg-surface-container'
                      } transition-colors`}
                    >
                      <td className="td-p text-mono-fluid text-on-surface-variant text-right">
                        {r.standing}
                      </td>
                      <td className="td-p text-body-fluid text-on-surface max-w-0">
                        <span className="block truncate">
                          {r.driverName}
                          {isMe && <span className="text-primary-container"> (you)</span>}
                        </span>
                      </td>
                      <td className="td-p text-small-fluid text-on-surface-variant">
                        {r.location}
                      </td>
                      <td className="td-p text-mono-fluid text-on-surface-variant text-right">
                        {r.starts.toLocaleString()}
                      </td>
                      <td className="td-p text-mono-fluid text-on-surface-variant text-right">
                        {r.wins.toLocaleString()}
                      </td>
                      <td className="td-p text-mono-fluid text-primary-container font-semibold text-right">
                        {r.iRating.toLocaleString()}
                      </td>
                      <td className="td-p text-mono-fluid text-on-surface-variant text-right">
                        {r.ttRating.toLocaleString()}
                      </td>
                      <td className="td-p text-mono-fluid text-on-surface-variant text-right">
                        {r.champPoints.toLocaleString()}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        </div>
      )}
    </main>
  );
}
