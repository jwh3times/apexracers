import { render } from '@testing-library/react';
import { describe, it, expect } from 'vitest';
import Sparkline from './Sparkline';

describe('Sparkline', () => {
  it('renders nothing when data has fewer than 2 points', () => {
    const { container } = render(<Sparkline data={[50]} />);
    expect(container.firstChild).toBeNull();
  });

  it('renders nothing for empty data', () => {
    const { container } = render(<Sparkline data={[]} />);
    expect(container.firstChild).toBeNull();
  });

  it('renders an SVG when data has at least 2 points', () => {
    const { container } = render(<Sparkline data={[30, 70]} />);
    expect(container.querySelector('svg')).not.toBeNull();
  });

  it('renders a polyline (the line chart)', () => {
    const { container } = render(<Sparkline data={[30, 50, 70]} />);
    expect(container.querySelector('polyline')).not.toBeNull();
  });

  it('renders a polygon (the fill area)', () => {
    const { container } = render(<Sparkline data={[30, 50, 70]} />);
    expect(container.querySelector('polygon')).not.toBeNull();
  });

  it('renders an end-point circle', () => {
    const { container } = render(<Sparkline data={[30, 50, 70]} />);
    expect(container.querySelector('circle')).not.toBeNull();
  });

  it('renders a linearGradient definition', () => {
    const { container } = render(<Sparkline data={[30, 70]} />);
    expect(container.querySelector('linearGradient')).not.toBeNull();
  });

  it('fills 100% of its container width', () => {
    const { container } = render(<Sparkline data={[30, 70]} />);
    const svg = container.querySelector('svg');
    expect(svg?.getAttribute('width')).toBe('100%');
  });

  it('uses the provided height', () => {
    const { container } = render(<Sparkline data={[30, 70]} h={60} />);
    const svg = container.querySelector('svg');
    expect(svg?.getAttribute('height')).toBe('60');
  });

  it('defaults to h=38 when not specified', () => {
    const { container } = render(<Sparkline data={[30, 70]} />);
    const svg = container.querySelector('svg');
    expect(svg?.getAttribute('height')).toBe('38');
  });

  it('handles flat data (min === max) without errors', () => {
    const { container } = render(<Sparkline data={[50, 50, 50]} />);
    expect(container.querySelector('svg')).not.toBeNull();
  });

  it('handles many data points', () => {
    const data = Array.from({ length: 12 }, (_, i) => i * 8);
    const { container } = render(<Sparkline data={data} />);
    expect(container.querySelector('polyline')).not.toBeNull();
  });
});
