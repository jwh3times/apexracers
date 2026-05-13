import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { vi, describe, it, expect, beforeEach } from 'vitest';
import PercentileCarPage from '../PercentileCarPage';
import { api } from '../../services/api';

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

const MOCK_RESULT = {
  seriesId: 9001,
  weekId: 1,
  carId: 9001,
  customerId: 100001,
  percentileRank: 73.4,
  sampleSize: 500,
  computedAt: '2026-05-11T12:00:00Z',
};

function renderPage(options: {
  carId?: string;
  state?: { carName?: string };
} = {}) {
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
          path="/series/:seriesId/weeks/:weekId/cars/:carId/percentile"
          element={<PercentileCarPage />}
        />
      </Routes>
    </MemoryRouter>,
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
      expect(mockGetPercentile).toHaveBeenCalledWith(9001, 1, 9001, 100001),
    );
  });

  it('shows result without the manual form when iRacingCustomerId is set', async () => {
    mockIRacingCustomerId = 100001;
    mockGetPercentile.mockResolvedValue(MOCK_RESULT);

    renderPage();

    await waitFor(() =>
      expect(screen.getByText(/you beat/i)).toBeInTheDocument(),
    );
    expect(screen.queryByLabelText(/iRacing Customer ID/i)).not.toBeInTheDocument();
  });

  it('shows not-found message on 404 during auto-fetch', async () => {
    mockIRacingCustomerId = 100001;
    mockGetPercentile.mockRejectedValue(new Error('GET ... → 404 Not Found'));

    renderPage({ state: { carName: 'Porsche GT3' } });

    await waitFor(() =>
      expect(screen.getByText(/no race lap found/i)).toBeInTheDocument(),
    );
  });

  // ── Manual form (no profile ID) ───────────────────────────────────────────

  it('shows the manual form and profile link when no iRacingCustomerId', () => {
    renderPage();
    expect(screen.getByLabelText(/iRacing Customer ID/i)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /profile/i })).toBeInTheDocument();
  });

  it('submit button is disabled when input is empty', () => {
    renderPage();
    expect(
      screen.getByRole('button', { name: /look up my percentile/i }),
    ).toBeDisabled();
  });

  it('calls getPercentile with correct ids on manual submit', async () => {
    mockGetPercentile.mockResolvedValue(MOCK_RESULT);
    renderPage();

    fireEvent.change(screen.getByLabelText(/iRacing Customer ID/i), {
      target: { value: '100001' },
    });
    fireEvent.click(screen.getByRole('button', { name: /look up my percentile/i }));

    await waitFor(() =>
      expect(mockGetPercentile).toHaveBeenCalledWith(9001, 1, 9001, 100001),
    );
  });

  it('shows percentile result after manual submit', async () => {
    mockGetPercentile.mockResolvedValue(MOCK_RESULT);
    renderPage();

    fireEvent.change(screen.getByLabelText(/iRacing Customer ID/i), {
      target: { value: '100001' },
    });
    fireEvent.click(screen.getByRole('button', { name: /look up my percentile/i }));

    await waitFor(() =>
      expect(screen.getByText(/you beat/i)).toBeInTheDocument(),
    );
    expect(screen.getByText(/73\.4%/)).toBeInTheDocument();
    expect(screen.getByText(/500/)).toBeInTheDocument();
  });

  it('shows rank label for high percentile', async () => {
    mockGetPercentile.mockResolvedValue({ ...MOCK_RESULT, percentileRank: 92.0 });
    renderPage();

    fireEvent.change(screen.getByLabelText(/iRacing Customer ID/i), {
      target: { value: '100001' },
    });
    fireEvent.click(screen.getByRole('button', { name: /look up my percentile/i }));

    await waitFor(() =>
      expect(screen.getByText('Elite')).toBeInTheDocument(),
    );
  });

  it('shows not-found message on 404 after manual submit', async () => {
    mockGetPercentile.mockRejectedValue(
      new Error('GET /api/... → 404 Not Found'),
    );
    renderPage();

    fireEvent.change(screen.getByLabelText(/iRacing Customer ID/i), {
      target: { value: '999999' },
    });
    fireEvent.click(screen.getByRole('button', { name: /look up my percentile/i }));

    await waitFor(() =>
      expect(screen.getByText(/no race lap found/i)).toBeInTheDocument(),
    );
  });

  it('shows error message on non-404 failure', async () => {
    mockGetPercentile.mockRejectedValue(new Error('Service unavailable'));
    renderPage();

    fireEvent.change(screen.getByLabelText(/iRacing Customer ID/i), {
      target: { value: '100001' },
    });
    fireEvent.click(screen.getByRole('button', { name: /look up my percentile/i }));

    await waitFor(() =>
      expect(screen.getByText(/service unavailable/i)).toBeInTheDocument(),
    );
  });
});
