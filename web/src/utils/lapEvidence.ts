import type { LapEvidence } from '../services/api';

/**
 * A Personal Best is drawn from one of two kinds of evidence, and which one it came from changes
 * how the number should be read.
 *
 * A **race lap** was set in an official race, in the same conditions as the field it is ranked
 * against. An **uploaded lap** comes from a driver's own telemetry — it may be a practice lap, from
 * a different season, on different weather or a different setup. The field a driver is ranked
 * against is composed entirely of race laps, so an uploaded personal best is being compared with
 * laps of a kind it is not.
 *
 * Showing which one produced the number is the whole point: the same lap time means something
 * different depending on where it came from, and the driver is the one who can tell which.
 */

/** The short label shown beside a personal best. */
export function lapEvidenceLabel(evidence: LapEvidence): string {
  return evidence === 'UploadedLap' ? 'Uploaded lap' : 'Race lap';
}

/** The longer explanation, for a title attribute or a caption. */
export function lapEvidenceDescription(evidence: LapEvidence): string {
  return evidence === 'UploadedLap'
    ? 'From your uploaded telemetry, not an official race. The field is made up of race laps.'
    : 'Set in an official race, in the same conditions as the field.';
}

/** True when the lap was not raced — the case worth drawing attention to. */
export function isUploadedEvidence(evidence: LapEvidence | null | undefined): boolean {
  return evidence === 'UploadedLap';
}
