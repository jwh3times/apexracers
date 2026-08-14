import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Routes, Route } from 'react-router';
import { vi, describe, it, expect, beforeEach } from 'vitest';
import TrackDetailPage from './TrackDetailPage';
import { api, type TrackCatalogDetail } from '../../services/api';

vi.mock('../../services/api', async importOriginal => {
  const { mockApiModule } = await import('../../test/apiMock');
  return mockApiModule(importOriginal);
});

const mockGetTrack = vi.mocked(api.getTrack);

const DETAIL: TrackCatalogDetail = {
  trackId: 18,
  name: 'Spa-Francorchamps',
  configName: 'Grand Prix',
  category: 'road',
  lengthMiles: 4.35,
  cornersPerLap: 20,
  location: 'Stavelot, Belgium',
  nightLighting: false,
  latitude: 50.4372,
  longitude: 5.9714,
  pitRoadSpeedLimit: 60,
  numberPitstalls: 40,
  hasSvgMap: true,
  smallImageUrl: 'https://img/s.jpg',
  largeImageUrl: 'https://img/l.jpg',
  trackMapUrl: 'https://map/spa/',
  yourBestLaps: [
    {
      carId: 132,
      carName: 'Mercedes-AMG GT3',
      trackId: 341,
      trackName: 'Spa-Francorchamps',
      configName: 'Grand Prix',
      bestLapSeconds: 138.5,
      lapCount: 5,
      lastRecordedAt: '2026-01-01T00:00:00Z',
    },
  ],
};

function renderPage() {
  return render(
    <MemoryRouter initialEntries={['/tracks/18']}>
      <Routes>
        <Route path="/tracks/:trackId" element={<TrackDetailPage />} />
      </Routes>
    </MemoryRouter>
  );
}

describe('TrackDetailPage', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    mockGetTrack.mockResolvedValue(DETAIL);
  });

  it('fetches the track by its route id', async () => {
    renderPage();
    await waitFor(() => expect(mockGetTrack).toHaveBeenCalledWith(18, expect.any(AbortSignal)));
  });

  it('renders the name, specs, and your best laps', async () => {
    renderPage();
    await waitFor(() => {
      expect(screen.getByText('Spa-Francorchamps')).toBeInTheDocument();
      expect(screen.getByText('20')).toBeInTheDocument(); // corners
      expect(screen.getByText('Mercedes-AMG GT3')).toBeInTheDocument(); // best-lap car
      expect(screen.getByText(/2:18/)).toBeInTheDocument(); // formatLapTime(138.5)
    });
  });

  it('links to the interactive track map', async () => {
    renderPage();
    const link = await screen.findByRole('link', { name: /track map/i });
    expect(link).toHaveAttribute('href', 'https://map/spa/');
  });

  it('shows an error state when the track is not found', async () => {
    mockGetTrack.mockRejectedValue(new Error('Track 999 was not found in the catalog.'));
    renderPage();
    await waitFor(() => expect(screen.getByText(/not found/i)).toBeInTheDocument());
  });
});
