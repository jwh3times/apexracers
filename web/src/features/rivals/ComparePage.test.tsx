import { render, screen, waitFor, fireEvent, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { vi, describe, it, expect, beforeEach } from 'vitest';
import ComparePage from './ComparePage';
import {
  api,
  IRacingNotLinkedError,
  type Rival,
  type RivalSuggestion,
  type DriverSearchResult,
  type DriverComparison,
} from '../../services/api';

vi.mock('../../services/api', () => {
  class IRacingNotLinkedError extends Error {
    code = 'IRACING_NOT_LINKED';
    constructor(message: string) {
      super(message);
      this.name = 'IRacingNotLinkedError';
    }
  }
  return {
    api: {
      getRivals: vi.fn(),
      getRivalSuggestions: vi.fn(),
      searchDrivers: vi.fn(),
      addRival: vi.fn(),
      removeRival: vi.fn(),
      compareRival: vi.fn(),
    },
    IRacingNotLinkedError,
  };
});

const RIVALS: Rival[] = [
  { custId: 200, displayName: 'Max Power', createdAt: '2026-01-01T00:00:00Z' },
];
const SUGGESTIONS: RivalSuggestion[] = [{ custId: 300, displayName: 'Ana Speed', sharedRaces: 4 }];
const SEARCH: DriverSearchResult[] = [{ custId: 400, displayName: 'Lee Apex' }];

const side = (custId: number, name: string, ir: number) => ({
  custId,
  displayName: name,
  country: 'United States',
  countryCode: 'USA',
  memberSince: '2021-11-05',
  licenses: [
    {
      categoryId: 5,
      categoryName: 'Sports Car',
      groupName: 'Class A',
      licenseLevel: 18,
      safetyRating: 3.2,
      iRating: ir,
      color: '0153db',
    },
  ],
  career: [
    {
      categoryId: 5,
      categoryName: 'Sports Car',
      starts: 100,
      wins: 5,
      top5: 30,
      poles: 2,
      avgStartPosition: 8,
      avgFinishPosition: 7,
      laps: 2000,
      lapsLed: 50,
      winPercentage: 5,
      top5Percentage: 30,
    },
  ],
  iRatingHistory: [
    {
      categoryId: 5,
      categoryName: 'Sports Car',
      points: [
        { when: '2026-01-01', value: ir - 100 },
        { when: '2026-02-01', value: ir },
      ],
    },
  ],
});

const COMPARISON: DriverComparison = {
  you: side(100, 'You Driver', 2000),
  rival: side(200, 'Max Power', 2500),
  shared: {
    totalShared: 3,
    youAhead: 1,
    rivalAhead: 2,
    races: [
      {
        subsessionId: 1,
        startTime: '2026-02-01T00:00:00Z',
        trackName: 'Spa',
        yourFinish: 2,
        rivalFinish: 5,
        yourIRatingDelta: 25,
        rivalIRatingDelta: -20,
        yourIncidents: 1,
        rivalIncidents: 4,
      },
    ],
    trackPace: [{ trackName: 'Spa', yourBestLapSeconds: 120.5, rivalBestLapSeconds: 121.0 }],
  },
};

const mockGetRivals = vi.mocked(api.getRivals);
const mockGetSuggestions = vi.mocked(api.getRivalSuggestions);
const mockSearch = vi.mocked(api.searchDrivers);
const mockAdd = vi.mocked(api.addRival);
const mockRemove = vi.mocked(api.removeRival);
const mockCompare = vi.mocked(api.compareRival);

function renderPage() {
  return render(
    <MemoryRouter initialEntries={['/compare']}>
      <ComparePage />
    </MemoryRouter>
  );
}

describe('ComparePage', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    mockGetRivals.mockResolvedValue(RIVALS);
    mockGetSuggestions.mockResolvedValue(SUGGESTIONS);
    mockSearch.mockResolvedValue(SEARCH);
    mockAdd.mockResolvedValue({
      custId: 400,
      displayName: 'Lee Apex',
      createdAt: '2026-03-01T00:00:00Z',
    });
    mockRemove.mockResolvedValue(undefined);
    mockCompare.mockResolvedValue(COMPARISON);
  });

  it('lists the followed rivals and the shared-race suggestions', async () => {
    renderPage();
    await waitFor(() => {
      expect(screen.getByText('Max Power')).toBeInTheDocument();
      expect(screen.getByText('Ana Speed')).toBeInTheDocument();
      expect(screen.getByText(/4 shared/i)).toBeInTheDocument();
    });
  });

  it('searches drivers and adds one from the results', async () => {
    renderPage();
    await screen.findByText('Max Power');

    fireEvent.change(screen.getByPlaceholderText(/search drivers/i), {
      target: { value: 'apex' },
    });
    const addBtn = await screen.findByRole('button', { name: /add lee apex/i });
    fireEvent.click(addBtn);

    await waitFor(() => expect(mockAdd).toHaveBeenCalledWith(400, 'Lee Apex'));
  });

  it('adds a suggested rival', async () => {
    mockAdd.mockResolvedValue({
      custId: 300,
      displayName: 'Ana Speed',
      createdAt: '2026-03-01T00:00:00Z',
    });
    renderPage();
    const addBtn = await screen.findByRole('button', { name: /add ana speed/i });
    fireEvent.click(addBtn);
    await waitFor(() => expect(mockAdd).toHaveBeenCalledWith(300, 'Ana Speed'));
  });

  it('removes a followed rival', async () => {
    renderPage();
    const removeBtn = await screen.findByRole('button', { name: /remove max power/i });
    fireEvent.click(removeBtn);
    await waitFor(() => expect(mockRemove).toHaveBeenCalledWith(200));
  });

  it('compares against a rival and shows the head-to-head', async () => {
    renderPage();
    const compareBtn = await screen.findByRole('button', { name: /compare against max power/i });
    fireEvent.click(compareBtn);

    await waitFor(() => {
      expect(mockCompare).toHaveBeenCalledWith(200);
      expect(screen.getByText('You Driver')).toBeInTheDocument();
      expect(screen.getByText(/3 shared races/i)).toBeInTheDocument();
      expect(screen.getAllByText('Spa').length).toBeGreaterThan(0);
    });
  });

  it('shows a link-iRacing prompt when comparison is not linked', async () => {
    mockCompare.mockRejectedValue(new IRacingNotLinkedError('not linked'));
    renderPage();
    const compareBtn = await screen.findByRole('button', { name: /compare against max power/i });
    fireEvent.click(compareBtn);

    await waitFor(() => {
      const link = screen.getByRole('link', { name: /settings/i });
      expect(link).toHaveAttribute('href', '/settings');
    });
  });

  it('still renders rivals when suggestions are unavailable (not linked)', async () => {
    mockGetSuggestions.mockRejectedValue(new IRacingNotLinkedError('not linked'));
    renderPage();
    await waitFor(() => expect(screen.getByText('Max Power')).toBeInTheDocument());
    expect(screen.queryByText('Ana Speed')).not.toBeInTheDocument();
  });

  it('shows an empty state when there are no rivals or suggestions', async () => {
    mockGetRivals.mockResolvedValue([]);
    mockGetSuggestions.mockResolvedValue([]);
    renderPage();
    await waitFor(() => expect(screen.getByText(/no rivals yet/i)).toBeInTheDocument());
  });

  it('surfaces an error when loading rivals fails', async () => {
    mockGetRivals.mockRejectedValue(new Error('boom'));
    renderPage();
    await waitFor(() => expect(screen.getByText(/boom/i)).toBeInTheDocument());
  });

  it('shows the comparison loading state, then the result', async () => {
    let resolve!: (v: DriverComparison) => void;
    mockCompare.mockReturnValue(new Promise<DriverComparison>(r => (resolve = r)));
    renderPage();
    const compareBtn = await screen.findByRole('button', { name: /compare against max power/i });
    fireEvent.click(compareBtn);

    expect(await screen.findByText(/comparing/i)).toBeInTheDocument();
    resolve(COMPARISON);
    await waitFor(() => expect(screen.getByText('You Driver')).toBeInTheDocument());
  });

  it('shows graceful empty states for no shared races and thin history', async () => {
    mockCompare.mockResolvedValue({
      you: {
        ...side(100, 'You Driver', 2000),
        iRatingHistory: [
          {
            categoryId: 5,
            categoryName: 'Sports Car',
            points: [{ when: '2026-01-01', value: 2000 }],
          },
        ],
      },
      rival: {
        ...side(200, 'Max Power', 2500),
        iRatingHistory: [
          {
            categoryId: 5,
            categoryName: 'Sports Car',
            points: [{ when: '2026-01-01', value: 2500 }],
          },
        ],
      },
      shared: { totalShared: 0, youAhead: 0, rivalAhead: 0, races: [], trackPace: [] },
    });
    renderPage();
    fireEvent.click(await screen.findByRole('button', { name: /compare against max power/i }));

    await waitFor(() => {
      expect(screen.getByText(/no shared races yet/i)).toBeInTheDocument();
      expect(screen.getByText(/not enough irating history/i)).toBeInTheDocument();
    });
  });

  it('marks an already-followed driver as Following in search results', async () => {
    mockSearch.mockResolvedValue([{ custId: 200, displayName: 'Max Power' }]); // already a rival
    renderPage();
    await screen.findByText('Max Power');
    fireEvent.change(screen.getByPlaceholderText(/search drivers/i), { target: { value: 'max' } });
    await waitFor(() => expect(screen.getByText('Following')).toBeInTheDocument());
  });

  it('switches the iRating overlay category and keeps the chart', async () => {
    renderPage();
    const compareBtn = await screen.findByRole('button', { name: /compare against max power/i });
    fireEvent.click(compareBtn);
    const region = await screen.findByText(/3 shared races/i);
    expect(region).toBeInTheDocument();
    // Only one shared category here, but the selector should render it.
    expect(within(document.body).getAllByText('Sports Car').length).toBeGreaterThan(0);
    // Clicking the category chip drives the selector's onClick.
    fireEvent.click(screen.getByRole('button', { name: /^sports car$/i }));
    expect(screen.getByText(/3 shared races/i)).toBeInTheDocument();
  });

  it('clears search results when the term is shortened below 2 characters', async () => {
    renderPage();
    await screen.findByText('Max Power');
    const box = screen.getByPlaceholderText(/search drivers/i);

    fireEvent.change(box, { target: { value: 'apex' } });
    expect(await screen.findByRole('button', { name: /add lee apex/i })).toBeInTheDocument();

    fireEvent.change(box, { target: { value: 'a' } }); // too short → results cleared
    await waitFor(() =>
      expect(screen.queryByRole('button', { name: /add lee apex/i })).not.toBeInTheDocument()
    );
  });

  it('shows no results when the driver search fails', async () => {
    mockSearch.mockRejectedValue(new Error('search down'));
    renderPage();
    await screen.findByText('Max Power');
    fireEvent.change(screen.getByPlaceholderText(/search drivers/i), { target: { value: 'apex' } });
    await waitFor(() => expect(mockSearch).toHaveBeenCalled());
    expect(screen.queryByRole('button', { name: /add lee apex/i })).not.toBeInTheDocument();
  });

  it('resets the comparison when the currently-compared rival is removed', async () => {
    renderPage();
    fireEvent.click(await screen.findByRole('button', { name: /compare against max power/i }));
    await screen.findByText(/3 shared races/i);

    fireEvent.click(screen.getByRole('button', { name: /remove max power/i }));
    await waitFor(() => expect(screen.queryByText(/3 shared races/i)).not.toBeInTheDocument());
  });

  it('surfaces a generic error when the comparison fails', async () => {
    mockCompare.mockRejectedValue(new Error('compare boom'));
    renderPage();
    fireEvent.click(await screen.findByRole('button', { name: /compare against max power/i }));
    await waitFor(() => expect(screen.getByText(/compare boom/i)).toBeInTheDocument());
  });
});
