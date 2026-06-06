import { render, screen } from '@testing-library/react';
import { describe, it, expect } from 'vitest';
import PercentileBadge from '../PercentileBadge';

describe('PercentileBadge', () => {
  it('renders the TOP label', () => {
    render(<PercentileBadge pct={12} />);
    expect(screen.getByText('TOP')).toBeInTheDocument();
  });

  it('renders the pct value with percent sign', () => {
    render(<PercentileBadge pct={12} />);
    expect(screen.getByText('12%')).toBeInTheDocument();
  });

  it('renders a different pct value correctly', () => {
    render(<PercentileBadge pct={4} />);
    expect(screen.getByText('4%')).toBeInTheDocument();
  });

  it('renders an SVG ring element', () => {
    const { container } = render(<PercentileBadge pct={12} />);
    const circles = container.querySelectorAll('circle');
    expect(circles.length).toBe(2);
  });

  it('applies lg size scaling', () => {
    const { container } = render(<PercentileBadge pct={12} size="lg" />);
    const svg = container.querySelector('svg');
    expect(svg).not.toBeNull();
    const width = parseFloat(svg!.getAttribute('width') ?? '0');
    // lg: 92 * 1.32 ≈ 121.44
    expect(width).toBeGreaterThan(100);
  });

  it('applies sm size scaling', () => {
    const { container } = render(<PercentileBadge pct={12} size="sm" />);
    const svg = container.querySelector('svg');
    expect(svg).not.toBeNull();
    const width = parseFloat(svg!.getAttribute('width') ?? '0');
    // sm: 92 * 0.74 ≈ 68.08
    expect(width).toBeLessThan(90);
  });

  it('applies md size (default) scaling', () => {
    const { container } = render(<PercentileBadge pct={12} />);
    const svg = container.querySelector('svg');
    expect(svg).not.toBeNull();
    const width = parseFloat(svg!.getAttribute('width') ?? '0');
    // md: 92 * 1 = 92
    expect(width).toBeCloseTo(92, 0);
  });

  it('calculates correct fill fraction for pct=0 (full ring)', () => {
    const { container } = render(<PercentileBadge pct={0} />);
    const circles = container.querySelectorAll('circle');
    const accentCircle = circles[1];
    const dashOffset = parseFloat(accentCircle.getAttribute('stroke-dashoffset') ?? '1');
    // fillFrac = (100 - 0) / 100 = 1.0, so dashoffset = circ * (1-1) = 0
    expect(dashOffset).toBeCloseTo(0, 0);
  });

  it('calculates correct fill fraction for pct=100 (empty ring)', () => {
    const { container } = render(<PercentileBadge pct={100} />);
    const circles = container.querySelectorAll('circle');
    const accentCircle = circles[1];
    const dashArray = parseFloat(
      (accentCircle.getAttribute('stroke-dasharray') ?? '0').split(' ')[0]
    );
    const dashOffset = parseFloat(accentCircle.getAttribute('stroke-dashoffset') ?? '0');
    // fillFrac = (100 - 100) / 100 = 0, so dashoffset = circ * 1 = circ
    expect(dashOffset).toBeCloseTo(dashArray, 0);
  });
});
