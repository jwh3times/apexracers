import { useId } from 'react';

interface Props {
  data: number[];
  w?: number;
  h?: number;
}

export default function Sparkline({ data, w = 132, h = 38 }: Props) {
  const rawId = useId();
  const id = `sg${rawId.replace(/[^a-zA-Z0-9]/g, '')}`;

  if (data.length < 2) return null;
  const c = 'var(--color-primary-container)';
  const min = Math.min(...data), max = Math.max(...data);
  const range = Math.max(1, max - min);
  const pad = 3;
  const X = (i: number) => pad + (i / (data.length - 1)) * (w - pad * 2);
  const Y = (v: number) => h - pad - ((v - min) / range) * (h - pad * 2);
  const pts = data.map((v, i) => [X(i), Y(v)] as [number, number]);
  const line = pts.map(p => p.join(',')).join(' ');
  return (
    <svg width={w} height={h} style={{ display: 'block', overflow: 'visible' }}>
      <defs>
        <linearGradient id={id} x1="0" y1="0" x2="0" y2="1">
          <stop offset="0" stopColor={c} stopOpacity={0.34} />
          <stop offset="1" stopColor={c} stopOpacity={0} />
        </linearGradient>
      </defs>
      <polygon points={`${pad},${h-pad} ${line} ${w-pad},${h-pad}`} fill={`url(#${id})`} />
      <polyline points={line} fill="none" stroke={c} strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" />
      <circle cx={pts[pts.length-1][0]} cy={pts[pts.length-1][1]} r="3" fill={c} />
    </svg>
  );
}
