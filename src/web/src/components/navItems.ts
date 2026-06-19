type NavItem = {
  readonly to: string;
  readonly label: string;
  readonly icon: string;
  readonly exact?: boolean;
};

export const GUEST_NAV: readonly NavItem[] = [
  { to: '/', label: 'Home', icon: 'home', exact: true },
  { to: '/series', label: 'Browse Series', icon: 'sports_motorsports' },
];

export const AUTH_NAV: readonly NavItem[] = [
  { to: '/dashboard', label: 'Dashboard', icon: 'dashboard', exact: true },
  { to: '/series', label: 'Browse Series', icon: 'sports_motorsports' },
  { to: '/analytics', label: 'Analytics', icon: 'analytics' },
  { to: '/progression', label: 'Progression', icon: 'trending_up' },
  { to: '/recommendations', label: 'Recommendations', icon: 'recommend' },
  { to: '/live', label: 'Race Now', icon: 'live_tv' },
  { to: '/races', label: 'Race History', icon: 'history' },
  { to: '/leaderboards', label: 'Leaderboards', icon: 'leaderboard' },
  { to: '/compare', label: 'Compare', icon: 'group' },
  { to: '/cars', label: 'Cars', icon: 'directions_car' },
  { to: '/tracks', label: 'Tracks', icon: 'route' },
  { to: '/my-laps', label: 'My Laps', icon: 'timer' },
  { to: '/telemetry', label: 'Telemetry', icon: 'sensors' },
  { to: '/settings', label: 'Settings', icon: 'settings' },
  { to: '/profile', label: 'Profile', icon: 'account_circle' },
];
