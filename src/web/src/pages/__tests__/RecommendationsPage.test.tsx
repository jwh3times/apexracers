import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { vi, describe, it, expect, beforeEach } from 'vitest';
import RecommendationsPage from '../RecommendationsPage';
import { api } from '../../services/api';

vi.mock('../../services/api', () => ({
  api: { getRecommendations: vi.fn() },
}));

const mockGetRecs = vi.mocked(api.getRecommendations);

const MOCK_RECS = [
  {
    rank: 1, carId: 2, carName: 'Ferrari 296 GT3',
    percentileRank: 87.5, sampleSize: 200,
    bestLapSeconds: 78.5, projectedLapSeconds: 78.2,
  },
  {
    rank: 2, carId: 1, carName: 'Porsche 992 GT3',
    percentileRank: 72.0, sampleSize: 180,
    bestLapSeconds: null, projectedLapSeconds: 79.1,
  },
];

function renderPage(search = '') {
  return render(
    <MemoryRouter initialEntries={[`/recommendations${search}`]}>
      <RecommendationsPage />
    </MemoryRouter>
  );
}

describe('RecommendationsPage', () => {
  beforeEach(() => { vi.resetAllMocks(); });

  it('shows navigation prompt when no seriesId/weekNumber in query string', () => {
    renderPage();
    expect(screen.getByText(/navigate to a week/i)).toBeInTheDocument();
    expect(mockGetRecs).not.toHaveBeenCalled();
  });

  it('shows empty-state prompt with profile link when no recommendations', async () => {
    mockGetRecs.mockResolvedValue([]);
    renderPage('?seriesId=1&weekNumber=10');
    await waitFor(() =>
      expect(screen.getByRole('link', { name: /profile/i })).toBeInTheDocument(),
    );
  });

  it('renders top recommendation with car name and formatted lap times', async () => {
    mockGetRecs.mockResolvedValue(MOCK_RECS);
    renderPage('?seriesId=1&weekNumber=10');
    await waitFor(() => {
      expect(screen.getByText('Ferrari 296 GT3')).toBeInTheDocument();
      expect(screen.getByText('#1')).toBeInTheDocument();
      // bestLapSeconds: 78.5 → 1:18.500
      expect(screen.getByText('1:18.500')).toBeInTheDocument();
      // projectedLapSeconds: 78.2 → 1:18.200
      expect(screen.getByText('1:18.200')).toBeInTheDocument();
      expect(screen.getByText('87.5th')).toBeInTheDocument();
    });
  });

  it('shows dash in Best Lap column for cars without an actual lap this week', async () => {
    mockGetRecs.mockResolvedValue(MOCK_RECS);
    renderPage('?seriesId=1&weekNumber=10');
    await waitFor(() => {
      // Porsche has bestLapSeconds: null — dash appears in the Best Lap cell
      expect(screen.getByText('—')).toBeInTheDocument();
    });
  });

  it('shows formatted best lap for cars with an actual lap', async () => {
    mockGetRecs.mockResolvedValue(MOCK_RECS);
    renderPage('?seriesId=1&weekNumber=10');
    await waitFor(() => {
      expect(screen.getByText('Ferrari 296 GT3')).toBeInTheDocument();
      // Ferrari has bestLapSeconds: 78.5 → 1:18.500 (no dash)
      expect(screen.getByText('1:18.500')).toBeInTheDocument();
    });
  });

  it('renders other-options list for second and subsequent recommendations', async () => {
    mockGetRecs.mockResolvedValue(MOCK_RECS);
    renderPage('?seriesId=1&weekNumber=10');
    await waitFor(() => {
      expect(screen.getByText('Porsche 992 GT3')).toBeInTheDocument();
      expect(screen.getByText('#2')).toBeInTheDocument();
    });
  });

  it('shows error message when API fails', async () => {
    mockGetRecs.mockRejectedValue(new Error('Unauthorized'));
    renderPage('?seriesId=1&weekNumber=10');
    await waitFor(() => expect(screen.getByText(/unauthorized/i)).toBeInTheDocument());
  });
});
