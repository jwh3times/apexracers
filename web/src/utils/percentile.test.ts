import { describe, expect, it } from 'vitest';
import { toTopPercent, topPercentLabel } from './percentile';

describe('toTopPercent', () => {
  it('converts a higher-is-better rank to a lower-is-better TOP value', () => {
    expect(toTopPercent(96)).toBe(4);
  });

  it('rounds up and floors the best rank at TOP 1%', () => {
    expect(toTopPercent(99.5)).toBe(1);
    expect(toTopPercent(100)).toBe(1);
  });

  it('maps the bottom of the field to TOP 100%', () => {
    expect(toTopPercent(0)).toBe(100);
  });
});

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
