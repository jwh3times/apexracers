import { useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { api, type PersonalLap, type TelemetryUploadResult } from '../services/api';

type Status = 'idle' | 'uploading' | 'done' | 'error';

function formatLapTime(seconds: number): string {
  const mins = Math.floor(seconds / 60);
  const secs = (seconds % 60).toFixed(3).padStart(6, '0');
  return `${mins}:${secs}`;
}

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString('en-US', {
    month: 'short',
    day: 'numeric',
    year: 'numeric',
  });
}

function trackLabel(lap: PersonalLap): string {
  return lap.configName ? `${lap.trackName} — ${lap.configName}` : lap.trackName;
}

export default function TelemetryPage() {
  const [status, setStatus] = useState<Status>('idle');
  const [result, setResult] = useState<TelemetryUploadResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [fileName, setFileName] = useState<string | null>(null);
  const inputRef = useRef<HTMLInputElement>(null);

  const [recentLaps, setRecentLaps] = useState<PersonalLap[]>([]);
  const [lapsLoading, setLapsLoading] = useState(false);

  function fetchLaps() {
    setLapsLoading(true);
    api.getMyLaps()
      .then(laps => setRecentLaps(laps.slice(0, 5)))
      .catch(() => {/* silently ignore — user may not be authenticated */})
      .finally(() => setLapsLoading(false));
  }

  useEffect(() => { fetchLaps(); }, []);

  async function handleChange(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0];
    if (!file) return;

    setFileName(file.name);
    setStatus('uploading');
    setResult(null);
    setError(null);

    try {
      const data = await api.uploadTelemetry(file);
      setResult(data);
      setStatus('done');
      fetchLaps();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Upload failed.');
      setStatus('error');
    } finally {
      if (inputRef.current) inputRef.current.value = '';
    }
  }

  return (
    <main className="px-6 pt-8 pb-20 max-w-[1440px] mx-auto w-full flex flex-col gap-10">
      {/* Page header */}
      <header>
        <h1 className="font-headline-md text-headline-md text-on-surface font-extrabold tracking-tighter">
          Upload Telemetry
        </h1>
        <p className="font-body-sm text-body-sm text-on-surface-variant mt-2 max-w-2xl">
          Upload your <code className="text-primary-fixed-dim">.ibt</code> file to analyze
          performance, track lap times, and compare against your historical data.
        </p>
      </header>

      {/* Bento grid */}
      <div className="grid grid-cols-1 lg:grid-cols-12 gap-4">
        {/* Drag & drop zone */}
        <div className="lg:col-span-8">
          <label
            htmlFor="telemetry-file"
            className={`relative border-2 border-dashed rounded-xl flex flex-col items-center justify-center p-12 lg:p-20 bg-surface/50 backdrop-blur-xl transition-all duration-300 cursor-pointer group h-full ${
              status === 'uploading'
                ? 'border-primary-fixed-dim/60 bg-primary-fixed-dim/5 cursor-not-allowed'
                : 'border-outline-variant hover:border-primary-fixed-dim/50 hover:bg-surface-container-high'
            }`}
          >
            <input
              id="telemetry-file"
              ref={inputRef}
              type="file"
              accept=".ibt"
              className="sr-only"
              disabled={status === 'uploading'}
              onChange={handleChange}
            />

            {/* Hover glow */}
            <div className="absolute inset-0 bg-primary-fixed-dim/5 opacity-0 group-hover:opacity-100 transition-opacity duration-500 rounded-xl pointer-events-none" />

            {/* Upload icon */}
            <div
              className={`h-20 w-20 rounded-full bg-surface-container-highest flex items-center justify-center mb-6 transition-all duration-300 ${
                status === 'uploading'
                  ? 'animate-pulse shadow-[0_0_25px_rgba(0,228,121,0.2)]'
                  : 'group-hover:scale-110 group-hover:shadow-[0_0_25px_rgba(0,228,121,0.2)]'
              }`}
            >
              <span
                className="material-symbols-outlined text-4xl text-primary-fixed-dim fill"
                aria-hidden="true"
              >
                {status === 'uploading' ? 'cloud_sync' : 'cloud_upload'}
              </span>
            </div>

            {status === 'uploading' ? (
              <>
                <h3 className="font-headline-sm text-headline-sm text-on-surface text-center mb-2">
                  Parsing telemetry&hellip;
                </h3>
                {fileName && (
                  <p className="font-body-sm text-body-sm text-on-surface-variant text-center">
                    {fileName}
                  </p>
                )}
              </>
            ) : (
              <>
                <h3 className="font-headline-sm text-headline-sm text-on-surface text-center mb-2">
                  Drop .ibt file here to sync lap data
                </h3>
                <p className="font-body-sm text-body-sm text-on-surface-variant text-center max-w-sm">
                  Or click to browse your local files. Supported format: .ibt
                </p>
                <div className="mt-8">
                  <span className="px-3 py-1 bg-surface-container-lowest border border-line-2 rounded font-data-md text-data-md text-on-surface-variant">
                    MAX 250 MB
                  </span>
                </div>
              </>
            )}
          </label>
        </div>

        {/* Status panel */}
        <div className="lg:col-span-4 flex flex-col gap-4">
          {status === 'idle' && (
            <div className="glass-panel rounded-xl p-6 flex flex-col items-center justify-center gap-4 h-full text-center">
              <span
                className="material-symbols-outlined text-3xl text-on-surface-variant"
                aria-hidden="true"
              >
                inbox
              </span>
              <p className="font-body-sm text-body-sm text-on-surface-variant max-w-[200px]">
                Upload a file to see your session results here.
              </p>
            </div>
          )}

          {status === 'uploading' && fileName && (
            <div className="bg-surface border border-line rounded-xl p-6 shadow-[0_4px_24px_rgba(0,0,0,0.5)]">
              <div className="flex items-start justify-between mb-4">
                <div className="flex items-center gap-3 min-w-0">
                  <span
                    className="material-symbols-outlined text-secondary-fixed-dim shrink-0"
                    aria-hidden="true"
                  >
                    description
                  </span>
                  <div className="min-w-0">
                    <p className="font-body-sm text-body-sm text-on-surface font-semibold truncate">
                      {fileName}
                    </p>
                    <p className="font-label-caps text-label-caps text-on-surface-variant mt-1">
                      Uploading&hellip;
                    </p>
                  </div>
                </div>
              </div>
              <div className="w-full h-1.5 bg-surface-container-highest rounded-full overflow-hidden">
                <div className="h-full bg-primary-fixed-dim animate-pulse w-3/4" />
              </div>
            </div>
          )}

          {status === 'done' && result && (
            <div className="bg-surface border border-primary-fixed-dim/30 rounded-xl p-6 shadow-[0_0_20px_rgba(0,228,121,0.1)] relative overflow-hidden flex flex-col gap-4">
              <div className="absolute -right-10 -top-10 w-32 h-32 bg-primary-container/10 blur-3xl rounded-full pointer-events-none" />

              <div className="flex items-center gap-3">
                <span
                  className="material-symbols-outlined text-primary-container text-2xl fill"
                  aria-hidden="true"
                >
                  check_circle
                </span>
                <h2 className="font-headline-sm text-headline-sm text-on-surface">
                  Upload Complete
                </h2>
              </div>

              <div className="bg-surface-container-lowest p-4 rounded-lg border border-line flex flex-col gap-2">
                <div className="flex justify-between items-center">
                  <span className="font-body-sm text-body-sm text-on-surface-variant">Driver</span>
                  <span className="font-body-sm text-body-sm text-on-surface font-semibold">
                    {result.driverName}
                  </span>
                </div>
                <div className="flex justify-between items-center">
                  <span className="font-body-sm text-body-sm text-on-surface-variant">Car</span>
                  <span className="font-body-sm text-body-sm text-on-surface font-semibold">
                    {result.carName}
                  </span>
                </div>
                <div className="flex justify-between items-center">
                  <span className="font-body-sm text-body-sm text-on-surface-variant">Track</span>
                  <span className="font-body-sm text-body-sm text-on-surface font-semibold">
                    {result.trackName}
                    {result.configName ? ` — ${result.configName}` : ''}
                  </span>
                </div>
                {result.bestLapSeconds != null && (
                  <div className="flex justify-between items-center pt-2 border-t border-line mt-1">
                    <span className="font-body-sm text-body-sm text-on-surface-variant">
                      Best Lap
                    </span>
                    <span className="font-data-lg text-data-lg text-primary-fixed-dim">
                      {formatLapTime(result.bestLapSeconds)}
                    </span>
                  </div>
                )}
              </div>

              <p className="font-body-sm text-body-sm text-on-surface-variant">
                {result.validLaps} valid lap{result.validLaps !== 1 ? 's' : ''} of{' '}
                {result.totalLaps} total recorded.
              </p>

              <Link
                to="/my-laps"
                className="inline-flex items-center gap-1 font-label-caps text-label-caps text-primary-fixed-dim hover:text-primary transition-colors"
              >
                View all my laps
                <span className="material-symbols-outlined text-sm" aria-hidden="true">
                  arrow_forward
                </span>
              </Link>
            </div>
          )}

          {status === 'error' && error && (
            <div className="glass-panel rounded-xl p-6 border border-error/30">
              <div className="flex items-center gap-3 mb-3">
                <span
                  className="material-symbols-outlined text-error text-2xl"
                  aria-hidden="true"
                >
                  error
                </span>
                <h2 className="font-headline-sm text-headline-sm text-on-surface">Upload Failed</h2>
              </div>
              <p className="font-body-sm text-body-sm text-error">{error}</p>
              <button
                onClick={() => { setStatus('idle'); setError(null); }}
                className="mt-4 font-label-caps text-label-caps text-on-surface-variant hover:text-on-surface transition-colors"
              >
                Try again
              </button>
            </div>
          )}
        </div>
      </div>

      {/* Recent Sessions */}
      <section>
        <div className="flex items-center justify-between mb-6">
          <h2 className="font-headline-md text-headline-md text-on-surface">Recent Sessions</h2>
          <Link
            to="/my-laps"
            className="font-label-caps text-label-caps text-on-surface-variant hover:text-primary-fixed-dim transition-colors flex items-center gap-1"
          >
            View All
            <span className="material-symbols-outlined text-[16px]" aria-hidden="true">
              arrow_forward
            </span>
          </Link>
        </div>

        <div className="bg-surface border border-line rounded-xl overflow-hidden">
          {lapsLoading && (
            <p className="px-6 py-8 font-body-sm text-body-sm text-on-surface-variant animate-pulse">
              Loading&hellip;
            </p>
          )}

          {!lapsLoading && recentLaps.length === 0 && (
            <div className="px-6 py-8 flex flex-col items-center gap-3 text-center">
              <span className="material-symbols-outlined text-3xl text-on-surface-variant" aria-hidden="true">
                timer_off
              </span>
              <p className="font-body-sm text-body-sm text-on-surface-variant">
                No sessions yet — upload an .ibt file to get started.
              </p>
            </div>
          )}

          {!lapsLoading && recentLaps.length > 0 && (
            <>
              {/* Table header */}
              <div className="grid grid-cols-12 gap-4 px-6 py-4 bg-surface-container-low border-b border-surface-container-high font-label-caps text-label-caps text-on-surface-variant">
                <div className="col-span-6 md:col-span-4">Car</div>
                <div className="col-span-6 md:col-span-4">Track</div>
                <div className="hidden md:block md:col-span-2 text-right">Best Lap</div>
                <div className="hidden md:block md:col-span-2 text-right">Date</div>
              </div>

              {/* Rows */}
              <div className="flex flex-col">
                {recentLaps.map((lap, i) => (
                  <div
                    key={i}
                    className={`grid grid-cols-12 gap-4 px-6 py-4 items-center hover:bg-surface-container-highest transition-colors ${
                      i < recentLaps.length - 1 ? 'border-b border-surface-container-high' : ''
                    }`}
                  >
                    <div className="col-span-6 md:col-span-4">
                      <p className="font-body-sm text-body-sm text-on-surface font-semibold">
                        {lap.carName}
                      </p>
                    </div>
                    <div className="col-span-6 md:col-span-4">
                      <p className="font-body-sm text-body-sm text-on-surface">{trackLabel(lap)}</p>
                    </div>
                    <div className="hidden md:block md:col-span-2 text-right font-data-md text-data-md text-primary-fixed-dim">
                      {formatLapTime(lap.bestLapSeconds)}
                    </div>
                    <div className="hidden md:block md:col-span-2 text-right font-body-sm text-body-sm text-on-surface-variant">
                      {formatDate(lap.lastRecordedAt)}
                    </div>
                  </div>
                ))}
              </div>
            </>
          )}
        </div>
      </section>
    </main>
  );
}
