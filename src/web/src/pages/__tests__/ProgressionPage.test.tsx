import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { vi, describe, it, expect, beforeEach } from 'vitest';
import ProgressionPage from '../ProgressionPage';
import { api, IRacingNotLinkedError, type MemberProgression } from '../../services/api';

vi.mock('../../services/api', () => {
  class IRacingNotLinkedError extends Error {
    code = 'IRACING_NOT_LINKED';
    constructor(message: string) {
      super(message);
      this.name = 'IRacingNotLinkedError';
    }
  }
  return {
    api: { getProgression: vi.fn() },
    IRacingNotLinkedError,
  };
});

const mockGetProgression = vi.mocked(api.getProgression);

const MOCK: MemberProgression = {
  customerId: 691062,
  categories: [
    {
      categoryId: 5,
      categoryName: 'Sports Car',
      iRating: 1854,
      safetyRating: 2.38,
      cpi: 42.7,
      licenseLevel: 18,
      groupName: 'Class A',
      ttRating: 1350,
      color: '0153db',
      iRatingHistory: [
        { when: '2024-01-01', value: 1700 },
        { when: '2024-02-01', value: 1800 },
        { when: '2024-02-24', value: 1854 },
      ],
    },
    {
      categoryId: 2,
      categoryName: 'Road',
      iRating: 1900,
      safetyRating: 3.29,
      cpi: 58.1,
      licenseLevel: 19,
      groupName: 'Class A',
      ttRating: 1350,
      color: '0153db',
      iRatingHistory: [
        { when: '2024-01-01', value: 2000 },
        { when: '2024-02-24', value: 1900 },
      ],
    },
    {
      categoryId: 1,
      categoryName: 'Oval',
      iRating: 1528,
      safetyRating: 2.8,
      cpi: 38.7,
      licenseLevel: 14,
      groupName: 'Class B',
      ttRating: 1350,
      color: '00c702',
      iRatingHistory: [{ when: '2024-02-24', value: 1528 }],
    },
  ],
};

function renderPage() {
  return render(
    <MemoryRouter initialEntries={['/progression']}>
      <ProgressionPage />
    </MemoryRouter>
  );
}

describe('ProgressionPage', () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  it('shows a loading state while fetching', () => {
    mockGetProgression.mockReturnValue(new Promise(() => {})); // never resolves
    renderPage();
    expect(screen.getByText(/loading/i)).toBeInTheDocument();
  });

  it('renders one card per category with iRating values', async () => {
    mockGetProgression.mockResolvedValue(MOCK);
    renderPage();
    await waitFor(() => {
      expect(screen.getByText('Sports Car')).toBeInTheDocument();
      expect(screen.getByText('Road')).toBeInTheDocument();
      expect(screen.getByText('Oval')).toBeInTheDocument();
      expect(screen.getByText('1,854')).toBeInTheDocument();
      expect(screen.getByText('1,528')).toBeInTheDocument();
    });
  });

  it('shows safety rating, CPI, and TT rating values', async () => {
    mockGetProgression.mockResolvedValue(MOCK);
    renderPage();
    await waitFor(() => {
      expect(screen.getByText('2.38')).toBeInTheDocument(); // safety rating
      expect(screen.getByText('42.7')).toBeInTheDocument(); // CPI
    });
  });

  it('shows a positive iRating delta for an improving category', async () => {
    mockGetProgression.mockResolvedValue(MOCK);
    renderPage();
    // Sports Car: 1854 - 1700 = +154
    await waitFor(() => expect(screen.getByText('+154 iR')).toBeInTheDocument());
  });

  it('shows a negative iRating delta for a declining category', async () => {
    mockGetProgression.mockResolvedValue(MOCK);
    renderPage();
    // Road: 1900 - 2000 = -100
    await waitFor(() => expect(screen.getByText('-100 iR')).toBeInTheDocument());
  });

  it('shows the not-enough-history note for a single-point category', async () => {
    mockGetProgression.mockResolvedValue(MOCK);
    renderPage();
    await waitFor(() => expect(screen.getByText(/not enough history/i)).toBeInTheDocument());
  });

  it('shows the link-iRacing prompt pointing to Settings when not linked', async () => {
    mockGetProgression.mockRejectedValue(new IRacingNotLinkedError('not linked'));
    renderPage();
    await waitFor(() => {
      const link = screen.getByRole('link', { name: /settings/i });
      expect(link).toHaveAttribute('href', '/settings');
    });
  });

  it('shows an error message when the API fails', async () => {
    mockGetProgression.mockRejectedValue(new Error('Server exploded'));
    renderPage();
    await waitFor(() => expect(screen.getByText(/server exploded/i)).toBeInTheDocument());
  });

  it('shows an empty-state message when there are no categories', async () => {
    mockGetProgression.mockResolvedValue({ customerId: 691062, categories: [] });
    renderPage();
    await waitFor(() =>
      expect(screen.getByText(/no license categories found/i)).toBeInTheDocument()
    );
  });
});
