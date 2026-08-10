import { render, screen } from '@testing-library/react';
import { describe, it, expect } from 'vitest';
import PercentileBadge from './PercentileBadge';

describe('PercentileBadge', () => {
  it('renders the TOP label', () => {
    render(<PercentileBadge rank={88} />);
    expect(screen.getByText('TOP')).toBeInTheDocument();
  });

  it('converts the percentile rank to a TOP value with percent sign', () => {
    render(<PercentileBadge rank={88} />);
    expect(screen.getByText('12%')).toBeInTheDocument();
  });

  it('renders a different percentile rank correctly', () => {
    render(<PercentileBadge rank={96} />);
    expect(screen.getByText('4%')).toBeInTheDocument();
  });

  it('renders an SVG ring element', () => {
    const { container } = render(<PercentileBadge rank={88} />);
    const circles = container.querySelectorAll('circle');
    expect(circles.length).toBe(2);
  });

  it('applies lg size scaling', () => {
    const { container } = render(<PercentileBadge rank={88} size="lg" />);
    const svg = container.querySelector('svg');
    expect(svg).not.toBeNull();
    const width = parseFloat(svg!.getAttribute('width') ?? '0');
    // lg: 92 * 1.32 ≈ 121.44
    expect(width).toBeGreaterThan(100);
  });

  it('applies sm size scaling', () => {
    const { container } = render(<PercentileBadge rank={88} size="sm" />);
    const svg = container.querySelector('svg');
    expect(svg).not.toBeNull();
    const width = parseFloat(svg!.getAttribute('width') ?? '0');
    // sm: 92 * 0.74 ≈ 68.08
    expect(width).toBeLessThan(90);
  });

  it('applies md size (default) scaling', () => {
    const { container } = render(<PercentileBadge rank={88} />);
    const svg = container.querySelector('svg');
    expect(svg).not.toBeNull();
    const width = parseFloat(svg!.getAttribute('width') ?? '0');
    // md: 92 * 1 = 92
    expect(width).toBeCloseTo(92, 0);
  });

  it('floors rank 100 at TOP 1% and renders a nearly full ring', () => {
    const { container } = render(<PercentileBadge rank={100} />);
    const circles = container.querySelectorAll('circle');
    const accentCircle = circles[1];
    const dashArray = parseFloat(
      (accentCircle.getAttribute('stroke-dasharray') ?? '0').split(' ')[0]
    );
    const dashOffset = parseFloat(accentCircle.getAttribute('stroke-dashoffset') ?? '1');
    expect(screen.getByText('1%')).toBeInTheDocument();
    expect(dashOffset).toBeCloseTo(dashArray * 0.01, 4);
  });

  it('renders rank 0 as TOP 100% with an empty ring', () => {
    const { container } = render(<PercentileBadge rank={0} />);
    const circles = container.querySelectorAll('circle');
    const accentCircle = circles[1];
    const dashArray = parseFloat(
      (accentCircle.getAttribute('stroke-dasharray') ?? '0').split(' ')[0]
    );
    const dashOffset = parseFloat(accentCircle.getAttribute('stroke-dashoffset') ?? '0');
    expect(screen.getByText('100%')).toBeInTheDocument();
    expect(dashOffset).toBeCloseTo(dashArray, 0);
  });

  it('renders a compact inline pill (no ring) for the chip size', () => {
    const { container } = render(<PercentileBadge rank={96} size="chip" />);
    expect(screen.getByText('TOP 4%')).toBeInTheDocument();
    // The chip is a pill, not the SVG ring gauge.
    expect(container.querySelector('svg')).toBeNull();
  });
});
