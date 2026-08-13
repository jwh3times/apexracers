import { describe, it, expect } from 'vitest';
import { breadcrumbs } from './breadcrumbs';

describe('breadcrumbs', () => {
  it('returns nothing for the root path', () => {
    expect(breadcrumbs('/')).toEqual([]);
  });

  it('maps a single top-level route to one (current-page) crumb', () => {
    expect(breadcrumbs('/analytics')).toEqual([{ label: 'Analytics' }]);
    expect(breadcrumbs('/dashboard')).toEqual([{ label: 'Race Center' }]);
    expect(breadcrumbs('/support')).toEqual([{ label: 'Support' }]);
  });

  it('falls back to the raw segment for an unknown route', () => {
    expect(breadcrumbs('/whatever')).toEqual([{ label: 'whatever' }]);
  });

  it('builds Series → Week N for a week detail route', () => {
    expect(breadcrumbs('/series/444/weeks/5')).toEqual([
      { label: 'Series', to: '/series' },
      { label: 'Week 6' },
    ]);
  });

  it('labels the opening race week as Week 1, not Week 0', () => {
    expect(breadcrumbs('/series/444/weeks/0')).toEqual([
      { label: 'Series', to: '/series' },
      { label: 'Week 1' },
    ]);
  });

  it('builds Series → Week N → Strategy with a navigable week crumb', () => {
    // The crumb reads one-based; the link it points at keeps the zero-based route index.
    expect(breadcrumbs('/series/444/weeks/5/strategy')).toEqual([
      { label: 'Series', to: '/series' },
      { label: 'Week 6', to: '/series/444/weeks/5' },
      { label: 'Strategy' },
    ]);
  });

  it('builds Series → Week N → Percentile for a car percentile route', () => {
    expect(breadcrumbs('/series/444/weeks/5/cars/132/percentile')).toEqual([
      { label: 'Series', to: '/series' },
      { label: 'Week 6', to: '/series/444/weeks/5' },
      { label: 'Percentile' },
    ]);
  });

  it('handles series schedule and standings sub-routes', () => {
    expect(breadcrumbs('/series/444/schedule')).toEqual([
      { label: 'Series', to: '/series' },
      { label: 'Schedule' },
    ]);
    expect(breadcrumbs('/series/444/standings')).toEqual([
      { label: 'Series', to: '/series' },
      { label: 'Standings' },
    ]);
  });

  it('builds list → detail crumbs for cars/tracks/races', () => {
    expect(breadcrumbs('/cars/132')).toEqual([{ label: 'Cars', to: '/cars' }, { label: 'Car' }]);
    expect(breadcrumbs('/tracks/18')).toEqual([
      { label: 'Tracks', to: '/tracks' },
      { label: 'Track' },
    ]);
    expect(breadcrumbs('/races/12345')).toEqual([
      { label: 'Race History', to: '/races' },
      { label: 'Race' },
    ]);
  });
});
