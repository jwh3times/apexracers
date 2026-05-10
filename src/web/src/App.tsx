import { BrowserRouter, Routes, Route, NavLink, Link, Outlet } from 'react-router-dom';
import HomePage from './pages/HomePage';
import SeriesPage from './pages/SeriesPage';
import WeekDetailPage from './pages/WeekDetailPage';
import RecommendationsPage from './pages/RecommendationsPage';
import TelemetryPage from './pages/TelemetryPage';
import MyLapsPage from './pages/MyLapsPage';
import LoginPage from './pages/LoginPage';
import ProfilePage from './pages/ProfilePage';

const navItems = [
  { to: '/', label: 'Home', icon: 'home', exact: true },
  { to: '/series', label: 'Browse Series', icon: 'sports_motorsports' },
  { to: '/recommendations', label: 'Recommendations', icon: 'recommend' },
  { to: '/my-laps', label: 'My Laps', icon: 'timer' },
];

function Sidebar() {
  return (
    <aside className="w-64 bg-surface-container-lowest border-r border-white/10 h-screen sticky top-0 flex flex-col z-50 hidden lg:flex">
      <div className="p-6 border-b border-white/10 flex items-center h-16">
        <span className="font-display-lg text-headline-md font-extrabold tracking-tighter text-primary-fixed-dim">
          ApexRacers
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
      </nav>
    </aside>
  );
}

function TopNav() {
  return (
    <nav className="bg-surface/80 backdrop-blur-xl text-primary-fixed-dim sticky top-0 w-full z-40 border-b border-white/10 shadow-[0_0_20px_rgba(0,228,121,0.15)] flex justify-between items-center px-6 h-16">
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
                  : 'text-on-surface-variant hover:text-on-surface hover:bg-white/5'
              }`
            }
          >
            {label}
          </NavLink>
        ))}
      </div>
      <div className="flex items-center gap-4 ml-auto">
        <Link
          to="/profile"
          className="relative flex items-center justify-center h-10 w-10 rounded-full border-2 border-primary-container p-0.5 hover:shadow-[0_0_15px_rgba(0,255,136,0.3)] transition-all active:scale-95"
          aria-label="User profile"
        >
          <div className="h-full w-full rounded-full bg-surface-container flex items-center justify-center overflow-hidden">
            <span className="material-symbols-outlined text-primary-container" aria-hidden="true">person</span>
          </div>
          <div className="absolute bottom-0 right-0 h-3 w-3 bg-primary-container border-2 border-surface rounded-full"></div>
        </Link>
      </div>
    </nav>
  );
}

function Footer() {
  return (
    <footer className="bg-surface-dim text-on-surface-variant font-body-sm text-body-sm w-full py-6 border-t border-white/10 flex flex-col md:flex-row justify-between items-center px-6 mt-auto">
      <div className="font-body-lg text-on-surface mb-4 md:mb-0">ApexRacers</div>
      <div className="flex gap-6 mb-4 md:mb-0">
        <a className="hover:text-primary-fixed-dim transition-colors" href="#">Terms of Service</a>
        <a className="hover:text-primary-fixed-dim transition-colors" href="#">Privacy Policy</a>
        <a className="hover:text-primary-fixed-dim transition-colors" href="#">API Status</a>
      </div>
      <div>© 2024 ApexRacers. Not affiliated with iRacing.com</div>
    </footer>
  );
}

function AppShell() {
  return (
    <div className="flex min-h-screen">
      <Sidebar />
      <div className="flex-1 flex flex-col min-w-0">
        <TopNav />
        <Outlet />
        <Footer />
      </div>
    </div>
  );
}

export default function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route path="/login" element={<LoginPage />} />
        <Route element={<AppShell />}>
          <Route path="/" element={<HomePage />} />
          <Route path="/series" element={<SeriesPage />} />
          <Route path="/series/:seriesId/weeks/:weekId" element={<WeekDetailPage />} />
          <Route path="/recommendations" element={<RecommendationsPage />} />
          <Route path="/my-laps" element={<MyLapsPage />} />
          <Route path="/telemetry" element={<TelemetryPage />} />
          <Route path="/profile" element={<ProfilePage />} />
        </Route>
      </Routes>
    </BrowserRouter>
  );
}
