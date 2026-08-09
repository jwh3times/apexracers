import { render, screen, waitFor, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router';
import { vi, describe, it, expect, beforeEach } from 'vitest';
import SeriesPage from './SeriesPage';
import { api, type Series } from '../../services/api';

vi.mock('../../services/api', async importOriginal => {
  const { mockApiModule } = await import('../../test/apiMock');
  return mockApiModule(importOriginal);
});

const mockGetSeries = vi.mocked(api.getSeries);

function mk(overrides: Partial<Series> = {}): Series {
  return {
    id: 1,
    name: 'GT3 Cup',
    seasonId: 10,
    currentWeekNumber: 5,
    category: null,
    trackName: null,
    trackConfigName: null,
    carCount: 0,
    driverCount: 0,
    ...overrides,
  };
}

function renderPage() {
  return render(
    <MemoryRouter>
      <SeriesPage />
    </MemoryRouter>
  );
}

describe('SeriesPage', () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  it('shows loading state initially', () => {
    mockGetSeries.mockReturnValue(new Promise(() => {}));
    renderPage();
    expect(screen.getByText(/loading/i)).toBeInTheDocument();
  });

  it('shows error message when API fails', async () => {
    mockGetSeries.mockRejectedValue(new Error('Network error'));
    renderPage();
    await waitFor(() => expect(screen.getByText(/network error/i)).toBeInTheDocument());
  });

  it('shows empty state when no series returned', async () => {
    mockGetSeries.mockResolvedValue([]);
    renderPage();
    await waitFor(() => expect(screen.getByText(/no active series/i)).toBeInTheDocument());
  });

  it('renders series as links when currentWeekNumber is set', async () => {
    mockGetSeries.mockResolvedValue([
      {
        id: 1,
        name: 'GT3 Cup',
        seasonId: 10,
        currentWeekNumber: 5,
        category: null,
        trackName: null,
        trackConfigName: null,
        carCount: 0,
        driverCount: 0,
      },
    ]);
    renderPage();
    await waitFor(() => {
      const link = screen.getByRole('link', { name: 'GT3 Cup' });
      expect(link).toHaveAttribute('href', '/series/1/weeks/5');
    });
  });

  it('renders series as plain text when currentWeekNumber is null', async () => {
    mockGetSeries.mockResolvedValue([mk({ currentWeekNumber: null })]);
    renderPage();
    await waitFor(() => {
      expect(screen.getByText('GT3 Cup')).toBeInTheDocument();
      expect(screen.queryByRole('link', { name: 'GT3 Cup' })).not.toBeInTheDocument();
    });
  });

  it('renders a rich card with category badge, track config, and car/driver counts', async () => {
    mockGetSeries.mockResolvedValue([
      mk({
        category: 'Sports Car',
        trackName: 'Spa',
        trackConfigName: 'Endurance',
        carCount: 12,
        driverCount: 3456,
      }),
    ]);
    renderPage();
    await waitFor(() => expect(screen.getByText('Sports Car')).toBeInTheDocument());
    expect(screen.getByText('Spa')).toBeInTheDocument();
    expect(screen.getByText('· Endurance')).toBeInTheDocument();
    expect(screen.getByText('12')).toBeInTheDocument(); // car count
    expect(screen.getByText('3,456')).toBeInTheDocument(); // driver count, localized
  });

  it('filters by the search box and shows an empty state when nothing matches', async () => {
    mockGetSeries.mockResolvedValue([
      mk({ id: 1, name: 'GT3 Cup' }),
      mk({ id: 2, name: 'Formula Vee' }),
    ]);
    renderPage();
    await waitFor(() => expect(screen.getByText('GT3 Cup')).toBeInTheDocument());

    const search = screen.getByPlaceholderText(/search series/i);
    fireEvent.change(search, { target: { value: 'formula' } });
    expect(screen.queryByText('GT3 Cup')).not.toBeInTheDocument();
    expect(screen.getByText('Formula Vee')).toBeInTheDocument();

    fireEvent.change(search, { target: { value: 'zzz' } });
    expect(screen.getByText(/no series match your search/i)).toBeInTheDocument();
  });

  it('filters by category chips and toggles the active chip off', async () => {
    mockGetSeries.mockResolvedValue([
      mk({ id: 1, name: 'GT3 Cup', category: 'Sports Car' }),
      mk({ id: 2, name: 'Formula Vee', category: 'Formula Car' }),
    ]);
    renderPage();
    await waitFor(() => expect(screen.getByText('GT3 Cup')).toBeInTheDocument());

    fireEvent.click(screen.getByRole('button', { name: 'Formula Car' }));
    expect(screen.queryByText('GT3 Cup')).not.toBeInTheDocument();
    expect(screen.getByText('Formula Vee')).toBeInTheDocument();

    // Clicking the active chip again clears the filter → both shown.
    fireEvent.click(screen.getByRole('button', { name: 'Formula Car' }));
    expect(screen.getByText('GT3 Cup')).toBeInTheDocument();
    expect(screen.getByText('Formula Vee')).toBeInTheDocument();
  });
});
