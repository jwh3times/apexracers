import { describe, expect, it } from 'vitest';
import { percentileLabel, topShareLabel } from './percentile';

describe('topShareLabel', () => {
  it('labels a placement share the server computed', () => {
    expect(topShareLabel(4)).toBe('TOP 4%');
  });

  it('does not reinterpret the value it is given', () => {
    // The share arrives already rounded and already a share of the whole field. Deriving it here
    // from a percentile rank is what previously turned first-of-two into "TOP 1%".
    expect(topShareLabel(50)).toBe('TOP 50%');
    expect(topShareLabel(100)).toBe('TOP 100%');
  });
});

describe('percentileLabel', () => {
  it('formats a percentile rank to one decimal', () => {
    expect(percentileLabel(92.34)).toBe('92.3%');
    expect(percentileLabel(50)).toBe('50.0%');
  });
});
