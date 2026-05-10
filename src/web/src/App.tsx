import { BrowserRouter, Routes, Route, NavLink, Outlet } from 'react-router-dom';
import HomePage from './pages/HomePage';
import SeriesPage from './pages/SeriesPage';
import WeekDetailPage from './pages/WeekDetailPage';
import RecommendationsPage from './pages/RecommendationsPage';
import TelemetryPage from './pages/TelemetryPage';
import MyLapsPage from './pages/MyLapsPage';
import LoginPage from './pages/LoginPage';

function Nav() {
  return (
    <nav>
      <NavLink to="/">Home</NavLink>
      <NavLink to="/series">Series</NavLink>
      <NavLink to="/recommendations">Recommendations</NavLink>
      <NavLink to="/my-laps">My Laps</NavLink>
      <NavLink to="/telemetry">Upload Telemetry</NavLink>
    </nav>
  );
}

function AppShell() {
  return (
    <>
      <Nav />
      <Outlet />
    </>
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
        </Route>
      </Routes>
    </BrowserRouter>
  );
}
