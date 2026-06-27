import { render, screen } from '@testing-library/react';
import { describe, it, expect } from 'vitest';
import LapTraceChart from './LapTraceChart';
import type { Lap } from '../services/api';

const lap = (n: number, s: number, incident = false): Lap => ({
  lapNumber: n,
  lapTimeSeconds: s,
  incident,
  valid: s > 0,
});

describe('LapTraceChart', () => {
  it('returns null when there are fewer than two timed laps', () => {
    const { container } = render(
      <LapTraceChart laps={[lap(1, 66.3)]} meanSeconds={66.3} stdDevSeconds={0} />
    );
    expect(container.querySelector('svg')).toBeNull();
  });

  it('ignores untimed laps when deciding whether to render', () => {
    const { container } = render(
      <LapTraceChart laps={[lap(1, 66.3), lap(2, -1)]} meanSeconds={66.3} stdDevSeconds={0} />
    );
    // Only one timed lap → not enough to plot.
    expect(container.querySelector('svg')).toBeNull();
  });

  it('renders an accessible svg with a polyline for two or more timed laps', () => {
    const { container } = render(
      <LapTraceChart
        laps={[lap(1, 66.3), lap(2, 66.7), lap(3, 66.5, true)]}
        meanSeconds={66.5}
        stdDevSeconds={0.2}
      />
    );
    expect(screen.getByRole('img', { name: /lap time trace/i })).toBeInTheDocument();
    expect(container.querySelector('polyline')).not.toBeNull();
    // One incident lap → one red marker circle.
    expect(container.querySelectorAll('circle').length).toBe(1);
  });
});
