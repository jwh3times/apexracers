import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Routes, Route } from 'react-router';
import { vi, describe, it, expect, beforeEach } from 'vitest';
import CarDetailPage from './CarDetailPage';
import { api, type CarCatalogDetail } from '../../services/api';

vi.mock('../../services/api', () => ({ api: { getCar: vi.fn() } }));

const mockGetCar = vi.mocked(api.getCar);

const DETAIL: CarCatalogDetail = {
  carId: 132,
  name: 'Mercedes-AMG GT3',
  nameAbbreviated: 'Merc',
  make: 'Mercedes-AMG',
  model: 'GT3',
  hp: 550,
  weight: 1300,
  rainEnabled: true,
  freeWithSubscription: false,
  categories: ['sports_car'],
  smallImageUrl: 'https://img/s.jpg',
  carTypes: ['road'],
  largeImageUrl: 'https://img/l.jpg',
  logoUrl: 'https://img/logo.png',
  carClasses: [{ carClassId: 2523, name: 'GT3 Class' }],
  yourBestLaps: [
    {
      carId: 132,
      carName: 'Mercedes-AMG GT3',
      trackName: 'Spa',
      configName: 'GP',
      bestLapSeconds: 138.5,
      lapCount: 3,
      lastRecordedAt: '2026-01-01T00:00:00Z',
    },
  ],
};

function renderPage() {
  return render(
    <MemoryRouter initialEntries={['/cars/132']}>
      <Routes>
        <Route path="/cars/:carId" element={<CarDetailPage />} />
      </Routes>
    </MemoryRouter>
  );
}

describe('CarDetailPage', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    mockGetCar.mockResolvedValue(DETAIL);
  });

  it('fetches the car by its route id', async () => {
    renderPage();
    await waitFor(() => expect(mockGetCar).toHaveBeenCalledWith(132));
  });

  it('renders the name, specs, car classes, and your best laps', async () => {
    renderPage();
    await waitFor(() => {
      expect(screen.getByText('Mercedes-AMG GT3')).toBeInTheDocument();
      expect(screen.getByText('550')).toBeInTheDocument(); // hp
      expect(screen.getByText('GT3 Class')).toBeInTheDocument();
      expect(screen.getByText(/Spa/)).toBeInTheDocument(); // best-lap track ("Spa · GP")
      expect(screen.getByText(/2:18/)).toBeInTheDocument(); // formatLapTime(138.5)
    });
  });

  it('shows an error state when the car is not found', async () => {
    mockGetCar.mockRejectedValue(new Error('Car 999 was not found in the catalog.'));
    renderPage();
    await waitFor(() => expect(screen.getByText(/not found/i)).toBeInTheDocument());
  });
});
