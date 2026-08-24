import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Routes, Route } from 'react-router';
import { vi, describe, it, expect, beforeEach } from 'vitest';
import SchedulePage from './SchedulePage';
import { api, type SeasonSchedule } from '../../services/api';

vi.mock('../../services/api', async importOriginal => {
  const { mockApiModule } = await import('../../test/apiMock');
  return mockApiModule(importOriginal);
});

const mockGetSchedule = vi.mocked(api.getSchedule);

const SCHEDULE: SeasonSchedule = {
  seriesId: 444,
  seriesName: 'GT3 Cup',
  weeks: [
    {
      weekNumber: 1,
      trackName: 'Thruxton',
      configName: 'Club',
      startDate: '2020-01-01', // safely in the past → "This Week"
      weather: {
        tempHighC: 28.9,
        tempLowC: 28.8,
        precipChancePct: 0,
        windHighKph: 20.2,
        windLowKph: 17.3,
        skies: 1,
      },
      bop: [
        {
          carId: 132,
          carName: 'BMW M4 GT3',
          weightPenaltyKg: 10,
          powerAdjustPct: -1.5,
          maxPctFuelFill: 50,
          maxDryTireSets: 0,
        },
      ],
      hasUploadedLapAtTrack: true,
    },
    {
      weekNumber: 2,
      trackName: 'Laguna Seca',
      configName: '',
      startDate: '2999-01-01', // future → "Next Week"
      weather: null,
      bop: [],
      hasUploadedLapAtTrack: false,
    },
  ],
};

function renderPage() {
  return render(
    <MemoryRouter initialEntries={['/series/444/schedule']}>
      <Routes>
        <Route path="/series/:seriesId/schedule" element={<SchedulePage />} />
      </Routes>
    </MemoryRouter>
  );
}

describe('SchedulePage', () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  it('shows a loading state while fetching', () => {
    mockGetSchedule.mockReturnValue(new Promise(() => {}));
    renderPage();
    expect(screen.getByText(/loading/i)).toBeInTheDocument();
  });

  it('fetches the schedule for the route series id', async () => {
    mockGetSchedule.mockResolvedValue(SCHEDULE);
    renderPage();
    await waitFor(() => expect(mockGetSchedule).toHaveBeenCalledWith(444, expect.any(AbortSignal)));
  });

  it('renders a card per week with track and series names', async () => {
    mockGetSchedule.mockResolvedValue(SCHEDULE);
    renderPage();
    await waitFor(() => expect(screen.getByText('GT3 Cup')).toBeInTheDocument());
    expect(screen.getByText(/Thruxton/)).toBeInTheDocument();
    expect(screen.getByText('Laguna Seca')).toBeInTheDocument();
  });

  it('renders weather chips for weeks with a forecast and a fallback otherwise', async () => {
    mockGetSchedule.mockResolvedValue(SCHEDULE);
    renderPage();
    await waitFor(() => expect(screen.getByText('Partly Cloudy')).toBeInTheDocument()); // skies index 1
    expect(screen.getByText(/no forecast yet/i)).toBeInTheDocument(); // week 2
  });

  it('renders the BoP table for a week with restrictions', async () => {
    mockGetSchedule.mockResolvedValue(SCHEDULE);
    renderPage();
    await waitFor(() => expect(screen.getByText('BMW M4 GT3')).toBeInTheDocument());
    expect(screen.getByText('+10kg')).toBeInTheDocument();
    expect(screen.getByText('-1.5%')).toBeInTheDocument();
  });

  it('labels uploaded track history without claiming it is a personal best', async () => {
    mockGetSchedule.mockResolvedValue({
      seriesId: 444,
      seriesName: 'GT3 Cup',
      weeks: [
        {
          weekNumber: 1,
          trackName: 'Thruxton',
          configName: '',
          startDate: '2999-01-01',
          weather: null,
          bop: [],
          hasUploadedLapAtTrack: true,
        },
      ],
    });
    renderPage();
    await screen.findByRole('heading', { name: 'Thruxton' });
    expect(screen.getByText('Uploaded lap here')).toBeInTheDocument();
    expect(screen.queryByText('Your PB here')).not.toBeInTheDocument();
  });

  it('tags this/next week', async () => {
    mockGetSchedule.mockResolvedValue(SCHEDULE);
    renderPage();
    await waitFor(() => expect(screen.getByText('This Week')).toBeInTheDocument());
    expect(screen.getByText('This Week')).toBeInTheDocument();
    expect(screen.getByText('Next Week')).toBeInTheDocument();
  });

  it('shows an empty-state message when there is no schedule', async () => {
    mockGetSchedule.mockResolvedValue({ ...SCHEDULE, weeks: [] });
    renderPage();
    await waitFor(() => expect(screen.getByText(/no schedule available/i)).toBeInTheDocument());
  });

  it('shows an error message when the API fails', async () => {
    mockGetSchedule.mockRejectedValue(new Error('Boom'));
    renderPage();
    await waitFor(() => expect(screen.getByText(/boom/i)).toBeInTheDocument());
  });
});
