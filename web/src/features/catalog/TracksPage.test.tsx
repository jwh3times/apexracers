import { render, screen, waitFor, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { vi, describe, it, expect, beforeEach } from 'vitest';
import TracksPage from './TracksPage';
import { api, type TrackCatalogItem } from '../../services/api';

vi.mock('../../services/api', () => ({ api: { getTracks: vi.fn() } }));

const mockGetTracks = vi.mocked(api.getTracks);

const TRACKS: TrackCatalogItem[] = [
  {
    trackId: 1,
    name: 'Spa-Francorchamps',
    configName: 'Grand Prix',
    category: 'road',
    lengthMiles: 4.35,
    cornersPerLap: 20,
    location: 'Stavelot, Belgium',
    nightLighting: false,
    smallImageUrl: 'https://img/spa.jpg',
  },
  {
    trackId: 2,
    name: 'Daytona',
    configName: 'Oval',
    category: 'oval',
    lengthMiles: 2.5,
    cornersPerLap: 4,
    location: 'Daytona Beach, USA',
    nightLighting: true,
    smallImageUrl: null,
  },
];

function renderPage() {
  return render(
    <MemoryRouter initialEntries={['/tracks']}>
      <TracksPage />
    </MemoryRouter>
  );
}

describe('TracksPage', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    mockGetTracks.mockResolvedValue(TRACKS);
  });

  it('shows a loading state while fetching', () => {
    mockGetTracks.mockReturnValue(new Promise(() => {}));
    renderPage();
    expect(screen.getByText(/loading/i)).toBeInTheDocument();
  });

  it('renders a card per track linking to its detail page', async () => {
    renderPage();
    await waitFor(() => {
      expect(screen.getByText('Spa-Francorchamps')).toBeInTheDocument();
      expect(screen.getByText('Daytona')).toBeInTheDocument();
    });
    const link = screen.getByRole('link', { name: /spa-francorchamps/i });
    expect(link).toHaveAttribute('href', '/tracks/1');
  });

  it('filters tracks by the name search box', async () => {
    renderPage();
    await screen.findByText('Spa-Francorchamps');
    fireEvent.change(screen.getByPlaceholderText(/search tracks/i), { target: { value: 'dayt' } });
    expect(screen.queryByText('Spa-Francorchamps')).not.toBeInTheDocument();
    expect(screen.getByText('Daytona')).toBeInTheDocument();
  });

  it('filters tracks by category chip', async () => {
    renderPage();
    await screen.findByText('Spa-Francorchamps');
    fireEvent.click(screen.getByRole('button', { name: /^oval$/i }));
    await waitFor(() => {
      expect(screen.queryByText('Spa-Francorchamps')).not.toBeInTheDocument();
      expect(screen.getByText('Daytona')).toBeInTheDocument();
    });
  });

  it('shows an error state when the catalog is unavailable', async () => {
    mockGetTracks.mockRejectedValue(new Error('iRacing not configured'));
    renderPage();
    await waitFor(() => expect(screen.getByText(/iRacing not configured/i)).toBeInTheDocument());
  });
});
