import type { RaceGuideEntry, CarAnalytics } from '../services/api';
import { topPercentLabel } from './percentile';

export interface Alert {
  id: string;
  kind: 'race' | 'percentile';
  message: string;
}

export interface DeriveAlertsInput {
  raceGuide: RaceGuideEntry[];
  analytics: CarAnalytics[];
  now?: Date;
}

/** A race counts as "soon" if it starts within this many minutes. */
const RACE_SOON_MINUTES = 30;

/**
 * Pure, client-side derivation of the driver's notifications. Two MVP sources (no persistence
 * or server push): official races starting within the next 30 minutes (from the race guide),
 * and per-car percentile improvements (latest tracked week beat the previous one). Extracted as
 * a pure function so the thresholds and messaging are unit-tested directly.
 */
export function deriveAlerts({
  raceGuide,
  analytics,
  now = new Date(),
}: DeriveAlertsInput): Alert[] {
  const alerts: Alert[] = [];
  const nowMs = now.getTime();

  // Races starting within the next 30 minutes (ignore already-started or far-off sessions).
  for (const r of raceGuide) {
    const minsUntil = Math.round((new Date(r.startTime).getTime() - nowMs) / 60000);
    if (minsUntil >= 0 && minsUntil <= RACE_SOON_MINUTES) {
      alerts.push({
        id: `race-${r.seriesId}-${r.startTime}`,
        kind: 'race',
        message:
          minsUntil === 0
            ? `${r.seriesName} is starting now`
            : `${r.seriesName} starts in ${minsUntil}m`,
      });
    }
  }

  // Percentile improvements: the latest tracked week beat the previous one for a car.
  for (const c of analytics) {
    const h = c.percentileHistory;
    if (h.length >= 2 && h[h.length - 1].percentileRank > h[h.length - 2].percentileRank) {
      alerts.push({
        id: `pct-${c.carId}-${c.seriesId}`,
        kind: 'percentile',
        message: `Improved to ${topPercentLabel(h[h.length - 1].percentileRank)} in ${c.carName}`,
      });
    }
  }

  return alerts;
}
