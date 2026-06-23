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
import StrategyPage from './pages/StrategyPage';
import StandingsPage from './pages/StandingsPage';
import LeaderboardsPage from './pages/LeaderboardsPage';
import ComparePage from './pages/ComparePage';
import CarsPage from './pages/CarsPage';
import CarDetailPage from './pages/CarDetailPage';
import TracksPage from './pages/TracksPage';
import TrackDetailPage from './pages/TrackDetailPage';
import LivePage from './pages/LivePage';
import TelemetryPage from './pages/TelemetryPage';
import MyLapsPage from './pages/MyLapsPage';
import LoginPage from './pages/LoginPage';
import ForgotPasswordPage from './pages/ForgotPasswordPage';
import ResetPasswordPage from './pages/ResetPasswordPage';
import VerifyEmailPage from './pages/VerifyEmailPage';
import ProfilePage from './pages/ProfilePage';
import SettingsPage from './pages/SettingsPage';
import AdminPage from './pages/AdminPage';
import TermsOfServicePage from './pages/TermsOfServicePage';
import PrivacyPolicyPage from './pages/PrivacyPolicyPage';
import SupportPage from './pages/SupportPage';

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
