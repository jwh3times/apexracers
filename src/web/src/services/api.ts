// Types matching the backend controller response shapes

export interface AuthResult {
  token: string;
  userId: string;
  displayName: string;
}

export interface Series {
  id: number;
  name: string;
  seasonId: number;
  currentWeekNumber: number | null;
}

export interface TelemetryUploadResult {
  totalLaps: number;
  validLaps: number;
  bestLapSeconds: number | null;
  trackName: string;
  configName: string;
  carName: string;
  customerId: number;
  driverName: string;
}

export interface WeeklyPercentile {
  weekNumber: number;
  trackName: string;
  configName: string;
  percentileRank: number;
  sampleSize: number;
  computedAt: string; // ISO 8601
}

export interface CarAnalytics {
  carId: number;
  carName: string;
  seriesId: number;
  seriesName: string;
  latestPercentileRank: number;
  bestPercentileRank: number;
  personalBestLapSeconds: number | null;
  medianLapSeconds: number | null;
  totalLaps: number;
  percentileHistory: WeeklyPercentile[];
}

export interface PersonalLap {
  carId: number;
  carName: string;
  trackName: string;
  configName: string;
  bestLapSeconds: number;
  lapCount: number;
  lastRecordedAt: string; // ISO 8601
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
  weekNumber: number;
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
  estimatedLapSeconds: number;
  isProjected: boolean;
}

export interface AdminUser {
  userId: string;
  email: string;
  displayName: string;
  role: string;
}

export interface FeatureFlag {
  id: number;
  key: string;
  name: string;
  description: string | null;
  isEnabled: boolean;
  minimumRole: string;
  createdAt: string;
  updatedAt: string;
}

// ── Internal helpers ──────────────────────────────────────────────────────────

let _token: string | null = null;

export function setToken(token: string): void { _token = token; }
export function clearToken(): void { _token = null; }

function authHeaders(): Record<string, string> {
  return _token ? { Authorization: `Bearer ${_token}` } : {};
}

async function get<T>(path: string): Promise<T> {
  const res = await fetch(path, { headers: authHeaders() });
  if (!res.ok) throw new Error(`GET ${path} → ${res.status} ${res.statusText}`);
  return res.json() as Promise<T>;
}

async function post<T>(path: string): Promise<T> {
  const res = await fetch(path, { method: 'POST', headers: authHeaders() });
  if (!res.ok) throw new Error(`POST ${path} → ${res.status} ${res.statusText}`);
  return res.json() as Promise<T>;
}

async function postJson<T>(path: string, body: unknown): Promise<T> {
  const res = await fetch(path, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', ...authHeaders() },
    body: JSON.stringify(body),
  });
  if (!res.ok) {
    const text = await res.text().catch(() => res.statusText);
    throw new Error(text || `POST ${path} → ${res.status}`);
  }
  return res.json() as Promise<T>;
}

async function putJson<T>(path: string, body: unknown): Promise<T> {
  const res = await fetch(path, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json', ...authHeaders() },
    body: JSON.stringify(body),
  });
  if (!res.ok) {
    const text = await res.text().catch(() => res.statusText);
    throw new Error(text || `PUT ${path} → ${res.status}`);
  }
  return res.json() as Promise<T>;
}

async function deleteReq(path: string): Promise<void> {
  const res = await fetch(path, { method: 'DELETE', headers: authHeaders() });
  if (!res.ok) throw new Error(`DELETE ${path} → ${res.status} ${res.statusText}`);
}

async function postForm<T>(path: string, body: FormData): Promise<T> {
  const res = await fetch(path, { method: 'POST', headers: authHeaders(), body });
  if (!res.ok) {
    const text = await res.text().catch(() => res.statusText);
    throw new Error(text || `POST ${path} → ${res.status}`);
  }
  return res.json() as Promise<T>;
}

// ── Public API surface ────────────────────────────────────────────────────────

export const api = {
  /** GET /api/series — list active weekly series */
  getSeries(): Promise<Series[]> {
    return get('/api/series');
  },

  /** GET /api/series/:seriesId/weeks/:weekNumber/cars — cars with aggregate lap stats */
  getCarsForWeek(seriesId: number, weekNumber: number): Promise<WeekCar[]> {
    return get(`/api/series/${seriesId}/weeks/${weekNumber}/cars`);
  },

  /** GET /api/series/:seriesId/weeks/:weekNumber/cars/:carId/percentile?customerId= */
  getPercentile(
    seriesId: number,
    weekNumber: number,
    carId: number,
    customerId: number,
  ): Promise<PercentileResult> {
    const qs = new URLSearchParams({ customerId: String(customerId) });
    return get(`/api/series/${seriesId}/weeks/${weekNumber}/cars/${carId}/percentile?${qs}`);
  },

  /** GET /api/users/me/recommendations?seriesId=&weekNumber= */
  getRecommendations(seriesId: number, weekNumber: number): Promise<CarRecommendation[]> {
    const qs = new URLSearchParams({ seriesId: String(seriesId), weekNumber: String(weekNumber) });
    return get(`/api/users/me/recommendations?${qs}`);
  },

  /** POST /api/auth/login — email + password sign-in, returns JWT */
  login(email: string, password: string): Promise<AuthResult> {
    return postJson('/api/auth/login', { email, password });
  },

  /** POST /api/auth/register — create account, returns JWT */
  register(email: string, password: string): Promise<AuthResult> {
    return postJson('/api/auth/register', { email, password });
  },

  /** PUT /api/auth/profile — update display name and optional iRacing customer ID, returns fresh JWT */
  updateProfile(displayName: string, iRacingCustomerId: number | null): Promise<AuthResult> {
    return putJson('/api/auth/profile', { displayName, iRacingCustomerId });
  },

  /** POST /api/auth/callback?code=&state= — OAuth 2.0 Authorization Code exchange */
  postAuthCallback(code: string, state: string): Promise<unknown> {
    const qs = new URLSearchParams({ code, state });
    return post(`/api/auth/callback?${qs}`);
  },

  /** POST /api/telemetry/upload — upload an iRacing .ibt file, returns extracted lap summary */
  uploadTelemetry(file: File): Promise<TelemetryUploadResult> {
    const form = new FormData();
    form.append('file', file);
    return postForm('/api/telemetry/upload', form);
  },

  /** GET /api/telemetry/laps — personal best per track+car for the authenticated user */
  getMyLaps(): Promise<PersonalLap[]> {
    return get('/api/telemetry/laps');
  },

  /** GET /api/users/me/analytics?seriesId= — per-car percentile history and trend for the authenticated user */
  getMyAnalytics(seriesId?: number): Promise<CarAnalytics[]> {
    const path =
      seriesId != null
        ? `/api/users/me/analytics?seriesId=${seriesId}`
        : '/api/users/me/analytics';
    return get(path);
  },

  /** PUT /api/auth/role — self-assign Standard, Beta, or Alpha role, returns fresh JWT */
  updateRole(role: string): Promise<AuthResult> {
    return putJson('/api/auth/role', { role });
  },

  /** GET /api/feature-flags — flags the authenticated user is entitled to see */
  getFeatureFlags(): Promise<FeatureFlag[]> {
    return get('/api/feature-flags');
  },

  // ── Admin ─────────────────────────────────────────────────────────────────

  /** GET /api/admin/users */
  getAdminUsers(): Promise<AdminUser[]> {
    return get('/api/admin/users');
  },

  /** PUT /api/admin/users/:userId/role */
  setAdminUserRole(userId: string, role: string): Promise<AdminUser> {
    return putJson(`/api/admin/users/${userId}/role`, { role });
  },

  /** GET /api/admin/feature-flags — all flags regardless of enabled state */
  getAdminFeatureFlags(): Promise<FeatureFlag[]> {
    return get('/api/admin/feature-flags');
  },

  /** POST /api/admin/feature-flags */
  createFeatureFlag(data: {
    key: string;
    name: string;
    description: string | null;
    isEnabled: boolean;
    minimumRole: string;
  }): Promise<FeatureFlag> {
    return postJson('/api/admin/feature-flags', data);
  },

  /** PUT /api/admin/feature-flags/:id */
  updateFeatureFlag(id: number, data: {
    name: string;
    description: string | null;
    isEnabled: boolean;
    minimumRole: string;
  }): Promise<FeatureFlag> {
    return putJson(`/api/admin/feature-flags/${id}`, data);
  },

  /** DELETE /api/admin/feature-flags/:id */
  deleteFeatureFlag(id: number): Promise<void> {
    return deleteReq(`/api/admin/feature-flags/${id}`);
  },
};
