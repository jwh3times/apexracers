import { render } from '@testing-library/react';
import { describe, it, expect } from 'vitest';
import IRatingCompareChart from './IRatingCompareChart';

describe('IRatingCompareChart', () => {
  it('draws two polylines when both series have at least two points', () => {
    const { container } = render(
      <IRatingCompareChart you={[1700, 1800, 1854]} rival={[1900, 1880]} />
    );
    expect(container.querySelectorAll('polyline')).toHaveLength(2);
  });

  it('draws one polyline when only one series has enough points', () => {
    const { container } = render(<IRatingCompareChart you={[1700, 1800, 1854]} rival={[1900]} />);
    expect(container.querySelectorAll('polyline')).toHaveLength(1);
  });

  it('renders nothing when neither series has enough points', () => {
    const { container } = render(<IRatingCompareChart you={[1700]} rival={[]} />);
    expect(container.querySelector('svg')).toBeNull();
  });
});
