import { describe, expect, it } from 'vitest';
import { isUploadedEvidence, lapEvidenceDescription, lapEvidenceLabel } from './lapEvidence';

describe('lapEvidenceLabel', () => {
  it('names a race lap', () => {
    expect(lapEvidenceLabel('RaceLap')).toBe('Race lap');
  });

  it('names an uploaded lap', () => {
    expect(lapEvidenceLabel('UploadedLap')).toBe('Uploaded lap');
  });
});

describe('lapEvidenceDescription', () => {
  it('says an uploaded lap was not raced, and that the field is race laps', () => {
    const description = lapEvidenceDescription('UploadedLap');
    expect(description).toMatch(/not an official race/i);
    expect(description).toMatch(/race laps/i);
  });

  it('says a race lap shares the field conditions', () => {
    expect(lapEvidenceDescription('RaceLap')).toMatch(/official race/i);
  });
});

describe('isUploadedEvidence', () => {
  it('is true only for an uploaded lap', () => {
    expect(isUploadedEvidence('UploadedLap')).toBe(true);
    expect(isUploadedEvidence('RaceLap')).toBe(false);
  });

  it('is false when no evidence is carried at all', () => {
    // A projected car has no lap, so nothing produced one.
    expect(isUploadedEvidence(null)).toBe(false);
    expect(isUploadedEvidence(undefined)).toBe(false);
  });
});
