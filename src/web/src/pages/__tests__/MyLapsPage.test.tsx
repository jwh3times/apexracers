import { render, screen, waitFor } from '@testing-library/react';
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
});
