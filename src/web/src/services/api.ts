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
  worldRecordLapSeconds: number | null;
  worldRecordGapSeconds: number | null;
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

export interface TimeSeriesPoint {
  when: string; // ISO date (yyyy-MM-dd)
  value: number;
}

export interface CategoryProgression {
  categoryId: number;
  categoryName: string;
  iRating: number;
  safetyRating: number;
  cpi: number;
  licenseLevel: number;
  groupName: string;
  ttRating: number;
  color: string; // hex without leading '#'
  iRatingHistory: TimeSeriesPoint[];
}

export interface MemberProgression {
  customerId: number;
  categories: CategoryProgression[];
}

export interface LicenseBadge {
  categoryId: number;
  categoryName: string;
  groupName: string;
  licenseLevel: number;
  safetyRating: number;
  iRating: number;
  color: string; // hex without leading '#'
}

export interface CategoryCareer {
  categoryId: number;
  categoryName: string;
  starts: number;
  wins: number;
  top5: number;
  poles: number;
  avgStartPosition: number;
  avgFinishPosition: number;
  laps: number;
  lapsLed: number;
  winPercentage: number;
  top5Percentage: number;
}

export interface ThisYearSummary {
  officialSessions: number;
  officialWins: number;
  leagueSessions: number;
  leagueWins: number;
}

export interface FavoriteCar {
  carId: number;
  carName: string;
  imageUrl: string | null;
}

export interface FavoriteTrack {
  trackId: number;
  trackName: string;
  configName: string | null;
  logoUrl: string | null;
}

export interface DriverProfile {
  customerId: number;
  displayName: string;
  country: string | null;
  countryCode: string | null;
  memberSince: string | null;
  licenses: LicenseBadge[];
  career: CategoryCareer[];
  thisYear: ThisYearSummary;
  favoriteCar: FavoriteCar | null;
  favoriteTrack: FavoriteTrack | null;
}

export interface RaceHistoryRow {
  subsessionId: number;
  startTime: string; // ISO 8601
  seriesName: string;
  trackName: string;
  carId: number;
  carName: string;
  startPosition: number;
  finishPosition: number;
  incidents: number;
  iRatingDelta: number;
  srDelta: number; // SR points (sub-level / 100)
  strengthOfField: number;
  points: number;
}

export interface Weather {
  tempCelsius: number;
  relHumidity: number;
  windKph: number;
  skies: number;
  precipChance: number;
}

export interface SubsessionResultRow {
  custId: number;
  driverName: string;
  finishPosition: number;
  startPosition: number;
  bestLapSeconds: number;
  averageLapSeconds: number;
  interval: number;
  lapsLead: number;
  incidents: number;
  division: number;
  iRatingDelta: number;
  srDelta: number;
}

export interface SubsessionDetail {
  subsessionId: number;
  startTime: string; // ISO 8601
  seriesName: string;
  trackName: string;
  trackConfigName: string | null;
  strengthOfField: number;
  numCautions: number;
  numLeadChanges: number;
  cornersPerLap: number;
  eventBestLapSeconds: number;
  eventAverageLapSeconds: number;
  eventLapsComplete: number;
  weather: Weather | null;
  results: SubsessionResultRow[];
}

export interface Lap {
  lapNumber: number;
  lapTimeSeconds: number; // -1 when the lap has no time
  incident: boolean;
  valid: boolean;
}

export interface DriverLaps {
  subsessionId: number;
  custId: number;
  meanSeconds: number;
  stdDevSeconds: number;
  fastestLapSeconds: number;
  degSlopeSecondsPerLap: number;
  laps: Lap[];
}

export interface WeatherSummary {
  tempHighC: number;
  tempLowC: number;
  precipChancePct: number;
  windHighKph: number;
  windLowKph: number;
  skies: number;
}

export interface CarBop {
  carId: number;
  carName: string;
  weightPenaltyKg: number;
  powerAdjustPct: number;
  maxPctFuelFill: number;
  maxDryTireSets: number;
}

export interface ScheduleWeek {
  weekNumber: number;
  trackName: string;
  configName: string;
  startDate: string; // ISO date (yyyy-MM-dd)
  weather: WeatherSummary | null;
  bop: CarBop[];
  hasPersonalBest: boolean;
}

export interface SeasonSchedule {
  seriesId: number;
  seriesName: string;
  weeks: ScheduleWeek[];
}

export interface GlobalLeaderboardEntry {
  categoryId: number;
  rank: number;
  custId: number;
  driver: string;
  location: string;
  starts: number;
  wins: number;
  iRating: number;
  ttRating: number;
  champPoints: number;
}

export interface RaceGuideEntry {
  seriesId: number;
  seriesName: string;
  startTime: string; // ISO 8601
  endTime: string; // ISO 8601
  entryCount: number;
  raceWeekNum: number;
}

export interface CarClassOption {
  carClassId: number;
  carClassName: string;
}

export interface SeasonStanding {
  rank: number;
  custId: number;
  driverName: string;
  division: number;
  starts: number;
  wins: number;
  top5: number;
  poles: number;
  points: number;
  avgFinishPosition: number;
  incidents: number;
}

export interface SeasonStandings {
  seriesId: number;
  seriesName: string;
  carClassId: number;
  carClassName: string;
  carClasses: CarClassOption[];
  standings: SeasonStanding[];
}

export interface SeasonTtStanding {
  rank: number;
  custId: number;
  driverName: string;
  division: number;
  ttRating: number | null;
  starts: number;
  wins: number;
  top5: number;
  poles: number;
  points: number;
  avgFinishPosition: number;
  incidents: number;
}

export interface SeasonTtStandings {
  seriesId: number;
  seriesName: string;
  carClassId: number;
  carClassName: string;
  carClasses: CarClassOption[];
  standings: SeasonTtStanding[];
}

export interface SeasonQualifyResult {
  rank: number;
  custId: number;
  driverName: string;
  division: number;
  iRating: number | null;
  bestQualLapSeconds: number;
  week: number;
}

export interface SeasonQualifyResults {
  seriesId: number;
  seriesName: string;
  carClassId: number;
  carClassName: string;
  carClasses: CarClassOption[];
  raceWeekNum: number;
  availableWeeks: number[];
  results: SeasonQualifyResult[];
}

// ── Rival comparison (3.1) ──────────────────────────────────────────────────

export interface Rival {
  custId: number;
  displayName: string;
  createdAt: string; // ISO 8601
}

export interface DriverSearchResult {
  custId: number;
  displayName: string;
}

export interface RivalSuggestion {
  custId: number;
  displayName: string;
  sharedRaces: number;
}

export interface CategoryHistory {
  categoryId: number;
  categoryName: string;
  points: TimeSeriesPoint[];
}

export interface ComparisonSide {
  custId: number;
  displayName: string;
  country: string | null;
  countryCode: string | null;
  memberSince: string | null;
  licenses: LicenseBadge[];
  career: CategoryCareer[];
  iRatingHistory: CategoryHistory[];
}

export interface SharedRaceRow {
  subsessionId: number;
  startTime: string; // ISO 8601
  trackName: string;
  yourFinish: number;
  rivalFinish: number;
  yourIRatingDelta: number;
  rivalIRatingDelta: number;
  yourIncidents: number;
  rivalIncidents: number;
}

export interface SharedTrackPace {
  trackName: string;
  yourBestLapSeconds: number; // -1 = no valid lap
  rivalBestLapSeconds: number;
}

export interface SharedRaceSummary {
  totalShared: number;
  youAhead: number;
  rivalAhead: number;
  races: SharedRaceRow[];
  trackPace: SharedTrackPace[];
}

export interface DriverComparison {
  you: ComparisonSide;
  rival: ComparisonSide;
  shared: SharedRaceSummary;
}

// ── Catalog explorer (3.5) ──────────────────────────────────────────────────

export interface CarClassRef {
  carClassId: number;
  name: string;
}

export interface CarCatalogItem {
  carId: number;
  name: string;
  nameAbbreviated: string;
  make: string | null;
  model: string | null;
  hp: number | null;
  weight: number | null;
  rainEnabled: boolean;
  freeWithSubscription: boolean;
  categories: string[];
  smallImageUrl: string | null;
}

export interface CarCatalogDetail extends CarCatalogItem {
  carTypes: string[];
  largeImageUrl: string | null;
  logoUrl: string | null;
  carClasses: CarClassRef[];
  yourBestLaps: PersonalLap[];
}

export interface TrackCatalogItem {
  trackId: number;
  name: string;
  configName: string;
  category: string | null;
  lengthMiles: number | null;
  cornersPerLap: number | null;
  location: string | null;
  nightLighting: boolean;
  smallImageUrl: string | null;
}

export interface TrackCatalogDetail extends TrackCatalogItem {
  latitude: number | null;
  longitude: number | null;
  pitRoadSpeedLimit: number | null;
  numberPitstalls: number | null;
  hasSvgMap: boolean;
  largeImageUrl: string | null;
  trackMapUrl: string | null;
  yourBestLaps: PersonalLap[];
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

  /** GET /api/users/me/progression — per-category iRating / SR / CPI / TT with iRating history */
  getProgression(): Promise<MemberProgression> {
    return request('/api/users/me/progression');
  },

  /** GET /api/users/me/profile-stats — career stats, license badges, recap favorites */
  getProfileStats(): Promise<DriverProfile> {
    return request('/api/users/me/profile-stats');
  },

  /** GET /api/users/me/races — recent official race history (newest first) */
  getRaceHistory(): Promise<RaceHistoryRow[]> {
    return request('/api/users/me/races');
  },

  /** GET /api/subsessions/:id — full classified field + session context for one race */
  getSubsession(id: number): Promise<SubsessionDetail> {
    return request(`/api/subsessions/${id}`);
  },

  /** GET /api/subsessions/:id/laps?customerId= — a driver's per-lap pace (defaults to caller) */
  getDriverLaps(subsessionId: number, customerId?: number): Promise<DriverLaps> {
    const qs = customerId != null ? `?customerId=${customerId}` : '';
    return request(`/api/subsessions/${subsessionId}/laps${qs}`);
  },

  /** GET /api/series/:seriesId/schedule — active-season calendar with weather, BoP, PB overlay */
  getSchedule(seriesId: number): Promise<SeasonSchedule> {
    return request(`/api/series/${seriesId}/schedule`);
  },

  /** GET /api/leaderboards?categoryId= — global top-N drivers for a category (ranked by iRating) */
  getLeaderboard(categoryId: number): Promise<GlobalLeaderboardEntry[]> {
    return request(`/api/leaderboards?categoryId=${categoryId}`);
  },

  /** GET /api/series/:seriesId/standings?carClassId= — active-season championship standings */
  getStandings(seriesId: number, carClassId?: number): Promise<SeasonStandings> {
    const qs = carClassId != null ? `?carClassId=${carClassId}` : '';
    return request(`/api/series/${seriesId}/standings${qs}`);
  },

  /** GET /api/series/:seriesId/tt-standings?carClassId= — active-season Time Trial standings */
  getTtStandings(seriesId: number, carClassId?: number): Promise<SeasonTtStandings> {
    const qs = carClassId != null ? `?carClassId=${carClassId}` : '';
    return request(`/api/series/${seriesId}/tt-standings${qs}`);
  },

  /** GET /api/series/:seriesId/qualify-results?carClassId=&weekNumber= — weekly qualifying results */
  getQualifyResults(
    seriesId: number,
    carClassId?: number,
    weekNumber?: number
  ): Promise<SeasonQualifyResults> {
    const params = new URLSearchParams();
    if (carClassId != null) params.set('carClassId', String(carClassId));
    if (weekNumber != null) params.set('weekNumber', String(weekNumber));
    const qs = params.toString();
    return request(`/api/series/${seriesId}/qualify-results${qs ? `?${qs}` : ''}`);
  },

  /** GET /api/race-guide — official sessions starting in the next ~3h (race-now board) */
  getRaceGuide(): Promise<RaceGuideEntry[]> {
    return request('/api/race-guide');
  },

  /** GET /api/users/me/rivals — drivers the caller follows for comparison (newest first) */
  getRivals(): Promise<Rival[]> {
    return request('/api/users/me/rivals');
  },

  /** POST /api/users/me/rivals — follow a driver (idempotent) */
  addRival(custId: number, displayName?: string): Promise<Rival> {
    return request('/api/users/me/rivals', { method: 'POST', json: { custId, displayName } });
  },

  /** DELETE /api/users/me/rivals/:custId — unfollow a driver */
  removeRival(custId: number): Promise<void> {
    return request(`/api/users/me/rivals/${custId}`, { method: 'DELETE' });
  },

  /** GET /api/users/me/rivals/search?term= — driver name search */
  searchDrivers(term: string): Promise<DriverSearchResult[]> {
    return request(`/api/users/me/rivals/search?term=${encodeURIComponent(term)}`);
  },

  /** GET /api/users/me/rivals/suggestions — drivers the caller has raced (409 if unlinked) */
  getRivalSuggestions(): Promise<RivalSuggestion[]> {
    return request('/api/users/me/rivals/suggestions');
  },

  /** GET /api/users/me/compare?rivalCustId= — head-to-head comparison (409 if unlinked) */
  compareRival(rivalCustId: number): Promise<DriverComparison> {
    return request(`/api/users/me/compare?rivalCustId=${rivalCustId}`);
  },

  /** GET /api/cars — full car catalog (browse grid) */
  getCars(): Promise<CarCatalogItem[]> {
    return request('/api/cars');
  },

  /** GET /api/cars/:id — car detail (specs, classes, your best laps when signed in) */
  getCar(carId: number): Promise<CarCatalogDetail> {
    return request(`/api/cars/${carId}`);
  },

  /** GET /api/tracks — full track catalog (browse grid) */
  getTracks(): Promise<TrackCatalogItem[]> {
    return request('/api/tracks');
  },

  /** GET /api/tracks/:id — track detail (specs, map, your best laps when signed in) */
  getTrack(trackId: number): Promise<TrackCatalogDetail> {
    return request(`/api/tracks/${trackId}`);
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
