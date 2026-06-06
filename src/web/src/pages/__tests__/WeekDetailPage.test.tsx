import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { vi, describe, it, expect, beforeEach } from 'vitest';
import WeekDetailPage from '../WeekDetailPage';
import { api } from '../../services/api';
import type { WeekDetail } from '../../services/api';

vi.mock('../../services/api', () => ({
  api: { getWeekDetail: vi.fn(), getCarsForWeek: vi.fn() },
}));

const mockGetWeekDetail = vi.mocked(api.getWeekDetail);

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
});
