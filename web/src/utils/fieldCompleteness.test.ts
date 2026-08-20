import { describe, expect, it } from 'vitest';
import { unrepresentedEntriesNote } from './fieldCompleteness';

describe('unrepresentedEntriesNote', () => {
  it('says nothing when the field is complete', () => {
    expect(unrepresentedEntriesNote(0, 0)).toBeNull();
  });

  it('says nothing when the entries were never counted', () => {
    expect(unrepresentedEntriesNote(null, null)).toBeNull();
    expect(unrepresentedEntriesNote(undefined, undefined)).toBeNull();
  });

  it('names team entries and why they have no result', () => {
    expect(unrepresentedEntriesNote(2, 0)).toBe(
      '2 team entries are not listed — a result names exactly one driver, so these held finishing positions with no result to show.'
    );
  });

  it('uses the singular for one entry', () => {
    expect(unrepresentedEntriesNote(1, 0)).toBe(
      '1 team entry is not listed — a result names exactly one driver, so these held finishing positions with no result to show.'
    );
  });

  it('names AI entries on their own', () => {
    expect(unrepresentedEntriesNote(0, 3)).toContain('3 AI entries are not listed');
  });

  it('names both kinds together', () => {
    expect(unrepresentedEntriesNote(2, 1)).toContain(
      '2 team entries and 1 AI entry are not listed'
    );
  });

  it('treats a counted kind as absent when only the other kind occurred', () => {
    expect(unrepresentedEntriesNote(0, 1)).not.toContain('team');
    expect(unrepresentedEntriesNote(1, 0)).not.toContain('AI');
  });

  it('reports a partial count rather than staying silent when only one kind was counted', () => {
    expect(unrepresentedEntriesNote(2, null)).toContain('2 team entries');
  });
});
