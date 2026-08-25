import { describe, expect, it } from 'vitest';
import { fieldSizeMessage } from './fieldSize';

describe('fieldSizeMessage', () => {
  it('uses singular grammar for one driver', () => {
    expect(fieldSizeMessage(1)).toBe('Only 1 driver has set a time this week.');
  });

  it('uses plural grammar for multiple drivers', () => {
    expect(fieldSizeMessage(2)).toBe('Only 2 drivers have set a time this week.');
  });
});
