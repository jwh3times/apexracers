import { render, screen, waitFor, fireEvent } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router';
import { vi, describe, it, expect, beforeEach } from 'vitest';
import WeekDetailPage from './WeekDetailPage';
import { api } from '../../services/api';
import type { WeekDetail } from '../../services/api';
import type { User } from '../../context/AuthContext';

let mockUser: User | null = null;
vi.mock('../../context/AuthContext', () => ({
  useAuth: () => ({ user: mockUser }),
}));

vi.mock('../../services/api', async importOriginal => {
  const { mockApiModule } = await import('../../test/apiMock');
  return mockApiModule(importOriginal);
});

const mockGetWeekDetail = vi.mocked(api.getWeekDetail);
const mockGetMyWeekPercentiles = vi.mocked(api.getMyWeekPercentiles);

const LINKED_USER: User = {
  token: 'tok',
  userId: 'u1',
  displayName: 'Driver',
  email: 't@t.com',
  iRacingCustomerId: 100042,
  role: 'Standard',
};

const emptyDetail: WeekDetail = {
  seriesName: 'VRS GT3 Sprint',
  category: 'Road',
  trackName: 'Spa-Francorchamps',
  trackConfigName: 'Full',
  trackLengthMiles: 4.35,
  cars: [],
};

function makeCar(overrides: Partial<WeekDetail['cars'][number]> = {}): WeekDetail['cars'][number] {
  return {
    carId: 1,
    carName: 'Porsche 992 GT3',
    className: 'GT3',
    entryCount: 150,
    fastestLapSeconds: 131.456,
    medianLapSeconds: 135.0,
    ...overrides,
  };
}

function renderPage(seriesId = '1', weekNumber = '10') {
  return render(
    <MemoryRouter initialEntries={[`/series/${seriesId}/weeks/${weekNumber}`]}>
      <Routes>
        <Route path="/series/:seriesId/weeks/:weekNumber" element={<WeekDetailPage />} />
      </Routes>
    </MemoryRouter>
  );
}

describe('WeekDetailPage', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    mockUser = null; // anonymous by default → no "Your pct" column
    mockGetMyWeekPercentiles.mockResolvedValue([]);
  });

  it('shows loading state initially', () => {
    mockGetWeekDetail.mockReturnValue(new Promise(() => {}));
    renderPage();
    expect(screen.getByText(/loading/i)).toBeInTheDocument();
  });

  it('shows error when API fails', async () => {
    mockGetWeekDetail.mockRejectedValue(new Error('Server error'));
    renderPage();
    await waitFor(() => expect(screen.getByText(/server error/i)).toBeInTheDocument());
  });

  it('shows empty state when no cars', async () => {
    mockGetWeekDetail.mockResolvedValue(emptyDetail);
    renderPage();
    await waitFor(() => expect(screen.getByText(/no lap time data/i)).toBeInTheDocument());
  });

  it('renders series name as page title', async () => {
    mockGetWeekDetail.mockResolvedValue(emptyDetail);
    renderPage();
    await waitFor(() => expect(screen.getByText('VRS GT3 Sprint')).toBeInTheDocument());
  });

  it('renders track subtitle', async () => {
    mockGetWeekDetail.mockResolvedValue(emptyDetail);
    renderPage();
    await waitFor(() => expect(screen.getByText(/Spa-Francorchamps/)).toBeInTheDocument());
  });

  it('renders car table with correct data', async () => {
    mockGetWeekDetail.mockResolvedValue({ ...emptyDetail, cars: [makeCar()] });
    renderPage();
    await waitFor(() => {
      // car name and best lap appear in both the KPI strip and the table row
      expect(screen.getAllByText('Porsche 992 GT3').length).toBeGreaterThan(0);
      expect(screen.getAllByText('2:11.456').length).toBeGreaterThan(0);
    });
  });

  it('renders class pill in car row', async () => {
    mockGetWeekDetail.mockResolvedValue({ ...emptyDetail, cars: [makeCar()] });
    renderPage();
    await waitFor(() => expect(screen.getByText('GT3')).toBeInTheDocument());
  });

  it('renders delta column', async () => {
    mockGetWeekDetail.mockResolvedValue({ ...emptyDetail, cars: [makeCar()] });
    renderPage();
    await waitFor(() => expect(screen.getByText('3.544')).toBeInTheDocument()); // 135.0 - 131.456
  });

  it('renders recommendations link', async () => {
    mockGetWeekDetail.mockResolvedValue(emptyDetail);
    renderPage('1', '10');
    await waitFor(() => {
      const link = screen.getByRole('link', { name: /see my car recommendations/i });
      expect(link).toHaveAttribute('href', '/recommendations?seriesId=1&weekNumber=10');
    });
  });

  it('renders Deep dive link to analytics', async () => {
    mockGetWeekDetail.mockResolvedValue(emptyDetail);
    renderPage();
    await waitFor(() => {
      const link = screen.getByRole('link', { name: /deep dive/i });
      expect(link).toHaveAttribute('href', '/analytics');
    });
  });

  // ── Sort modes, row navigation, null laps, cross-links (T8) ────────────────

  function rowOrder(): string[] {
    return screen
      .getAllByRole('row')
      .map(r => r.textContent ?? '')
      .filter(t => t.includes('Alpha') || t.includes('Bravo'));
  }

  it('re-sorts the car table by consistency and by volume', async () => {
    mockGetWeekDetail.mockResolvedValue({
      ...emptyDetail,
      cars: [
        makeCar({
          carId: 1,
          carName: 'Alpha',
          fastestLapSeconds: 131.0,
          medianLapSeconds: 140.0,
          entryCount: 10,
        }),
        makeCar({
          carId: 2,
          carName: 'Bravo',
          fastestLapSeconds: 132.0,
          medianLapSeconds: 132.5,
          entryCount: 500,
        }),
      ],
    });
    renderPage();
    await waitFor(() => expect(screen.getAllByText('Alpha').length).toBeGreaterThan(0));

    // Best lap (default): Alpha (131.0) before Bravo (132.0).
    expect(rowOrder()[0]).toContain('Alpha');

    // Consistency: Bravo's tight best↔median gap wins → first.
    fireEvent.click(screen.getByRole('button', { name: /consistency/i }));
    expect(rowOrder()[0]).toContain('Bravo');

    // Volume: Bravo's 500 entries win → first.
    fireEvent.click(screen.getByRole('button', { name: /^volume$/i }));
    expect(rowOrder()[0]).toContain('Bravo');
  });

  it('navigates to the car percentile page when a row is clicked', async () => {
    mockGetWeekDetail.mockResolvedValue({ ...emptyDetail, cars: [makeCar({ carName: 'Alpha' })] });
    render(
      <MemoryRouter initialEntries={['/series/1/weeks/10']}>
        <Routes>
          <Route path="/series/:seriesId/weeks/:weekNumber" element={<WeekDetailPage />} />
          <Route
            path="/series/:seriesId/weeks/:weekNumber/cars/:carId/percentile"
            element={<div>Percentile Detail</div>}
          />
        </Routes>
      </MemoryRouter>
    );
    await waitFor(() => expect(screen.getAllByText('Alpha').length).toBeGreaterThan(0));
    // Click the table-row occurrence (not the KPI strip), which carries the row onClick.
    const cell = screen.getAllByText('Alpha').find(el => el.closest('tr'));
    fireEvent.click(cell!);
    await waitFor(() => expect(screen.getByText('Percentile Detail')).toBeInTheDocument());
  });

  it('renders dashes for cars with no lap data and sorts them last', async () => {
    mockGetWeekDetail.mockResolvedValue({
      ...emptyDetail,
      cars: [
        makeCar({ carId: 1, carName: 'Alpha', fastestLapSeconds: 131.0, medianLapSeconds: 135.0 }),
        makeCar({
          carId: 2,
          carName: 'Bravo',
          fastestLapSeconds: null,
          medianLapSeconds: null,
          className: null,
        }),
      ],
    });
    renderPage();
    await waitFor(() => expect(screen.getByText('Bravo')).toBeInTheDocument());
    expect(rowOrder()[0]).toContain('Alpha'); // the timed car sorts ahead of the null one
    expect(screen.getAllByText('—').length).toBeGreaterThan(0); // dash cells for the null car
  });

  it('renders Strategy, Standings, and Season schedule cross-links', async () => {
    mockGetWeekDetail.mockResolvedValue(emptyDetail);
    renderPage('1', '10');
    await waitFor(() => {
      expect(screen.getByRole('link', { name: /^strategy$/i })).toHaveAttribute(
        'href',
        '/series/1/weeks/10/strategy'
      );
    });
    expect(screen.getByRole('link', { name: /standings/i })).toHaveAttribute(
      'href',
      '/series/1/standings'
    );
    expect(screen.getByRole('link', { name: /season schedule/i })).toHaveAttribute(
      'href',
      '/series/1/schedule'
    );
  });

  // ── "Your pct" column (T12) ────────────────────────────────────────────────

  it('does not render the Your pct column for anonymous visitors', async () => {
    mockGetWeekDetail.mockResolvedValue({ ...emptyDetail, cars: [makeCar()] });
    renderPage();
    await waitFor(() => expect(screen.getAllByText('Porsche 992 GT3').length).toBeGreaterThan(0));
    expect(screen.queryByText(/your pct/i)).not.toBeInTheDocument();
    expect(mockGetMyWeekPercentiles).not.toHaveBeenCalled();
  });

  it('renders the Your pct chip for a signed-in driver with a percentile', async () => {
    mockUser = LINKED_USER;
    mockGetWeekDetail.mockResolvedValue({ ...emptyDetail, cars: [makeCar({ carId: 1 })] });
    mockGetMyWeekPercentiles.mockResolvedValue([{ carId: 1, percentileRank: 92 }]);
    renderPage('1', '10');
    await waitFor(() => expect(screen.getByText(/your pct/i)).toBeInTheDocument());
    expect(mockGetMyWeekPercentiles).toHaveBeenCalledWith(1, 10, expect.any(AbortSignal));
    // percentileRank 92 → ceil(100-92) = TOP 8%
    expect(screen.getByText('TOP 8%')).toBeInTheDocument();
  });

  it('shows a dash in the Your pct column for cars the driver has no percentile for', async () => {
    mockUser = LINKED_USER;
    mockGetWeekDetail.mockResolvedValue({ ...emptyDetail, cars: [makeCar({ carId: 1 })] });
    mockGetMyWeekPercentiles.mockResolvedValue([]); // no percentile for car 1
    renderPage();
    await waitFor(() => expect(screen.getByText(/your pct/i)).toBeInTheDocument());
    expect(screen.queryByText(/TOP \d+%/)).not.toBeInTheDocument();
  });
});
