import { useEffect, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { api, type WeekCar } from '../services/api';

function formatLapTime(seconds: number): string {
  const mins = Math.floor(seconds / 60);
  const secs = (seconds % 60).toFixed(3).padStart(6, '0');
  return `${mins}:${secs}`;
}

export default function WeekDetailPage() {
  const { seriesId, weekId } = useParams<{ seriesId: string; weekId: string }>();
  const [cars, setCars] = useState<WeekCar[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!seriesId || !weekId) return;
    api.getCarsForWeek(Number(seriesId), Number(weekId))
      .then(setCars)
      .catch((err: unknown) =>
        setError(err instanceof Error ? err.message : 'Failed to load week data.'))
      .finally(() => setLoading(false));
  }, [seriesId, weekId]);

  if (loading) return <main><h1>Week Detail</h1><p>Loading&hellip;</p></main>;

  if (error) return <main><h1>Week Detail</h1><p style={{ color: 'red' }}>{error}</p></main>;

  return (
    <main>
      <h1>Week Detail</h1>
      <p>Series <strong>{seriesId}</strong> &mdash; Week <strong>{weekId}</strong></p>

      {weekId && (
        <p>
          <Link to={`/recommendations?weekId=${weekId}`}>See my car recommendations &rarr;</Link>
        </p>
      )}

      {cars.length === 0 ? (
        <p>No lap time data yet for this week.</p>
      ) : (
        <table>
          <thead>
            <tr>
              <th>Car</th>
              <th>Entries</th>
              <th>Fastest Lap</th>
              <th>Median Lap</th>
            </tr>
          </thead>
          <tbody>
            {cars.map(car => (
              <tr key={car.carId}>
                <td>{car.carName}</td>
                <td>{car.entryCount.toLocaleString()}</td>
                <td>{car.fastestLapSeconds != null ? formatLapTime(car.fastestLapSeconds) : '—'}</td>
                <td>{car.medianLapSeconds != null ? formatLapTime(car.medianLapSeconds) : '—'}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </main>
  );
}
