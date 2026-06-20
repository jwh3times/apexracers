import { describe, it, expect } from 'vitest';
import { topPercentLabel } from '../percentile';

describe('topPercentLabel', () => {
  it('converts a percentile rank (higher better) to a TOP X% label', () => {
    expect(topPercentLabel(96)).toBe('TOP 4%');
  });

  it('rounds up to the nearest whole percent', () => {
    expect(topPercentLabel(99.5)).toBe('TOP 1%');
  });

  it('floors the label at TOP 1% (never TOP 0%)', () => {
    expect(topPercentLabel(100)).toBe('TOP 1%');
  });

  it('handles the bottom of the field', () => {
    expect(topPercentLabel(0)).toBe('TOP 100%');
  });
});
