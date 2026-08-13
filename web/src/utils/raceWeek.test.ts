import { describe, expect, it } from 'vitest';
import { raceWeekLabel, raceWeekNumber } from './raceWeek';

describe('raceWeekNumber', () => {
  it('maps the opening race week index to Week 1', () => {
    // The bug this exists to prevent: index 0 rendered as "Week 0" on most screens.
    expect(raceWeekNumber(0)).toBe(1);
  });

  it('maps a later race week index', () => {
    expect(raceWeekNumber(11)).toBe(12);
  });
});

describe('raceWeekLabel', () => {
  it('labels the opening race week', () => {
    expect(raceWeekLabel(0)).toBe('Week 1');
  });

  it('labels a later race week', () => {
    expect(raceWeekLabel(4)).toBe('Week 5');
  });
});
