import { useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { api, type PersonalLap, type TelemetryUploadResult } from '../services/api';
import { formatLapTime } from '../utils/lapTime';

type FileStatus = {
  file: File;
  status: 'pending' | 'uploading' | 'done' | 'error';
  result?: TelemetryUploadResult;
  error?: string;
};

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

function FileRow({ item }: { item: FileStatus }) {
  return (
    <div className="px-4 py-3 flex flex-col gap-2">
      <div className="flex items-center justify-between gap-3 min-w-0">
        <div className="flex items-center gap-2 min-w-0">
          <span
            className="material-symbols-outlined text-[16px] text-on-surface-variant shrink-0"
            aria-hidden="true"
          >
            description
          </span>
          <span className="font-body-sm text-body-sm text-on-surface font-medium truncate">
            {item.file.name}
          </span>
        </div>
        <span className="shrink-0">
          {item.status === 'pending' && (
            <span className="font-label-caps text-label-caps text-on-surface-variant">Pending</span>
          )}
          {item.status === 'uploading' && (
            <span className="font-label-caps text-label-caps text-primary-fixed-dim animate-pulse">
              Uploading…
            </span>
          )}
          {item.status === 'done' && (
            <span
              className="material-symbols-outlined text-[18px] text-primary-container fill"
              aria-hidden="true"
            >
              check_circle
            </span>
          )}
          {item.status === 'error' && (
            <span className="material-symbols-outlined text-[18px] text-error" aria-hidden="true">
              error
            </span>
          )}
        </span>
      </div>

      {item.status === 'done' && item.result && (
        <div className="ml-6 flex flex-col gap-1">
          <p className="font-body-sm text-body-sm text-on-surface-variant">
            {item.result.driverName} &mdash; {item.result.carName}
          </p>
          <p className="font-body-sm text-body-sm text-on-surface-variant">
            {item.result.trackName}
            {item.result.configName ? ` — ${item.result.configName}` : ''}
          </p>
          <div className="flex items-center gap-4 mt-0.5">
            <span className="font-body-sm text-body-sm text-on-surface-variant">
              {item.result.validLaps} valid lap{item.result.validLaps !== 1 ? 's' : ''} of{' '}
              {item.result.totalLaps} total
            </span>
            {item.result.bestLapSeconds != null && (
              <span className="font-data-md text-data-md text-primary-fixed-dim">
                {formatLapTime(item.result.bestLapSeconds)}
              </span>
            )}
          </div>
        </div>
      )}

      {item.status === 'error' && item.error && (
        <p className="ml-6 font-body-sm text-body-sm text-error">{item.error}</p>
      )}
    </div>
  );
}

export default function TelemetryPage() {
  const [queue, setQueue] = useState<FileStatus[]>([]);
  const inputRef = useRef<HTMLInputElement>(null);

  const [recentLaps, setRecentLaps] = useState<PersonalLap[]>([]);
  const [lapsLoading, setLapsLoading] = useState(false);

  const isUploading = queue.some(f => f.status === 'pending' || f.status === 'uploading');
  const hasQueue = queue.length > 0;
  const allDone = hasQueue && !isUploading;
  const doneCount = queue.filter(f => f.status === 'done').length;
  const currentlyUploading = queue.find(f => f.status === 'uploading');

  function fetchLaps() {
    setLapsLoading(true);
    api
      .getMyLaps()
      .then(laps => setRecentLaps(laps.slice(0, 5)))
      .catch(() => {
        /* silently ignore — user may not be authenticated */
      })
      .finally(() => setLapsLoading(false));
  }

  useEffect(() => {
    fetchLaps();
  }, []);

  async function handleChange(e: React.ChangeEvent<HTMLInputElement>) {
    const files = Array.from(e.target.files ?? []);
    if (files.length === 0) return;

    const initial: FileStatus[] = files.map(file => ({ file, status: 'pending' }));
    setQueue(initial);

    for (let i = 0; i < files.length; i++) {
      setQueue(q => q.map((item, idx) => (idx === i ? { ...item, status: 'uploading' } : item)));

      try {
        const result = await api.uploadTelemetry(files[i]);
        setQueue(q =>
          q.map((item, idx) => (idx === i ? { ...item, status: 'done', result } : item))
        );
      } catch (err) {
        const error = err instanceof Error ? err.message : 'Upload failed.';
        setQueue(q =>
          q.map((item, idx) => (idx === i ? { ...item, status: 'error', error } : item))
        );
      }
    }

    fetchLaps();
    if (inputRef.current) inputRef.current.value = '';
  }

  return (
    <main className="px-6 pt-8 pb-20 max-w-[1440px] mx-auto w-full flex flex-col gap-10">
      {/* Page header */}
      <header>
        <h1 className="font-headline-md text-headline-md text-on-surface font-extrabold tracking-tighter">
          Upload Telemetry
        </h1>
        <p className="font-body-sm text-body-sm text-on-surface-variant mt-2 max-w-2xl">
          Upload your <code className="text-primary-fixed-dim">.ibt</code> files to analyze
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
              isUploading
                ? 'border-primary-fixed-dim/60 bg-primary-fixed-dim/5 cursor-not-allowed'
                : 'border-outline-variant hover:border-primary-fixed-dim/50 hover:bg-surface-container-high'
            }`}
          >
            <input
              id="telemetry-file"
              ref={inputRef}
              type="file"
              accept=".ibt"
              multiple
              className="sr-only"
              disabled={isUploading}
              onChange={handleChange}
            />

            {/* Hover glow */}
            <div className="absolute inset-0 bg-primary-fixed-dim/5 opacity-0 group-hover:opacity-100 transition-opacity duration-500 rounded-xl pointer-events-none" />

            {/* Upload icon */}
            <div
              className={`h-20 w-20 rounded-full bg-surface-container-highest flex items-center justify-center mb-6 transition-all duration-300 ${
                isUploading
                  ? 'animate-pulse shadow-[0_0_25px_rgba(0,224,255,0.2)]'
                  : 'group-hover:scale-110 group-hover:shadow-[0_0_25px_rgba(0,224,255,0.2)]'
              }`}
            >
              <span
                className="material-symbols-outlined text-4xl text-primary-fixed-dim fill"
                aria-hidden="true"
              >
                {isUploading ? 'cloud_sync' : 'cloud_upload'}
              </span>
            </div>

            {isUploading ? (
              <>
                <h3 className="font-headline-sm text-headline-sm text-on-surface text-center mb-2">
                  Parsing telemetry&hellip;
                </h3>
                {currentlyUploading && (
                  <p className="font-body-sm text-body-sm text-on-surface-variant text-center">
                    {currentlyUploading.file.name}
                    {queue.length > 1 && (
                      <span className="ml-2 text-on-surface-variant/60">
                        ({doneCount + 1} of {queue.length})
                      </span>
                    )}
                  </p>
                )}
              </>
            ) : (
              <>
                <h3 className="font-headline-sm text-headline-sm text-on-surface text-center mb-2">
                  Drop .ibt files here to sync lap data
                </h3>
                <p className="font-body-sm text-body-sm text-on-surface-variant text-center max-w-sm">
                  Or click to browse your local files. Select multiple files at once.
                </p>
                <div className="mt-8">
                  <span className="px-3 py-1 bg-surface-container-lowest border border-line-2 rounded font-data-md text-data-md text-on-surface-variant">
                    MAX 250 MB per file
                  </span>
                </div>
              </>
            )}
          </label>
        </div>

        {/* Status panel */}
        <div className="lg:col-span-4 flex flex-col gap-4">
          {!hasQueue && (
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

          {hasQueue && (
            <div className="bg-surface border border-line rounded-xl overflow-hidden shadow-[0_4px_24px_rgba(0,0,0,0.5)]">
              {/* Panel header */}
              <div
                className={`px-4 py-3 border-b border-line flex items-center gap-3 ${allDone ? 'border-primary-fixed-dim/30' : ''}`}
              >
                {allDone ? (
                  <>
                    <span
                      className="material-symbols-outlined text-primary-container text-xl fill"
                      aria-hidden="true"
                    >
                      check_circle
                    </span>
                    <span className="font-headline-sm text-headline-sm text-on-surface">
                      Upload Complete
                    </span>
                    {queue.length > 1 && (
                      <span className="ml-auto font-label-caps text-label-caps text-on-surface-variant">
                        {doneCount}/{queue.length}
                      </span>
                    )}
                  </>
                ) : (
                  <>
                    <span
                      className="material-symbols-outlined text-secondary-fixed-dim text-xl animate-pulse"
                      aria-hidden="true"
                    >
                      cloud_sync
                    </span>
                    <span className="font-headline-sm text-headline-sm text-on-surface">
                      Uploading&hellip;
                    </span>
                    {queue.length > 1 && (
                      <span className="ml-auto font-label-caps text-label-caps text-on-surface-variant">
                        {doneCount}/{queue.length}
                      </span>
                    )}
                  </>
                )}
              </div>

              {/* File rows */}
              <div className="divide-y divide-line-2 max-h-[22rem] overflow-y-auto">
                {queue.map((item, i) => (
                  <FileRow key={i} item={item} />
                ))}
              </div>

              {/* Footer */}
              {allDone && (
                <div className="px-4 py-3 border-t border-line flex items-center justify-between">
                  <Link
                    to="/my-laps"
                    className="inline-flex items-center gap-1 font-label-caps text-label-caps text-primary-fixed-dim hover:text-primary transition-colors"
                  >
                    View all my laps
                    <span className="material-symbols-outlined text-sm" aria-hidden="true">
                      arrow_forward
                    </span>
                  </Link>
                  <button
                    onClick={() => setQueue([])}
                    className="font-label-caps text-label-caps text-on-surface-variant hover:text-on-surface transition-colors"
                  >
                    Clear
                  </button>
                </div>
              )}
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
              <span
                className="material-symbols-outlined text-3xl text-on-surface-variant"
                aria-hidden="true"
              >
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
