import { Link } from 'react-router';

export default function ComingSoonPage() {
  return (
    <main className="page-wrap">
      <div className="card-r card-shadow border border-white/10 bg-surface overflow-hidden max-w-2xl mx-auto">
        <div className="card-hp scan-texture border-b border-white/10">
          <p className="text-eyebrow text-primary-container">Coming soon</p>
        </div>
        <div className="card-p flex flex-col items-center gap-4 text-center py-12">
          <span
            className="material-symbols-outlined text-5xl text-primary-container"
            aria-hidden="true"
            style={{ filter: 'drop-shadow(0 0 18px rgba(0,224,255,0.35))' }}
          >
            speed
          </span>
          <h1 className="text-page-title text-on-surface">Live iRacing analytics arriving soon</h1>
          <p className="text-body-fluid text-on-surface-variant max-w-md">
            Series, leaderboards, standings, the car &amp; track catalog, and live race data are on
            the way. In the meantime your personal telemetry tools are ready to use right now.
          </p>
          <div className="flex flex-wrap items-center justify-center gap-fluid mt-2">
            <Link
              to="/telemetry"
              className="inline-flex items-center gap-2 btn-fluid border-transparent bg-primary-container text-on-primary-fixed font-semibold transition-all"
              style={{ boxShadow: '0 0 26px -8px var(--color-primary-container)' }}
            >
              <span className="material-symbols-outlined text-[17px]" aria-hidden="true">
                upload_file
              </span>
              Upload telemetry
            </Link>
            <Link
              to="/my-laps"
              className="inline-flex items-center gap-2 btn-fluid border border-line-2 bg-surface-container text-on-surface font-semibold transition-all hover:bg-surface-container-high"
            >
              <span className="material-symbols-outlined text-[17px]" aria-hidden="true">
                timer
              </span>
              My Laps
            </Link>
          </div>
        </div>
      </div>
    </main>
  );
}
