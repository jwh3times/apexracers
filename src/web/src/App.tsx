import { BrowserRouter, Routes, Route, Outlet, Navigate } from 'react-router-dom';
import { useAuth } from './context/AuthContext';
import { AuthProvider } from './context/AuthProvider';
import { FeatureFlagProvider } from './context/FeatureFlagProvider';
import { ThemeProvider } from './context/ThemeProvider';
import Sidebar from './components/Sidebar';
import TopNav from './components/TopNav';
import Footer from './components/Footer';
import DashboardPage from './pages/DashboardPage';
import HomePage from './pages/HomePage';
import SeriesPage from './pages/SeriesPage';
import WeekDetailPage from './pages/WeekDetailPage';
import PercentileCarPage from './pages/PercentileCarPage';
import AnalyticsPage from './pages/AnalyticsPage';
import ProgressionPage from './pages/ProgressionPage';
import RecommendationsPage from './pages/RecommendationsPage';
import RacesPage from './pages/RacesPage';
import RaceDetailPage from './pages/RaceDetailPage';
import SchedulePage from './pages/SchedulePage';
import LeaderboardsPage from './pages/LeaderboardsPage';
import TelemetryPage from './pages/TelemetryPage';
import MyLapsPage from './pages/MyLapsPage';
import LoginPage from './pages/LoginPage';
import ProfilePage from './pages/ProfilePage';
import SettingsPage from './pages/SettingsPage';
import AdminPage from './pages/AdminPage';
import TermsOfServicePage from './pages/TermsOfServicePage';
import PrivacyPolicyPage from './pages/PrivacyPolicyPage';

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

// Gate for routes that require any authenticated user. Renders nothing while the
// session is still being restored (silent refresh on startup) so a logged-in user
// is not bounced to /login on a hard refresh.
export function RequireAuth() {
  const { user, loading } = useAuth();
  if (loading) return null;
  if (!user) return <Navigate to="/login" replace />;
  return <Outlet />;
}

export function AdminGuard() {
  const { user, loading } = useAuth();
  if (loading) return null;
  if (!user) return <Navigate to="/login" replace />;
  if (user.role !== 'Admin') return <Navigate to="/dashboard" replace />;
  return <Outlet />;
}

function AppRoutes() {
  const { user } = useAuth();
  return (
    <Routes>
      {/* Public routes with their own layout (no AppShell) */}
      <Route path="/" element={<HomePage />} />
      <Route path="/login" element={<LoginPage />} />
      <Route path="/terms" element={<TermsOfServicePage />} />
      <Route path="/privacy" element={<PrivacyPolicyPage />} />

      <Route element={<AppShell />}>
        {/* Series browsing is reachable by guests (see GUEST_NAV) */}
        <Route path="/series" element={<SeriesPage />} />
        <Route path="/series/:seriesId/schedule" element={<SchedulePage />} />
        <Route path="/series/:seriesId/weeks/:weekNumber" element={<WeekDetailPage />} />
        <Route
          path="/series/:seriesId/weeks/:weekNumber/cars/:carId/percentile"
          element={<PercentileCarPage />}
        />
        {/* Public — official race results (shareable); reached from /races */}
        <Route path="/races/:subsessionId" element={<RaceDetailPage />} />

        {/* Everything below requires an authenticated user */}
        <Route element={<RequireAuth />}>
          <Route path="/dashboard" element={<DashboardPage />} />
          <Route path="/analytics" element={<AnalyticsPage />} />
          <Route path="/progression" element={<ProgressionPage />} />
          <Route path="/recommendations" element={<RecommendationsPage />} />
          <Route path="/races" element={<RacesPage />} />
          <Route path="/leaderboards" element={<LeaderboardsPage />} />
          <Route path="/my-laps" element={<MyLapsPage />} />
          <Route path="/telemetry" element={<TelemetryPage />} />
          <Route path="/profile" element={<ProfilePage />} />
          <Route path="/settings" element={<SettingsPage key={user?.userId} />} />
          <Route element={<AdminGuard />}>
            <Route path="/admin" element={<AdminPage />} />
          </Route>
        </Route>
      </Route>
    </Routes>
  );
}

export default function App() {
  return (
    <ThemeProvider>
      <AuthProvider>
        <FeatureFlagProvider>
          <BrowserRouter>
            <AppRoutes />
          </BrowserRouter>
        </FeatureFlagProvider>
      </AuthProvider>
    </ThemeProvider>
  );
}
