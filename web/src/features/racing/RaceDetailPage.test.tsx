import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Routes, Route } from 'react-router-dom';
import { vi, describe, it, expect, beforeEach } from 'vitest';
import RaceDetailPage from './RaceDetailPage';
import { api, type DriverLaps, type SubsessionDetail } from '../../services/api';
import type { User } from '../../context/AuthContext';

let mockUser: User | null = null;

vi.mock('../../context/AuthContext', () => ({
  useAuth: () => ({ user: mockUser }),
}));

vi.mock('../../services/api', () => ({
  api: { getSubsession: vi.fn(), getDriverLaps: vi.fn() },
}));

const mockGetSubsession = vi.mocked(api.getSubsession);
const mockGetDriverLaps = vi.mocked(api.getDriverLaps);

const EMPTY_LAPS: DriverLaps = {
  subsessionId: 100,
  custId: 0,
  meanSeconds: 0,
  stdDevSeconds: 0,
  fastestLapSeconds: 0,
  degSlopeSecondsPerLap: 0,
  laps: [],
};

const DETAIL: SubsessionDetail = {
  subsessionId: 100,
  startTime: '2026-05-29T17:15:00Z',
  seriesName: 'GT3 Cup',
  trackName: 'Thruxton',
  trackConfigName: 'Club',
  strengthOfField: 2509,
  numCautions: 1,
  numLeadChanges: 2,
  cornersPerLap: 12,
  eventBestLapSeconds: 66.295,
  eventAverageLapSeconds: 66.89,
  eventLapsComplete: 18,
  weather: { tempCelsius: 19, relHumidity: 45, windKph: 18, skies: 1, precipChance: 0 },
  results: [
    {
      custId: 111,
      driverName: 'Winner',
      finishPosition: 1,
      startPosition: 1,
      bestLapSeconds: 66.295,
      averageLapSeconds: 66.8,
      interval: 0,
      lapsLead: 18,
      incidents: 0,
      division: 1,
      iRatingDelta: 50,
      srDelta: 0.15,
    },
    {
      custId: 222,
      driverName: 'Me Driver',
      finishPosition: 3,
      startPosition: 5,
      bestLapSeconds: 67.1,
      averageLapSeconds: 67.5,
      interval: 1.2,
      lapsLead: 0,
      incidents: 4,
      division: 2,
      iRatingDelta: -53,
      srDelta: 0.01,
    },
  ],
};

function renderPage() {
  return render(
    <MemoryRouter initialEntries={['/races/100']}>
      <Routes>
        <Route path="/races/:subsessionId" element={<RaceDetailPage />} />
      </Routes>
    </MemoryRouter>
  );
}

describe('RaceDetailPage', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    mockUser = null;
    mockGetDriverLaps.mockResolvedValue(EMPTY_LAPS);
  });

  it('shows a loading state while fetching', () => {
    mockGetSubsession.mockReturnValue(new Promise(() => {}));
    renderPage();
    expect(screen.getByText(/loading/i)).toBeInTheDocument();
  });

  it('fetches the subsession from the route param', async () => {
    mockGetSubsession.mockResolvedValue(DETAIL);
    renderPage();
    await waitFor(() => expect(mockGetSubsession).toHaveBeenCalledWith(100));
  });

  it('renders the header KPIs', async () => {
    mockGetSubsession.mockResolvedValue(DETAIL);
    renderPage();
    await waitFor(() =>
      expect(screen.getByRole('heading', { level: 1, name: /Thruxton/ })).toBeInTheDocument()
    );
    expect(screen.getByText('GT3 Cup')).toBeInTheDocument();
    expect(screen.getByText('2,509')).toBeInTheDocument(); // SOF
    // Fastest lap (1:06.295) shows in both the KPI and the winner's best-lap cell.
    expect(screen.getAllByText('1:06.295').length).toBeGreaterThanOrEqual(1);
  });

  it('renders weather KPIs when weather is present', async () => {
    mockGetSubsession.mockResolvedValue(DETAIL);
    renderPage();
    await waitFor(() => expect(screen.getByText('19°C')).toBeInTheDocument());
    expect(screen.getByText('Partly Cloudy')).toBeInTheDocument(); // skies index 1
  });

  it('renders classified rows and colors iRating deltas', async () => {
    mockGetSubsession.mockResolvedValue(DETAIL);
    renderPage();
    await waitFor(() => expect(screen.getByText('Winner')).toBeInTheDocument());
    expect(screen.getByText('+50')).toHaveClass('text-primary-container');
    expect(screen.getByText('-53')).toHaveClass('text-red-400');
  });

  it("highlights the logged-in user's row", async () => {
    mockUser = {
      token: 't',
      userId: 'u1',
      displayName: 'Me',
      email: 'm@m.com',
      iRacingCustomerId: 222,
      role: 'Standard',
    };
    mockGetSubsession.mockResolvedValue(DETAIL);
    renderPage();
    await waitFor(() => expect(screen.getByText('(you)')).toBeInTheDocument());
  });

  it('shows an error message when the API fails', async () => {
    mockGetSubsession.mockRejectedValue(new Error('Boom'));
    renderPage();
    await waitFor(() => expect(screen.getByText(/boom/i)).toBeInTheDocument());
  });

  it('shows an empty-results message when the field is empty', async () => {
    mockGetSubsession.mockResolvedValue({ ...DETAIL, results: [], weather: null });
    renderPage();
    await waitFor(() => expect(screen.getByText(/no classified results/i)).toBeInTheDocument());
  });

  // ── Your Race Pace card (1.4) ─────────────────────────────────────────────

  it('renders the Your Race Pace card with a lap trace for the logged-in driver', async () => {
    mockUser = {
      token: 't',
      userId: 'u1',
      displayName: 'Winner',
      email: 'w@w.com',
      iRacingCustomerId: 111, // Winner is in DETAIL.results
      role: 'Standard',
    };
    mockGetSubsession.mockResolvedValue(DETAIL);
    mockGetDriverLaps.mockResolvedValue({
      subsessionId: 100,
      custId: 111,
      meanSeconds: 66.5,
      stdDevSeconds: 0.3,
      fastestLapSeconds: 66.295,
      degSlopeSecondsPerLap: 0.05,
      laps: [
        { lapNumber: 1, lapTimeSeconds: 66.3, incident: false, valid: true },
        { lapNumber: 2, lapTimeSeconds: 66.7, incident: false, valid: true },
      ],
    });
    renderPage();

    await waitFor(() => expect(screen.getByText(/your race pace/i)).toBeInTheDocument());
    expect(mockGetDriverLaps).toHaveBeenCalledWith(100, 111);
    expect(screen.getByRole('img', { name: /lap time trace/i })).toBeInTheDocument();
  });

  it('does not request laps or show the pace card when the user did not race', async () => {
    mockUser = {
      token: 't',
      userId: 'u1',
      displayName: 'Bystander',
      email: 'b@b.com',
      iRacingCustomerId: 999, // not among DETAIL.results
      role: 'Standard',
    };
    mockGetSubsession.mockResolvedValue(DETAIL);
    renderPage();

    await waitFor(() => expect(screen.getByText('Winner')).toBeInTheDocument());
    expect(screen.queryByText(/your race pace/i)).not.toBeInTheDocument();
    expect(mockGetDriverLaps).not.toHaveBeenCalled();
  });
});
