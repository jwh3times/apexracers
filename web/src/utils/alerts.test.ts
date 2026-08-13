import { describe, it, expect } from 'vitest';
import { deriveAlerts } from './alerts';
import type { RaceGuideEntry, CarAnalytics } from '../services/api';

const NOW = new Date('2026-06-20T12:00:00Z');

function race(overrides: Partial<RaceGuideEntry> = {}): RaceGuideEntry {
  return {
    seriesId: 1,
    seriesName: 'GT3 Challenge',
    startTime: '2026-06-20T12:15:00Z', // 15 min out
    endTime: '2026-06-20T13:00:00Z',
    entryCount: 20,
    raceWeekNum: 3,
    ...overrides,
  };
}

function analytics(history: number[], overrides: Partial<CarAnalytics> = {}): CarAnalytics {
  return {
    carId: 1,
    carName: 'BMW M4 GT3',
    seriesId: 1,
    seriesName: 'GT3 Challenge',
    latestPercentileRank: history[history.length - 1] ?? 0,
    latestTopSharePercent: 100 - Math.round(history[history.length - 1] ?? 0),
    bestPercentileRank: Math.max(...history, 0),
    bestTopSharePercent: 100 - Math.round(Math.max(...history, 0)),
    personalBestLapSeconds: null,
    medianLapSeconds: null,
    totalWeeks: history.length,
    percentileHistory: history.map((percentileRank, i) => ({
      weekNumber: i + 1,
      trackName: 'Spa',
      configName: '',
      percentileRank,
      topSharePercent: 100 - Math.round(percentileRank),
      sampleSize: 100,
      computedAt: `2026-0${i + 1}-01T00:00:00Z`,
    })),
    ...overrides,
  };
}

describe('deriveAlerts', () => {
  it('returns nothing for empty inputs', () => {
    expect(deriveAlerts({ raceGuide: [], analytics: [], now: NOW })).toEqual([]);
  });

  it('alerts for a race starting within 30 minutes', () => {
    const alerts = deriveAlerts({ raceGuide: [race()], analytics: [], now: NOW });
    expect(alerts).toHaveLength(1);
    expect(alerts[0].kind).toBe('race');
    expect(alerts[0].message).toBe('GT3 Challenge starts in 15m');
  });

  it('uses "starting now" for a race at the current time', () => {
    const alerts = deriveAlerts({
      raceGuide: [race({ startTime: '2026-06-20T12:00:00Z' })],
      analytics: [],
      now: NOW,
    });
    expect(alerts[0].message).toBe('GT3 Challenge is starting now');
  });

  it('ignores races more than 30 minutes out or already started', () => {
    const alerts = deriveAlerts({
      raceGuide: [
        race({ seriesId: 2, startTime: '2026-06-20T13:00:00Z' }), // 60 min out
        race({ seriesId: 3, startTime: '2026-06-20T11:55:00Z' }), // started 5 min ago
      ],
      analytics: [],
      now: NOW,
    });
    expect(alerts).toEqual([]);
  });

  it('alerts when a car percentile improved over the previous week', () => {
    const alerts = deriveAlerts({ raceGuide: [], analytics: [analytics([80, 92])], now: NOW });
    expect(alerts).toHaveLength(1);
    expect(alerts[0].kind).toBe('percentile');
    expect(alerts[0].message).toBe('Improved to TOP 8% in BMW M4 GT3'); // topSharePercent = 8
  });

  it('does not alert when percentile did not improve or has too little history', () => {
    const alerts = deriveAlerts({
      raceGuide: [],
      analytics: [analytics([92, 80]), analytics([85], { carId: 2 })], // declined, single-week
      now: NOW,
    });
    expect(alerts).toEqual([]);
  });

  it('combines race and percentile alerts', () => {
    const alerts = deriveAlerts({
      raceGuide: [race()],
      analytics: [analytics([80, 92])],
      now: NOW,
    });
    expect(alerts.map(a => a.kind)).toEqual(['race', 'percentile']);
  });
});
