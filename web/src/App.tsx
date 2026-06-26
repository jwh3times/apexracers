import { BrowserRouter, Routes, Route, Outlet, Navigate } from 'react-router-dom';
import { useAuth } from './context/AuthContext';
import { useFeatureFlag } from './context/FeatureFlagContext';
import { AuthProvider } from './context/AuthProvider';
import { FeatureFlagProvider } from './context/FeatureFlagProvider';
import { ThemeProvider } from './context/ThemeProvider';
import ComingSoonPage from './pages/ComingSoonPage';
import Sidebar from './components/Sidebar';
import TopNav from './components/TopNav';
import DemoBanner from './components/DemoBanner';
import Footer from './components/Footer';
import DashboardPage from './features/driver/DashboardPage';
import HomePage from './pages/HomePage';
import SeriesPage from './features/series/SeriesPage';
import WeekDetailPage from './features/series/WeekDetailPage';
import PercentileCarPage from './features/series/PercentileCarPage';
import AnalyticsPage from './features/driver/AnalyticsPage';
import ProgressionPage from './features/driver/ProgressionPage';
import RecommendationsPage from './features/driver/RecommendationsPage';
import RacesPage from './features/racing/RacesPage';
import RaceDetailPage from './features/racing/RaceDetailPage';
import SchedulePage from './features/series/SchedulePage';
import StrategyPage from './features/series/StrategyPage';
import StandingsPage from './features/series/StandingsPage';
import LeaderboardsPage from './features/rivals/LeaderboardsPage';
import ComparePage from './features/rivals/ComparePage';
import CarsPage from './features/catalog/CarsPage';
import CarDetailPage from './features/catalog/CarDetailPage';
import TracksPage from './features/catalog/TracksPage';
import TrackDetailPage from './features/catalog/TrackDetailPage';
import LivePage from './features/racing/LivePage';
import TelemetryPage from './features/telemetry/TelemetryPage';
import MyLapsPage from './features/telemetry/MyLapsPage';
import LoginPage from './features/auth/LoginPage';
import ForgotPasswordPage from './features/auth/ForgotPasswordPage';
import ResetPasswordPage from './features/auth/ResetPasswordPage';
import VerifyEmailPage from './features/auth/VerifyEmailPage';
import ProfilePage from './features/profile/ProfilePage';
import SettingsPage from './features/profile/SettingsPage';
import AdminPage from './features/admin/AdminPage';
import TermsOfServicePage from './pages/TermsOfServicePage';
import PrivacyPolicyPage from './pages/PrivacyPolicyPage';
import SupportPage from './features/profile/SupportPage';

function AppShell() {
  return (
    <div className="flex min-h-screen">
      <Sidebar />
      <div className="flex-1 flex flex-col min-w-0">
        <TopNav />
        <DemoBanner />
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

// Gate for routes behind the iRacing surface. Auth-independent: renders the
// ComingSoon page for everyone (guest or signed-in) when neither flag is on, so deep
// links degrade gracefully instead of 404/redirect. Shows the surface when real data
// (iracing-live) OR synthetic demo data (iracing-demo) is available.
export function RequireFlag() {
  // Both hooks must be called unconditionally (rules-of-hooks) — `||` on the calls
  // would short-circuit the second, so evaluate each first, then OR the results.
  const live = useFeatureFlag('iracing-live');
  const demo = useFeatureFlag('iracing-demo');
  return live || demo ? <Outlet /> : <ComingSoonPage />;
}

function AppRoutes() {
  const { user } = useAuth();
  return (
    <Routes>
      {/* Public routes with their own layout (no AppShell) */}
      <Route path="/" element={<HomePage />} />
      <Route path="/login" element={<LoginPage />} />
      <Route path="/forgot-password" element={<ForgotPasswordPage />} />
      <Route path="/reset-password" element={<ResetPasswordPage />} />
      <Route path="/verify-email" element={<VerifyEmailPage />} />
      <Route path="/terms" element={<TermsOfServicePage />} />
      <Route path="/privacy" element={<PrivacyPolicyPage />} />

      <Route element={<AppShell />}>
        {/* Public but iRacing-data-dependent → gated behind iracing-live */}
        <Route element={<RequireFlag />}>
          <Route path="/series" element={<SeriesPage />} />
          <Route path="/series/:seriesId/schedule" element={<SchedulePage />} />
          <Route path="/series/:seriesId/standings" element={<StandingsPage />} />
          <Route path="/series/:seriesId/weeks/:weekNumber" element={<WeekDetailPage />} />
          <Route path="/series/:seriesId/weeks/:weekNumber/strategy" element={<StrategyPage />} />
          <Route
            path="/series/:seriesId/weeks/:weekNumber/cars/:carId/percentile"
            element={<PercentileCarPage />}
          />
          <Route path="/races/:subsessionId" element={<RaceDetailPage />} />
          <Route path="/cars" element={<CarsPage />} />
          <Route path="/cars/:carId" element={<CarDetailPage />} />
          <Route path="/tracks" element={<TracksPage />} />
          <Route path="/tracks/:trackId" element={<TrackDetailPage />} />
        </Route>

        {/* Everything below requires an authenticated user */}
        <Route element={<RequireAuth />}>
          <Route path="/dashboard" element={<DashboardPage />} />
          <Route path="/my-laps" element={<MyLapsPage />} />
          <Route path="/telemetry" element={<TelemetryPage />} />
          <Route path="/profile" element={<ProfilePage />} />
          <Route path="/support" element={<SupportPage />} />
          <Route path="/settings" element={<SettingsPage key={user?.userId} />} />

          {/* Authed + iRacing-data-dependent → gated behind iracing-live */}
          <Route element={<RequireFlag />}>
            <Route path="/analytics" element={<AnalyticsPage />} />
            <Route path="/progression" element={<ProgressionPage />} />
            <Route path="/recommendations" element={<RecommendationsPage />} />
            <Route path="/races" element={<RacesPage />} />
            <Route path="/leaderboards" element={<LeaderboardsPage />} />
            <Route path="/compare" element={<ComparePage />} />
            <Route path="/live" element={<LivePage />} />
          </Route>

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
