import { render, screen, waitFor, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { vi, describe, it, expect, beforeEach } from 'vitest';
import RacesPage from './RacesPage';
import { api, IRacingNotLinkedError, type RaceHistoryRow } from '../../services/api';

vi.mock('../../services/api', () => {
  class IRacingNotLinkedError extends Error {
    code = 'IRACING_NOT_LINKED';
    constructor(message: string) {
      super(message);
      this.name = 'IRacingNotLinkedError';
    }
  }
  return {
    api: { getRaceHistory: vi.fn() },
    IRacingNotLinkedError,
  };
});

const mockGetRaceHistory = vi.mocked(api.getRaceHistory);

const ROWS: RaceHistoryRow[] = [
  {
    subsessionId: 200,
    startTime: '2026-05-29T17:15:00Z',
    seriesName: 'GT4 Challenge',
    trackName: 'Laguna Seca',
    carId: 119,
    carName: 'Porsche 718 GT4',
    startPosition: 20,
    finishPosition: 11,
    incidents: 7,
    iRatingDelta: 28,
    srDelta: -0.14,
    strengthOfField: 1833,
    points: 73,
  },
  {
    subsessionId: 100,
    startTime: '2026-05-01T10:00:00Z',
    seriesName: 'GT3 Challenge',
    trackName: 'Thruxton',
    carId: 132,
    carName: 'BMW M4 GT3 EVO',
    startPosition: 11,
    finishPosition: 3,
    incidents: 5,
    iRatingDelta: -53,
    srDelta: 0.01,
    strengthOfField: 2502,
    points: 13,
  },
];

function renderPage() {
  return render(
    <MemoryRouter>
      <RacesPage />
    </MemoryRouter>
  );
}

describe('RacesPage', () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  it('shows a loading state while fetching', () => {
    mockGetRaceHistory.mockReturnValue(new Promise(() => {}));
    renderPage();
    expect(screen.getByText(/loading/i)).toBeInTheDocument();
  });

  it('renders a row per race with track and car (series names are unique per row)', async () => {
    mockGetRaceHistory.mockResolvedValue(ROWS);
    renderPage();
    // Track and car names appear only in the table (series names also appear as filter chips).
    await waitFor(() => expect(screen.getByText('Laguna Seca')).toBeInTheDocument());
    expect(screen.getByText('Thruxton')).toBeInTheDocument();
    expect(screen.getByText('BMW M4 GT3 EVO')).toBeInTheDocument();
    expect(screen.getByText('Porsche 718 GT4')).toBeInTheDocument();
    // Each series renders both as a filter chip and a table cell.
    expect(screen.getAllByText('GT3 Challenge').length).toBeGreaterThanOrEqual(2);
  });

  it('colors a positive iRating delta cyan and a negative one red', async () => {
    mockGetRaceHistory.mockResolvedValue(ROWS);
    renderPage();
    await waitFor(() => expect(screen.getByText('+28')).toBeInTheDocument());
    expect(screen.getByText('+28')).toHaveClass('text-primary-container');
    expect(screen.getByText('-53')).toHaveClass('text-red-400');
  });

  it('formats the SR delta with sign and two decimals', async () => {
    mockGetRaceHistory.mockResolvedValue(ROWS);
    renderPage();
    await waitFor(() => expect(screen.getByText('-0.14')).toBeInTheDocument());
    expect(screen.getByText('+0.01')).toBeInTheDocument();
  });

  it('filters rows by series when a chip is selected', async () => {
    mockGetRaceHistory.mockResolvedValue(ROWS);
    renderPage();
    await waitFor(() => expect(screen.getByText('Laguna Seca')).toBeInTheDocument());

    // Click the GT3 Challenge chip (button, not the table cell).
    fireEvent.click(screen.getByRole('button', { name: 'GT3 Challenge' }));

    expect(screen.getByText('Thruxton')).toBeInTheDocument();
    expect(screen.queryByText('Laguna Seca')).not.toBeInTheDocument();
  });

  it('links each row to its race detail page', async () => {
    mockGetRaceHistory.mockResolvedValue(ROWS);
    renderPage();
    await waitFor(() =>
      expect(screen.getByRole('link', { name: /view race 100/i })).toHaveAttribute(
        'href',
        '/races/100'
      )
    );
  });

  it('shows the link-iRacing prompt when not linked', async () => {
    mockGetRaceHistory.mockRejectedValue(new IRacingNotLinkedError('nope'));
    renderPage();
    await waitFor(() =>
      expect(screen.getByRole('link', { name: /settings/i })).toHaveAttribute('href', '/settings')
    );
  });

  it('shows an error message when the API fails', async () => {
    mockGetRaceHistory.mockRejectedValue(new Error('Boom'));
    renderPage();
    await waitFor(() => expect(screen.getByText(/boom/i)).toBeInTheDocument());
  });

  it('shows an empty-state message when there are no races', async () => {
    mockGetRaceHistory.mockResolvedValue([]);
    renderPage();
    await waitFor(() => expect(screen.getByText(/no recent races found/i)).toBeInTheDocument());
  });
});
