export interface Crumb {
  label: string;
  /** Present for ancestor crumbs (navigable); omitted for the current page. */
  to?: string;
}

const TOP_LABELS: Record<string, string> = {
  dashboard: 'Race Center',
  series: 'Series',
  analytics: 'Analytics',
  progression: 'Progression',
  recommendations: 'Recommendations',
  races: 'Race History',
  leaderboards: 'Leaderboards',
  compare: 'Compare',
  live: 'Race Now',
  cars: 'Cars',
  tracks: 'Tracks',
  'my-laps': 'My Laps',
  telemetry: 'Telemetry',
  profile: 'My Profile',
  settings: 'Settings',
  support: 'Support',
  admin: 'Admin Panel',
};

/**
 * Pure route → breadcrumb derivation for the top nav. Labels come from the URL only (no data
 * fetch), so dynamic segments render as e.g. "Week 5" rather than the resolved series name.
 * Ancestor crumbs carry a `to`; the final (current-page) crumb does not. Unit-tested directly.
 */
export function breadcrumbs(pathname: string): Crumb[] {
  const segs = pathname.split('/').filter(Boolean);
  if (segs.length === 0) return [];
  const head = segs[0];

  // /series/:id/(schedule|standings|weeks/:n[/strategy|/cars/.../percentile])
  if (head === 'series' && segs.length > 1) {
    const crumbs: Crumb[] = [{ label: 'Series', to: '/series' }];
    const sub = segs[2];
    if (sub === 'schedule') {
      crumbs.push({ label: 'Schedule' });
    } else if (sub === 'standings') {
      crumbs.push({ label: 'Standings' });
    } else if (sub === 'weeks') {
      const weekNum = segs[3];
      const weekPath = `/series/${segs[1]}/weeks/${weekNum}`;
      const tail = segs[4];
      if (tail === 'strategy') {
        crumbs.push({ label: `Week ${weekNum}`, to: weekPath }, { label: 'Strategy' });
      } else if (tail === 'cars') {
        crumbs.push({ label: `Week ${weekNum}`, to: weekPath }, { label: 'Percentile' });
      } else {
        crumbs.push({ label: `Week ${weekNum}` });
      }
    }
    return crumbs;
  }

  // List → detail routes (/races/:id, /cars/:id, /tracks/:id).
  if ((head === 'races' || head === 'cars' || head === 'tracks') && segs.length > 1) {
    const leaf = head === 'races' ? 'Race' : head === 'cars' ? 'Car' : 'Track';
    return [{ label: TOP_LABELS[head], to: `/${head}` }, { label: leaf }];
  }

  // Single top-level route.
  return [{ label: TOP_LABELS[head] ?? head }];
}
