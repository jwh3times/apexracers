import { Link } from 'react-router-dom';
import { formatLapTime } from '../utils/lapTime';

const MAKE_COLOR: Record<string, string> = {
  Ferrari: '#ff3b30',
  Porsche: '#cBA24a',
  BMW: '#3b82f6',
  Mercedes: '#13c2c2',
  Audi: '#e0457b',
};

const TIMING_ROWS = [
  { p: 1, drv: 'you', car: 'Ferrari 296 GT3', t: 111.418, gap: 0, make: 'Ferrari' },
  { p: 2, drv: 'M. Tervonen', car: 'Porsche 911 GT3 R', t: 111.602, gap: 0.184, make: 'Porsche' },
  { p: 3, drv: 'Y. Tanaka', car: 'BMW M4 GT3', t: 111.744, gap: 0.326, make: 'BMW' },
  { p: 4, drv: 'D. Reyes', car: 'Mercedes-AMG GT3', t: 111.91, gap: 0.492, make: 'Mercedes' },
  { p: 5, drv: 'S. Halberg', car: 'Audi R8 LMS EVO II', t: 112.087, gap: 0.669, make: 'Audi' },
];

const SCAN_BG =
  'repeating-linear-gradient(115deg, rgba(255,255,255,0.04) 0 1px, transparent 1px 9px)';

function TimingStrip() {
  return (
    <div
      className="card-r border border-line-2 bg-surface overflow-hidden w-full max-w-[clamp(320px,38vw,580px)] shrink-0"
      style={{ backgroundImage: SCAN_BG }}
    >
      <div className="flex items-center justify-between px-[18px] py-[15px] border-b border-line-2">
        <div className="flex items-center gap-[9px]">
          <span
            className="w-2 h-2 rounded-full bg-primary-container"
            style={{ boxShadow: '0 0 10px var(--color-primary-container)' }}
          />
          <span className="font-mono text-[11px] tracking-[.16em] uppercase text-primary-container font-semibold">
            LIVE · IMSA GT3 · WATKINS GLEN
          </span>
        </div>
        <span className="font-mono text-[11px] text-on-surface-variant/50">
          WK 9 · 4,820 drivers
        </span>
      </div>

      {TIMING_ROWS.map(r => (
        <div
          key={r.p}
          className={`flex items-center justify-between px-[18px] py-3 border-b border-line-2 last:border-b-0 ${
            r.drv === 'you' ? 'bg-primary-container/[0.16]' : ''
          }`}
        >
          <div className="flex items-center gap-[13px] min-w-0">
            <span
              className={`font-mono text-[13px] font-bold w-[18px] shrink-0 ${
                r.p === 1 ? 'text-primary-container' : 'text-on-surface-variant/50'
              }`}
            >
              P{r.p}
            </span>
            <span
              className="w-[9px] h-[9px] rounded-[3px] shrink-0"
              style={{ background: MAKE_COLOR[r.make] }}
            />
            <div className="min-w-0">
              <div
                className={`font-semibold text-[13.5px] text-on-surface ${
                  r.drv === 'you' ? 'uppercase tracking-[.06em]' : ''
                }`}
              >
                {r.drv}
              </div>
              <div className="text-[11.5px] text-on-surface-variant/60 truncate">{r.car}</div>
            </div>
          </div>
          <div className="flex items-center gap-4 shrink-0">
            <span className="font-mono text-[13.5px] font-semibold">{formatLapTime(r.t)}</span>
            <span className="font-mono text-[12px] text-on-surface-variant/50 w-[52px] text-right">
              {r.gap === 0 ? '—' : `+${r.gap.toFixed(3)}`}
            </span>
          </div>
        </div>
      ))}

      <div className="flex items-center justify-between px-[18px] py-[14px]">
        <span className="text-[12.5px] text-on-surface-variant">Your standing this week</span>
        <span className="font-mono text-[10px] font-bold tracking-[.18em] px-3 py-1 rounded-[7px] bg-primary-container text-on-primary-fixed">
          TOP 4%
        </span>
      </div>
    </div>
  );
}

const FEATURES = [
  {
    icon: 'speed',
    title: 'Performance Percentiles',
    description:
      'See exactly where your pace ranks against every recorded lap for a car, in a series week. Surfaced as high-impact TOP X% badges.',
  },
  {
    icon: 'recommend',
    title: 'Edge Recommendations',
    description:
      'A deterministic ranking of the cars where you hold the greatest competitive advantage. Data-driven — never a guess.',
  },
  {
    icon: 'monitoring',
    title: 'Telemetry, Decoded',
    description:
      'Drag in your .ibt files or sync straight from iRacing. Best vs. median deltas, lap volume, and trend — all in one place.',
  },
];

export default function HomePage() {
  return (
    <div className="min-h-screen flex flex-col bg-background text-on-surface">
      {/* ── Header ────────────────────────────────────────────── */}
      <header
        className="h-[70px] flex items-center px-[clamp(20px,5vw,64px)] border-b border-line-2 sticky top-0 z-20 backdrop-blur-[12px]"
        style={{ background: 'color-mix(in oklab, var(--color-background) 84%, transparent)' }}
      >
        <div className="flex items-center gap-[11px]">
          <div
            className="w-7 h-7 shrink-0 rounded-[7px] bg-primary-container grid place-items-center text-on-primary-fixed font-extrabold text-[17px]"
            style={{ boxShadow: '0 0 22px -4px var(--color-primary-container)' }}
          >
            A
          </div>
          <span className="font-bold tracking-[-0.02em] text-[16px] whitespace-nowrap">
            APEX<b className="text-primary-container font-bold">//</b>RACERS
          </span>
        </div>

        <nav className="ml-11 hidden md:flex gap-1">
          {['Series', 'How it works', 'Pricing'].map(l => (
            <span
              key={l}
              className="inline-flex items-center h-[34px] px-4 rounded-[10px] font-medium text-[14px] text-on-surface-variant cursor-default hover:text-on-surface hover:bg-surface-container transition-colors"
            >
              {l}
            </span>
          ))}
        </nav>

        <div className="ml-auto flex items-center gap-[10px]">
          <Link
            to="/login"
            className="btn-fluid inline-flex items-center border border-line-2 font-semibold text-on-surface-variant hover:bg-surface-container hover:text-on-surface transition-colors"
          >
            Sign in
          </Link>
          <Link
            to="/login"
            className="btn-fluid inline-flex items-center gap-2 font-semibold text-on-primary-fixed bg-primary-container hover:brightness-105 transition-all"
            style={{ boxShadow: '0 0 26px -8px var(--color-primary-container)' }}
          >
            Start free
            <span className="material-symbols-outlined text-[16px]">chevron_right</span>
          </Link>
        </div>
      </header>

      {/* ── Hero ──────────────────────────────────────────────── */}
      <section className="max-w-[1240px] mx-auto w-full px-[clamp(20px,5vw,64px)] py-[clamp(40px,7vw,88px)] pb-[60px] flex flex-col lg:flex-row gap-14 lg:items-center">
        <div className="flex-1 min-w-0">
          <div className="inline-flex items-center gap-[9px] px-3 py-[6px] rounded-full border border-primary-container/40 bg-primary-container/[0.16] font-mono text-[11px] tracking-[.16em] uppercase text-primary-container font-semibold">
            <span className="material-symbols-outlined fill text-[13px]">bolt</span>
            COMPANION APP FOR iRACING
          </div>

          <h1
            className="font-bold leading-[.98] tracking-[-0.03em] mt-[22px] mb-0"
            style={{ fontSize: 'clamp(38px, 5.6vw, 68px)' }}
          >
            Find the
            <br />
            tenths that
            <br />
            <span className="text-primary-container">win races.</span>
          </h1>

          <p className="text-[17px] text-on-surface-variant mt-[22px] max-w-[44ch] leading-[1.55]">
            ApexRacers turns the iRacing leaderboard into real-time percentile intelligence — so you
            always know which car, which series, and which corner is costing you the win.
          </p>

          <div className="flex gap-3 mt-[30px] flex-wrap">
            <Link
              to="/login"
              className="inline-flex items-center gap-2 h-[clamp(38px,3.2vw,56px)] px-[clamp(16px,1.8vw,28px)] text-[clamp(14px,1vw,17px)] rounded-[clamp(8px,0.8vw,12px)] font-semibold text-on-primary-fixed bg-primary-container hover:brightness-105 transition-all"
              style={{ boxShadow: '0 0 26px -8px var(--color-primary-container)' }}
            >
              Start free
              <span className="material-symbols-outlined text-[17px]">chevron_right</span>
            </Link>
            <Link
              to="/series"
              className="inline-flex items-center gap-2 h-[clamp(38px,3.2vw,56px)] px-[clamp(16px,1.8vw,28px)] text-[clamp(14px,1vw,17px)] rounded-[clamp(8px,0.8vw,12px)] font-semibold border border-line-2 bg-surface-container text-on-surface hover:bg-surface-container-high transition-all"
            >
              Browse series
            </Link>
          </div>

          <div className="flex gap-7 mt-[38px]">
            {[
              ['48K', 'laps ingested / day'],
              ['1,240', 'active series'],
              ['A→D', 'all license classes'],
            ].map(([v, l]) => (
              <div key={l}>
                <div className="font-mono text-[23px] font-bold">{v}</div>
                <div className="text-[12px] text-on-surface-variant/60 mt-[3px]">{l}</div>
              </div>
            ))}
          </div>
        </div>

        <TimingStrip />
      </section>

      {/* ── Features ──────────────────────────────────────────── */}
      <section className="max-w-[1240px] mx-auto w-full px-[clamp(20px,5vw,64px)] pb-10">
        <div className="grid gap-4 grid-cols-1 md:grid-cols-3">
          {FEATURES.map(f => (
            <div
              key={f.title}
              className="card-r border border-line-2 bg-surface card-p flex flex-col gap-fluid"
              style={{
                boxShadow: '0 1px 0 rgba(255,255,255,.03) inset, 0 18px 40px -24px rgba(0,0,0,.8)',
              }}
            >
              <div className="w-11 h-11 rounded-[12px] grid place-items-center bg-primary-container/[0.16] text-primary-container border border-primary-container/40">
                <span className="material-symbols-outlined text-[22px]">{f.icon}</span>
              </div>
              <h3 className="m-0 text-[18px] font-bold tracking-[-0.01em]">{f.title}</h3>
              <p className="m-0 text-[13.5px] text-on-surface-variant leading-[1.55]">
                {f.description}
              </p>
            </div>
          ))}
        </div>
      </section>

      {/* ── CTA band ──────────────────────────────────────────── */}
      <section className="max-w-[1240px] mx-auto w-full px-[clamp(20px,5vw,64px)] mt-5 pb-[70px]">
        <div
          className="card-r border border-line-2 text-center px-[clamp(24px,4vw,64px)] py-[clamp(36px,4.5vw,72px)]"
          style={{
            backgroundImage: [
              SCAN_BG,
              'linear-gradient(180deg, var(--color-surface) 0%, var(--color-surface-container-low) 100%)',
            ].join(', '),
          }}
        >
          <h2
            className="font-bold tracking-[-0.025em] m-0"
            style={{ fontSize: 'clamp(26px, 4vw, 40px)' }}
          >
            Know your edge. Every week.
          </h2>
          <p className="text-[14px] text-on-surface-variant mt-[14px] mb-[26px] max-w-[60ch] mx-auto leading-[1.55]">
            Free for Standard drivers. No iRacing OAuth required to get started — link your customer
            ID and go.
          </p>
          <Link
            to="/login"
            className="inline-flex items-center gap-2 h-[clamp(38px,3.2vw,56px)] px-[clamp(16px,1.8vw,28px)] text-[clamp(14px,1vw,17px)] rounded-[clamp(8px,0.8vw,12px)] font-semibold text-on-primary-fixed bg-primary-container hover:brightness-105 transition-all"
            style={{ boxShadow: '0 0 26px -8px var(--color-primary-container)' }}
          >
            Create your account
          </Link>
        </div>
      </section>

      {/* ── Footer ────────────────────────────────────────────── */}
      <footer className="mt-auto border-t border-line-2 px-[clamp(20px,5vw,64px)] py-[26px] flex items-center gap-4 text-on-surface-variant/50 text-[12.5px] flex-wrap">
        <span className="font-bold tracking-[-0.02em] text-[14px] whitespace-nowrap text-on-surface-variant">
          APEX<b className="text-primary-container font-bold">//</b>RACERS
        </span>
        <span className="ml-auto">© 2026 ApexRacers · Not affiliated with iRacing.com</span>
        <Link to="/terms" className="hover:text-on-surface-variant transition-colors">
          Terms
        </Link>
        <Link to="/privacy" className="hover:text-on-surface-variant transition-colors">
          Privacy
        </Link>
      </footer>
    </div>
  );
}
