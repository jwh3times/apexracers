import { render, screen, waitFor, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { vi, describe, it, expect, beforeEach } from 'vitest';
import AnalyticsPage from '../AnalyticsPage';
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
    getSeries: vi.fn(),
    getMyAnalytics: vi.fn(),
  },
}));

const mockGetSeries = vi.mocked(api.getSeries);
const mockGetMyAnalytics = vi.mocked(api.getMyAnalytics);

const MOCK_SERIES = [
  { id: 1, name: 'VRS GT3 Sprint', seasonId: 2024, currentWeekNumber: 5 },
  { id: 2, name: 'Porsche Cup', seasonId: 2024, currentWeekNumber: 3 },
];

const MOCK_ANALYTICS = [
  {
    carId: 101,
    carName: 'Porsche 911 GT3 R',
    seriesId: 1,
    seriesName: 'VRS GT3 Sprint',
    latestPercentileRank: 92.0,
    bestPercentileRank: 92.0,
    personalBestLapSeconds: 137.2,
    medianLapSeconds: 139.5,
    totalLaps: 48,
    percentileHistory: [
      { weekNumber: 1, trackName: 'Monza', configName: 'GP', percentileRank: 80.0, sampleSize: 100, computedAt: '2026-01-01T00:00:00Z' },
      { weekNumber: 2, trackName: 'Spa', configName: 'Full', percentileRank: 92.0, sampleSize: 110, computedAt: '2026-01-08T00:00:00Z' },
    ],
  },
  {
    carId: 102,
    carName: 'BMW M4 GT3',
    seriesId: 1,
    seriesName: 'VRS GT3 Sprint',
    latestPercentileRank: 70.0,
    bestPercentileRank: 70.0,
    personalBestLapSeconds: 138.9,
    medianLapSeconds: 139.5,
    totalLaps: 20,
    percentileHistory: [
      { weekNumber: 1, trackName: 'Monza', configName: 'GP', percentileRank: 60.0, sampleSize: 100, computedAt: '2026-01-01T00:00:00Z' },
      { weekNumber: 2, trackName: 'Spa', configName: 'Full', percentileRank: 70.0, sampleSize: 110, computedAt: '2026-01-08T00:00:00Z' },
    ],
  },
];

function renderPage() {
  return render(
    <MemoryRouter>
      <AnalyticsPage />
    </MemoryRouter>,
  );
}

describe('AnalyticsPage', () => {
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
    mockGetSeries.mockResolvedValue([]);
    mockGetMyAnalytics.mockResolvedValue([]);
  });

  it('shows sign-in prompt when user is not authenticated', () => {
    mockUser = null;
    renderPage();
    expect(screen.getByText(/sign in to view analytics/i)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /sign in/i })).toBeInTheDocument();
  });

  it('does not call getMyAnalytics when user is unauthenticated', () => {
    mockUser = null;
    renderPage();
    expect(mockGetMyAnalytics).not.toHaveBeenCalled();
  });

  it('renders series tabs once series load', async () => {
    mockGetSeries.mockResolvedValue(MOCK_SERIES);
    renderPage();
    await waitFor(() => {
      expect(screen.getByText('VRS GT3 Sprint')).toBeInTheDocument();
      expect(screen.getByText('Porsche Cup')).toBeInTheDocument();
    });
  });

  it('auto-selects first series and calls getMyAnalytics on load', async () => {
    mockGetSeries.mockResolvedValue(MOCK_SERIES);
    renderPage();
    await waitFor(() => {
      expect(mockGetMyAnalytics).toHaveBeenCalledWith(1);
    });
  });

  it('renders featured car name and TOP X% percentile label', async () => {
    mockGetSeries.mockResolvedValue(MOCK_SERIES);
    mockGetMyAnalytics.mockResolvedValue(MOCK_ANALYTICS);
    renderPage();
    await waitFor(() => {
      expect(screen.getByText('Porsche 911 GT3 R')).toBeInTheDocument();
      // latestPercentileRank = 92, ceil(100-92) = 8 → TOP 8%
      expect(screen.getByText('TOP 8%')).toBeInTheDocument();
    });
  });

  it('renders secondary car in its own card', async () => {
    mockGetSeries.mockResolvedValue(MOCK_SERIES);
    mockGetMyAnalytics.mockResolvedValue(MOCK_ANALYTICS);
    renderPage();
    await waitFor(() => {
      expect(screen.getByText('BMW M4 GT3')).toBeInTheDocument();
      // latestPercentileRank = 70, ceil(100-70) = 30 → TOP 30%
      expect(screen.getByText('TOP 30%')).toBeInTheDocument();
    });
  });

  it('shows IMPROVING badge for a car with rising percentile trend', async () => {
    mockGetSeries.mockResolvedValue(MOCK_SERIES);
    mockGetMyAnalytics.mockResolvedValue(MOCK_ANALYTICS);
    renderPage();
    await waitFor(() => {
      // BMW M4 history: 60 → 70, IMPROVING badge shows
      expect(screen.getByText('IMPROVING')).toBeInTheDocument();
    });
  });

  it('shows empty state message when no analytics data exists for the series', async () => {
    mockGetSeries.mockResolvedValue(MOCK_SERIES);
    mockGetMyAnalytics.mockResolvedValue([]);
    renderPage();
    await waitFor(() => {
      expect(screen.getByText(/no percentile data for this series/i)).toBeInTheDocument();
    });
  });

  it('calls getMyAnalytics with the new seriesId when a different series tab is clicked', async () => {
    mockGetSeries.mockResolvedValue(MOCK_SERIES);
    renderPage();
    await waitFor(() => expect(screen.getByText('Porsche Cup')).toBeInTheDocument());
    fireEvent.click(screen.getByText('Porsche Cup'));
    await waitFor(() => expect(mockGetMyAnalytics).toHaveBeenCalledWith(2));
  });

  it('shows error message when getMyAnalytics fails', async () => {
    mockGetSeries.mockResolvedValue(MOCK_SERIES);
    mockGetMyAnalytics.mockRejectedValue(new Error('Unauthorized'));
    renderPage();
    await waitFor(() => {
      expect(screen.getByText('Unauthorized')).toBeInTheDocument();
    });
  });

  it('shows no active series message when series list is empty', async () => {
    mockGetSeries.mockResolvedValue([]);
    renderPage();
    await waitFor(() => {
      expect(screen.getByText(/no active series found/i)).toBeInTheDocument();
    });
  });
});
