import { render, screen } from '@testing-library/react';
import { describe, it, expect } from 'vitest';
import PercentileBadge from './PercentileBadge';

describe('PercentileBadge', () => {
  it('renders the TOP label', () => {
    render(<PercentileBadge topSharePercent={12} />);
    expect(screen.getByText('TOP')).toBeInTheDocument();
  });

  it('renders the placement share with a percent sign', () => {
    render(<PercentileBadge topSharePercent={12} />);
    expect(screen.getByText('12%')).toBeInTheDocument();
  });

  it('renders a different placement share correctly', () => {
    render(<PercentileBadge topSharePercent={4} />);
    expect(screen.getByText('4%')).toBeInTheDocument();
  });

  it('renders an SVG ring element', () => {
    const { container } = render(<PercentileBadge topSharePercent={12} />);
    const circles = container.querySelectorAll('circle');
    expect(circles.length).toBe(2);
  });

  it('applies lg size scaling', () => {
    const { container } = render(<PercentileBadge topSharePercent={12} size="lg" />);
    const svg = container.querySelector('svg');
    expect(svg).not.toBeNull();
    const width = parseFloat(svg!.getAttribute('width') ?? '0');
    // lg: 92 * 1.32 ≈ 121.44
    expect(width).toBeGreaterThan(100);
  });

  it('applies sm size scaling', () => {
    const { container } = render(<PercentileBadge topSharePercent={12} size="sm" />);
    const svg = container.querySelector('svg');
    expect(svg).not.toBeNull();
    const width = parseFloat(svg!.getAttribute('width') ?? '0');
    // sm: 92 * 0.74 ≈ 68.08
    expect(width).toBeLessThan(90);
  });

  it('applies md size (default) scaling', () => {
    const { container } = render(<PercentileBadge topSharePercent={12} />);
    const svg = container.querySelector('svg');
    expect(svg).not.toBeNull();
    const width = parseFloat(svg!.getAttribute('width') ?? '0');
    // md: 92 * 1 = 92
    expect(width).toBeCloseTo(92, 0);
  });

  it('renders a top-1% placement as a nearly full ring', () => {
    const { container } = render(<PercentileBadge topSharePercent={1} />);
    const circles = container.querySelectorAll('circle');
    const accentCircle = circles[1];
    const dashArray = parseFloat(
      (accentCircle.getAttribute('stroke-dasharray') ?? '0').split(' ')[0]
    );
    const dashOffset = parseFloat(accentCircle.getAttribute('stroke-dashoffset') ?? '1');
    expect(screen.getByText('1%')).toBeInTheDocument();
    expect(dashOffset).toBeCloseTo(dashArray * 0.01, 4);
  });

  it('renders a bottom-of-field placement as an empty ring', () => {
    const { container } = render(<PercentileBadge topSharePercent={100} />);
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
    const { container } = render(<PercentileBadge topSharePercent={4} size="chip" />);
    expect(screen.getByText('TOP 4%')).toBeInTheDocument();
    // The chip is a pill, not the SVG ring gauge.
    expect(container.querySelector('svg')).toBeNull();
  });
});
