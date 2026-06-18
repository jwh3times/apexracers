import { render, screen, waitFor, fireEvent } from '@testing-library/react';
import { MemoryRouter, Routes, Route } from 'react-router-dom';
import { vi, describe, it, expect, beforeEach } from 'vitest';
import StandingsPage from '../StandingsPage';
import { api, type SeasonStandings } from '../../services/api';

vi.mock('../../services/api', () => ({
  api: { getStandings: vi.fn() },
}));

let mockIRacingCustomerId: number | null = null;
vi.mock('../../context/AuthContext', () => ({
  useAuth: () => ({ user: { iRacingCustomerId: mockIRacingCustomerId } }),
}));

const mockGetStandings = vi.mocked(api.getStandings);

const STANDINGS: SeasonStandings = {
  seriesId: 444,
  seriesName: 'GT3 Cup',
  carClassId: 4091,
  carClassName: 'GT3 Class',
  carClasses: [
    { carClassId: 4091, carClassName: 'GT3 Class' },
    { carClassId: 2000, carClassName: 'GT4 Class' },
  ],
  standings: [
    {
      rank: 1,
      custId: 111,
      driverName: 'Leader',
      division: 1,
      starts: 12,
      wins: 7,
      top5: 9,
      poles: 3,
      points: 950,
      avgFinishPosition: 3.2,
      incidents: 18,
    },
    {
      rank: 2,
      custId: 222,
      driverName: 'Me Driver',
      division: 1,
      starts: 12,
      wins: 3,
      top5: 7,
      poles: 1,
      points: 880,
      avgFinishPosition: 5.4,
      incidents: 30,
    },
  ],
};

function renderPage() {
  return render(
    <MemoryRouter initialEntries={['/series/444/standings']}>
      <Routes>
        <Route path="/series/:seriesId/standings" element={<StandingsPage />} />
      </Routes>
    </MemoryRouter>
  );
}

describe('StandingsPage', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    mockIRacingCustomerId = null;
  });

  it('shows a loading state while fetching', () => {
    mockGetStandings.mockReturnValue(new Promise(() => {}));
    renderPage();
    expect(screen.getByText(/loading/i)).toBeInTheDocument();
  });

  it('fetches standings for the route series id', async () => {
    mockGetStandings.mockResolvedValue(STANDINGS);
    renderPage();
    await waitFor(() => expect(mockGetStandings).toHaveBeenCalledWith(444, undefined));
  });

  it('renders the standings rows and series name', async () => {
    mockGetStandings.mockResolvedValue(STANDINGS);
    renderPage();
    await waitFor(() => expect(screen.getByText('GT3 Cup')).toBeInTheDocument());
    expect(screen.getByText('Leader')).toBeInTheDocument();
    expect(screen.getByText('950')).toBeInTheDocument(); // points
  });

  it('refetches when a different car class chip is selected', async () => {
    mockGetStandings.mockResolvedValue(STANDINGS);
    renderPage();
    await waitFor(() => expect(mockGetStandings).toHaveBeenCalledWith(444, undefined));

    fireEvent.click(screen.getByRole('button', { name: 'GT4 Class' }));
    await waitFor(() => expect(mockGetStandings).toHaveBeenCalledWith(444, 2000));
  });

  it("highlights the logged-in driver's row", async () => {
    mockIRacingCustomerId = 222;
    mockGetStandings.mockResolvedValue(STANDINGS);
    renderPage();
    await waitFor(() => expect(screen.getByText('(you)')).toBeInTheDocument());
  });

  it('shows an empty-state message when there are no standings', async () => {
    mockGetStandings.mockResolvedValue({ ...STANDINGS, standings: [] });
    renderPage();
    await waitFor(() => expect(screen.getByText(/no standings available/i)).toBeInTheDocument());
  });

  it('shows an error message when the API fails', async () => {
    mockGetStandings.mockRejectedValue(new Error('Boom'));
    renderPage();
    await waitFor(() => expect(screen.getByText(/boom/i)).toBeInTheDocument());
  });
});
