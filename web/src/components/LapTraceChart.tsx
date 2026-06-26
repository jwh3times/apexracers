import { useEffect, useRef, useState } from 'react';
import type { Lap } from '../services/api';

interface Props {
  laps: Lap[];
  meanSeconds: number;
  stdDevSeconds: number;
  h?: number;
}

/**
 * Per-lap pace trace: a line of timed laps over the run, a shaded mean ± 1σ
 * consistency band, and red markers on incident laps. Returns null when there are
 * fewer than two timed laps to plot.
 */
export default function LapTraceChart({ laps, meanSeconds, stdDevSeconds, h = 160 }: Props) {
  const svgRef = useRef<SVGSVGElement>(null);
  const [w, setW] = useState(0);

  useEffect(() => {
    const el = svgRef.current;
    if (!el) return;
    const ro = new ResizeObserver(entries => setW(entries[0].contentRect.width));
    ro.observe(el);
    return () => ro.disconnect();
  }, []);

  const valid = laps.filter(l => l.valid);
  if (valid.length < 2) return null;

  const accent = 'var(--color-primary-container)';
  const times = valid.map(l => l.lapTimeSeconds);
  const lapNums = valid.map(l => l.lapNumber);
  const minLap = Math.min(...lapNums);
  const lapRange = Math.max(1, Math.max(...lapNums) - minLap);

  const hasBand = stdDevSeconds > 0;
  const bandLo = hasBand ? meanSeconds - stdDevSeconds : Math.min(...times);
  const bandHi = hasBand ? meanSeconds + stdDevSeconds : Math.max(...times);
  const minY = Math.min(Math.min(...times), bandLo);
  const yRange = Math.max(0.001, Math.max(Math.max(...times), bandHi) - minY);

  const pad = 6;
  const X = (lapNumber: number) => pad + ((lapNumber - minLap) / lapRange) * (w - pad * 2);
  const Y = (t: number) => pad + ((t - minY) / yRange) * (h - pad * 2);

  const points = w > 0 ? valid.map(l => [X(l.lapNumber), Y(l.lapTimeSeconds)] as const) : [];
  const line = points.map(p => `${p[0]},${p[1]}`).join(' ');
  const incidents = w > 0 ? valid.filter(l => l.incident) : [];

  return (
    <svg
      ref={svgRef}
      role="img"
      aria-label="Lap time trace"
      width="100%"
      height={h}
      style={{ display: 'block', overflow: 'visible' }}
    >
      {points.length > 0 && (
        <>
          {hasBand && (
            <rect
              x={pad}
              width={Math.max(0, w - pad * 2)}
              y={Y(bandLo)}
              height={Math.max(0, Y(bandHi) - Y(bandLo))}
              fill={accent}
              fillOpacity={0.12}
            />
          )}
          {hasBand && (
            <line
              x1={pad}
              x2={w - pad}
              y1={Y(meanSeconds)}
              y2={Y(meanSeconds)}
              stroke={accent}
              strokeOpacity={0.5}
              strokeDasharray="4 4"
            />
          )}
          <polyline
            points={line}
            fill="none"
            stroke={accent}
            strokeWidth="2"
            strokeLinecap="round"
            strokeLinejoin="round"
          />
          {incidents.map(l => (
            <circle
              key={l.lapNumber}
              cx={X(l.lapNumber)}
              cy={Y(l.lapTimeSeconds)}
              r="3.5"
              fill="#f87171"
            />
          ))}
        </>
      )}
    </svg>
  );
}
