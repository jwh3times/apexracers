import { render, screen, waitFor, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router';
import { vi, describe, it, expect, beforeEach } from 'vitest';
import RecommendationsPage from './RecommendationsPage';
import { api, IRacingNotLinkedError } from '../../services/api';
import { PaceSourceProvider } from '../../context/PaceSourceProvider';

vi.mock('../../context/AuthContext', () => ({
  useAuth: () => ({ user: { userId: 'user-1' } }),
}));

vi.mock('../../services/api', async importOriginal => {
  const { mockApiModule } = await import('../../test/apiMock');
  return mockApiModule(importOriginal);
});

const mockGetSeries = vi.mocked(api.getSeries);
const mockGetRecs = vi.mocked(api.getRecommendations);

// Ferrari sorts before Porsche alphabetically — auto-selected first
const MOCK_SERIES = [
  {
    id: 1,
    name: 'Porsche Cup',
    seasonId: 10,
    currentWeekNumber: 10,
    category: 'Road',
    trackName: null,
    trackConfigName: null,
    carCount: 1,
    driverCount: 50,
  },
  {
    id: 2,
    name: 'Ferrari GT3 Challenge',
    seasonId: 11,
    currentWeekNumber: 8,
    category: 'Road',
    trackName: null,
    trackConfigName: null,
    carCount: 3,
    driverCount: 100,
  },
];

const MOCK_RECS = [
  {
    rank: 1,
    carId: 2,
    carName: 'Ferrari 296 GT3',
    percentileRank: 87.5,
    topSharePercent: 12,
    sampleSize: 200,
    isPercentilePresentable: true,
    bestLapSeconds: 78.5,
    bestLapEvidence: 'RaceLap' as const,
    projectedLapSeconds: 78.2,
  },
  {
    rank: 2,
    carId: 1,
    carName: 'Porsche 992 GT3',
    percentileRank: 72.0,
    topSharePercent: 28,
    sampleSize: 180,
    isPercentilePresentable: true,
    bestLapSeconds: null,
    bestLapEvidence: null,
    projectedLapSeconds: 79.1,
  },
];

function renderPage(search = '') {
  return render(
    <PaceSourceProvider>
      <MemoryRouter initialEntries={[`/recommendations${search}`]}>
        <RecommendationsPage />
      </MemoryRouter>
    </PaceSourceProvider>
  );
}

describe('RecommendationsPage', () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  it('renders series dropdown and auto-selects first alphabetical series', async () => {
    mockGetSeries.mockResolvedValue(MOCK_SERIES);
    mockGetRecs.mockResolvedValue([]);
    renderPage();
    await waitFor(() => {
      expect(screen.getByRole('combobox')).toBeInTheDocument();
    });
    // Ferrari GT3 Challenge (id 2) sorts before Porsche Cup alphabetically.
    // The recommendations fetch fires in a follow-up effect after the series
    // auto-selects, so poll for it rather than asserting synchronously.
    await waitFor(() => {
      expect(mockGetRecs).toHaveBeenCalledWith(2, 8, expect.any(Object), expect.any(AbortSignal));
    });
  });

  it('uses seriesId from URL when present', async () => {
    mockGetSeries.mockResolvedValue(MOCK_SERIES);
    mockGetRecs.mockResolvedValue([]);
    renderPage('?seriesId=1');
    await waitFor(() => {
      expect(mockGetRecs).toHaveBeenCalledWith(1, 10, expect.any(Object), expect.any(AbortSignal));
    });
  });

  it('shows empty-state prompt with profile link when no recommendations', async () => {
    mockGetSeries.mockResolvedValue(MOCK_SERIES);
    mockGetRecs.mockResolvedValue([]);
    renderPage('?seriesId=1');
    await waitFor(() => expect(screen.getByRole('link', { name: /profile/i })).toBeInTheDocument());
    expect(screen.getByRole('link', { name: /profile/i })).toHaveClass('underline');
  });

  it('renders top recommendation with car name and formatted lap times', async () => {
    mockGetSeries.mockResolvedValue(MOCK_SERIES);
    mockGetRecs.mockResolvedValue(MOCK_RECS);
    renderPage('?seriesId=1');
    await waitFor(() => {
      expect(screen.getByText('Ferrari 296 GT3')).toBeInTheDocument();
      expect(screen.getByText('#1')).toBeInTheDocument();
      // bestLapSeconds: 78.5 → 1:18.500
      expect(screen.getByText('1:18.500')).toBeInTheDocument();
      // projectedLapSeconds: 78.2 → 1:18.200
      expect(screen.getByText('1:18.200')).toBeInTheDocument();
      expect(screen.getByText('87.5%')).toBeInTheDocument();
    });
  });

  it('names the evidence behind each best lap', async () => {
    mockGetSeries.mockResolvedValue(MOCK_SERIES);
    mockGetRecs.mockResolvedValue(MOCK_RECS);
    renderPage('?seriesId=1');
    await waitFor(() => {
      // The Ferrari's best is a race lap; it appears both on the top-match card and in the table.
      expect(screen.getAllByText('Race lap').length).toBeGreaterThan(0);
    });
  });

  it('says so when a recommendation rests on an uploaded lap', async () => {
    mockGetSeries.mockResolvedValue(MOCK_SERIES);
    mockGetRecs.mockResolvedValue([
      { ...MOCK_RECS[0], bestLapEvidence: 'UploadedLap' as const },
      MOCK_RECS[1],
    ]);
    renderPage('?seriesId=1');
    await waitFor(() => expect(screen.getAllByText('Uploaded lap').length).toBeGreaterThan(0));
    expect(screen.queryByText('Race lap')).not.toBeInTheDocument();
  });

  it('shows no evidence label for a car the driver holds no lap for', async () => {
    mockGetSeries.mockResolvedValue(MOCK_SERIES);
    // Porsche has bestLapSeconds: null and bestLapEvidence: null — no lap, so no evidence.
    mockGetRecs.mockResolvedValue([MOCK_RECS[1]]);
    renderPage('?seriesId=1');
    await waitFor(() => expect(screen.getByText('Porsche 992 GT3')).toBeInTheDocument());
    expect(screen.queryByText('Race lap')).not.toBeInTheDocument();
    expect(screen.queryByText('Uploaded lap')).not.toBeInTheDocument();
  });

  it('shows dash in Best Lap column for cars without an actual lap this week', async () => {
    mockGetSeries.mockResolvedValue(MOCK_SERIES);
    mockGetRecs.mockResolvedValue(MOCK_RECS);
    renderPage('?seriesId=1');
    await waitFor(() => {
      // Porsche has bestLapSeconds: null — dash appears in the Best Lap cell
      expect(screen.getByText('—')).toBeInTheDocument();
    });
  });

  it('shows formatted best lap for cars with an actual lap', async () => {
    mockGetSeries.mockResolvedValue(MOCK_SERIES);
    mockGetRecs.mockResolvedValue(MOCK_RECS);
    renderPage('?seriesId=1');
    await waitFor(() => {
      expect(screen.getByText('Ferrari 296 GT3')).toBeInTheDocument();
      // Ferrari has bestLapSeconds: 78.5 → 1:18.500 (no dash)
      expect(screen.getByText('1:18.500')).toBeInTheDocument();
    });
  });

  it('renders other-options list for second and subsequent recommendations', async () => {
    mockGetSeries.mockResolvedValue(MOCK_SERIES);
    mockGetRecs.mockResolvedValue(MOCK_RECS);
    renderPage('?seriesId=1');
    await waitFor(() => {
      expect(screen.getByText('Porsche 992 GT3')).toBeInTheDocument();
      expect(screen.getByText('#2')).toBeInTheDocument();
    });
  });

  it('shows the link-iRacing prompt pointing to Settings when the account is not linked', async () => {
    mockGetSeries.mockResolvedValue(MOCK_SERIES);
    mockGetRecs.mockRejectedValue(new IRacingNotLinkedError('not linked'));
    renderPage('?seriesId=1');
    await waitFor(() => {
      const link = screen.getByRole('link', { name: /settings/i });
      expect(link).toBeInTheDocument();
      expect(link).toHaveAttribute('href', '/settings');
      expect(link).toHaveClass('underline');
    });
  });

  it('shows error message when API fails', async () => {
    mockGetSeries.mockResolvedValue(MOCK_SERIES);
    mockGetRecs.mockRejectedValue(new Error('Unauthorized'));
    renderPage('?seriesId=1');
    await waitFor(() => expect(screen.getByText(/unauthorized/i)).toBeInTheDocument());
  });

  it('shows loading message while fetching series', () => {
    mockGetSeries.mockReturnValue(new Promise(() => {})); // never resolves
    renderPage();
    expect(screen.getByText('Loading…')).toBeInTheDocument();
  });

  it('shows no active series message when getSeries returns empty', async () => {
    mockGetSeries.mockResolvedValue([]);
    renderPage();
    await waitFor(() => expect(screen.getByText(/no active series found/i)).toBeInTheDocument());
  });

  it('shows the series error instead of the empty-series message when loading fails', async () => {
    mockGetSeries.mockRejectedValue(new Error('Series unavailable'));
    renderPage();

    expect(await screen.findByText('Series unavailable')).toBeInTheDocument();
    expect(screen.queryByText(/no active series found/i)).not.toBeInTheDocument();
  });

  it('switching to blend mode re-fetches with includeUploadedLaps true', async () => {
    mockGetSeries.mockResolvedValue(MOCK_SERIES);
    mockGetRecs.mockResolvedValue([]);
    renderPage('?seriesId=1');

    await waitFor(() => expect(mockGetRecs).toHaveBeenCalledTimes(1));

    fireEvent.click(screen.getByRole('radio', { name: /official \+ my uploaded laps/i }));

    await waitFor(() =>
      expect(mockGetRecs).toHaveBeenCalledWith(
        1,
        10,
        expect.objectContaining({ includeUploadedLaps: true }),
        expect.any(AbortSignal)
      )
    );
  });
});
