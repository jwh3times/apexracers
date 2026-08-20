import { describe, expect, it } from 'vitest';
import { splitLabel, splitNumber } from './split';

describe('splitNumber', () => {
  it('turns the zero-based Split Index into the one-based number a reader expects', () => {
    expect(splitNumber(0)).toBe(1);
    expect(splitNumber(4)).toBe(5);
  });
});

describe('splitLabel', () => {
  it('labels the strongest Split as the first of its Race Session', () => {
    expect(splitLabel(0, 3)).toBe('1 of 3');
  });

  it('labels a lower Split by its rank', () => {
    expect(splitLabel(2, 3)).toBe('3 of 3');
  });

  it('labels a Race Session that was never divided', () => {
    expect(splitLabel(0, 1)).toBe('1 of 1');
  });

  it('returns null for an unknown Split Index rather than calling it the first', () => {
    expect(splitLabel(null, 3)).toBeNull();
    expect(splitLabel(undefined, 3)).toBeNull();
  });

  it('returns null when the Split count is unknown, so a position is never half-known', () => {
    expect(splitLabel(0, null)).toBeNull();
    expect(splitLabel(0, undefined)).toBeNull();
  });
});
