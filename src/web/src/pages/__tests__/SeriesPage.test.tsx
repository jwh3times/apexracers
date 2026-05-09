import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { vi, describe, it, expect, beforeEach } from 'vitest';
import SeriesPage from '../SeriesPage';
import { api } from '../../services/api';

vi.mock('../../services/api', () => ({
  api: { getSeries: vi.fn() },
}));

const mockGetSeries = vi.mocked(api.getSeries);

function renderPage() {
  return render(<MemoryRouter><SeriesPage /></MemoryRouter>);
}

describe('SeriesPage', () => {
  beforeEach(() => { vi.resetAllMocks(); });

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

  it('renders series as links when currentWeekId is set', async () => {
    mockGetSeries.mockResolvedValue([
      { id: 1, name: 'GT3 Cup', seasonId: 10, currentWeekId: 5 },
    ]);
    renderPage();
    await waitFor(() => {
      const link = screen.getByRole('link', { name: 'GT3 Cup' });
      expect(link).toHaveAttribute('href', '/series/1/weeks/5');
    });
  });

  it('renders series as plain text when currentWeekId is null', async () => {
    mockGetSeries.mockResolvedValue([
      { id: 1, name: 'GT3 Cup', seasonId: 10, currentWeekId: null },
    ]);
    renderPage();
    await waitFor(() => {
      expect(screen.getByText('GT3 Cup')).toBeInTheDocument();
      expect(screen.queryByRole('link', { name: 'GT3 Cup' })).not.toBeInTheDocument();
    });
  });
});
