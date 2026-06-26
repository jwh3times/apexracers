import { useState } from 'react';
import { NavLink } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import { GUEST_NAV, AUTH_NAV, visibleNav } from './navItems';
import { useFeatureFlag } from '../context/FeatureFlagContext';

const STORAGE_KEY = 'ar_sidebar_collapsed';

export default function Sidebar() {
  const { user } = useAuth();
  const live = useFeatureFlag('iracing-live');
  const demo = useFeatureFlag('iracing-demo');
  const navItems = visibleNav(user ? AUTH_NAV : GUEST_NAV, live || demo);
  const [collapsed, setCollapsed] = useState(() => localStorage.getItem(STORAGE_KEY) === 'true');

  const toggle = () =>
    setCollapsed(prev => {
      const next = !prev;
      localStorage.setItem(STORAGE_KEY, String(next));
      return next;
    });

  const linkClass = ({ isActive }: { isActive: boolean }) =>
    `flex items-center ${collapsed ? 'justify-center px-0' : 'gap-3 px-4'} py-3 rounded-lg font-body-sm font-medium transition-colors ${
      isActive
        ? 'bg-primary-container/10 text-primary-fixed-dim'
        : 'text-on-surface-variant hover:text-on-surface hover:bg-surface-container-highest'
    }`;

  return (
    <aside
      className={`${collapsed ? 'w-16' : 'w-64'} bg-surface-container-lowest border-r border-line-2 h-screen sticky top-0 flex flex-col z-50 hidden lg:flex transition-[width] duration-200`}
    >
      <div
        className={`border-b border-line-2 flex items-center h-16 gap-[11px] overflow-hidden ${collapsed ? 'justify-center px-0' : 'px-5'}`}
      >
        <div
          className="w-7 h-7 shrink-0 rounded-[7px] bg-primary-container grid place-items-center text-on-primary-fixed font-extrabold text-[17px]"
          style={{ boxShadow: '0 0 22px -4px var(--color-primary-container)' }}
        >
          A
        </div>
        {!collapsed && (
          <span className="font-bold tracking-[-0.02em] text-base whitespace-nowrap text-on-surface">
            APEX<b className="text-primary-container font-bold">//</b>RACERS
          </span>
        )}
      </div>
      <nav className="flex-1 overflow-y-auto py-6 px-4 flex flex-col gap-2">
        {navItems.map(({ to, label, icon, exact }) => (
          <NavLink
            key={to}
            to={to}
            end={exact}
            title={collapsed ? label : undefined}
            className={linkClass}
          >
            <span className="material-symbols-outlined text-[20px]" aria-hidden="true">
              {icon}
            </span>
            {!collapsed && label}
          </NavLink>
        ))}
        {user?.role === 'Admin' && (
          <NavLink to="/admin" title={collapsed ? 'Admin Panel' : undefined} className={linkClass}>
            <span className="material-symbols-outlined text-[20px]" aria-hidden="true">
              admin_panel_settings
            </span>
            {!collapsed && 'Admin Panel'}
          </NavLink>
        )}
      </nav>
      <button
        type="button"
        onClick={toggle}
        aria-label={collapsed ? 'Expand sidebar' : 'Collapse sidebar'}
        aria-pressed={collapsed}
        className={`border-t border-line-2 h-12 flex items-center ${collapsed ? 'justify-center' : 'gap-3 px-4'} text-on-surface-variant hover:text-on-surface hover:bg-surface-container-highest transition-colors font-body-sm`}
      >
        <span className="material-symbols-outlined text-[20px]" aria-hidden="true">
          {collapsed ? 'chevron_right' : 'chevron_left'}
        </span>
        {!collapsed && 'Collapse'}
      </button>
    </aside>
  );
}
