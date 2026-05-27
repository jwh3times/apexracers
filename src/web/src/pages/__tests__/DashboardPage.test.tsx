import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { vi, describe, it, expect, beforeEach } from 'vitest';
import DashboardPage from '../DashboardPage';
import { api } from '../../services/api';
import type { Series, PersonalLap } from '../../services/api';

vi.mock('../../context/AuthContext', () => ({
  useAuth: () => ({ user: { displayName: 'Jerry', token: 't', userId: 'u1', email: 'j@j.com' } }),
}));

vi.mock('../../services/api', () => ({
  api: { getSeries: vi.fn(), getMyLaps: vi.fn() },
}));

function renderPage() {
  return render(<MemoryRouter><DashboardPage /></MemoryRouter>);
}

const baseSeries: Series = { id: 1, name: 'GT3 Cup', seasonId: 10, currentWeekNumber: 5 };

const baseLap: PersonalLap = {
  carId: 1,
  carName: 'Porsche 911',
  trackName: 'Spa',
  configName: 'Full Circuit',
  bestLapSeconds: 130.5,
  lapCount: 20,
  lastRecordedAt: '2024-01-01T00:00:00Z',
};

describe('DashboardPage', () => {
  beforeEach(() => {
    vi.mocked(api.getSeries).mockResolvedValue([]);
    vi.mocked(api.getMyLaps).mockResolvedValue([]);
  });

  it('renders the Race Center heading', async () => {
    renderPage();
    expect(screen.getByText('Race Center')).toBeInTheDocument();
  });

  it('shows dash placeholders while loading', () => {
    vi.mocked(api.getSeries).mockReturnValue(new Promise(() => {}));
    vi.mocked(api.getMyLaps).mockReturnValue(new Promise(() => {}));
    renderPage();
    expect(screen.getAllByText('—')).toHaveLength(2);
  });

  it('shows the series count once loaded', async () => {
    vi.mocked(api.getSeries).mockResolvedValue([baseSeries, { ...baseSeries, id: 2, name: 'F3' }]);
    renderPage();
    await waitFor(() => expect(screen.getByText('2')).toBeInTheDocument());
  });

  it('shows total laps count once loaded', async () => {
    vi.mocked(api.getMyLaps).mockResolvedValue([
      { ...baseLap, lapCount: 15 },
      { ...baseLap, carId: 2, lapCount: 10 },
    ]);
    renderPage();
    await waitFor(() => expect(screen.getByText('25')).toBeInTheDocument());
  });

  it('shows "No active series available" when series list is empty', async () => {
    renderPage();
    await waitFor(() =>
      expect(screen.getByText('No active series available.')).toBeInTheDocument(),
    );
  });

  it('renders series names in the This Week section', async () => {
    vi.mocked(api.getSeries).mockResolvedValue([baseSeries]);
    renderPage();
    await waitFor(() => expect(screen.getAllByText('GT3 Cup').length).toBeGreaterThan(0));
  });

  it('shows "Season upcoming" when currentWeekNumber is null', async () => {
    vi.mocked(api.getSeries).mockResolvedValue([{ ...baseSeries, currentWeekNumber: null }]);
    renderPage();
    await waitFor(() => expect(screen.getAllByText('Season upcoming').length).toBeGreaterThan(0));
  });

  it('shows View Week link when currentWeekNumber is set', async () => {
    vi.mocked(api.getSeries).mockResolvedValue([baseSeries]);
    renderPage();
    await waitFor(() =>
      expect(screen.getByRole('link', { name: /view week/i })).toBeInTheDocument(),
    );
  });

  it('shows "No laps yet" and upload link when laps list is empty', async () => {
    renderPage();
    await waitFor(() => expect(screen.getByText(/no laps yet/i)).toBeInTheDocument());
    expect(screen.getByRole('link', { name: /upload a telemetry file/i })).toBeInTheDocument();
  });

  it('renders the laps table with car and track data', async () => {
    vi.mocked(api.getMyLaps).mockResolvedValue([baseLap]);
    renderPage();
    await waitFor(() => expect(screen.getByText('Porsche 911')).toBeInTheDocument());
    expect(screen.getByText('Spa — Full Circuit')).toBeInTheDocument();
  });

  it('formats lap time as minutes:seconds.milliseconds', async () => {
    vi.mocked(api.getMyLaps).mockResolvedValue([{ ...baseLap, bestLapSeconds: 130.5 }]);
    renderPage();
    await waitFor(() => expect(screen.getAllByText('2:10.500').length).toBeGreaterThan(0));
  });

  it('shows trackName only when configName is empty', async () => {
    vi.mocked(api.getMyLaps).mockResolvedValue([{ ...baseLap, configName: '' }]);
    renderPage();
    await waitFor(() => expect(screen.getAllByText('Spa').length).toBeGreaterThan(0));
  });

  it('shows overall best lap section when laps are present', async () => {
    vi.mocked(api.getMyLaps).mockResolvedValue([baseLap]);
    renderPage();
    await waitFor(() => expect(screen.getByText('Overall Best')).toBeInTheDocument());
  });

  it('picks the lap with the lowest bestLapSeconds as overall best', async () => {
    const faster: PersonalLap = { ...baseLap, carName: 'Ferrari', bestLapSeconds: 120.0 };
    const slower: PersonalLap = { ...baseLap, carId: 2, carName: 'Porsche 911', bestLapSeconds: 130.5 };
    vi.mocked(api.getMyLaps).mockResolvedValue([slower, faster]);
    renderPage();
    await waitFor(() => {
      const bestSection = screen.getByText('Overall Best').closest('div')!.parentElement!;
      expect(bestSection.textContent).toContain('Ferrari');
    });
  });

  it('shows welcome message with user display name', async () => {
    renderPage();
    await waitFor(() => expect(screen.getByText('Jerry')).toBeInTheDocument());
  });

  it('gracefully handles api errors without crashing', async () => {
    vi.mocked(api.getSeries).mockRejectedValue(new Error('network'));
    vi.mocked(api.getMyLaps).mockRejectedValue(new Error('network'));
    renderPage();
    await waitFor(() =>
      expect(screen.getByText('No active series available.')).toBeInTheDocument(),
    );
  });
});
