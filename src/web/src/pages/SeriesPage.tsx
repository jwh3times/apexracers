import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { api, type Series } from '../services/api';

export default function SeriesPage() {
  const [series, setSeries] = useState<Series[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    api.getSeries()
      .then(setSeries)
      .catch((err: unknown) =>
        setError(err instanceof Error ? err.message : 'Failed to load series.'))
      .finally(() => setLoading(false));
  }, []);

  if (loading) return <main><h1>Active Series</h1><p>Loading&hellip;</p></main>;

  if (error) return <main><h1>Active Series</h1><p style={{ color: 'red' }}>{error}</p></main>;

  if (series.length === 0) {
    return (
      <main>
        <h1>Active Series</h1>
        <p>No active series found. Check back after the ingestion worker has run.</p>
      </main>
    );
  }

  return (
    <main>
      <h1>Active Series</h1>
      <ul>
        {series.map(s => (
          <li key={s.id}>
            {s.currentWeekId != null ? (
              <Link to={`/series/${s.id}/weeks/${s.currentWeekId}`}>{s.name}</Link>
            ) : (
              <span>{s.name}</span>
            )}
          </li>
        ))}
      </ul>
    </main>
  );
}
