import { NavLink } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import { GUEST_NAV, AUTH_NAV } from './navItems';

export default function Sidebar() {
  const { user } = useAuth();
  const navItems = user ? AUTH_NAV : GUEST_NAV;
  return (
    <aside className="w-64 bg-surface-container-lowest border-r border-white/10 h-screen sticky top-0 flex flex-col z-50 hidden lg:flex">
      <div className="px-5 border-b border-white/10 flex items-center h-16 gap-[11px] overflow-hidden">
        <div
          className="w-7 h-7 shrink-0 rounded-[7px] bg-primary-container grid place-items-center text-on-primary-fixed font-extrabold text-[17px]"
          style={{ boxShadow: '0 0 22px -4px var(--color-primary-container)' }}
        >
          A
        </div>
        <span className="font-bold tracking-[-0.02em] text-base whitespace-nowrap text-on-surface">
          APEX<b className="text-primary-container font-bold">//</b>RACERS
        </span>
      </div>
      <nav className="flex-1 overflow-y-auto py-6 px-4 flex flex-col gap-2">
        {navItems.map(({ to, label, icon, exact }) => (
          <NavLink
            key={to}
            to={to}
            end={exact}
            className={({ isActive }) =>
              `flex items-center gap-3 px-4 py-3 rounded-lg font-body-sm font-medium transition-colors ${
                isActive
                  ? 'bg-primary-container/10 text-primary-fixed-dim'
                  : 'text-on-surface-variant hover:text-on-surface hover:bg-white/5'
              }`
            }
          >
            <span className="material-symbols-outlined text-[20px]" aria-hidden="true">{icon}</span>
            {label}
          </NavLink>
        ))}
        {user?.role === 'Admin' && (
          <NavLink
            to="/admin"
            className={({ isActive }) =>
              `flex items-center gap-3 px-4 py-3 rounded-lg font-body-sm font-medium transition-colors ${
                isActive
                  ? 'bg-primary-container/10 text-primary-fixed-dim'
                  : 'text-on-surface-variant hover:text-on-surface hover:bg-white/5'
              }`
            }
          >
            <span className="material-symbols-outlined text-[20px]" aria-hidden="true">admin_panel_settings</span>
            Admin Panel
          </NavLink>
        )}
      </nav>
    </aside>
  );
}
