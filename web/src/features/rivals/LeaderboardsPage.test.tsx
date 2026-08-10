import { render, screen, waitFor, fireEvent } from '@testing-library/react';
import { vi, describe, it, expect, beforeEach } from 'vitest';
import LeaderboardsPage from './LeaderboardsPage';
import { api, type GlobalLeaderboardEntry } from '../../services/api';

vi.mock('../../services/api', async importOriginal => {
  const { mockApiModule } = await import('../../test/apiMock');
  return mockApiModule(importOriginal);
});

let mockIRacingCustomerId: number | null = null;
vi.mock('../../context/AuthContext', () => ({
  useAuth: () => ({ user: { iRacingCustomerId: mockIRacingCustomerId } }),
}));

const mockGetLeaderboard = vi.mocked(api.getLeaderboard);

function entry(
  rank: number,
  custId: number,
  driver: string,
  iRating: number
): GlobalLeaderboardEntry {
  return {
    categoryId: 5,
    rank,
    custId,
    driver,
    location: 'US',
    starts: 100,
    wins: 10,
    iRating,
    ttRating: 1300,
    champPoints: 5000,
  };
}

const ROWS = [entry(1, 111, 'Fast Guy', 9000), entry(2, 222, 'Me Driver', 8500)];

describe('LeaderboardsPage', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    mockIRacingCustomerId = null;
  });

  it('shows a loading state while fetching', () => {
    mockGetLeaderboard.mockReturnValue(new Promise(() => {}));
    render(<LeaderboardsPage />);
    expect(screen.getByText(/loading/i)).toBeInTheDocument();
  });

  it('loads sports car (category 5) by default and renders rows', async () => {
    mockGetLeaderboard.mockResolvedValue(ROWS);
    render(<LeaderboardsPage />);
    await waitFor(() =>
      expect(mockGetLeaderboard).toHaveBeenCalledWith(5, expect.any(AbortSignal))
    );
    expect(screen.getByText('Fast Guy')).toBeInTheDocument();
    expect(screen.getByText('9,000')).toBeInTheDocument();
  });

  it('refetches when a different category chip is selected', async () => {
    mockGetLeaderboard.mockResolvedValue(ROWS);
    render(<LeaderboardsPage />);
    await waitFor(() =>
      expect(mockGetLeaderboard).toHaveBeenCalledWith(5, expect.any(AbortSignal))
    );

    fireEvent.click(screen.getByRole('button', { name: 'Oval' }));
    await waitFor(() =>
      expect(mockGetLeaderboard).toHaveBeenCalledWith(1, expect.any(AbortSignal))
    );
  });

  it("highlights the logged-in driver's row", async () => {
    mockIRacingCustomerId = 222;
    mockGetLeaderboard.mockResolvedValue(ROWS);
    render(<LeaderboardsPage />);
    await waitFor(() => expect(screen.getByText('(you)')).toBeInTheDocument());
  });

  it('shows an empty-state message when there are no rows', async () => {
    mockGetLeaderboard.mockResolvedValue([]);
    render(<LeaderboardsPage />);
    await waitFor(() => expect(screen.getByText(/no leaderboard data/i)).toBeInTheDocument());
  });

  it('shows an error message when the API fails', async () => {
    mockGetLeaderboard.mockRejectedValue(new Error('Boom'));
    render(<LeaderboardsPage />);
    await waitFor(() => expect(screen.getByText(/boom/i)).toBeInTheDocument());
  });
});
