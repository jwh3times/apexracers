import { render, screen, waitFor } from '@testing-library/react';
import { vi, describe, it, expect, beforeEach, afterEach } from 'vitest';
import LivePage from './LivePage';
import { api, type RaceGuideEntry } from '../../services/api';

vi.mock('../../services/api', () => ({
  api: { getRaceGuide: vi.fn() },
}));

const mockGetRaceGuide = vi.mocked(api.getRaceGuide);

// Build rows relative to "now" so live/countdown branches are deterministic.
function rows(): RaceGuideEntry[] {
  const now = Date.now();
  return [
    {
      seriesId: 999,
      seriesName: 'Endurance Now',
      startTime: new Date(now - 10 * 60_000).toISOString(), // started 10m ago
      endTime: new Date(now + 20 * 60_000).toISOString(), // ends in 20m → LIVE
      entryCount: 53,
      raceWeekNum: 5,
    },
    {
      seriesId: 231,
      seriesName: 'GT Sprint',
      startTime: new Date(now + 30 * 60_000).toISOString(), // starts in 30m
      endTime: new Date(now + 60 * 60_000).toISOString(),
      entryCount: 79,
      raceWeekNum: 11,
    },
  ];
}

describe('LivePage', () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });
  afterEach(() => {
    vi.useRealTimers();
  });

  it('shows a loading state while fetching', () => {
    mockGetRaceGuide.mockReturnValue(new Promise(() => {}));
    render(<LivePage />);
    expect(screen.getByText(/loading/i)).toBeInTheDocument();
  });

  it('renders a row per upcoming session', async () => {
    mockGetRaceGuide.mockResolvedValue(rows());
    render(<LivePage />);
    await waitFor(() => expect(screen.getByText('Endurance Now')).toBeInTheDocument());
    expect(screen.getByText('GT Sprint')).toBeInTheDocument();
    expect(screen.getByText('79')).toBeInTheDocument();
  });

  it('marks an in-progress session Live and shows a countdown for upcoming ones', async () => {
    mockGetRaceGuide.mockResolvedValue(rows());
    render(<LivePage />);
    await waitFor(() => expect(screen.getByText('Live')).toBeInTheDocument());
    // The +30m session shows an "in …" countdown.
    expect(screen.getByText(/^in \d/)).toBeInTheDocument();
  });

  it('formats multi-hour countdowns', async () => {
    const now = Date.now();
    mockGetRaceGuide.mockResolvedValue([
      {
        seriesId: 1,
        seriesName: 'Long Wait',
        startTime: new Date(now + 95 * 60_000).toISOString(), // 1h 35m out
        endTime: new Date(now + 120 * 60_000).toISOString(),
        entryCount: 10,
        raceWeekNum: 1,
      },
    ]);
    render(<LivePage />);
    await waitFor(() => expect(screen.getByText(/in 1h/)).toBeInTheDocument());
  });

  it('hides the bogus start time for a perpetually-live (stale) session but still shows Live', async () => {
    mockGetRaceGuide.mockResolvedValue([
      {
        seriesId: 42,
        seriesName: 'Demo Sentinel',
        startTime: '2020-01-01T00:00:00Z', // fixed 2020→2099 window (demo race-guide sentinel)
        endTime: '2099-01-01T00:00:00Z',
        entryCount: 30,
        raceWeekNum: 3,
      },
    ]);
    render(<LivePage />);
    await waitFor(() => expect(screen.getByText('Live')).toBeInTheDocument());
    expect(screen.getByText('—')).toBeInTheDocument();
  });

  it('still renders a formatted start time and countdown for a session starting soon', async () => {
    const now = Date.now();
    mockGetRaceGuide.mockResolvedValue([
      {
        seriesId: 7,
        seriesName: 'Sprint Soon',
        startTime: new Date(now + 10 * 60_000).toISOString(),
        endTime: new Date(now + 40 * 60_000).toISOString(),
        entryCount: 12,
        raceWeekNum: 2,
      },
    ]);
    render(<LivePage />);
    await waitFor(() => expect(screen.getByText(/^in \d/)).toBeInTheDocument());
    expect(screen.queryByText('—')).not.toBeInTheDocument();
  });

  it('shows an empty-state message when nothing is starting soon', async () => {
    mockGetRaceGuide.mockResolvedValue([]);
    render(<LivePage />);
    await waitFor(() => expect(screen.getByText(/no official sessions/i)).toBeInTheDocument());
  });

  it('shows an error message when the API fails', async () => {
    mockGetRaceGuide.mockRejectedValue(new Error('Boom'));
    render(<LivePage />);
    await waitFor(() => expect(screen.getByText(/boom/i)).toBeInTheDocument());
  });
});
