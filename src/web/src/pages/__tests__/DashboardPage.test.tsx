import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { vi, describe, it, expect, beforeEach } from 'vitest';
import DashboardPage from '../DashboardPage';
import { api } from '../../services/api';
import type { Series, PersonalLap, DriverProfile, CarAnalytics } from '../../services/api';

vi.mock('../../context/AuthContext', () => ({
  useAuth: () => ({ user: { displayName: 'Jerry', token: 't', userId: 'u1', email: 'j@j.com' } }),
}));

vi.mock('../../services/api', () => ({
  api: {
    getSeries: vi.fn(),
    getMyLaps: vi.fn(),
    getProfileStats: vi.fn(),
    getMyAnalytics: vi.fn(),
  },
}));

let mockLiveFlag = true;
let mockDemoFlag = false;
vi.mock('../../context/FeatureFlagContext', () => ({
  useFeatureFlag: (key: string) => (key === 'iracing-demo' ? mockDemoFlag : mockLiveFlag),
}));

function renderPage() {
  return render(
    <MemoryRouter>
      <DashboardPage />
    </MemoryRouter>
  );
}

const baseSeries: Series = {
  id: 1,
  name: 'GT3 Cup',
  seasonId: 10,
  currentWeekNumber: 5,
  category: null,
  trackName: null,
  trackConfigName: null,
  carCount: 0,
  driverCount: 0,
};

const baseLap: PersonalLap = {
  carId: 1,
  carName: 'Porsche 911',
  trackName: 'Spa',
  configName: 'Full Circuit',
  bestLapSeconds: 130.5,
  lapCount: 20,
  lastRecordedAt: '2024-01-01T00:00:00Z',
};

const emptyProfile: DriverProfile = {
  customerId: 1,
  displayName: 'Jerry',
  country: null,
  countryCode: null,
  memberSince: null,
  licenses: [],
  career: [],
  thisYear: { officialSessions: 0, officialWins: 0, leagueSessions: 0, leagueWins: 0 },
  favoriteCar: null,
  favoriteTrack: null,
};

const fullProfile: DriverProfile = {
  ...emptyProfile,
  licenses: [
    {
      categoryId: 1,
      categoryName: 'Oval',
      groupName: 'Class B',
      licenseLevel: 13,
      safetyRating: 2.1,
      iRating: 1800,
      color: 'feec04',
    },
    {
      categoryId: 2,
      categoryName: 'Road',
      groupName: 'Class A',
      licenseLevel: 20,
      safetyRating: 3.45,
      iRating: 2500,
      color: '0153db',
    },
  ],
  career: [
    {
      categoryId: 1,
      categoryName: 'Oval',
      starts: 10,
      wins: 1,
      top5: 3,
      poles: 0,
      avgStartPosition: 8,
      avgFinishPosition: 9.0,
      laps: 100,
      lapsLed: 5,
      winPercentage: 10,
      top5Percentage: 30,
    },
    {
      categoryId: 2,
      categoryName: 'Road',
      starts: 50,
      wins: 5,
      top5: 20,
      poles: 2,
      avgStartPosition: 5,
      avgFinishPosition: 6.7,
      laps: 500,
      lapsLed: 30,
      winPercentage: 10,
      top5Percentage: 40,
    },
  ],
};

const carAnalytics: CarAnalytics = {
  carId: 1,
  carName: 'Porsche 911',
  seriesId: 10,
  seriesName: 'GT3 Cup',
  latestPercentileRank: 90,
  bestPercentileRank: 96,
  personalBestLapSeconds: 130.5,
  medianLapSeconds: 131,
  totalWeeks: 4,
  percentileHistory: [],
};

describe('DashboardPage', () => {
  beforeEach(() => {
    mockLiveFlag = true;
    mockDemoFlag = false;
    vi.clearAllMocks();
    vi.mocked(api.getSeries).mockResolvedValue([]);
    vi.mocked(api.getMyLaps).mockResolvedValue([]);
    vi.mocked(api.getProfileStats).mockResolvedValue(emptyProfile);
    vi.mocked(api.getMyAnalytics).mockResolvedValue([]);
  });

  it('renders the Race Center heading', async () => {
    renderPage();
    expect(screen.getByText('Race Center')).toBeInTheDocument();
  });

  it('shows dash placeholders while loading', () => {
    vi.mocked(api.getSeries).mockReturnValue(new Promise(() => {}));
    vi.mocked(api.getMyLaps).mockReturnValue(new Promise(() => {}));
    vi.mocked(api.getProfileStats).mockReturnValue(new Promise(() => {}));
    vi.mocked(api.getMyAnalytics).mockReturnValue(new Promise(() => {}));
    renderPage();
    // 2 lap/series tiles + 4 stat tiles (best percentile, iRating, SR, avg finish)
    expect(screen.getAllByText('—')).toHaveLength(6);
  });

  it('shows iRating, safety rating, and avg finish for the highest-iRating category', async () => {
    vi.mocked(api.getProfileStats).mockResolvedValue(fullProfile);
    renderPage();
    await waitFor(() => expect(screen.getByText('2500')).toBeInTheDocument());
    expect(screen.getByText('3.45')).toBeInTheDocument();
    expect(screen.getByText('6.7')).toBeInTheDocument();
  });

  it('falls back to a dash for avg finish when career has no matching category', async () => {
    vi.mocked(api.getProfileStats).mockResolvedValue({ ...fullProfile, career: [] });
    vi.mocked(api.getMyAnalytics).mockResolvedValue([carAnalytics]);
    renderPage();
    await waitFor(() => expect(screen.getByText('2500')).toBeInTheDocument());
    expect(screen.getAllByText('—')).toHaveLength(1);
  });

  it('shows dashes for driver stats when the profile is unavailable', async () => {
    vi.mocked(api.getProfileStats).mockRejectedValue(new Error('not linked'));
    vi.mocked(api.getMyAnalytics).mockResolvedValue([carAnalytics]);
    renderPage();
    await waitFor(() => expect(screen.getAllByText('—')).toHaveLength(3));
  });

  it('shows the best (highest) percentile across cars as a TOP X% label', async () => {
    vi.mocked(api.getMyAnalytics).mockResolvedValue([
      { ...carAnalytics, bestPercentileRank: 80 },
      { ...carAnalytics, carId: 2, bestPercentileRank: 96 },
      { ...carAnalytics, carId: 3, bestPercentileRank: 50 },
    ]);
    renderPage();
    await waitFor(() => expect(screen.getByText('TOP 4%')).toBeInTheDocument());
  });

  it('shows a dash for best percentile when there is no analytics data', async () => {
    vi.mocked(api.getProfileStats).mockResolvedValue(fullProfile);
    vi.mocked(api.getMyAnalytics).mockResolvedValue([]);
    renderPage();
    await waitFor(() => expect(screen.getByText('2500')).toBeInTheDocument());
    expect(screen.getAllByText('—')).toHaveLength(1);
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
      expect(screen.getByText('No active series available.')).toBeInTheDocument()
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
      expect(screen.getByRole('link', { name: /view week/i })).toBeInTheDocument()
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
    const slower: PersonalLap = {
      ...baseLap,
      carId: 2,
      carName: 'Porsche 911',
      bestLapSeconds: 130.5,
    };
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
      expect(screen.getByText('No active series available.')).toBeInTheDocument()
    );
  });

  it('hides iRacing widgets and skips their fetches when iracing-live is off', async () => {
    mockLiveFlag = false;
    vi.mocked(api.getMyLaps).mockResolvedValue([baseLap]);
    renderPage();
    // Local content stays
    await waitFor(() => expect(screen.getByText('Laps recorded')).toBeInTheDocument());
    expect(screen.getByText('Cars tracked')).toBeInTheDocument();
    expect(screen.getByText('Personal bests')).toBeInTheDocument();
    // iRacing widgets are gone
    expect(screen.queryByText('This week')).not.toBeInTheDocument();
    expect(screen.queryByText('Best percentile')).not.toBeInTheDocument();
    expect(screen.queryByText('iRating')).not.toBeInTheDocument();
    // iRacing fetches never fire
    expect(api.getSeries).not.toHaveBeenCalled();
    expect(api.getProfileStats).not.toHaveBeenCalled();
    expect(api.getMyAnalytics).not.toHaveBeenCalled();
  });

  it('shows iRacing widgets when iracing-demo is on and iracing-live is off', async () => {
    mockLiveFlag = false;
    mockDemoFlag = true;
    vi.mocked(api.getSeries).mockResolvedValue([baseSeries]);
    vi.mocked(api.getMyAnalytics).mockResolvedValue([carAnalytics]);
    vi.mocked(api.getProfileStats).mockResolvedValue(emptyProfile);
    renderPage();
    // iRacing widgets are shown (demo data available)
    await waitFor(() => expect(screen.getByText('This week')).toBeInTheDocument());
    expect(screen.getByText('Best percentile')).toBeInTheDocument();
    expect(screen.getByText('iRating')).toBeInTheDocument();
    // iRacing fetches fire
    expect(api.getSeries).toHaveBeenCalled();
    expect(api.getProfileStats).toHaveBeenCalled();
    expect(api.getMyAnalytics).toHaveBeenCalled();
  });
});
