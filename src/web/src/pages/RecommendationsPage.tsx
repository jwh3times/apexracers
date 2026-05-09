import { useEffect, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { api, type CarRecommendation } from '../services/api';

export default function RecommendationsPage() {
  const [searchParams] = useSearchParams();
  const weekIdParam = searchParams.get('weekId');
  const weekId = weekIdParam != null ? Number(weekIdParam) : null;

  const [recs, setRecs] = useState<CarRecommendation[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (weekId == null) return;
    setLoading(true);
    setRecs([]);
    setError(null);
    api.getRecommendations(weekId)
      .then(setRecs)
      .catch((err: unknown) =>
        setError(err instanceof Error ? err.message : 'Failed to load recommendations.'))
      .finally(() => setLoading(false));
  }, [weekId]);

  if (weekId == null) {
    return (
      <main>
        <h1>My Car Recommendations</h1>
        <p>
          Navigate to a week from the <a href="/series">Series page</a> and click
          &ldquo;See my car recommendations&rdquo; to view your personalized rankings.
        </p>
      </main>
    );
  }

  return (
    <main>
      <h1>My Car Recommendations</h1>

      {loading && <p>Loading&hellip;</p>}
      {error && <p style={{ color: 'red' }}>{error}</p>}

      {!loading && !error && recs.length === 0 && (
        <p>
          No recommendations available for this week. Sign in with iRacing so your
          official time trial results can be matched against the field.
        </p>
      )}

      {recs.length > 0 && (
        <table>
          <thead>
            <tr>
              <th>Rank</th>
              <th>Car</th>
              <th>Your Percentile</th>
              <th>Field Size</th>
            </tr>
          </thead>
          <tbody>
            {recs.map(r => (
              <tr key={r.carId}>
                <td>#{r.rank}</td>
                <td>{r.carName}</td>
                <td>{r.percentileRank.toFixed(1)}th</td>
                <td>{r.sampleSize.toLocaleString()}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </main>
  );
}
