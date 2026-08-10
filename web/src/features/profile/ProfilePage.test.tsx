import { render, screen, waitFor, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router';
import { vi, describe, it, expect, beforeEach } from 'vitest';
import ProfilePage from './ProfilePage';
import { api, IRacingNotLinkedError, type Award, type DriverProfile } from '../../services/api';
import type { User } from '../../context/AuthContext';

let mockUser: User | null = {
  token: 'tok',
  userId: 'u1',
  displayName: 'Test Driver',
  email: 't@t.com',
  iRacingCustomerId: 100042,
  role: 'Standard',
};

vi.mock('../../context/AuthContext', () => ({
  useAuth: () => ({ user: mockUser }),
}));

let mockLiveFlag = true;
let mockDemoFlag = false;
vi.mock('../../context/FeatureFlagContext', () => ({
  useIracingSurface: () => ({ enabled: mockLiveFlag || mockDemoFlag, ready: true }),
}));

vi.mock('../../services/api', async importOriginal => {
  const { mockApiModule } = await import('../../test/apiMock');
  return mockApiModule(importOriginal);
});

const mockGetMyLaps = vi.mocked(api.getMyLaps);
const mockGetSeries = vi.mocked(api.getSeries);
const mockGetProfileStats = vi.mocked(api.getProfileStats);
const mockGetAchievements = vi.mocked(api.getAchievements);

const minimalProfile: DriverProfile = {
  customerId: 100042,
  displayName: 'Test Driver',
  country: null,
  countryCode: null,
  memberSince: null,
  licenses: [],
  career: [],
  thisYear: { officialSessions: 0, officialWins: 0, leagueSessions: 0, leagueWins: 0 },
  favoriteCar: null,
  favoriteTrack: null,
};

const sampleProfile: DriverProfile = {
  customerId: 100042,
  displayName: 'Test Driver',
  country: 'United States',
  countryCode: 'USA',
  memberSince: '2021-11-05',
  licenses: [
    {
      categoryId: 5,
      categoryName: 'Sports Car',
      groupName: 'Class A',
      licenseLevel: 18,
      safetyRating: 2.38,
      iRating: 1854,
      color: '0153db',
    },
  ],
  career: [
    {
      categoryId: 2,
      categoryName: 'Road',
      starts: 576,
      wins: 42,
      top5: 180,
      poles: 31,
      avgStartPosition: 10,
      avgFinishPosition: 10,
      laps: 6401,
      lapsLed: 351,
      winPercentage: 7.29,
      top5Percentage: 31.25,
    },
  ],
  thisYear: { officialSessions: 75, officialWins: 1, leagueSessions: 0, leagueWins: 0 },
  favoriteCar: {
    carId: 119,
    carName: 'Porsche 718 Cayman GT4 Clubsport MR',
    imageUrl: 'https://x/car.jpg',
  },
  favoriteTrack: {
    trackId: 249,
    trackName: 'Nürburgring Nordschleife',
    configName: 'Industriefahrten',
    logoUrl: null,
  },
};

const sampleLaps = [
  {
    carId: 1,
    carName: 'Porsche 911 GT3 R',
    trackName: 'Spa-Francorchamps',
    configName: '',
    bestLapSeconds: 137.482,
    lapCount: 10,
    lastRecordedAt: '2024-01-01T00:00:00Z',
  },
  {
    carId: 2,
    carName: 'Ferrari 296 GT3',
    trackName: 'Nürburgring',
    configName: 'GP',
    bestLapSeconds: 120.015,
    lapCount: 5,
    lastRecordedAt: '2024-02-01T00:00:00Z',
  },
];

const sampleSeries = [
  {
    id: 1,
    name: 'VRS GT3 Sprint',
    seasonId: 2024,
    currentWeekNumber: 5,
    category: null,
    trackName: null,
    trackConfigName: null,
    carCount: 0,
    driverCount: 0,
  },
  {
    id: 2,
    name: 'Porsche Cup',
    seasonId: 2024,
    currentWeekNumber: null,
    category: null,
    trackName: null,
    trackConfigName: null,
    carCount: 0,
    driverCount: 0,
  },
];

function renderPage() {
  return render(
    <MemoryRouter>
      <ProfilePage />
    </MemoryRouter>
  );
}

describe('ProfilePage', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    mockLiveFlag = true;
    mockDemoFlag = false;
    mockUser = {
      token: 'tok',
      userId: 'u1',
      displayName: 'Test Driver',
      email: 't@t.com',
      iRacingCustomerId: 100042,
      role: 'Standard',
    };
    mockGetMyLaps.mockResolvedValue([]);
    mockGetSeries.mockResolvedValue([]);
    mockGetProfileStats.mockResolvedValue(minimalProfile);
    mockGetAchievements.mockResolvedValue({ customerId: 100042, awardCount: 0, awards: [] });
  });

  it('renders the driver display name', async () => {
    renderPage();
    expect(screen.getByText('Test Driver')).toBeInTheDocument();
  });

  it('shows iRacing customer ID when set', async () => {
    renderPage();
    expect(screen.getByText(/ID 100042/)).toBeInTheDocument();
  });

  it('shows loading placeholders initially', () => {
    mockGetMyLaps.mockReturnValue(new Promise(() => {}));
    mockGetSeries.mockReturnValue(new Promise(() => {}));
    renderPage();
    expect(screen.getAllByText(/loading/i).length).toBeGreaterThan(0);
  });

  it('shows empty lap state when no telemetry has been uploaded', async () => {
    renderPage();
    await waitFor(() => expect(screen.getByText(/no lap data yet/i)).toBeInTheDocument());
  });

  it('shows no active series message when series list is empty', async () => {
    renderPage();
    await waitFor(() => expect(screen.getByText(/no active series/i)).toBeInTheDocument());
  });

  it('renders car rows in the performance table when laps are present', async () => {
    mockGetMyLaps.mockResolvedValue(sampleLaps);
    renderPage();
    await waitFor(() => expect(screen.getByText('Porsche 911 GT3 R')).toBeInTheDocument());
    expect(screen.getByText('Ferrari 296 GT3')).toBeInTheDocument();
  });

  it('renders only the best lap per car when a car appears multiple times', async () => {
    const duplicateLaps = [
      ...sampleLaps,
      {
        carId: 1,
        carName: 'Porsche 911 GT3 R',
        trackName: 'Monza',
        configName: '',
        bestLapSeconds: 140.0,
        lapCount: 3,
        lastRecordedAt: '2024-03-01T00:00:00Z',
      },
    ];
    mockGetMyLaps.mockResolvedValue(duplicateLaps);
    renderPage();
    await waitFor(() => expect(screen.getByText('Porsche 911 GT3 R')).toBeInTheDocument());
    // Only one row for the Porsche — the faster lap (137.482)
    expect(screen.getAllByText('Porsche 911 GT3 R')).toHaveLength(1);
  });

  it('formats lap times correctly in the table', async () => {
    mockGetMyLaps.mockResolvedValue(sampleLaps);
    renderPage();
    // 137.482s → 2:17.482 (table only)
    await waitFor(() => expect(screen.getByText('2:17.482')).toBeInTheDocument());
    // 120.015s → 2:00.015 appears in both the stat chip and the table row
    expect(screen.getAllByText('2:00.015').length).toBeGreaterThanOrEqual(1);
  });

  it('shows series cards when series data is available', async () => {
    mockGetSeries.mockResolvedValue(sampleSeries);
    renderPage();
    await waitFor(() => expect(screen.getByText('VRS GT3 Sprint')).toBeInTheDocument());
    expect(screen.getByText('Porsche Cup')).toBeInTheDocument();
  });

  it('shows WK badge for active series and Off Season for inactive', async () => {
    mockGetSeries.mockResolvedValue(sampleSeries);
    renderPage();
    await waitFor(() => expect(screen.getByText('WK 5')).toBeInTheDocument());
    expect(screen.getByText('Off Season')).toBeInTheDocument();
  });

  it('shows cars driven stat after laps load', async () => {
    mockGetMyLaps.mockResolvedValue(sampleLaps);
    renderPage();
    await waitFor(() => expect(screen.getByText('2')).toBeInTheDocument());
  });

  it('shows Personal Best stat as the fastest lap across all cars', async () => {
    mockGetMyLaps.mockResolvedValue(sampleLaps);
    renderPage();
    // Ferrari's 120.015s → 2:00.015 is faster; displayed in the gold stat chip
    await waitFor(() => {
      const cells = screen.getAllByText('2:00.015');
      expect(cells.length).toBeGreaterThan(0);
    });
  });

  // ── Enriched driver stats (1.5) ───────────────────────────────────────────

  it('shows license badges with safety rating when stats load', async () => {
    mockGetProfileStats.mockResolvedValue(sampleProfile);
    renderPage();
    await waitFor(() => expect(screen.getByText('Sports Car')).toBeInTheDocument());
    expect(screen.getByText(/2\.38 SR/)).toBeInTheDocument();
  });

  it('shows this-year official sessions count', async () => {
    mockGetProfileStats.mockResolvedValue(sampleProfile);
    renderPage();
    await waitFor(() => expect(screen.getByText('75')).toBeInTheDocument());
  });

  it('shows favorite car and track from the recap', async () => {
    mockGetProfileStats.mockResolvedValue(sampleProfile);
    renderPage();
    await waitFor(() =>
      expect(screen.getByText('Porsche 718 Cayman GT4 Clubsport MR')).toBeInTheDocument()
    );
    expect(screen.getByText('Nürburgring Nordschleife')).toBeInTheDocument();
  });

  it('renders a career-by-category row', async () => {
    mockGetProfileStats.mockResolvedValue(sampleProfile);
    renderPage();
    await waitFor(() => expect(screen.getByText('Career by Category')).toBeInTheDocument());
    expect(screen.getByText('Road')).toBeInTheDocument();
    expect(screen.getByText('576')).toBeInTheDocument();
  });

  it('prompts to link iRacing when the user has no customer ID', async () => {
    mockUser = { ...mockUser!, iRacingCustomerId: null };
    renderPage();
    await waitFor(() =>
      expect(screen.getByRole('link', { name: /settings/i })).toHaveAttribute('href', '/settings')
    );
    expect(screen.getByRole('link', { name: /settings/i })).toHaveClass('underline');
    expect(mockGetProfileStats).not.toHaveBeenCalled();
  });

  it('prompts to link iRacing when the API reports not linked', async () => {
    mockGetProfileStats.mockRejectedValue(new IRacingNotLinkedError('nope'));
    renderPage();
    await waitFor(() =>
      expect(screen.getByRole('link', { name: /settings/i })).toHaveAttribute('href', '/settings')
    );
  });

  it('shows the shared error card when driver stats fail', async () => {
    mockGetProfileStats.mockRejectedValue(new Error('boom'));
    renderPage();
    await waitFor(() => expect(screen.getByText('boom')).toBeInTheDocument());
    expect(screen.queryByText('Licenses')).not.toBeInTheDocument();
  });

  // ── Trophy case (3.4) ──────────────────────────────────────────────────────

  const award = (id: number, name: string, count = 1): Award => ({
    awardId: id,
    name,
    description: `${name} description`,
    groupName: 'Racing',
    count,
    awardDate: '2026-05-01T00:00:00Z',
    iconUrl: `https://img/${id}.png`,
    iconBackgroundColor: '112233',
    progress: 1,
    threshold: 1,
  });

  it('renders the trophy case with award count and a multi-earn badge', async () => {
    mockGetAchievements.mockResolvedValue({
      customerId: 100042,
      awardCount: 2,
      awards: [award(1, 'Hat Trick', 3), award(2, 'Clean Race')],
    });
    renderPage();
    await waitFor(() => expect(screen.getByText('Trophy Case')).toBeInTheDocument());
    expect(screen.getByText('2 awards')).toBeInTheDocument();
    expect(screen.getByText('Hat Trick')).toBeInTheDocument();
    expect(screen.getByText('×3')).toBeInTheDocument(); // earned 3 times
  });

  it('toggles show-all when there are more awards than the preview limit', async () => {
    const many = Array.from({ length: 20 }, (_, i) => award(i + 1, `Award ${i + 1}`));
    mockGetAchievements.mockResolvedValue({ customerId: 100042, awardCount: 20, awards: many });
    renderPage();
    await waitFor(() => expect(screen.getByText('Award 1')).toBeInTheDocument());
    // Only the first 18 are shown initially.
    expect(screen.queryByText('Award 19')).not.toBeInTheDocument();
    const toggle = screen.getByRole('button', { name: /show all 20/i });
    fireEvent.click(toggle);
    expect(screen.getByText('Award 19')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /show fewer/i }));
    expect(screen.queryByText('Award 19')).not.toBeInTheDocument();
  });

  it('renders a color-tile fallback when an award has no icon', async () => {
    mockGetAchievements.mockResolvedValue({
      customerId: 100042,
      awardCount: 1,
      awards: [{ ...award(1, 'Iconless'), iconUrl: null }],
    });
    renderPage();
    await waitFor(() => expect(screen.getByText('Trophy Case')).toBeInTheDocument());
    expect(screen.getByText('Iconless')).toBeInTheDocument();
  });

  it('hides the trophy case when the achievements fetch fails', async () => {
    mockGetAchievements.mockRejectedValue(new Error('boom'));
    renderPage();
    await waitFor(() => expect(screen.getByText('Active Series')).toBeInTheDocument());
    expect(screen.queryByText('Trophy Case')).not.toBeInTheDocument();
  });

  it('does not fetch achievements when the user is not linked', async () => {
    mockUser = { ...mockUser!, iRacingCustomerId: null };
    renderPage();
    await waitFor(() => expect(screen.getByText('Active Series')).toBeInTheDocument());
    expect(mockGetAchievements).not.toHaveBeenCalled();
    expect(screen.queryByText('Trophy Case')).not.toBeInTheDocument();
  });

  it('hides iRacing sections and skips their fetches when iracing-live is off', async () => {
    mockLiveFlag = false;
    mockGetMyLaps.mockResolvedValue(sampleLaps);
    mockGetProfileStats.mockResolvedValue(sampleProfile);
    renderPage();
    // Local content stays
    await waitFor(() => expect(screen.getByText('Personal Best by Car')).toBeInTheDocument());
    expect(screen.getByText('Porsche 911 GT3 R')).toBeInTheDocument();
    // iRacing sections are gone
    expect(screen.queryByText('Licenses')).not.toBeInTheDocument();
    expect(screen.queryByText('Trophy Case')).not.toBeInTheDocument();
    expect(screen.queryByText('Active Series')).not.toBeInTheDocument();
    // iRacing fetches never fire; local laps still load
    expect(mockGetSeries).not.toHaveBeenCalled();
    expect(mockGetProfileStats).not.toHaveBeenCalled();
    expect(mockGetAchievements).not.toHaveBeenCalled();
    expect(mockGetMyLaps).toHaveBeenCalled();
  });

  it('shows iRacing sections when iracing-demo is on and iracing-live is off', async () => {
    mockLiveFlag = false;
    mockDemoFlag = true;
    mockGetMyLaps.mockResolvedValue(sampleLaps);
    mockGetProfileStats.mockResolvedValue(sampleProfile);
    mockGetSeries.mockResolvedValue(sampleSeries);
    mockGetAchievements.mockResolvedValue({ customerId: 100042, awardCount: 0, awards: [] });
    renderPage();
    // iRacing sections are visible (demo data)
    await waitFor(() => expect(screen.getByText('Active Series')).toBeInTheDocument());
    expect(screen.getByText('Licenses')).toBeInTheDocument();
    // iRacing fetches fire
    expect(mockGetSeries).toHaveBeenCalled();
    expect(mockGetProfileStats).toHaveBeenCalled();
    expect(mockGetAchievements).toHaveBeenCalled();
  });
});
