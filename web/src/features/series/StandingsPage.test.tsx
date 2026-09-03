import { render, screen, waitFor, fireEvent } from '@testing-library/react';
import { MemoryRouter, Routes, Route } from 'react-router';
import { vi, describe, it, expect, beforeEach } from 'vitest';
import StandingsPage from './StandingsPage';
import {
  api,
  type SeasonStandings,
  type SeasonTtStandings,
  type SeasonQualifyResults,
} from '../../services/api';

vi.mock('../../services/api', async importOriginal => {
  const { mockApiModule } = await import('../../test/apiMock');
  return mockApiModule(importOriginal);
});

let mockIRacingCustomerId: number | null = null;
vi.mock('../../context/AuthContext', () => ({
  useAuth: () => ({ user: { iRacingCustomerId: mockIRacingCustomerId } }),
}));

const mockGetStandings = vi.mocked(api.getStandings);
const mockGetTtStandings = vi.mocked(api.getTtStandings);
const mockGetQualifyResults = vi.mocked(api.getQualifyResults);

const CAR_CLASSES = [
  { carClassId: 4091, carClassName: 'GT3 Class' },
  { carClassId: 2000, carClassName: 'GT4 Class' },
];

const STANDINGS: SeasonStandings = {
  seriesId: 444,
  seriesName: 'GT3 Cup',
  carClassId: 4091,
  carClassName: 'GT3 Class',
  carClasses: CAR_CLASSES,
  standings: [
    {
      standing: 1,
      customerId: 111,
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
      standing: 2,
      customerId: 222,
      driverName: 'Me Driver',
      division: 4,
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

const TT_STANDINGS: SeasonTtStandings = {
  seriesId: 444,
  seriesName: 'GT3 Cup',
  carClassId: 4091,
  carClassName: 'GT3 Class',
  carClasses: CAR_CLASSES,
  standings: [
    {
      standing: 1,
      customerId: 111,
      driverName: 'TT Leader',
      division: 2,
      ttRating: 2500,
      starts: 12,
      wins: 4,
      top5: 7,
      poles: 1,
      points: 800,
      avgFinishPosition: 5.5,
      incidents: 20,
    },
    {
      standing: 2,
      customerId: 333,
      driverName: 'No Rating',
      division: 3,
      ttRating: null,
      starts: 5,
      wins: 0,
      top5: 1,
      poles: 0,
      points: 200,
      avgFinishPosition: 9.1,
      incidents: 12,
    },
  ],
};

const QUAL_RESULTS: SeasonQualifyResults = {
  seriesId: 444,
  seriesName: 'GT3 Cup',
  carClassId: 4091,
  carClassName: 'GT3 Class',
  carClasses: CAR_CLASSES,
  raceWeekIndex: 2,
  availableRaceWeekIndices: [0, 1, 2],
  results: [
    {
      standing: 1,
      customerId: 111,
      driverName: 'Pole Sitter',
      division: 2,
      iRating: 7000,
      bestQualLapSeconds: 375.0,
      raceWeekIndex: 2,
    },
    {
      standing: 2,
      customerId: 444,
      driverName: 'No Time',
      division: 5,
      iRating: null,
      bestQualLapSeconds: -1,
      raceWeekIndex: 2,
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
    mockGetStandings.mockResolvedValue(STANDINGS);
    mockGetTtStandings.mockResolvedValue(TT_STANDINGS);
    mockGetQualifyResults.mockResolvedValue(QUAL_RESULTS);
  });

  it('shows a loading state while fetching', () => {
    mockGetStandings.mockReturnValue(new Promise(() => {}));
    renderPage();
    expect(screen.getByText(/loading/i)).toBeInTheDocument();
  });

  it('fetches championship standings for the route series id by default', async () => {
    renderPage();
    await waitFor(() =>
      expect(mockGetStandings).toHaveBeenCalledWith(444, undefined, expect.any(AbortSignal))
    );
  });

  it('renders championship rows and series name', async () => {
    renderPage();
    await waitFor(() => expect(screen.getByText('GT3 Cup')).toBeInTheDocument());
    expect(screen.getByText('Leader')).toBeInTheDocument();
    expect(screen.getByText('950')).toBeInTheDocument();
  });

  it('refetches when a different car class chip is selected', async () => {
    renderPage();
    await waitFor(() =>
      expect(mockGetStandings).toHaveBeenCalledWith(444, undefined, expect.any(AbortSignal))
    );
    fireEvent.click(screen.getByRole('button', { name: 'GT4 Class' }));
    await waitFor(() =>
      expect(mockGetStandings).toHaveBeenCalledWith(444, 2000, expect.any(AbortSignal))
    );
  });

  it("highlights the logged-in driver's row and shows their division", async () => {
    mockIRacingCustomerId = 222;
    renderPage();
    await waitFor(() => expect(screen.getByText('(you)')).toBeInTheDocument());
    expect(screen.getByTestId('your-division')).toHaveTextContent('Your division: 4');
  });

  it('does not show a division badge when the user is not in the standings', async () => {
    mockIRacingCustomerId = 999;
    renderPage();
    await waitFor(() => expect(screen.getByText('Leader')).toBeInTheDocument());
    expect(screen.queryByTestId('your-division')).not.toBeInTheDocument();
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

  it('switches to the Time Trial tab and renders TT rating (with — for missing)', async () => {
    renderPage();
    await waitFor(() => expect(screen.getByText('Leader')).toBeInTheDocument());

    fireEvent.click(screen.getByRole('button', { name: 'Time Trial' }));
    await waitFor(() =>
      expect(mockGetTtStandings).toHaveBeenCalledWith(444, undefined, expect.any(AbortSignal))
    );
    expect(screen.getByText('TT Leader')).toBeInTheDocument();
    expect(screen.getByText('2,500')).toBeInTheDocument(); // tt rating
    expect(screen.getByText('No Rating')).toBeInTheDocument();
    expect(screen.getByText('TT Rating')).toBeInTheDocument(); // column header
  });

  it('switches to the Qualifying tab, formats lap times, and offers a week selector', async () => {
    renderPage();
    await waitFor(() => expect(screen.getByText('Leader')).toBeInTheDocument());

    fireEvent.click(screen.getByRole('button', { name: 'Qualifying' }));
    await waitFor(() =>
      expect(mockGetQualifyResults).toHaveBeenCalledWith(
        444,
        undefined,
        undefined,
        expect.any(AbortSignal)
      )
    );
    expect(screen.getByText('Pole Sitter')).toBeInTheDocument();
    expect(screen.getByText('6:15.000')).toBeInTheDocument(); // 375.0s formatted
    expect(screen.getByText('No Time')).toBeInTheDocument(); // sentinel row still renders

    // Week selector reflects availableRaceWeekIndices with one-based display labels and refetches on click.
    fireEvent.click(screen.getByRole('button', { name: 'Week 1' }));
    await waitFor(() =>
      expect(mockGetQualifyResults).toHaveBeenCalledWith(444, undefined, 0, expect.any(AbortSignal))
    );
  });

  it('shows an empty-state for qualifying when there are no results', async () => {
    mockGetQualifyResults.mockResolvedValue({ ...QUAL_RESULTS, results: [] });
    renderPage();
    await waitFor(() => expect(screen.getByText('Leader')).toBeInTheDocument());
    fireEvent.click(screen.getByRole('button', { name: 'Qualifying' }));
    await waitFor(() =>
      expect(screen.getByText(/no qualifying results available/i)).toBeInTheDocument()
    );
  });
});
