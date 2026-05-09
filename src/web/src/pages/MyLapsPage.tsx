import { useEffect, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { api, type PersonalLap } from '../services/api';

function formatLapTime(seconds: number): string {
  const mins = Math.floor(seconds / 60);
  const secs = (seconds % 60).toFixed(3).padStart(6, '0');
  return `${mins}:${secs}`;
}

export default function MyLapsPage() {
  const [searchParams] = useSearchParams();
  const customerIdParam = searchParams.get('customerId');
  const customerId = customerIdParam != null ? Number(customerIdParam) : null;

  const [laps, setLaps] = useState<PersonalLap[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (customerId == null || customerId <= 0) return;
    setLoading(true);
    api.getMyLaps(customerId)
      .then(setLaps)
      .catch((err: unknown) =>
        setError(err instanceof Error ? err.message : 'Failed to load laps.'))
      .finally(() => setLoading(false));
  }, [customerId]);

  if (customerId == null || customerId <= 0) {
    return (
      <main>
        <h1>My Laps</h1>
        <p>
          Upload an <a href="/telemetry">.ibt telemetry file</a> to record your personal
          lap times. Your customer ID will appear in the upload result.
        </p>
      </main>
    );
  }

  return (
    <main>
      <h1>My Laps</h1>
      <p>Customer ID: <strong>{customerId}</strong></p>

      {loading && <p>Loading&hellip;</p>}
      {error && <p style={{ color: 'red' }}>{error}</p>}

      {!loading && !error && laps.length === 0 && (
        <p>No laps recorded yet. <a href="/telemetry">Upload a telemetry file</a> to get started.</p>
      )}

      {laps.length > 0 && (
        <table>
          <thead>
            <tr>
              <th>Track</th>
              <th>Car</th>
              <th>Best Lap</th>
              <th>Laps Recorded</th>
              <th>Last Session</th>
            </tr>
          </thead>
          <tbody>
            {laps.map((lap, i) => (
              <tr key={i}>
                <td>
                  {lap.trackName}
                  {lap.configName ? ` — ${lap.configName}` : ''}
                </td>
                <td>{lap.carName}</td>
                <td>{formatLapTime(lap.bestLapSeconds)}</td>
                <td>{lap.lapCount}</td>
                <td>{new Date(lap.lastRecordedAt).toLocaleDateString()}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </main>
  );
}
