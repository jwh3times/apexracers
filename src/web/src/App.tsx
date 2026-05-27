import { BrowserRouter, Routes, Route, Outlet } from 'react-router-dom';
import { AuthProvider, useAuth } from './context/AuthContext';
import Sidebar from './components/Sidebar';
import TopNav from './components/TopNav';
import Footer from './components/Footer';
import DashboardPage from './pages/DashboardPage';
import HomePage from './pages/HomePage';
import SeriesPage from './pages/SeriesPage';
import WeekDetailPage from './pages/WeekDetailPage';
import PercentileCarPage from './pages/PercentileCarPage';
import AnalyticsPage from './pages/AnalyticsPage';
import RecommendationsPage from './pages/RecommendationsPage';
import TelemetryPage from './pages/TelemetryPage';
import MyLapsPage from './pages/MyLapsPage';
import LoginPage from './pages/LoginPage';
import ProfilePage from './pages/ProfilePage';
import SettingsPage from './pages/SettingsPage';
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

function AppRoutes() {
  const { user } = useAuth();
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route element={<AppShell />}>
        <Route path="/" element={<HomePage />} />
        <Route path="/dashboard" element={<DashboardPage />} />
        <Route path="/series" element={<SeriesPage />} />
        <Route path="/series/:seriesId/weeks/:weekNumber" element={<WeekDetailPage />} />
        <Route path="/series/:seriesId/weeks/:weekNumber/cars/:carId/percentile" element={<PercentileCarPage />} />
        <Route path="/analytics" element={<AnalyticsPage />} />
        <Route path="/recommendations" element={<RecommendationsPage />} />
        <Route path="/my-laps" element={<MyLapsPage />} />
        <Route path="/telemetry" element={<TelemetryPage />} />
        <Route path="/profile" element={<ProfilePage />} />
        <Route path="/settings" element={<SettingsPage key={user?.userId} />} />
        <Route path="/terms" element={<TermsOfServicePage />} />
        <Route path="/privacy" element={<PrivacyPolicyPage />} />
      </Route>
    </Routes>
  );
}

export default function App() {
  return (
    <AuthProvider>
      <BrowserRouter>
        <AppRoutes />
      </BrowserRouter>
    </AuthProvider>
  );
}
