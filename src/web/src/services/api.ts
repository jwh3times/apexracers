// Types matching the backend controller response shapes

export interface AuthResult {
  token: string;
  userId: string;
  displayName: string;
  refreshToken?: string;
}

export interface Series {
  id: number;
  name: string;
  seasonId: number;
  currentWeekNumber: number | null;
  category: string | null;
  trackName: string | null;
  trackConfigName: string | null;
  carCount: number;
  driverCount: number;
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
  totalWeeks: number;
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
  className: string | null;
  entryCount: number;
  fastestLapSeconds: number | null;
  medianLapSeconds: number | null;
}

export interface WeekDetail {
  seriesName: string;
  category: string | null;
  trackName: string | null;
  trackConfigName: string | null;
  trackLengthMiles: number | null;
  cars: WeekCar[];
}

export interface DistributionBin {
  minSeconds: number;
  maxSeconds: number;
  count: number;
  containsUser: boolean;
}

export interface PercentileResult {
  seriesId: number;
  weekNumber: number;
  carId: number;
  customerId: number;
  percentileRank: number;
  sampleSize: number;
  computedAt: string; // ISO 8601
  seriesName: string;
  trackName: string | null;
  trackConfigName: string | null;
  yourBestLapSeconds: number;
  fieldBestLapSeconds: number;
  fieldMedianLapSeconds: number;
  distribution: DistributionBin[];
}

export interface CarRecommendation {
  rank: number;
  carId: number;
  carName: string;
  percentileRank: number;
  sampleSize: number;
  projectedLapSeconds: number;
  bestLapSeconds: number | null;
}

export type LapSessionType =
  | 'Unknown'
  | 'Practice'
  | 'Qualifying'
  | 'TimeTrial'
  | 'Race'
  | 'LoneQualify';

export interface RecommendationOptions {
  includePersonalLaps?: boolean;
  personalLapTypes?: LapSessionType[];
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
let _refreshToken: string | null = null;
let _onTokenRefreshed: ((token: string, refreshToken: string) => void) | null = null;
let _onSessionExpired: (() => void) | null = null;
let _refreshPromise: Promise<boolean> | null = null;

export function setToken(token: string): void {
  _token = token;
}
export function setRefreshToken(token: string | null): void {
  _refreshToken = token;
}
export function clearToken(): void {
  _token = null;
  _refreshToken = null;
}
export function onTokenRefreshed(cb: (token: string, refreshToken: string) => void): void {
  _onTokenRefreshed = cb;
}
export function onSessionExpired(cb: () => void): void {
  _onSessionExpired = cb;
}

function authHeaders(): Record<string, string> {
  return _token ? { Authorization: `Bearer ${_token}` } : {};
}

async function tryRefresh(): Promise<boolean> {
  if (!_refreshToken) return false;
  if (_refreshPromise) return _refreshPromise;

  _refreshPromise = (async (): Promise<boolean> => {
    try {
      const res = await fetch('/api/auth/refresh', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ refreshToken: _refreshToken }),
      });
      if (!res.ok) {
        _onSessionExpired?.();
        return false;
      }
      const data = (await res.json()) as AuthResult;
      _token = data.token;
      if (data.refreshToken) _refreshToken = data.refreshToken;
      _onTokenRefreshed?.(data.token, data.refreshToken ?? '');
      return true;
    } catch {
      _onSessionExpired?.();
      return false;
    } finally {
      _refreshPromise = null;
    }
  })();

  return _refreshPromise;
}

/**
 * Error thrown when a per-user endpoint reports the caller has not linked an
 * iRacing customer ID (HTTP 409 with code IRACING_NOT_LINKED). Pages catch this to
 * show a "link your iRacing account" prompt instead of a generic error or an empty
 * result.
 */
export class IRacingNotLinkedError extends Error {
  readonly code = 'IRACING_NOT_LINKED';
  constructor(message: string) {
    super(message);
    this.name = 'IRacingNotLinkedError';
  }
}

function tryParseJson(raw: string): { code?: string; message?: string; detail?: string } | null {
  try {
    return raw ? (JSON.parse(raw) as { code?: string; message?: string; detail?: string }) : null;
  } catch {
    return null;
  }
}

// Maps a non-ok response to the right thrown error: a typed IRacingNotLinkedError
// for the 409 not-linked contract, otherwise an Error carrying the most useful
// message available — an RFC-7807 ProblemDetails `detail`, then the raw body, then
// the status line.
async function throwForResponse(res: Response, path: string, method: string): Promise<never> {
  const raw = await res.text().catch(() => '');
  const parsed = tryParseJson(raw);
  if (res.status === 409 && parsed?.code === 'IRACING_NOT_LINKED') {
    throw new IRacingNotLinkedError(parsed.message ?? 'iRacing account not linked.');
  }
  const detail = parsed?.detail ?? parsed?.message;
  throw new Error(detail || raw || `${method} ${path} → ${res.status} ${res.statusText}`);
}

type ReqInit = { method?: string; body?: BodyInit; json?: unknown };

/**
 * Single fetch wrapper for the whole API surface: attaches auth headers, retries
 * once after a silent token refresh on 401, maps errors via throwForResponse, and
 * returns undefined for 204 responses. Pass a JSON payload via `json`; pass a raw
 * body (e.g. FormData) via `body`.
 */
async function request<T>(path: string, init: ReqInit = {}): Promise<T> {
  const build = (): RequestInit => {
    const headers: Record<string, string> = { ...authHeaders() };
    let body = init.body;
    if (init.json !== undefined) {
      headers['Content-Type'] = 'application/json';
      body = JSON.stringify(init.json);
    }
    return { method: init.method ?? 'GET', headers, body };
  };

  let res = await fetch(path, build());
  if (res.status === 401 && (await tryRefresh())) {
    res = await fetch(path, build());
  }
  if (!res.ok) return throwForResponse(res, path, init.method ?? 'GET');
  if (res.status === 204) return undefined as T;
  return res.json() as Promise<T>;
}

// ── Public API surface ────────────────────────────────────────────────────────

export const api = {
  /** GET /api/series — list active weekly series */
  getSeries(): Promise<Series[]> {
    return request('/api/series');
  },

  /** GET /api/series/:seriesId/weeks/:weekNumber — series + track metadata and full car breakdown */
  getWeekDetail(seriesId: number, weekNumber: number): Promise<WeekDetail> {
    return request(`/api/series/${seriesId}/weeks/${weekNumber}`);
  },

  /** GET /api/series/:seriesId/weeks/:weekNumber/cars — cars with aggregate lap stats */
  getCarsForWeek(seriesId: number, weekNumber: number): Promise<WeekCar[]> {
    return request(`/api/series/${seriesId}/weeks/${weekNumber}/cars`);
  },

  /** GET /api/series/:seriesId/weeks/:weekNumber/cars/:carId/percentile?customerId= */
  getPercentile(
    seriesId: number,
    weekNumber: number,
    carId: number,
    customerId: number,
    options?: RecommendationOptions
  ): Promise<PercentileResult> {
    const qs = new URLSearchParams({ customerId: String(customerId) });
    if (options?.includePersonalLaps) qs.set('includePersonalLaps', 'true');
    options?.personalLapTypes?.forEach(t => qs.append('personalLapTypes', t));
    return request(`/api/series/${seriesId}/weeks/${weekNumber}/cars/${carId}/percentile?${qs}`);
  },

  /** GET /api/users/me/recommendations?seriesId=&weekNumber= */
  getRecommendations(
    seriesId: number,
    weekNumber: number,
    options?: RecommendationOptions
  ): Promise<CarRecommendation[]> {
    const qs = new URLSearchParams({ seriesId: String(seriesId), weekNumber: String(weekNumber) });
    if (options?.includePersonalLaps) qs.set('includePersonalLaps', 'true');
    options?.personalLapTypes?.forEach(t => qs.append('personalLapTypes', t));
    return request(`/api/users/me/recommendations?${qs}`);
  },

  /** POST /api/auth/login — email + password sign-in, returns JWT */
  login(email: string, password: string): Promise<AuthResult> {
    return request('/api/auth/login', { method: 'POST', json: { email, password } });
  },

  /** POST /api/auth/register — create account, returns JWT */
  register(email: string, password: string): Promise<AuthResult> {
    return request('/api/auth/register', { method: 'POST', json: { email, password } });
  },

  /** PUT /api/auth/profile — update display name, email, and optional iRacing customer ID, returns fresh JWT */
  updateProfile(
    displayName: string,
    iRacingCustomerId: number | null,
    email: string
  ): Promise<AuthResult> {
    return request('/api/auth/profile', {
      method: 'PUT',
      json: { displayName, iRacingCustomerId, email },
    });
  },

  /** POST /api/auth/callback?code=&state= — OAuth 2.0 Authorization Code exchange */
  postAuthCallback(code: string, state: string): Promise<unknown> {
    const qs = new URLSearchParams({ code, state });
    return request(`/api/auth/callback?${qs}`, { method: 'POST' });
  },

  /** POST /api/telemetry/upload — upload an iRacing .ibt file, returns extracted lap summary */
  uploadTelemetry(file: File): Promise<TelemetryUploadResult> {
    const form = new FormData();
    form.append('file', file);
    return request('/api/telemetry/upload', { method: 'POST', body: form });
  },

  /** GET /api/telemetry/laps — personal best per track+car for the authenticated user */
  getMyLaps(): Promise<PersonalLap[]> {
    return request('/api/telemetry/laps');
  },

  /** GET /api/users/me/analytics?seriesId= — per-car percentile history and trend for the authenticated user */
  getMyAnalytics(seriesId?: number): Promise<CarAnalytics[]> {
    const path =
      seriesId != null ? `/api/users/me/analytics?seriesId=${seriesId}` : '/api/users/me/analytics';
    return request(path);
  },

  /** PUT /api/auth/theme — update theme preference (auto/light/dark), returns fresh JWT */
  updateTheme(themePreference: string): Promise<AuthResult> {
    return request('/api/auth/theme', { method: 'PUT', json: { themePreference } });
  },

  /** PUT /api/auth/role — self-assign Standard, Beta, or Alpha role, returns fresh JWT */
  updateRole(role: string): Promise<AuthResult> {
    return request('/api/auth/role', { method: 'PUT', json: { role } });
  },

  /** POST /api/auth/refresh — exchange a refresh token for a new access + refresh token pair */
  refreshTokens(refreshToken: string): Promise<AuthResult> {
    return request('/api/auth/refresh', { method: 'POST', json: { refreshToken } });
  },

  /** POST /api/auth/logout — revoke a refresh token (best-effort, never throws) */
  revokeToken(refreshToken: string): Promise<void> {
    return fetch('/api/auth/logout', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', ...authHeaders() },
      body: JSON.stringify({ refreshToken }),
    })
      .then(() => void 0)
      .catch(() => void 0);
  },

  /** GET /api/feature-flags — flags the authenticated user is entitled to see */
  getFeatureFlags(): Promise<FeatureFlag[]> {
    return request('/api/feature-flags');
  },

  // ── Admin ─────────────────────────────────────────────────────────────────

  /** GET /api/admin/users */
  getAdminUsers(): Promise<AdminUser[]> {
    return request('/api/admin/users');
  },

  /** PUT /api/admin/users/:userId/role */
  setAdminUserRole(userId: string, role: string): Promise<AdminUser> {
    return request(`/api/admin/users/${userId}/role`, { method: 'PUT', json: { role } });
  },

  /** GET /api/admin/feature-flags — all flags regardless of enabled state */
  getAdminFeatureFlags(): Promise<FeatureFlag[]> {
    return request('/api/admin/feature-flags');
  },

  /** POST /api/admin/feature-flags */
  createFeatureFlag(data: {
    key: string;
    name: string;
    description: string | null;
    isEnabled: boolean;
    minimumRole: string;
  }): Promise<FeatureFlag> {
    return request('/api/admin/feature-flags', { method: 'POST', json: data });
  },

  /** PUT /api/admin/feature-flags/:id */
  updateFeatureFlag(
    id: number,
    data: {
      name: string;
      description: string | null;
      isEnabled: boolean;
      minimumRole: string;
    }
  ): Promise<FeatureFlag> {
    return request(`/api/admin/feature-flags/${id}`, { method: 'PUT', json: data });
  },

  /** DELETE /api/admin/feature-flags/:id */
  deleteFeatureFlag(id: number): Promise<void> {
    return request(`/api/admin/feature-flags/${id}`, { method: 'DELETE' });
  },
};
