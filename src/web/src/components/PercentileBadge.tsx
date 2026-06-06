interface Props {
  pct: number; // TOP X% (lower = better), e.g. 4 means "TOP 4%"
  size?: 'lg' | 'md' | 'sm';
}

export default function PercentileBadge({ pct, size = 'md' }: Props) {
  const S = size === 'lg' ? 1.32 : size === 'sm' ? 0.74 : 1;
  const d = 92 * S;
  const r = (d - 12 * S) / 2;
  const circ = 2 * Math.PI * r;
  const fillFrac = (100 - pct) / 100; // top 4% → 96% of ring filled
  return (
    <div style={{ position: 'relative', width: d, height: d, flexShrink: 0 }}>
      <svg width={d} height={d} style={{ transform: 'rotate(-90deg)' }}>
        <circle
          cx={d / 2}
          cy={d / 2}
          r={r}
          fill="none"
          stroke="var(--color-surface-container-high)"
          strokeWidth={7 * S}
        />
        <circle
          cx={d / 2}
          cy={d / 2}
          r={r}
          fill="none"
          stroke="var(--color-primary-container)"
          strokeWidth={7 * S}
          strokeLinecap="round"
          strokeDasharray={circ}
          strokeDashoffset={circ * (1 - fillFrac)}
          style={{ filter: 'drop-shadow(0 0 6px var(--color-primary-container))' }}
        />
      </svg>
      <div
        style={{
          position: 'absolute',
          inset: 0,
          display: 'grid',
          placeItems: 'center',
          textAlign: 'center',
          lineHeight: 1,
        }}
      >
        <div>
          <div
            style={{
              fontFamily: '"JetBrains Mono",monospace',
              fontSize: 9 * S,
              letterSpacing: '.16em',
              textTransform: 'uppercase',
              color: 'var(--color-primary-container)',
              fontWeight: 600,
            }}
          >
            TOP
          </div>
          <div
            style={{
              fontFamily: '"JetBrains Mono",monospace',
              fontSize: 24 * S,
              fontWeight: 700,
              marginTop: 2,
              color: 'var(--color-on-surface)',
            }}
          >
            {pct}%
          </div>
        </div>
      </div>
    </div>
  );
}
