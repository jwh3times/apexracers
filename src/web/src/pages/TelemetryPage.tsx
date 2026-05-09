import { useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { api, type TelemetryUploadResult } from '../services/api';

type Status = 'idle' | 'uploading' | 'done' | 'error';

function formatLapTime(seconds: number): string {
  const mins = Math.floor(seconds / 60);
  const secs = (seconds % 60).toFixed(3).padStart(6, '0');
  return `${mins}:${secs}`;
}

export default function TelemetryPage() {
  const [status, setStatus]   = useState<Status>('idle');
  const [result, setResult]   = useState<TelemetryUploadResult | null>(null);
  const [error, setError]     = useState<string | null>(null);
  const inputRef              = useRef<HTMLInputElement>(null);

  async function handleChange(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0];
    if (!file) return;

    setStatus('uploading');
    setResult(null);
    setError(null);

    try {
      const data = await api.uploadTelemetry(file);
      setResult(data);
      setStatus('done');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Upload failed.');
      setStatus('error');
    } finally {
      // Reset so the same file can be re-uploaded after an error
      if (inputRef.current) inputRef.current.value = '';
    }
  }

  return (
    <main>
      <h1>Upload Telemetry</h1>
      <p>
        Upload an iRacing <code>.ibt</code> telemetry file to record your personal
        lap times for a car and track combination.
      </p>

      <input
        ref={inputRef}
        type="file"
        accept=".ibt"
        disabled={status === 'uploading'}
        onChange={handleChange}
      />

      {status === 'uploading' && <p>Parsing telemetry&hellip;</p>}

      {status === 'error' && (
        <p style={{ color: 'red' }}>{error}</p>
      )}

      {status === 'done' && result && (
        <div>
          <h2>Upload complete</h2>
          <p>
            {result.driverName} &mdash; <strong>{result.trackName}</strong>
            {result.configName ? ` — ${result.configName}` : ''}
          </p>
          <p>{result.carName}</p>
          <p>
            {result.validLaps} valid lap{result.validLaps !== 1 ? 's' : ''} of{' '}
            {result.totalLaps} total
          </p>
          {result.bestLapSeconds != null && (
            <p>Best lap: <strong>{formatLapTime(result.bestLapSeconds)}</strong></p>
          )}
          <p>
            <Link to={`/my-laps?customerId=${result.customerId}`}>
              View all my laps &rarr;
            </Link>
          </p>
        </div>
      )}
    </main>
  );
}
