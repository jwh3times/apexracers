import type { LapSessionType } from '../services/api';
import type { PaceMode, PaceSourceValue } from '../context/PaceSourceContext';

const UPLOAD_SESSIONS: { id: LapSessionType; label: string }[] = [
  { id: 'Race', label: 'Race' },
  { id: 'TimeTrial', label: 'Time Trial' },
  { id: 'Qualifying', label: 'Qualifying' },
  { id: 'Practice', label: 'Practice' },
];

const IcoTrophy = () => (
  <svg
    viewBox="0 0 24 24"
    width="18"
    height="18"
    fill="none"
    stroke="currentColor"
    strokeWidth="1.8"
    strokeLinecap="round"
    strokeLinejoin="round"
    aria-hidden="true"
  >
    <path d="M7 4h10v4a5 5 0 01-10 0zM7 6H4v1a3 3 0 003 3M17 6h3v1a3 3 0 01-3 3M9 18h6M12 13v5M9 21h6" />
  </svg>
);

const IcoUpload = () => (
  <svg
    viewBox="0 0 24 24"
    width="18"
    height="18"
    fill="none"
    stroke="currentColor"
    strokeWidth="1.8"
    strokeLinecap="round"
    strokeLinejoin="round"
    aria-hidden="true"
  >
    <path d="M12 16V4M12 4l-4 4M12 4l4 4M4 16v2a2 2 0 002 2h12a2 2 0 002-2v-2" />
  </svg>
);

const IcoLock = () => (
  <svg
    viewBox="0 0 24 24"
    width="12"
    height="12"
    fill="none"
    stroke="currentColor"
    strokeWidth="2"
    strokeLinecap="round"
    strokeLinejoin="round"
    aria-hidden="true"
  >
    <rect x="5" y="11" width="14" height="9" rx="2" />
    <path d="M8 11V8a4 4 0 018 0v3" />
  </svg>
);

function Tile({
  id,
  current,
  icon,
  title,
  desc,
  onClick,
}: {
  id: PaceMode;
  current: PaceMode;
  icon: React.ReactNode;
  title: string;
  desc: string;
  onClick: (id: PaceMode) => void;
}) {
  return (
    <button
      type="button"
      role="radio"
      aria-checked={current === id}
      className={`pcs-tile${current === id ? ' is-on' : ''}`}
      onClick={() => onClick(id)}
    >
      <span className="pcs-radio" />
      <span className="pcs-tile-ic">{icon}</span>
      <span className="pcs-tile-txt">
        <span className="pcs-tile-title">{title}</span>
        <span className="pcs-tile-desc">{desc}</span>
      </span>
    </button>
  );
}

interface Props {
  value: PaceSourceValue;
  onChange: (next: PaceSourceValue) => void;
  className?: string;
}

export default function CalculationSource({ value, onChange, className = '' }: Props) {
  const blended = value.mode === 'blend';

  const setMode = (mode: PaceMode) => {
    if (mode !== value.mode) {
      onChange({ mode, sessions: mode === 'official' ? [] : value.sessions });
    }
  };

  const toggleSession = (id: LapSessionType) => {
    const sessions = value.sessions.includes(id)
      ? value.sessions.filter(s => s !== id)
      : [...value.sessions, id];
    onChange({ ...value, sessions });
  };

  return (
    <div className={`pcs ${className}`}>
      <div className="pcs-head">How we calculate your pace</div>

      <div className="pcs-tiles" role="radiogroup" aria-label="How we calculate your pace">
        <Tile
          id="official"
          current={value.mode}
          icon={<IcoTrophy />}
          title="Official race laps only"
          desc="Your official iRacing race results."
          onClick={setMode}
        />
        <Tile
          id="blend"
          current={value.mode}
          icon={<IcoUpload />}
          title="Official + my uploaded laps"
          desc="Add uploaded sessions and pick which types count."
          onClick={setMode}
        />
      </div>

      <div className={`pcs-reveal${blended ? ' is-open' : ''}`}>
        <div>
          <div className="pcs-inset">
            <div className="pcs-inset-lbl">Uploaded sessions to blend in</div>
            <div className="pcs-pill-row">
              <span
                className="pcs-pill is-locked"
                aria-label="Official race laps — always included"
              >
                <span className="pcs-lock">
                  <IcoLock />
                </span>
                <span className="pcs-pill-lbl">Official race</span>
              </span>
              <span className="pcs-plus" aria-hidden="true">
                ＋
              </span>
              {UPLOAD_SESSIONS.map(s => (
                <button
                  key={s.id}
                  type="button"
                  className={`pcs-pill${value.sessions.includes(s.id) ? ' is-on' : ''}`}
                  aria-pressed={value.sessions.includes(s.id)}
                  onClick={() => toggleSession(s.id)}
                >
                  <span className="pcs-pill-dot" />
                  <span className="pcs-pill-lbl">{s.label}</span>
                </button>
              ))}
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
