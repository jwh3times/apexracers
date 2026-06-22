import { Link } from 'react-router-dom';

const cardStyle: React.CSSProperties = {
  boxShadow: '0 1px 0 rgba(255,255,255,.03) inset, 0 18px 40px -24px rgba(0,0,0,.8)',
};

const scanTexture: React.CSSProperties = {
  backgroundImage:
    'repeating-linear-gradient(115deg, rgba(255,255,255,0.04) 0 1px, transparent 1px 9px)',
};

export default function ComingSoonPage() {
  return (
    <main className="page-wrap">
      <div
        className="card-r border border-white/10 bg-surface overflow-hidden max-w-2xl mx-auto"
        style={cardStyle}
      >
        <div className="card-hp border-b border-white/10" style={scanTexture}>
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
