// Types matching the backend controller response shapes

export interface Series {
  id: number;
  name: string;
  currentSeason: number;
}

export interface WeekCar {
  carId: number;
  carName: string;
  entryCount: number;
  fastestLapSeconds: number | null;
  medianLapSeconds: number | null;
}

export interface PercentileResult {
  seriesId: number;
  weekId: number;
  carId: number;
  customerId: number;
  percentileRank: number;
  sampleSize: number;
  computedAt: string; // ISO 8601
}

export interface CarRecommendation {
  rank: number;
  carId: number;
  carName: string;
  percentileRank: number;
  sampleSize: number;
}

// ── Internal helpers ──────────────────────────────────────────────────────────

async function get<T>(path: string): Promise<T> {
  const res = await fetch(path);
  if (!res.ok) throw new Error(`GET ${path} → ${res.status} ${res.statusText}`);
  return res.json() as Promise<T>;
}

async function post<T>(path: string): Promise<T> {
  const res = await fetch(path, { method: 'POST' });
  if (!res.ok) throw new Error(`POST ${path} → ${res.status} ${res.statusText}`);
  return res.json() as Promise<T>;
}

// ── Public API surface ────────────────────────────────────────────────────────

export const api = {
  /** GET /api/series — list active weekly series */
  getSeries(): Promise<Series[]> {
    return get('/api/series');
  },

  /** GET /api/series/:seriesId/weeks/:weekId/cars — cars with aggregate lap stats */
  getCarsForWeek(seriesId: number, weekId: number): Promise<WeekCar[]> {
    return get(`/api/series/${seriesId}/weeks/${weekId}/cars`);
  },

  /** GET /api/series/:seriesId/weeks/:weekId/cars/:carId/percentile?customerId= */
  getPercentile(
    seriesId: number,
    weekId: number,
    carId: number,
    customerId: number,
  ): Promise<PercentileResult> {
    const qs = new URLSearchParams({ customerId: String(customerId) });
    return get(`/api/series/${seriesId}/weeks/${weekId}/cars/${carId}/percentile?${qs}`);
  },

  /** GET /api/users/me/recommendations?weekId= */
  getRecommendations(weekId: number): Promise<CarRecommendation[]> {
    const qs = new URLSearchParams({ weekId: String(weekId) });
    return get(`/api/users/me/recommendations?${qs}`);
  },

  /** POST /api/auth/callback?code=&state= — OAuth 2.0 Authorization Code exchange */
  postAuthCallback(code: string, state: string): Promise<unknown> {
    const qs = new URLSearchParams({ code, state });
    return post(`/api/auth/callback?${qs}`);
  },
};
