import { createHttpClient } from './http';
import { session } from './session';

// Types matching the backend controller response shapes

export interface AuthResult {
  token: string;
  userId: string;
  displayName: string;
  refreshToken?: string;
}

export interface ForgotPasswordResult {
  message: string;
  // Only populated in the API's Development environment so the reset flow is testable
  // without an email provider; null in every other environment.
  resetToken: string | null;
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
  topSharePercent: number;
  sampleSize: number;
  computedAt: string; // ISO 8601
}

/**
 * Which kind of evidence produced a lap. A personal best drawn from either one names the evidence
 * it came from, because the two are never interchangeable.
 */
export type LapEvidence = 'RaceLap' | 'UploadedLap';

/**
 * The fastest uploaded lap for this car and track that the race week's bound left out, and the
 * date it was driven. Present only when it is faster than the personal best that was ranked — a
 * slower excluded lap would have changed nothing, so reporting it would be noise.
 */
export interface UploadedBestOutsideWeek {
  lapSeconds: number;
  recordedAt: string; // ISO 8601
}

export interface CarAnalytics {
  carId: number;
  carName: string;
  seriesId: number;
  seriesName: string;
  latestPercentileRank: number;
  latestTopSharePercent: number;
  bestPercentileRank: number;
  bestTopSharePercent: number;
  personalBestLapSeconds: number | null;
  /** Which evidence produced personalBestLapSeconds; null exactly when that lap is. */
  personalBestLapEvidence: LapEvidence | null;
  medianLapSeconds: number | null;
  totalWeeks: number;
  percentileHistory: WeeklyPercentile[];
}

export interface UploadedBest {
  carId: number;
  carName: string;
  trackId: number; // the track identity; trackName is the venue's and is shared by every layout
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

export interface WeekCarPercentile {
  carId: number;
  percentileRank: number; // higher is better (e.g. 92 → faster than 92% of the field)
  topSharePercent: number; // placement share of the field; lower is better
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
  fieldPosition: number;
  topSharePercent: number;
  sampleSize: number;
  isPercentilePresentable: boolean;
  computedAt: string; // ISO 8601
  seriesName: string;
  trackName: string | null;
  trackConfigName: string | null;
  yourBestLapSeconds: number;
  /** Which evidence produced yourBestLapSeconds. The field itself is all race laps. */
  yourBestLapEvidence: LapEvidence;
  fieldBestLapSeconds: number;
  fieldMedianLapSeconds: number;
  distribution: DistributionBin[];
  worldRecordLapSeconds: number | null;
  worldRecordGapSeconds: number | null;
  uploadedBestOutsideWeek: UploadedBestOutsideWeek | null;
}

export interface CarRecommendation {
  rank: number;
  carId: number;
  carName: string;
  percentileRank: number;
  topSharePercent: number | null;
  sampleSize: number;
  isPercentilePresentable: boolean;
  projectedLapSeconds: number;
  bestLapSeconds: number | null;
  /** Which evidence produced bestLapSeconds; null exactly when that lap is. */
  bestLapEvidence: LapEvidence | null;
}

export type LapSessionType =
  'Unknown' | 'Practice' | 'Qualifying' | 'TimeTrial' | 'Race' | 'LoneQualify';

export interface PersonalBestEvidenceOptions {
  includeUploadedLaps?: boolean;
  uploadedLapTypes?: LapSessionType[];
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

export interface DriverProgression {
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

export interface Award {
  awardId: number;
  name: string;
  description: string | null;
  groupName: string | null;
  count: number;
  awardDate: string; // ISO 8601
  iconUrl: string | null;
  iconBackgroundColor: string | null;
  progress: number;
  threshold: number;
}

export interface Achievements {
  customerId: number;
  awardCount: number;
  awards: Award[];
}

export interface RaceHistoryRow {
  subsessionId: number;
  startTime: string; // ISO 8601
  seriesName: string;
  trackId: number; // 0 = iRacing named no track; the name alone is the venue's
  trackName: string;
  configName: string | null; // null when the track has none or isn't in the local catalog
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
  /** Zero-based Split Index; null when unknown — never 0 standing in for unknown. */
  splitIndex: number | null;
  /** How many Splits the Race Session divided into; null exactly when splitIndex is. */
  splitCount: number | null;
  /** Entries that raced under a team and so produced no result; null when never counted. */
  teamEntryCount: number | null;
  /** AI entries, which hold no racing identity; null when never counted. */
  aiEntryCount: number | null;
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
  hasUploadedLapAtTrack: boolean;
}

export interface SeasonSchedule {
  seriesId: number;
  seriesName: string;
  weeks: ScheduleWeek[];
}

export interface WeatherRisk {
  level: string; // "Low" | "Medium" | "High"
  precipChancePct: number;
  fieldRainCapable: boolean;
  note: string;
}

export interface CarStrategy {
  carId: number;
  carName: string;
  weightPenaltyKg: number;
  powerAdjustPct: number;
  maxPctFuelFill: number;
  maxDryTireSets: number;
  weightDeltaKg: number;
  powerDeltaPct: number;
  bopTrend: string; // "Nerfed" | "Buffed" | "Mixed" | "Unchanged" | "—"
  fuelCapped: boolean;
  fuelNote: string;
  limitedTireSets: boolean;
  tireNote: string;
  rainEnabled: boolean;
  percentileRank: number | null;
  topSharePercent: number | null;
  fieldSize: number | null;
  isPercentilePresentable: boolean;
  projectedLapSeconds: number | null;
  optimalRank: number | null;
}

export interface WeekStrategy {
  seriesId: number;
  seriesName: string;
  weekNumber: number;
  trackName: string;
  configName: string;
  trackLengthMiles: number | null;
  cornersPerLap: number | null;
  numberPitstalls: number | null;
  pitRoadSpeedLimit: number | null;
  nightLighting: boolean;
  weather: WeatherSummary | null;
  weatherRisk: WeatherRisk;
  personalized: boolean;
  cars: CarStrategy[];
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
  trackId: number; // the track identity — a name alone is the venue's, shared by every layout
  trackName: string;
  configName: string | null;
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
  yourUploadedBests: UploadedBest[];
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
  yourUploadedBests: UploadedBest[];
}

// ── Internal helpers ─────────────────────────────────────────────────────────────

// The request core lives in ./http and the session in ./session — see those modules. Re-exported
// here so the ~90 existing `from '../services/api'` imports keep working and the error classes
// have exactly one identity across the app.
export { ApiError, IRacingNotLinkedError } from './http';
export { session } from './session';

const http = createHttpClient({
  fetch: (input, init) => fetch(input, init),
  getAccessToken: () => session.accessToken,
  refresh: () => session.refresh(),
});

const request = http.request.bind(http);

function appendPersonalBestEvidence(
  params: URLSearchParams,
  options?: PersonalBestEvidenceOptions
): URLSearchParams {
  if (options?.includeUploadedLaps) params.set('includeUploadedLaps', 'true');
  options?.uploadedLapTypes?.forEach(type => params.append('uploadedLapTypes', type));
  return params;
}

// ── Public API surface ────────────────────────────────────────────────────────

export const api = {
  /** GET /api/series — list active weekly series */
  getSeries(signal?: AbortSignal): Promise<Series[]> {
    return request('/api/series', { signal });
  },

  /** GET /api/series/:seriesId/weeks/:weekNumber — series + track metadata and full car breakdown */
  getWeekDetail(seriesId: number, weekNumber: number, signal?: AbortSignal): Promise<WeekDetail> {
    return request(`/api/series/${seriesId}/weeks/${weekNumber}`, { signal });
  },

  /** GET /api/series/:seriesId/weeks/:weekNumber/cars — cars with aggregate lap stats */
  getCarsForWeek(seriesId: number, weekNumber: number): Promise<WeekCar[]> {
    return request(`/api/series/${seriesId}/weeks/${weekNumber}/cars`);
  },

  /** GET /api/series/:seriesId/weeks/:weekNumber/my-percentiles — the caller's per-car percentile
   * for the week (only cars they've raced). Authorize; throws IRacingNotLinkedError when unlinked. */
  getMyWeekPercentiles(
    seriesId: number,
    weekNumber: number,
    options?: PersonalBestEvidenceOptions,
    signal?: AbortSignal
  ): Promise<WeekCarPercentile[]> {
    const qs = appendPersonalBestEvidence(new URLSearchParams(), options).toString();
    return request(
      `/api/series/${seriesId}/weeks/${weekNumber}/my-percentiles${qs ? `?${qs}` : ''}`,
      { signal }
    );
  },

  /** GET /api/series/:seriesId/weeks/:weekNumber/cars/:carId/percentile?customerId= */
  getPercentile(
    seriesId: number,
    weekNumber: number,
    carId: number,
    customerId: number,
    options?: PersonalBestEvidenceOptions,
    signal?: AbortSignal
  ): Promise<PercentileResult> {
    const qs = new URLSearchParams({ customerId: String(customerId) });
    appendPersonalBestEvidence(qs, options);
    return request(`/api/series/${seriesId}/weeks/${weekNumber}/cars/${carId}/percentile?${qs}`, {
      signal,
    });
  },

  /** GET /api/users/me/recommendations?seriesId=&weekNumber= */
  getRecommendations(
    seriesId: number,
    weekNumber: number,
    options?: PersonalBestEvidenceOptions,
    signal?: AbortSignal
  ): Promise<CarRecommendation[]> {
    const qs = new URLSearchParams({ seriesId: String(seriesId), weekNumber: String(weekNumber) });
    appendPersonalBestEvidence(qs, options);
    return request(`/api/users/me/recommendations?${qs}`, { signal });
  },

  /** POST /api/auth/login — email + password sign-in, returns JWT */
  login(email: string, password: string): Promise<AuthResult> {
    return request('/api/auth/login', { method: 'POST', json: { email, password } });
  },

  /** POST /api/auth/register — create account, returns JWT */
  register(email: string, password: string): Promise<AuthResult> {
    return request('/api/auth/register', { method: 'POST', json: { email, password } });
  },

  /** PUT /api/auth/profile — update display name and optional iRacing customer ID, returns fresh JWT */
  updateProfile(displayName: string, iRacingCustomerId: number | null): Promise<AuthResult> {
    return request('/api/auth/profile', {
      method: 'PUT',
      json: { displayName, iRacingCustomerId },
    });
  },

  /** POST /api/auth/callback?code=&state= — OAuth 2.0 Authorization Code exchange */
  postAuthCallback(code: string, state: string): Promise<unknown> {
    const qs = new URLSearchParams({ code, state });
    return request(`/api/auth/callback?${qs}`, { method: 'POST' });
  },

  /** POST /api/auth/change-password — change password for the authenticated user */
  changePassword(currentPassword: string, newPassword: string): Promise<void> {
    return request('/api/auth/change-password', {
      method: 'POST',
      json: { currentPassword, newPassword },
    });
  },

  /** POST /api/auth/forgot-password — request a password reset (dev echoes the token) */
  forgotPassword(email: string): Promise<ForgotPasswordResult> {
    return request('/api/auth/forgot-password', { method: 'POST', json: { email } });
  },

  /** POST /api/auth/reset-password — complete a password reset with a token */
  resetPassword(email: string, token: string, newPassword: string): Promise<void> {
    return request('/api/auth/reset-password', {
      method: 'POST',
      json: { email, token, newPassword },
    });
  },

  /** POST /api/auth/request-email-change — send a verification link to the new address */
  requestEmailChange(newEmail: string): Promise<{ message: string }> {
    return request('/api/auth/request-email-change', { method: 'POST', json: { newEmail } });
  },

  /** POST /api/auth/confirm-email-change — apply a pending email change from the emailed link */
  confirmEmailChange(userId: string, email: string, token: string): Promise<void> {
    return request('/api/auth/confirm-email-change', {
      method: 'POST',
      json: { userId, newEmail: email, token },
    });
  },

  /** POST /api/telemetry/upload — upload an iRacing .ibt file, returns extracted lap summary */
  uploadTelemetry(file: File): Promise<TelemetryUploadResult> {
    const form = new FormData();
    form.append('file', file);
    return request('/api/telemetry/upload', { method: 'POST', body: form });
  },

  /** GET /api/telemetry/laps — personal best per track+car for the authenticated user */
  getMyUploadedBests(signal?: AbortSignal): Promise<UploadedBest[]> {
    return request('/api/telemetry/laps', { signal });
  },

  /** GET /api/users/me/analytics?seriesId= — per-car percentile history and trend for the authenticated user */
  getMyAnalytics(
    seriesId?: number,
    options?: PersonalBestEvidenceOptions,
    signal?: AbortSignal
  ): Promise<CarAnalytics[]> {
    const qs = new URLSearchParams();
    if (seriesId != null) qs.set('seriesId', String(seriesId));
    appendPersonalBestEvidence(qs, options);
    const query = qs.toString();
    return request(`/api/users/me/analytics${query ? `?${query}` : ''}`, { signal });
  },

  /** GET /api/users/me/progression — per-category iRating / SR / CPI / TT with iRating history */
  getProgression(signal?: AbortSignal): Promise<DriverProgression> {
    return request('/api/users/me/progression', { signal });
  },

  /** GET /api/users/me/profile-stats — career stats, license badges, recap favorites */
  getProfileStats(signal?: AbortSignal): Promise<DriverProfile> {
    return request('/api/users/me/profile-stats', { signal });
  },

  /** GET /api/users/me/achievements — the driver's awards trophy case (newest first) */
  getAchievements(signal?: AbortSignal): Promise<Achievements> {
    return request('/api/users/me/achievements', { signal });
  },

  /** GET /api/users/me/races — recent official race history (newest first) */
  getRaceHistory(signal?: AbortSignal): Promise<RaceHistoryRow[]> {
    return request('/api/users/me/races', { signal });
  },

  /** GET /api/subsessions/:id — classified field + session context for one race */
  getSubsession(id: number, signal?: AbortSignal): Promise<SubsessionDetail> {
    return request(`/api/subsessions/${id}`, { signal });
  },

  /** GET /api/subsessions/:id/laps?customerId= — a driver's per-lap pace (defaults to caller) */
  getDriverLaps(
    subsessionId: number,
    customerId?: number,
    signal?: AbortSignal
  ): Promise<DriverLaps> {
    const qs = customerId != null ? `?customerId=${customerId}` : '';
    return request(`/api/subsessions/${subsessionId}/laps${qs}`, { signal });
  },

  /** GET /api/series/:seriesId/schedule — active-season schedule with weather, BoP, and caller Uploaded Lap presence by Track */
  getSchedule(seriesId: number, signal?: AbortSignal): Promise<SeasonSchedule> {
    return request(`/api/series/${seriesId}/schedule`, { signal });
  },

  /** GET /api/series/:seriesId/weeks/:weekNumber/strategy — per-week BoP/weather/fuel strategy
   * briefing; personalizes the "optimal for you" overlay when the caller is iRacing-linked */
  getWeekStrategy(
    seriesId: number,
    weekNumber: number,
    signal?: AbortSignal
  ): Promise<WeekStrategy> {
    return request(`/api/series/${seriesId}/weeks/${weekNumber}/strategy`, { signal });
  },

  /** GET /api/leaderboards?categoryId= — global top-N drivers for a category (ranked by iRating) */
  getLeaderboard(categoryId: number, signal?: AbortSignal): Promise<GlobalLeaderboardEntry[]> {
    return request(`/api/leaderboards?categoryId=${categoryId}`, { signal });
  },

  /** GET /api/series/:seriesId/standings?carClassId= — active-season championship standings */
  getStandings(
    seriesId: number,
    carClassId?: number,
    signal?: AbortSignal
  ): Promise<SeasonStandings> {
    const qs = carClassId != null ? `?carClassId=${carClassId}` : '';
    return request(`/api/series/${seriesId}/standings${qs}`, { signal });
  },

  /** GET /api/series/:seriesId/tt-standings?carClassId= — active-season Time Trial standings */
  getTtStandings(
    seriesId: number,
    carClassId?: number,
    signal?: AbortSignal
  ): Promise<SeasonTtStandings> {
    const qs = carClassId != null ? `?carClassId=${carClassId}` : '';
    return request(`/api/series/${seriesId}/tt-standings${qs}`, { signal });
  },

  /** GET /api/series/:seriesId/qualify-results?carClassId=&weekNumber= — weekly qualifying results */
  getQualifyResults(
    seriesId: number,
    carClassId?: number,
    weekNumber?: number,
    signal?: AbortSignal
  ): Promise<SeasonQualifyResults> {
    const params = new URLSearchParams();
    if (carClassId != null) params.set('carClassId', String(carClassId));
    if (weekNumber != null) params.set('weekNumber', String(weekNumber));
    const qs = params.toString();
    return request(`/api/series/${seriesId}/qualify-results${qs ? `?${qs}` : ''}`, { signal });
  },

  /** GET /api/race-guide — official sessions starting in the next ~3h (race-now board) */
  getRaceGuide(signal?: AbortSignal): Promise<RaceGuideEntry[]> {
    return request('/api/race-guide', { signal });
  },

  /** GET /api/users/me/rivals — drivers the caller follows for comparison (newest first) */
  getRivals(signal?: AbortSignal): Promise<Rival[]> {
    return request('/api/users/me/rivals', { signal });
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
  getRivalSuggestions(signal?: AbortSignal): Promise<RivalSuggestion[]> {
    return request('/api/users/me/rivals/suggestions', { signal });
  },

  /** GET /api/users/me/compare?rivalCustId= — head-to-head comparison (409 if unlinked) */
  compareRival(rivalCustId: number): Promise<DriverComparison> {
    return request(`/api/users/me/compare?rivalCustId=${rivalCustId}`);
  },

  /** GET /api/cars — full car catalog (browse grid) */
  getCars(signal?: AbortSignal): Promise<CarCatalogItem[]> {
    return request('/api/cars', { signal });
  },

  /** GET /api/cars/:id — car detail (specs, classes, your best laps when signed in) */
  getCar(carId: number, signal?: AbortSignal): Promise<CarCatalogDetail> {
    return request(`/api/cars/${carId}`, { signal });
  },

  /** GET /api/tracks — full track catalog (browse grid) */
  getTracks(signal?: AbortSignal): Promise<TrackCatalogItem[]> {
    return request('/api/tracks', { signal });
  },

  /** GET /api/tracks/:id — track detail (specs, map, your best laps when signed in) */
  getTrack(trackId: number, signal?: AbortSignal): Promise<TrackCatalogDetail> {
    return request(`/api/tracks/${trackId}`, { signal });
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
      headers: {
        'Content-Type': 'application/json',
        ...(session.accessToken ? { Authorization: `Bearer ${session.accessToken}` } : {}),
      },
      body: JSON.stringify({ refreshToken }),
    })
      .then(() => void 0)
      .catch(() => void 0);
  },

  /** GET /api/feature-flags — the caller's entitled flags (authenticated: their role set; anonymous: the public Standard set) */
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
