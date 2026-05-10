import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { vi, describe, it, expect, beforeEach } from 'vitest';
import WeekDetailPage from '../WeekDetailPage';
import { api } from '../../services/api';

vi.mock('../../services/api', () => ({
  api: { getCarsForWeek: vi.fn() },
}));

const mockGetCars = vi.mocked(api.getCarsForWeek);

function renderPage(seriesId = '1', weekId = '10') {
  return render(
    <MemoryRouter initialEntries={[`/series/${seriesId}/weeks/${weekId}`]}>
      <Routes>
        <Route path="/series/:seriesId/weeks/:weekId" element={<WeekDetailPage />} />
      </Routes>
    </MemoryRouter>
  );
}

describe('WeekDetailPage', () => {
  beforeEach(() => { vi.resetAllMocks(); });

  it('shows loading state initially', () => {
    mockGetCars.mockReturnValue(new Promise(() => {}));
    renderPage();
    expect(screen.getByText(/loading/i)).toBeInTheDocument();
  });

  it('shows error when API fails', async () => {
    mockGetCars.mockRejectedValue(new Error('Server error'));
    renderPage();
    await waitFor(() => expect(screen.getByText(/server error/i)).toBeInTheDocument());
  });

  it('shows empty state when no cars', async () => {
    mockGetCars.mockResolvedValue([]);
    renderPage();
    await waitFor(() => expect(screen.getByText(/no lap time data/i)).toBeInTheDocument());
  });

  it('renders car table with correct data', async () => {
    mockGetCars.mockResolvedValue([
      { carId: 1, carName: 'Porsche 992 GT3', entryCount: 150, fastestLapSeconds: 131.456, medianLapSeconds: 135.0 },
    ]);
    renderPage();
    await waitFor(() => {
      expect(screen.getByText('Porsche 992 GT3')).toBeInTheDocument();
      // entryCount appears in both the stat card and the table cell
      expect(screen.getAllByText('150').length).toBeGreaterThan(0);
      // lap times are table-only
      expect(screen.getByText('2:11.456')).toBeInTheDocument();
    });
  });

  it('renders recommendations link', async () => {
    mockGetCars.mockResolvedValue([]);
    renderPage('1', '10');
    await waitFor(() => {
      const link = screen.getByRole('link', { name: /see my car recommendations/i });
      expect(link).toHaveAttribute('href', '/recommendations?weekId=10');
    });
  });
});
