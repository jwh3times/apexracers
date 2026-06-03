import { useState, useEffect, useRef } from 'react';
import { NavLink, Link, useNavigate } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import { GUEST_NAV, AUTH_NAV } from './navItems';

function ProfileDropdown() {
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);
  const navigate = useNavigate();
  const { logout } = useAuth();

  useEffect(() => {
    function handleClickOutside(e: MouseEvent) {
      if (ref.current && !ref.current.contains(e.target as Node)) {
        setOpen(false);
      }
    }
    if (open) document.addEventListener('mousedown', handleClickOutside);
    return () => document.removeEventListener('mousedown', handleClickOutside);
  }, [open]);

  async function handleLogout() {
    setOpen(false);
    await logout();
    navigate('/login');
  }

  return (
    <div ref={ref} className="relative">
      <button
        onClick={() => setOpen(o => !o)}
        className="relative flex items-center justify-center h-10 w-10 rounded-full border-2 border-primary-container p-0.5 hover:shadow-[0_0_15px_rgba(0,255,136,0.3)] transition-all active:scale-95"
        aria-label="User menu"
        aria-expanded={open}
        aria-haspopup="true"
      >
        <div className="h-full w-full rounded-full bg-surface-container flex items-center justify-center overflow-hidden">
          <span className="material-symbols-outlined text-primary-container" aria-hidden="true">person</span>
        </div>
        <div className="absolute bottom-0 right-0 h-3 w-3 bg-primary-container border-2 border-surface rounded-full"></div>
      </button>

      {open && (
        <div className="absolute right-0 mt-2 w-44 bg-surface-container border border-line-2 rounded-xl shadow-[0_8px_32px_rgba(0,0,0,0.4)] overflow-hidden z-50">
          <Link
            to="/profile"
            onClick={() => setOpen(false)}
            className="flex items-center gap-3 px-4 py-3 text-on-surface-variant hover:text-on-surface hover:bg-surface-container-highest transition-colors font-body-sm"
          >
            <span className="material-symbols-outlined text-[18px]" aria-hidden="true">person</span>
            Profile
          </Link>
          <Link
            to="/settings"
            onClick={() => setOpen(false)}
            className="flex items-center gap-3 px-4 py-3 text-on-surface-variant hover:text-on-surface hover:bg-surface-container-highest transition-colors font-body-sm"
          >
            <span className="material-symbols-outlined text-[18px]" aria-hidden="true">settings</span>
            Settings
          </Link>
          <div className="border-t border-line-2" />
          <button
            onClick={handleLogout}
            className="flex items-center gap-3 w-full px-4 py-3 text-error hover:bg-error/10 transition-colors font-body-sm"
          >
            <span className="material-symbols-outlined text-[18px]" aria-hidden="true">logout</span>
            Logout
          </button>
        </div>
      )}
    </div>
  );
}

export default function TopNav() {
  const { user } = useAuth();
  const navItems = user ? AUTH_NAV : GUEST_NAV;
  return (
    <nav className="bg-surface/80 backdrop-blur-xl text-primary-fixed-dim sticky top-0 w-full z-40 border-b border-line-2 shadow-[0_0_20px_rgba(0,228,121,0.15)] flex justify-between items-center px-6 h-16">
      <div className="flex items-center gap-4 lg:hidden">
        <span className="font-display-lg text-headline-md font-extrabold tracking-tighter text-primary-fixed-dim">
          ApexRacers
        </span>
      </div>
      <div className="hidden md:flex items-center gap-2 lg:hidden">
        {navItems.slice(1).map(({ to, label }) => (
          <NavLink
            key={to}
            to={to}
            className={({ isActive }) =>
              `transition-all duration-200 px-3 py-2 rounded font-body-sm ${
                isActive
                  ? 'text-on-surface'
                  : 'text-on-surface-variant hover:text-on-surface hover:bg-surface-container-highest'
              }`
            }
          >
            {label}
          </NavLink>
        ))}
      </div>
      <div className="flex items-center gap-4 ml-auto">
        <ProfileDropdown />
      </div>
    </nav>
  );
}
