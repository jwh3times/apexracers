import { render, screen, waitFor, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router';
import { vi, describe, it, expect, beforeEach } from 'vitest';
import NotificationsBell from './NotificationsBell';
import { api, type RaceGuideEntry } from '../services/api';

let mockAlertsEnabled = true;
vi.mock('../context/AuthContext', () => ({
  useAuth: () => ({ alertsEnabled: mockAlertsEnabled }),
}));

let mockLiveFlag = true;
let mockDemoFlag = false;
vi.mock('../context/FeatureFlagContext', () => ({
  useFeatureFlag: (key: string) => (key === 'iracing-demo' ? mockDemoFlag : mockLiveFlag),
}));

vi.mock('../services/api', () => ({
  api: { getRaceGuide: vi.fn(), getMyAnalytics: vi.fn() },
}));

const mockGetRaceGuide = vi.mocked(api.getRaceGuide);
const mockGetMyAnalytics = vi.mocked(api.getMyAnalytics);

function soonRace(): RaceGuideEntry {
  return {
    seriesId: 1,
    seriesName: 'GT3 Challenge',
    startTime: new Date(Date.now() + 10 * 60000).toISOString(), // 10 min out
    endTime: new Date(Date.now() + 45 * 60000).toISOString(),
    entryCount: 20,
    raceWeekNum: 3,
  };
}

function renderBell() {
  return render(
    <MemoryRouter>
      <NotificationsBell />
    </MemoryRouter>
  );
}

describe('NotificationsBell', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    mockAlertsEnabled = true;
    mockLiveFlag = true;
    mockDemoFlag = false;
    mockGetRaceGuide.mockResolvedValue([]);
    mockGetMyAnalytics.mockResolvedValue([]);
  });

  it('shows a count badge and lists alerts when alerts are enabled', async () => {
    mockGetRaceGuide.mockResolvedValue([soonRace()]);
    renderBell();
    await waitFor(() => expect(screen.getByText('1')).toBeInTheDocument()); // badge count
    fireEvent.click(screen.getByRole('button', { name: /notifications/i }));
    expect(screen.getByText(/GT3 Challenge starts in/i)).toBeInTheDocument();
  });

  it('shows an empty state in the dropdown when there are no alerts', async () => {
    renderBell();
    await waitFor(() => expect(mockGetRaceGuide).toHaveBeenCalled());
    expect(screen.queryByText('1')).not.toBeInTheDocument(); // no badge
    fireEvent.click(screen.getByRole('button', { name: /notifications/i }));
    expect(screen.getByText(/no new notifications/i)).toBeInTheDocument();
  });

  it('is dormant and links to Settings when alerts are disabled', async () => {
    mockAlertsEnabled = false;
    renderBell();
    fireEvent.click(screen.getByRole('button', { name: /notifications/i }));
    expect(screen.getByText(/notifications are off/i)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /enable in settings/i })).toHaveAttribute(
      'href',
      '/settings'
    );
    expect(mockGetRaceGuide).not.toHaveBeenCalled();
  });

  it('toggles the dropdown open and closed', async () => {
    renderBell();
    await waitFor(() => expect(mockGetRaceGuide).toHaveBeenCalled());
    const bell = screen.getByRole('button', { name: /notifications/i });
    fireEvent.click(bell);
    expect(screen.getByText('Notifications')).toBeInTheDocument();
    fireEvent.click(bell);
    expect(screen.queryByText('Notifications')).not.toBeInTheDocument();
  });

  it('skips iRacing fetches and shows no badge when iracing-live flag is off', async () => {
    mockLiveFlag = false;
    renderBell();
    // Yield so any erroneously-triggered effect would have time to run
    await new Promise(r => setTimeout(r, 0));
    expect(mockGetRaceGuide).not.toHaveBeenCalled();
    expect(mockGetMyAnalytics).not.toHaveBeenCalled();
    expect(screen.queryByText('1')).not.toBeInTheDocument(); // no badge
  });

  it('fetches and shows a badge when iracing-demo is on and iracing-live is off', async () => {
    mockLiveFlag = false;
    mockDemoFlag = true;
    mockGetRaceGuide.mockResolvedValue([soonRace()]);
    renderBell();
    await waitFor(() => expect(mockGetRaceGuide).toHaveBeenCalled());
    expect(mockGetMyAnalytics).toHaveBeenCalled();
    // Badge should appear (1 race-starting-soon alert)
    await waitFor(() => expect(screen.getByText('1')).toBeInTheDocument());
  });
});
