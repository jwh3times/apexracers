import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Routes, Route } from 'react-router';
import { vi, describe, it, expect, beforeEach } from 'vitest';
import StrategyPage from './StrategyPage';
import { api, type CarStrategy, type WeekStrategy } from '../../services/api';

vi.mock('../../services/api', async importOriginal => {
  const { mockApiModule } = await import('../../test/apiMock');
  return mockApiModule(importOriginal);
});

const mockGet = vi.mocked(api.getWeekStrategy);

function makeCar(overrides: Partial<CarStrategy> = {}): CarStrategy {
  return {
    carId: 1,
    carName: 'Car',
    weightPenaltyKg: 0,
    powerAdjustPct: 0,
    maxPctFuelFill: 100,
    maxDryTireSets: 0,
    weightDeltaKg: 0,
    powerDeltaPct: 0,
    bopTrend: '—',
    topSharePercent: null,
    fuelCapped: false,
    fuelNote: 'No fuel-fill restriction.',
    limitedTireSets: false,
    tireNote: 'Unlimited dry tire sets.',
    rainEnabled: false,
    percentileRank: null,
    expectedPercentile: null,
    fieldSize: null,
    isPercentilePresentable: false,
    projectedLapSeconds: null,
    recommendationRank: null,
    ...overrides,
  };
}

function makeData(overrides: Partial<WeekStrategy> = {}): WeekStrategy {
  return {
    seriesId: 444,
    seriesName: 'GT Sprint',
    raceWeekIndex: 3,
    trackName: 'Thruxton',
    configName: 'Club',
    trackLengthMiles: 2.36,
    cornersPerLap: 10,
    numberPitstalls: 40,
    pitRoadSpeedLimit: 60,
    nightLighting: true,
    weather: {
      tempHighC: 24,
      tempLowC: 20,
      precipChancePct: 60,
      windHighKph: 14,
      windLowKph: 7,
      skies: 1,
    },
    weatherRisk: {
      level: 'High',
      precipChancePct: 60,
      fieldRainCapable: true,
      note: 'Wet running likely — prioritize a rain setup and tire management.',
    },
    personalized: true,
    cars: [
      makeCar({
        carId: 132,
        carName: 'BMW M4 GT3',
        weightPenaltyKg: 10,
        powerAdjustPct: -1.5,
        maxPctFuelFill: 50,
        weightDeltaKg: 5,
        powerDeltaPct: -0.5,
        bopTrend: 'Nerfed',
        fuelCapped: true,
        fuelNote: 'Fuel capped at 50% — shorter stints; plan an extra stop.',
        rainEnabled: true,
        percentileRank: 92,
        expectedPercentile: 88,
        topSharePercent: 8,
        fieldSize: 100,
        isPercentilePresentable: true,
        projectedLapSeconds: 65.123,
        recommendationRank: 1,
      }),
      makeCar({
        carId: 119,
        carName: 'Porsche 718 GT4',
        weightDeltaKg: -5,
        powerAdjustPct: 1,
        powerDeltaPct: 0.5,
        bopTrend: 'Buffed',
        maxDryTireSets: 2,
        limitedTireSets: true,
        tireNote: 'Limited to 2 dry sets — manage tire wear.',
      }),
      makeCar({
        carId: 200,
        carName: 'Audi R8 GT3',
        weightDeltaKg: 3,
        powerDeltaPct: 1,
        bopTrend: 'Mixed',
      }),
    ],
    ...overrides,
  };
}

function renderPage() {
  return render(
    <MemoryRouter initialEntries={['/series/444/weeks/3/strategy']}>
      <Routes>
        <Route path="/series/:seriesId/weeks/:raceWeekIndex/strategy" element={<StrategyPage />} />
      </Routes>
    </MemoryRouter>
  );
}

describe('StrategyPage', () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  it('shows a loading state while fetching', () => {
    mockGet.mockReturnValue(new Promise(() => {}));
    renderPage();
    expect(screen.getByText(/loading/i)).toBeInTheDocument();
  });

  it('fetches strategy for the route series and week ids', async () => {
    mockGet.mockResolvedValue(makeData());
    renderPage();
    await waitFor(() => expect(mockGet).toHaveBeenCalledWith(444, 3, expect.any(AbortSignal)));
  });

  it('renders the header, track context chips and high weather-risk banner', async () => {
    mockGet.mockResolvedValue(makeData());
    renderPage();
    await waitFor(() => expect(screen.getByText('GT Sprint')).toBeInTheDocument());
    expect(screen.getByText(/Thruxton/)).toBeInTheDocument();
    expect(screen.getByText('10 corners')).toBeInTheDocument();
    expect(screen.getByText('Night lighting')).toBeInTheDocument();
    expect(screen.getByText(/Weather risk: High/)).toBeInTheDocument();
    expect(screen.getByText(/prioritize a rain setup/)).toBeInTheDocument();
  });

  it('renders per-car BoP, trend, deltas, fuel/tire notes and rain badge', async () => {
    mockGet.mockResolvedValue(makeData());
    renderPage();
    await waitFor(() => expect(screen.getByText('BMW M4 GT3')).toBeInTheDocument());
    expect(screen.getByText('Nerfed')).toBeInTheDocument();
    expect(screen.getByText('Buffed')).toBeInTheDocument();
    expect(screen.getByText('Mixed')).toBeInTheDocument();
    expect(screen.getByText('+5kg')).toBeInTheDocument(); // weight delta
    expect(screen.getByText(/Fuel capped at 50%/)).toBeInTheDocument();
    expect(screen.getByText(/Limited to 2 dry sets/)).toBeInTheDocument();
    expect(screen.getByText('Rain OK')).toBeInTheDocument();
  });

  it('shows the personal overlay (recommendation rank, percentile, projected lap) when personalized', async () => {
    mockGet.mockResolvedValue(makeData());
    renderPage();
    await waitFor(() => expect(screen.getByText('Recommendation Rank #1')).toBeInTheDocument());
    expect(screen.getByText('TOP 8%')).toBeInTheDocument(); // topSharePercent 8 → TOP 8%
  });

  it('labels projected-only pace as an expected percentile without implying field membership', async () => {
    mockGet.mockResolvedValue(
      makeData({
        cars: [
          makeCar({
            expectedPercentile: 72,
            projectedLapSeconds: 70,
            recommendationRank: 1,
          }),
        ],
      })
    );
    renderPage();

    expect(await screen.findByText('Expected Percentile')).toBeInTheDocument();
    expect(screen.getByText('72.0%')).toBeInTheDocument();
    expect(screen.queryByText(/drivers in field/i)).not.toBeInTheDocument();
  });

  it('keeps an undersized current-week result visible when no expected percentile exists', async () => {
    mockGet.mockResolvedValue(
      makeData({
        cars: [
          makeCar({
            percentileRank: 100,
            expectedPercentile: null,
            fieldSize: 1,
            isPercentilePresentable: false,
            projectedLapSeconds: 70,
            recommendationRank: 1,
          }),
        ],
      })
    );
    renderPage();

    expect(await screen.findByText(/Only 1 driver has set a time this week/i)).toBeInTheDocument();
    expect(screen.getByText('1:10.000')).toBeInTheDocument();
    expect(screen.queryByText('Expected Percentile')).not.toBeInTheDocument();
  });

  it('shows a link-account hint per car when not personalized', async () => {
    mockGet.mockResolvedValue(
      makeData({
        personalized: false,
        weatherRisk: {
          level: 'Medium',
          precipChancePct: 25,
          fieldRainCapable: true,
          note: 'Showers possible — keep a wet setup ready.',
        },
        weather: null,
        cars: [makeCar({ carId: 1, carName: 'Solo Car', bopTrend: 'Unchanged' })],
      })
    );
    renderPage();
    await waitFor(() => expect(screen.getByText(/Weather risk: Medium/)).toBeInTheDocument());
    expect(screen.getByText(/Link your iRacing account/)).toBeInTheDocument();
    expect(screen.getByText('Unchanged')).toBeInTheDocument();
  });

  it('handles a low-risk week with no track context and an empty car list', async () => {
    mockGet.mockResolvedValue(
      makeData({
        weatherRisk: {
          level: 'Low',
          precipChancePct: 0,
          fieldRainCapable: false,
          note: 'Dry conditions expected.',
        },
        trackLengthMiles: null,
        cornersPerLap: null,
        numberPitstalls: null,
        pitRoadSpeedLimit: null,
        nightLighting: false,
        cars: [],
      })
    );
    renderPage();
    await waitFor(() => expect(screen.getByText(/Weather risk: Low/)).toBeInTheDocument());
    expect(screen.getByText(/No Balance of Performance data/)).toBeInTheDocument();
  });

  it('shows an error message when the API fails', async () => {
    mockGet.mockRejectedValue(new Error('Boom'));
    renderPage();
    await waitFor(() => expect(screen.getByText(/boom/i)).toBeInTheDocument());
  });
});
