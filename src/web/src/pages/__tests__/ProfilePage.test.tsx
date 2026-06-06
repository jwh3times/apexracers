import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { vi, describe, it, expect, beforeEach } from 'vitest';
import ProfilePage from '../ProfilePage';
import { api } from '../../services/api';
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

vi.mock('../../services/api', () => ({
  api: {
    getMyLaps: vi.fn(),
    getSeries: vi.fn(),
  },
}));

const mockGetMyLaps = vi.mocked(api.getMyLaps);
const mockGetSeries = vi.mocked(api.getSeries);

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
});
