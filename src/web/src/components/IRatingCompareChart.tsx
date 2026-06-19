interface Props {
  you: number[];
  rival: number[];
  h?: number;
}

const YOU_COLOR = 'var(--color-primary-container)'; // cyan accent
const RIVAL_COLOR = '#f5a623'; // warm amber — distinct from the cyan "you" line

/**
 * Overlays two drivers' iRating trajectories on a shared scale. Each series is plotted
 * across the full width by index (start/end aligned), so the shapes are comparable even
 * when the histories differ in length. Uses a fixed viewBox stretched to the container
 * width, so it scales fluidly without measuring layout. Returns null when neither series
 * has at least two points.
 */
export default function IRatingCompareChart({ you, rival, h = 48 }: Props) {
  const youOk = you.length >= 2;
  const rivalOk = rival.length >= 2;
  if (!youOk && !rivalOk) return null;

  const W = 100;
  const pad = 4;
  const all = [...(youOk ? you : []), ...(rivalOk ? rival : [])];
  const min = Math.min(...all);
  const max = Math.max(...all);
  const range = Math.max(1, max - min);

  const toLine = (data: number[]): string =>
    data
      .map((v, i) => {
        const x = pad + (i / (data.length - 1)) * (W - pad * 2);
        const y = h - pad - ((v - min) / range) * (h - pad * 2);
        return `${x.toFixed(2)},${y.toFixed(2)}`;
      })
      .join(' ');

  return (
    <svg
      width="100%"
      height={h}
      viewBox={`0 0 ${W} ${h}`}
      preserveAspectRatio="none"
      style={{ display: 'block', overflow: 'visible' }}
    >
      {rivalOk && (
        <polyline
          points={toLine(rival)}
          fill="none"
          stroke={RIVAL_COLOR}
          strokeWidth={1.5}
          strokeLinecap="round"
          strokeLinejoin="round"
          vectorEffect="non-scaling-stroke"
        />
      )}
      {youOk && (
        <polyline
          points={toLine(you)}
          fill="none"
          stroke={YOU_COLOR}
          strokeWidth={1.5}
          strokeLinecap="round"
          strokeLinejoin="round"
          vectorEffect="non-scaling-stroke"
        />
      )}
    </svg>
  );
}
