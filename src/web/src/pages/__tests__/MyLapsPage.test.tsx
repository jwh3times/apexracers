import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { vi, describe, it, expect, beforeEach } from 'vitest';
import MyLapsPage from '../MyLapsPage';
import { api } from '../../services/api';

vi.mock('../../services/api', () => ({
  api: { getMyLaps: vi.fn() },
}));

const mockGetMyLaps = vi.mocked(api.getMyLaps);

function renderPage(search = '') {
  return render(
    <MemoryRouter initialEntries={[`/my-laps${search}`]}>
      <MyLapsPage />
    </MemoryRouter>
  );
}

describe('MyLapsPage', () => {
  beforeEach(() => { vi.resetAllMocks(); });

  it('shows empty state when no laps recorded', async () => {
    mockGetMyLaps.mockResolvedValue([]);
    renderPage();
    await waitFor(() => expect(screen.getByText(/no laps recorded yet/i)).toBeInTheDocument());
  });

  it('renders laps table with car and track data', async () => {
    mockGetMyLaps.mockResolvedValue([
      {
        carId: 1,
        carName: 'Porsche 992 GT3',
        trackName: 'Spa-Francorchamps',
        configName: 'Full',
        bestLapSeconds: 131.456,
        lapCount: 12,
        lastRecordedAt: '2026-05-01T10:00:00Z',
      },
    ]);
    renderPage();
    await waitFor(() => {
      expect(screen.getByText('Porsche 992 GT3')).toBeInTheDocument();
      expect(screen.getByText(/spa-francorchamps/i)).toBeInTheDocument();
      expect(screen.getByText('12')).toBeInTheDocument();
    });
  });

  it('shows error when API fails', async () => {
    mockGetMyLaps.mockRejectedValue(new Error('Not found'));
    renderPage();
    await waitFor(() => expect(screen.getByText(/not found/i)).toBeInTheDocument());
  });

  it('formats track name without config suffix when configName is null', async () => {
    mockGetMyLaps.mockResolvedValue([
      {
        carId: 2,
        carName: 'Mazda MX-5',
        trackName: 'Lime Rock Park',
        configName: null,
        bestLapSeconds: 60.0,
        lapCount: 5,
        lastRecordedAt: '2026-05-10T08:00:00Z',
      },
    ]);
    renderPage();
    await waitFor(() => {
      expect(screen.getByText('Lime Rock Park')).toBeInTheDocument();
      expect(screen.queryByText(/—/)).not.toBeInTheDocument();
    });
  });

  it('best lap is determined from multiple laps correctly', async () => {
    mockGetMyLaps.mockResolvedValue([
      {
        carId: 1,
        carName: 'Slow Car',
        trackName: 'Track A',
        configName: null,
        bestLapSeconds: 150.0,
        lapCount: 3,
        lastRecordedAt: '2026-05-01T10:00:00Z',
      },
      {
        carId: 2,
        carName: 'Fast Car',
        trackName: 'Track B',
        configName: null,
        bestLapSeconds: 88.5,
        lapCount: 7,
        lastRecordedAt: '2026-05-02T10:00:00Z',
      },
      {
        carId: 3,
        carName: 'Medium Car',
        trackName: 'Track C',
        configName: null,
        bestLapSeconds: 120.0,
        lapCount: 5,
        lastRecordedAt: '2026-05-03T10:00:00Z',
      },
    ]);
    renderPage();
    // 88.5 s → 1:28.500 — verify it appears in the Best Recorded Time stat card
    await waitFor(() => {
      const label = screen.getByText('Best Recorded Time');
      const card = label.closest('div[class*="glass-panel"]');
      expect(card).not.toBeNull();
      expect(card).toHaveTextContent('1:28.500');
    });
  });

  it('filters laps by car name search', async () => {
    const user = userEvent.setup();
    mockGetMyLaps.mockResolvedValue([
      {
        carId: 1,
        carName: 'Porsche 992 GT3',
        trackName: 'Spa-Francorchamps',
        configName: 'Full',
        bestLapSeconds: 131.456,
        lapCount: 12,
        lastRecordedAt: '2026-05-01T10:00:00Z',
      },
      {
        carId: 2,
        carName: 'Mazda MX-5',
        trackName: 'Lime Rock Park',
        configName: null,
        bestLapSeconds: 60.0,
        lapCount: 5,
        lastRecordedAt: '2026-05-10T08:00:00Z',
      },
    ]);
    renderPage();
    await waitFor(() => expect(screen.getByText('Porsche 992 GT3')).toBeInTheDocument());

    await user.type(screen.getByPlaceholderText(/search/i), 'Mazda');

    await waitFor(() => {
      expect(screen.queryByText('Porsche 992 GT3')).not.toBeInTheDocument();
      expect(screen.getByText('Mazda MX-5')).toBeInTheDocument();
    });
  });

  it('search filter counts singular record correctly', async () => {
    const user = userEvent.setup();
    mockGetMyLaps.mockResolvedValue([
      {
        carId: 1,
        carName: 'Porsche 992 GT3',
        trackName: 'Spa-Francorchamps',
        configName: 'Full',
        bestLapSeconds: 131.456,
        lapCount: 12,
        lastRecordedAt: '2026-05-01T10:00:00Z',
      },
      {
        carId: 2,
        carName: 'Mazda MX-5',
        trackName: 'Lime Rock Park',
        configName: null,
        bestLapSeconds: 60.0,
        lapCount: 5,
        lastRecordedAt: '2026-05-10T08:00:00Z',
      },
    ]);
    renderPage();
    await waitFor(() => expect(screen.getByText('Porsche 992 GT3')).toBeInTheDocument());

    await user.type(screen.getByPlaceholderText(/search/i), 'Mazda');

    await waitFor(() => {
      expect(screen.getByText('1 record')).toBeInTheDocument();
      expect(screen.queryByText('1 records')).not.toBeInTheDocument();
    });
  });

  it('shows fallback error message for non-Error rejection', async () => {
    mockGetMyLaps.mockRejectedValue('oops');
    renderPage();
    await waitFor(() => expect(screen.getByText('Failed to load laps.')).toBeInTheDocument());
  });
});
