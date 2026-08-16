import { render, screen, waitFor, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router';
import { vi, describe, it, expect, beforeEach } from 'vitest';
import AnalyticsPage from './AnalyticsPage';
import { api, IRacingNotLinkedError } from '../../services/api';
import type { User } from '../../context/AuthContext';
import { PaceSourceProvider } from '../../context/PaceSourceProvider';

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

vi.mock('../../services/api', async importOriginal => {
  const { mockApiModule } = await import('../../test/apiMock');
  return mockApiModule(importOriginal);
});

const mockGetSeries = vi.mocked(api.getSeries);
const mockGetMyAnalytics = vi.mocked(api.getMyAnalytics);
const mockGetRecommendations = vi.mocked(api.getRecommendations);

const MOCK_SERIES = [
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
    currentWeekNumber: 3,
    category: null,
    trackName: null,
    trackConfigName: null,
    carCount: 0,
    driverCount: 0,
  },
];

const MOCK_ANALYTICS = [
  {
    carId: 101,
    carName: 'Porsche 911 GT3 R',
    seriesId: 1,
    seriesName: 'VRS GT3 Sprint',
    latestPercentileRank: 92.0,
    latestTopSharePercent: 8,
    bestPercentileRank: 92.0,
    bestTopSharePercent: 8,
    personalBestLapSeconds: 137.2,
    medianLapSeconds: 139.5,
    totalWeeks: 48,
    percentileHistory: [
      {
        weekNumber: 1,
        trackName: 'Monza',
        configName: 'GP',
        percentileRank: 80.0,
        topSharePercent: 20,
        sampleSize: 100,
        computedAt: '2026-01-01T00:00:00Z',
      },
      {
        weekNumber: 2,
        trackName: 'Spa',
        configName: 'Full',
        percentileRank: 92.0,
        topSharePercent: 8,
        sampleSize: 110,
        computedAt: '2026-01-08T00:00:00Z',
      },
    ],
  },
  {
    carId: 102,
    carName: 'BMW M4 GT3',
    seriesId: 1,
    seriesName: 'VRS GT3 Sprint',
    latestPercentileRank: 70.0,
    latestTopSharePercent: 30,
    bestPercentileRank: 70.0,
    bestTopSharePercent: 30,
    personalBestLapSeconds: 138.9,
    medianLapSeconds: 139.5,
    totalWeeks: 20,
    percentileHistory: [
      {
        weekNumber: 1,
        trackName: 'Monza',
        configName: 'GP',
        percentileRank: 60.0,
        topSharePercent: 40,
        sampleSize: 100,
        computedAt: '2026-01-01T00:00:00Z',
      },
      {
        weekNumber: 2,
        trackName: 'Spa',
        configName: 'Full',
        percentileRank: 70.0,
        topSharePercent: 30,
        sampleSize: 110,
        computedAt: '2026-01-08T00:00:00Z',
      },
    ],
  },
];

function renderPage() {
  return render(
    <PaceSourceProvider>
      <MemoryRouter>
        <AnalyticsPage />
      </MemoryRouter>
    </PaceSourceProvider>
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
    mockGetRecommendations.mockResolvedValue([]);
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
      expect(mockGetMyAnalytics).toHaveBeenCalledWith(
        1,
        { includePersonalLaps: false, personalLapTypes: undefined },
        expect.any(AbortSignal)
      );
    });
  });

  it('applies the active evidence choice to series analytics and percentile computation', async () => {
    mockGetSeries.mockResolvedValue(MOCK_SERIES);
    renderPage();
    await screen.findByRole('radiogroup', { name: /how we calculate your pace/i });

    fireEvent.click(screen.getByRole('radio', { name: /official \+ my uploaded laps/i }));

    await waitFor(() =>
      expect(mockGetMyAnalytics).toHaveBeenCalledWith(
        1,
        { includePersonalLaps: true, personalLapTypes: undefined },
        expect.any(AbortSignal)
      )
    );
    fireEvent.click(screen.getByRole('button', { name: /compute my percentiles/i }));
    await waitFor(() =>
      expect(mockGetRecommendations).toHaveBeenCalledWith(1, 5, {
        includePersonalLaps: true,
        personalLapTypes: undefined,
      })
    );
  });

  it('renders featured car name and TOP X% percentile label', async () => {
    mockGetSeries.mockResolvedValue(MOCK_SERIES);
    mockGetMyAnalytics.mockResolvedValue(MOCK_ANALYTICS);
    renderPage();
    await waitFor(() => {
      expect(screen.getByText('Porsche 911 GT3 R')).toBeInTheDocument();
      // latestTopSharePercent = 8 → TOP 8%
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
    expect(screen.getByRole('link', { name: /browse series/i })).toHaveClass('underline');
  });

  // ── First-visit "Compute my percentiles" CTA (T15) ─────────────────────────

  it('empty series analytics shows the compute CTA and populates on click', async () => {
    mockGetSeries.mockResolvedValue(MOCK_SERIES);
    mockGetMyAnalytics.mockResolvedValueOnce([]).mockResolvedValueOnce(MOCK_ANALYTICS);
    renderPage();

    await waitFor(() => {
      expect(screen.getByRole('button', { name: /compute my percentiles/i })).toBeInTheDocument();
    });

    fireEvent.click(screen.getByRole('button', { name: /compute my percentiles/i }));

    // MOCK_SERIES[0] has id 1 and currentWeekNumber 5
    await waitFor(() =>
      expect(mockGetRecommendations).toHaveBeenCalledWith(1, 5, {
        includePersonalLaps: false,
        personalLapTypes: undefined,
      })
    );
    await waitFor(() => {
      expect(mockGetMyAnalytics).toHaveBeenCalledTimes(2);
      expect(screen.getByText('Porsche 911 GT3 R')).toBeInTheDocument();
    });
  });

  it('hides the compute CTA when the series has no current week', async () => {
    mockGetSeries.mockResolvedValue([{ ...MOCK_SERIES[0], currentWeekNumber: null }]);
    mockGetMyAnalytics.mockResolvedValue([]);
    renderPage();

    await waitFor(() => {
      expect(screen.getByText(/no percentile data for this series/i)).toBeInTheDocument();
    });
    expect(
      screen.queryByRole('button', { name: /compute my percentiles/i })
    ).not.toBeInTheDocument();
  });

  it('shows an inline error when compute fails', async () => {
    mockGetSeries.mockResolvedValue(MOCK_SERIES);
    mockGetMyAnalytics.mockResolvedValue([]);
    mockGetRecommendations.mockRejectedValue(new Error('compute boom'));
    renderPage();

    await waitFor(() => {
      expect(screen.getByRole('button', { name: /compute my percentiles/i })).toBeInTheDocument();
    });

    fireEvent.click(screen.getByRole('button', { name: /compute my percentiles/i }));

    await waitFor(() => {
      expect(screen.getByText(/could not compute percentiles/i)).toBeInTheDocument();
    });
  });

  it('calls getMyAnalytics with the new seriesId when a different series is selected', async () => {
    mockGetSeries.mockResolvedValue(MOCK_SERIES);
    renderPage();
    await waitFor(() => expect(screen.getByRole('combobox')).toBeInTheDocument());
    fireEvent.change(screen.getByRole('combobox'), { target: { value: '2' } });
    await waitFor(() =>
      expect(mockGetMyAnalytics).toHaveBeenCalledWith(
        2,
        { includePersonalLaps: false, personalLapTypes: undefined },
        expect.any(AbortSignal)
      )
    );
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

  it('shows the series error instead of the empty-series message when loading fails', async () => {
    mockGetSeries.mockRejectedValue(new Error('Series unavailable'));
    renderPage();

    expect(await screen.findByText('Series unavailable')).toBeInTheDocument();
    expect(screen.queryByText(/no active series found/i)).not.toBeInTheDocument();
  });

  // ── By Car mode + badge thresholds (T8) ────────────────────────────────────

  it('switches to By Car mode, fetches all analytics, and shows the car selector', async () => {
    mockGetSeries.mockResolvedValue(MOCK_SERIES);
    mockGetMyAnalytics.mockResolvedValue(MOCK_ANALYTICS);
    renderPage();
    await waitFor(() => expect(screen.getByText('Porsche 911 GT3 R')).toBeInTheDocument());

    fireEvent.click(screen.getByRole('button', { name: /by car/i }));

    await waitFor(() => expect(screen.getByLabelText('Car:')).toBeInTheDocument());
    expect(screen.getByRole('option', { name: 'Porsche 911 GT3 R' })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: 'BMW M4 GT3' })).toBeInTheDocument();
  });

  it('filters to the chosen car in By Car mode', async () => {
    mockGetSeries.mockResolvedValue(MOCK_SERIES);
    mockGetMyAnalytics.mockResolvedValue(MOCK_ANALYTICS);
    renderPage();
    await waitFor(() => expect(screen.getByText('Porsche 911 GT3 R')).toBeInTheDocument());
    fireEvent.click(screen.getByRole('button', { name: /by car/i }));
    await waitFor(() => expect(screen.getByLabelText('Car:')).toBeInTheDocument());

    fireEvent.change(screen.getByLabelText('Car:'), { target: { value: '102' } });
    // flipLabels in car mode → the series name is the card title; only BMW's row remains.
    await waitFor(() => expect(screen.getByText('TOP 30%')).toBeInTheDocument()); // BMW latest 70
  });

  it('shows the car-mode empty state when there is no analytics data', async () => {
    mockGetSeries.mockResolvedValue(MOCK_SERIES);
    mockGetMyAnalytics.mockResolvedValue([]);
    renderPage();
    await waitFor(() =>
      expect(screen.getByText(/no percentile data for this series/i)).toBeInTheDocument()
    );
    fireEvent.click(screen.getByRole('button', { name: /by car/i }));
    await waitFor(() => expect(screen.getByText(/no percentile data yet/i)).toBeInTheDocument());
    expect(screen.getByRole('link', { name: /browse series/i })).toHaveClass('underline');
  });

  it('shows the ELITE badge and gold styling for a ≥95 percentile car', async () => {
    mockGetSeries.mockResolvedValue(MOCK_SERIES);
    mockGetMyAnalytics.mockResolvedValue([
      {
        ...MOCK_ANALYTICS[0],
        bestPercentileRank: 97,
        latestPercentileRank: 97,
        latestTopSharePercent: 3,
        bestTopSharePercent: 3,
      },
    ]);
    renderPage();
    await waitFor(() => expect(screen.getByText('ELITE')).toBeInTheDocument());
  });

  it('renders a positive Best-vs-Median delta when the best lap is slower than the median', async () => {
    mockGetSeries.mockResolvedValue(MOCK_SERIES);
    mockGetMyAnalytics.mockResolvedValue([
      { ...MOCK_ANALYTICS[0], personalBestLapSeconds: 141.0, medianLapSeconds: 139.5 },
    ]);
    renderPage();
    await waitFor(() => expect(screen.getByText('+1.500s')).toBeInTheDocument());
  });

  it('labels the trend axis with years when history spans calendar years', async () => {
    mockGetSeries.mockResolvedValue(MOCK_SERIES);
    mockGetMyAnalytics.mockResolvedValue([
      {
        ...MOCK_ANALYTICS[0],
        percentileHistory: [
          { ...MOCK_ANALYTICS[0].percentileHistory[0], computedAt: '2025-12-01T00:00:00Z' },
          { ...MOCK_ANALYTICS[0].percentileHistory[1], computedAt: '2026-01-08T00:00:00Z' },
        ],
      },
    ]);
    renderPage();
    await waitFor(() => expect(screen.getByText('2025')).toBeInTheDocument());
    expect(screen.getByText('2026')).toBeInTheDocument();
  });

  it('shows the shared account-link prompt for a typed 409', async () => {
    mockGetSeries.mockResolvedValue(MOCK_SERIES);
    mockGetMyAnalytics.mockRejectedValue(new IRacingNotLinkedError('not linked'));
    renderPage();

    expect(
      await screen.findByText(/link your iracing account to view personalized analytics/i)
    ).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Open Settings' })).toHaveAttribute(
      'href',
      '/settings'
    );
  });
});
