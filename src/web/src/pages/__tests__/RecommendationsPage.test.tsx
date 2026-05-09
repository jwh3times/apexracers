import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { vi, describe, it, expect, beforeEach } from 'vitest';
import RecommendationsPage from '../RecommendationsPage';
import { api } from '../../services/api';

vi.mock('../../services/api', () => ({
  api: { getRecommendations: vi.fn() },
}));

const mockGetRecs = vi.mocked(api.getRecommendations);

function renderPage(search = '') {
  return render(
    <MemoryRouter initialEntries={[`/recommendations${search}`]}>
      <RecommendationsPage />
    </MemoryRouter>
  );
}

describe('RecommendationsPage', () => {
  beforeEach(() => { vi.resetAllMocks(); });

  it('shows navigation prompt when no weekId in query string', () => {
    renderPage();
    expect(screen.getByText(/navigate to a week/i)).toBeInTheDocument();
    expect(mockGetRecs).not.toHaveBeenCalled();
  });

  it('shows sign-in prompt when recommendations are empty', async () => {
    mockGetRecs.mockResolvedValue([]);
    renderPage('?weekId=10');
    await waitFor(() => expect(screen.getByText(/sign in with iracing/i)).toBeInTheDocument());
  });

  it('renders recommendations table with rank and percentile', async () => {
    mockGetRecs.mockResolvedValue([
      { rank: 1, carId: 2, carName: 'Ferrari 296 GT3', percentileRank: 87.5, sampleSize: 200 },
      { rank: 2, carId: 1, carName: 'Porsche 992 GT3', percentileRank: 72.0, sampleSize: 180 },
    ]);
    renderPage('?weekId=10');
    await waitFor(() => {
      expect(screen.getByText('Ferrari 296 GT3')).toBeInTheDocument();
      expect(screen.getByText('#1')).toBeInTheDocument();
      expect(screen.getByText('87.5th')).toBeInTheDocument();
      expect(screen.getByText('Porsche 992 GT3')).toBeInTheDocument();
    });
  });

  it('shows error message when API fails', async () => {
    mockGetRecs.mockRejectedValue(new Error('Unauthorized'));
    renderPage('?weekId=10');
    await waitFor(() => expect(screen.getByText(/unauthorized/i)).toBeInTheDocument());
  });
});
