import { describe, it, expect } from 'vitest';
import { GUEST_NAV, AUTH_NAV, visibleNav } from './navItems';

describe('visibleNav', () => {
  it('returns all items unchanged when iracing-live is on', () => {
    expect(visibleNav(AUTH_NAV, true)).toEqual(AUTH_NAV);
    expect(visibleNav(GUEST_NAV, true)).toEqual(GUEST_NAV);
  });

  it('drops gated items from the auth nav when off, keeping the always-on tools', () => {
    const result = visibleNav(AUTH_NAV, false);
    const paths = result.map(i => i.to);
    expect(paths).toEqual([
      '/dashboard',
      '/my-laps',
      '/telemetry',
      '/settings',
      '/profile',
      '/support',
    ]);
  });

  it('drops /series from the guest nav when off, leaving only Home', () => {
    const result = visibleNav(GUEST_NAV, false);
    expect(result.map(i => i.to)).toEqual(['/']);
  });
});
