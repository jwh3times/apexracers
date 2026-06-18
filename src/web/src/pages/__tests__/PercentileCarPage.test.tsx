import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { vi, describe, it, expect, beforeEach } from 'vitest';
import PercentileCarPage from '../PercentileCarPage';
import { api } from '../../services/api';
import type { PercentileResult } from '../../services/api';

vi.mock('../../services/api', () => ({
  api: { getPercentile: vi.fn() },
}));

// Auth mock — iRacingCustomerId drives which code path the page takes
let mockIRacingCustomerId: number | null = null;

vi.mock('../../context/AuthContext', () => ({
  useAuth: () => ({
    user: { iRacingCustomerId: mockIRacingCustomerId },
  }),
}));

const mockGetPercentile = vi.mocked(api.getPercentile);

function makeBin(i: number, containsUser = false) {
  return { minSeconds: 120 + i, maxSeconds: 121 + i, count: 5, containsUser };
}

const MOCK_RESULT: PercentileResult = {
  seriesId: 9001,
  weekNumber: 1,
  carId: 9001,
  customerId: 100001,
  percentileRank: 73.4,
  sampleSize: 500,
  computedAt: '2026-05-11T12:00:00Z',
  seriesName: 'VRS GT3 Sprint',
  trackName: 'Spa-Francorchamps',
  trackConfigName: 'Full',
  yourBestLapSeconds: 132.5,
  fieldBestLapSeconds: 130.0,
  fieldMedianLapSeconds: 136.0,
  distribution: Array.from({ length: 20 }, (_, i) => makeBin(i, i === 10)),
  worldRecordLapSeconds: null,
  worldRecordGapSeconds: null,
};

function renderPage(
  options: {
    carId?: string;
    state?: { carName?: string };
  } = {}
) {
  const carId = options.carId ?? '9001';
  return render(
    <MemoryRouter
      initialEntries={[
        {
          pathname: `/series/9001/weeks/1/cars/${carId}/percentile`,
          state: options.state,
        },
      ]}
    >
      <Routes>
        <Route
          path="/series/:seriesId/weeks/:weekNumber/cars/:carId/percentile"
          element={<PercentileCarPage />}
        />
      </Routes>
    </MemoryRouter>
  );
}

describe('PercentileCarPage', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    mockIRacingCustomerId = null;
  });

  // ── Header / name ─────────────────────────────────────────────────────────

  it('shows car name from route state', () => {
    renderPage({ state: { carName: 'Porsche 911 GT3 R (992)' } });
    expect(screen.getByText('Porsche 911 GT3 R (992)')).toBeInTheDocument();
  });

  it('falls back to "Car <id>" when no route state', () => {
    renderPage({ carId: '9001' });
    expect(screen.getByText('Car 9001')).toBeInTheDocument();
  });

  // ── Auto-fetch when profile has iRacingCustomerId ─────────────────────────

  it('auto-fetches on mount when iRacingCustomerId is in auth context', async () => {
    mockIRacingCustomerId = 100001;
    mockGetPercentile.mockResolvedValue(MOCK_RESULT);

    renderPage();

    await waitFor(() =>
      expect(mockGetPercentile).toHaveBeenCalledWith(9001, 1, 9001, 100001, expect.any(Object))
    );
  });

  it('shows result without the manual form when iRacingCustomerId is set', async () => {
    mockIRacingCustomerId = 100001;
    mockGetPercentile.mockResolvedValue(MOCK_RESULT);

    renderPage();

    await waitFor(() => expect(screen.getByText(/you're faster than/i)).toBeInTheDocument());
    expect(screen.queryByLabelText(/iRacing Customer ID/i)).not.toBeInTheDocument();
  });

  it('shows not-found message on 404 during auto-fetch', async () => {
    mockIRacingCustomerId = 100001;
    mockGetPercentile.mockRejectedValue(new Error('GET ... → 404 Not Found'));

    renderPage({ state: { carName: 'Porsche GT3' } });

    await waitFor(() => expect(screen.getByText(/no race lap found/i)).toBeInTheDocument());
  });

  // ── Manual form (no profile ID) ───────────────────────────────────────────

  it('shows the manual form and profile link when no iRacingCustomerId', () => {
    renderPage();
    expect(screen.getByLabelText(/iRacing Customer ID/i)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /profile/i })).toBeInTheDocument();
  });

  it('submit button is disabled when input is empty', () => {
    renderPage();
    expect(screen.getByRole('button', { name: /look up my percentile/i })).toBeDisabled();
  });

  it('calls getPercentile with correct ids on manual submit', async () => {
    mockGetPercentile.mockResolvedValue(MOCK_RESULT);
    renderPage();

    fireEvent.change(screen.getByLabelText(/iRacing Customer ID/i), {
      target: { value: '100001' },
    });
    fireEvent.click(screen.getByRole('button', { name: /look up my percentile/i }));

    await waitFor(() =>
      expect(mockGetPercentile).toHaveBeenCalledWith(9001, 1, 9001, 100001, expect.any(Object))
    );
  });

  it('shows percentile result after manual submit', async () => {
    mockGetPercentile.mockResolvedValue(MOCK_RESULT);
    renderPage();

    fireEvent.change(screen.getByLabelText(/iRacing Customer ID/i), {
      target: { value: '100001' },
    });
    fireEvent.click(screen.getByRole('button', { name: /look up my percentile/i }));

    await waitFor(() => expect(screen.getByText(/you're faster than/i)).toBeInTheDocument());
    expect(screen.getAllByText(/73\.4%/).length).toBeGreaterThan(0);
    expect(screen.getAllByText(/500/).length).toBeGreaterThan(0);
  });

  it('shows stat grid with your best and field best lap times', async () => {
    mockGetPercentile.mockResolvedValue(MOCK_RESULT);
    mockIRacingCustomerId = 100001;
    renderPage();

    await waitFor(() => {
      expect(screen.getByText('Your best')).toBeInTheDocument();
      expect(screen.getByText('Field best')).toBeInTheDocument();
      expect(screen.getByText('Field median')).toBeInTheDocument();
      expect(screen.getByText('Gap to P1')).toBeInTheDocument();
    });
  });

  it('shows distribution chart label', async () => {
    mockGetPercentile.mockResolvedValue(MOCK_RESULT);
    mockIRacingCustomerId = 100001;
    renderPage();

    await waitFor(() => expect(screen.getByText(/lap time distribution/i)).toBeInTheDocument());
  });

  it('shows world-record stats when a WR is available', async () => {
    mockGetPercentile.mockResolvedValue({
      ...MOCK_RESULT,
      worldRecordLapSeconds: 129.0,
      worldRecordGapSeconds: 3.5,
    });
    mockIRacingCustomerId = 100001;
    renderPage();

    await waitFor(() => expect(screen.getByText('World record')).toBeInTheDocument());
    expect(screen.getByText('Gap to WR')).toBeInTheDocument();
    expect(screen.getByText('+3.500')).toBeInTheDocument();
  });

  it('omits world-record stats when no WR is available', async () => {
    mockGetPercentile.mockResolvedValue(MOCK_RESULT); // WR fields null
    mockIRacingCustomerId = 100001;
    renderPage();

    await waitFor(() => expect(screen.getByText('Field best')).toBeInTheDocument());
    expect(screen.queryByText('World record')).not.toBeInTheDocument();
  });

  it('shows not-found message on 404 after manual submit', async () => {
    mockGetPercentile.mockRejectedValue(new Error('GET /api/... → 404 Not Found'));
    renderPage();

    fireEvent.change(screen.getByLabelText(/iRacing Customer ID/i), {
      target: { value: '999999' },
    });
    fireEvent.click(screen.getByRole('button', { name: /look up my percentile/i }));

    await waitFor(() => expect(screen.getByText(/no race lap found/i)).toBeInTheDocument());
  });

  it('shows error message on non-404 failure', async () => {
    mockGetPercentile.mockRejectedValue(new Error('Service unavailable'));
    renderPage();

    fireEvent.change(screen.getByLabelText(/iRacing Customer ID/i), {
      target: { value: '100001' },
    });
    fireEvent.click(screen.getByRole('button', { name: /look up my percentile/i }));

    await waitFor(() => expect(screen.getByText(/service unavailable/i)).toBeInTheDocument());
  });

  // ── CalculationSource ─────────────────────────────────────────────────────

  it('shows CalculationSource pace controls when profileId is set', async () => {
    mockIRacingCustomerId = 100001;
    mockGetPercentile.mockResolvedValue(MOCK_RESULT);
    renderPage();
    await waitFor(() => expect(screen.getByRole('radiogroup')).toBeInTheDocument());
  });

  it('switching to blend mode re-fetches with includePersonalLaps true', async () => {
    mockIRacingCustomerId = 100001;
    mockGetPercentile.mockResolvedValue(MOCK_RESULT);
    renderPage();
    await waitFor(() => expect(mockGetPercentile).toHaveBeenCalledTimes(1));

    fireEvent.click(screen.getByRole('radio', { name: /official \+ my uploaded laps/i }));

    await waitFor(() =>
      expect(mockGetPercentile).toHaveBeenCalledWith(
        9001,
        1,
        9001,
        100001,
        expect.objectContaining({ includePersonalLaps: true })
      )
    );
  });
});
