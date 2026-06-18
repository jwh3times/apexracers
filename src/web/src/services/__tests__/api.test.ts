import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import {
  api,
  setToken,
  setRefreshToken,
  clearToken,
  onTokenRefreshed,
  onSessionExpired,
  IRacingNotLinkedError,
} from '../api';

// ── Fetch mock helpers ────────────────────────────────────────────────────────

type MockResponseOpts = { ok?: boolean; status?: number; statusText?: string; body?: string };

function mockFetchOk(data: unknown) {
  vi.mocked(fetch).mockResolvedValue({
    ok: true,
    status: 200,
    statusText: 'OK',
    json: () => Promise.resolve(data),
    text: () => Promise.resolve(''),
  } as Response);
}

function mockFetchError({
  ok = false,
  status = 500,
  statusText = 'Server Error',
  body = '',
}: MockResponseOpts = {}) {
  vi.mocked(fetch).mockResolvedValue({
    ok,
    status,
    statusText,
    json: () => Promise.resolve(null),
    text: () => Promise.resolve(body),
  } as Response);
}

describe('api', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn());
  });
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  // ── getSeries ───────────────────────────────────────────────────────────────

  describe('getSeries', () => {
    it('calls GET /api/series and returns parsed data', async () => {
      const data = [{ id: 1, name: 'GT3 Cup', seasonId: 10, currentWeekNumber: 5 }];
      mockFetchOk(data);
      const result = await api.getSeries();
      expect(fetch).toHaveBeenCalledWith('/api/series', expect.objectContaining({}));
      expect(result).toEqual(data);
    });

    it('throws with status info on non-ok response', async () => {
      mockFetchError({ status: 503, statusText: 'Unavailable' });
      await expect(api.getSeries()).rejects.toThrow('503');
    });
  });

  // ── getCarsForWeek ──────────────────────────────────────────────────────────

  describe('getCarsForWeek', () => {
    it('calls GET with correct path', async () => {
      mockFetchOk([]);
      await api.getCarsForWeek(7, 12);
      expect(fetch).toHaveBeenCalledWith(
        '/api/series/7/weeks/12/cars',
        expect.objectContaining({})
      );
    });

    it('throws on non-ok response', async () => {
      mockFetchError({ status: 404, statusText: 'Not Found' });
      await expect(api.getCarsForWeek(1, 1)).rejects.toThrow('404');
    });
  });

  // ── getPercentile ───────────────────────────────────────────────────────────

  describe('getPercentile', () => {
    it('calls GET with seriesId, weekNumber, carId, and customerId query param', async () => {
      const data = {
        seriesId: 1,
        weekNumber: 5,
        carId: 3,
        customerId: 99,
        percentileRank: 75.0,
        sampleSize: 100,
        computedAt: '',
      };
      mockFetchOk(data);
      const result = await api.getPercentile(1, 5, 3, 99);
      expect(fetch).toHaveBeenCalledWith(
        '/api/series/1/weeks/5/cars/3/percentile?customerId=99',
        expect.objectContaining({})
      );
      expect(result.percentileRank).toBe(75.0);
    });
  });

  // ── getRecommendations ──────────────────────────────────────────────────────

  describe('getRecommendations', () => {
    it('calls GET with seriesId and weekNumber query params', async () => {
      mockFetchOk([]);
      await api.getRecommendations(1, 4);
      expect(fetch).toHaveBeenCalledWith(
        '/api/users/me/recommendations?seriesId=1&weekNumber=4',
        expect.objectContaining({})
      );
    });

    it('throws IRacingNotLinkedError on a 409 carrying the not-linked code', async () => {
      mockFetchError({
        status: 409,
        statusText: 'Conflict',
        body: JSON.stringify({ code: 'IRACING_NOT_LINKED', message: 'Link your iRacing ID.' }),
      });
      await expect(api.getRecommendations(1, 4)).rejects.toBeInstanceOf(IRacingNotLinkedError);
    });

    it('throws a generic error on a 409 whose body is not the not-linked contract', async () => {
      mockFetchError({ status: 409, statusText: 'Conflict', body: 'some other conflict' });
      const promise = api.getRecommendations(1, 4);
      await expect(promise).rejects.toThrow('some other conflict');
      await expect(promise).rejects.not.toBeInstanceOf(IRacingNotLinkedError);
    });
  });

  // ── login ───────────────────────────────────────────────────────────────────

  describe('login', () => {
    it('calls POST /api/auth/login with JSON body and correct headers', async () => {
      mockFetchOk({ token: 'jwt', userId: 'u1', displayName: 'Jerry' });
      const result = await api.login('jerry@example.com', 'password123');
      expect(fetch).toHaveBeenCalledWith(
        '/api/auth/login',
        expect.objectContaining({
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ email: 'jerry@example.com', password: 'password123' }),
        })
      );
      expect(result.token).toBe('jwt');
    });

    it('throws with server error body on failure', async () => {
      mockFetchError({ body: 'Invalid email or password.' });
      await expect(api.login('a@b.com', 'bad')).rejects.toThrow('Invalid email or password.');
    });

    it('falls back to status text when body is empty', async () => {
      mockFetchError({ status: 401, statusText: 'Unauthorized', body: '' });
      await expect(api.login('a@b.com', 'bad')).rejects.toThrow('POST /api/auth/login → 401');
    });
  });

  // ── register ────────────────────────────────────────────────────────────────

  describe('register', () => {
    it('calls POST /api/auth/register with JSON body', async () => {
      mockFetchOk({ token: 'newjwt', userId: 'u2', displayName: 'New' });
      await api.register('new@example.com', 'secret');
      expect(fetch).toHaveBeenCalledWith(
        '/api/auth/register',
        expect.objectContaining({
          method: 'POST',
          body: JSON.stringify({ email: 'new@example.com', password: 'secret' }),
        })
      );
    });

    it('throws with server error body on failure', async () => {
      mockFetchError({ body: 'Email already registered.' });
      await expect(api.register('dup@example.com', 'pass')).rejects.toThrow(
        'Email already registered.'
      );
    });

    it('surfaces the RFC-7807 ProblemDetails detail as the error message', async () => {
      mockFetchError({
        status: 400,
        statusText: 'Bad Request',
        body: JSON.stringify({
          status: 400,
          title: 'Bad Request',
          detail: 'Email already registered.',
        }),
      });
      await expect(api.register('dup@example.com', 'pass')).rejects.toThrow(
        'Email already registered.'
      );
    });
  });

  // ── postAuthCallback ────────────────────────────────────────────────────────

  describe('postAuthCallback', () => {
    it('calls POST with code and state as query params', async () => {
      mockFetchOk({});
      await api.postAuthCallback('auth-code-123', 'state-abc');
      expect(fetch).toHaveBeenCalledWith(
        '/api/auth/callback?code=auth-code-123&state=state-abc',
        expect.objectContaining({ method: 'POST' })
      );
    });

    it('throws on non-ok response', async () => {
      mockFetchError({ status: 400, statusText: 'Bad Request' });
      await expect(api.postAuthCallback('bad', 'bad')).rejects.toThrow('400');
    });
  });

  // ── uploadTelemetry ─────────────────────────────────────────────────────────

  describe('uploadTelemetry', () => {
    it('calls POST /api/telemetry/upload with FormData containing the file', async () => {
      const result = {
        totalLaps: 10,
        validLaps: 8,
        bestLapSeconds: 130.5,
        trackName: 'Spa',
        configName: 'Full',
        carName: 'Porsche',
        customerId: 12345,
        driverName: 'Jerry',
      };
      mockFetchOk(result);
      const file = new File(['data'], 'session.ibt');
      const output = await api.uploadTelemetry(file);
      expect(fetch).toHaveBeenCalledWith(
        '/api/telemetry/upload',
        expect.objectContaining({ method: 'POST', body: expect.any(FormData) })
      );
      expect(output.driverName).toBe('Jerry');
    });

    it('throws with server error body on failure', async () => {
      mockFetchError({ body: 'Invalid file format' });
      await expect(api.uploadTelemetry(new File([''], 'bad.ibt'))).rejects.toThrow(
        'Invalid file format'
      );
    });
  });

  // ── getMyLaps ───────────────────────────────────────────────────────────────

  describe('getMyLaps', () => {
    it('calls GET /api/telemetry/laps', async () => {
      mockFetchOk([]);
      await api.getMyLaps();
      expect(fetch).toHaveBeenCalledWith('/api/telemetry/laps', expect.objectContaining({}));
    });
  });

  // ── updateProfile ───────────────────────────────────────────────────────────

  describe('updateProfile', () => {
    it('calls PUT /api/auth/profile with display name and customer ID', async () => {
      mockFetchOk({ token: 'new-jwt', userId: 'u1', displayName: 'Updated Name' });
      const result = await api.updateProfile('Updated Name', 100042, 'driver@example.com');
      expect(fetch).toHaveBeenCalledWith(
        '/api/auth/profile',
        expect.objectContaining({
          method: 'PUT',
          body: JSON.stringify({
            displayName: 'Updated Name',
            iRacingCustomerId: 100042,
            email: 'driver@example.com',
          }),
        })
      );
      expect(result.displayName).toBe('Updated Name');
    });

    it('sends null iRacingCustomerId when not provided', async () => {
      mockFetchOk({ token: 'new-jwt', userId: 'u1', displayName: 'Updated Name' });
      await api.updateProfile('Updated Name', null, 'driver@example.com');
      expect(fetch).toHaveBeenCalledWith(
        '/api/auth/profile',
        expect.objectContaining({
          body: JSON.stringify({
            displayName: 'Updated Name',
            iRacingCustomerId: null,
            email: 'driver@example.com',
          }),
        })
      );
    });

    it('throws with server error body on failure', async () => {
      mockFetchError({ body: 'Display name too long.' });
      await expect(api.updateProfile('A'.repeat(100), null, 'driver@example.com')).rejects.toThrow(
        'Display name too long.'
      );
    });

    it('falls back to status line when body is empty', async () => {
      mockFetchError({ status: 400, statusText: 'Bad Request', body: '' });
      await expect(api.updateProfile('name', null, 'driver@example.com')).rejects.toThrow(
        'PUT /api/auth/profile → 400'
      );
    });
  });

  // ── getMyAnalytics ──────────────────────────────────────────────────────────

  describe('getMyAnalytics', () => {
    it('calls GET /api/users/me/analytics with seriesId query param when provided', async () => {
      mockFetchOk([]);
      await api.getMyAnalytics(3);
      expect(fetch).toHaveBeenCalledWith(
        '/api/users/me/analytics?seriesId=3',
        expect.objectContaining({})
      );
    });

    it('calls GET /api/users/me/analytics without query param when seriesId is omitted', async () => {
      mockFetchOk([]);
      await api.getMyAnalytics();
      expect(fetch).toHaveBeenCalledWith('/api/users/me/analytics', expect.objectContaining({}));
    });

    it('throws on non-ok response', async () => {
      mockFetchError({ status: 401, statusText: 'Unauthorized' });
      await expect(api.getMyAnalytics(1)).rejects.toThrow('401');
    });
  });

  // ── getProgression ──────────────────────────────────────────────────────────

  describe('getProgression', () => {
    it('calls GET /api/users/me/progression and returns parsed data', async () => {
      const data = { customerId: 691062, categories: [] };
      mockFetchOk(data);
      const result = await api.getProgression();
      expect(fetch).toHaveBeenCalledWith('/api/users/me/progression', expect.objectContaining({}));
      expect(result).toEqual(data);
    });

    it('throws IRacingNotLinkedError on a 409 carrying the not-linked code', async () => {
      mockFetchError({
        status: 409,
        statusText: 'Conflict',
        body: JSON.stringify({ code: 'IRACING_NOT_LINKED', message: 'Link your iRacing ID.' }),
      });
      await expect(api.getProgression()).rejects.toBeInstanceOf(IRacingNotLinkedError);
    });
  });

  // ── getProfileStats ─────────────────────────────────────────────────────────

  describe('getProfileStats', () => {
    it('calls GET /api/users/me/profile-stats and returns parsed data', async () => {
      const data = { customerId: 691062, displayName: 'Jerry', licenses: [], career: [] };
      mockFetchOk(data);
      const result = await api.getProfileStats();
      expect(fetch).toHaveBeenCalledWith(
        '/api/users/me/profile-stats',
        expect.objectContaining({})
      );
      expect(result).toEqual(data);
    });

    it('throws IRacingNotLinkedError on a 409 carrying the not-linked code', async () => {
      mockFetchError({
        status: 409,
        statusText: 'Conflict',
        body: JSON.stringify({ code: 'IRACING_NOT_LINKED', message: 'Link your iRacing ID.' }),
      });
      await expect(api.getProfileStats()).rejects.toBeInstanceOf(IRacingNotLinkedError);
    });
  });

  // ── getRaceHistory ──────────────────────────────────────────────────────────

  describe('getRaceHistory', () => {
    it('calls GET /api/users/me/races and returns parsed rows', async () => {
      mockFetchOk([]);
      const result = await api.getRaceHistory();
      expect(fetch).toHaveBeenCalledWith('/api/users/me/races', expect.objectContaining({}));
      expect(result).toEqual([]);
    });

    it('throws IRacingNotLinkedError on a 409 carrying the not-linked code', async () => {
      mockFetchError({
        status: 409,
        statusText: 'Conflict',
        body: JSON.stringify({ code: 'IRACING_NOT_LINKED', message: 'Link your iRacing ID.' }),
      });
      await expect(api.getRaceHistory()).rejects.toBeInstanceOf(IRacingNotLinkedError);
    });
  });

  // ── getSubsession ───────────────────────────────────────────────────────────

  describe('getSubsession', () => {
    it('calls GET /api/subsessions/:id and returns parsed data', async () => {
      const data = { subsessionId: 100, seriesName: 'GT3 Cup', results: [] };
      mockFetchOk(data);
      const result = await api.getSubsession(100);
      expect(fetch).toHaveBeenCalledWith('/api/subsessions/100', expect.objectContaining({}));
      expect(result).toEqual(data);
    });

    it('throws with status info on a 404 (unknown subsession)', async () => {
      mockFetchError({ status: 404, statusText: 'Not Found' });
      await expect(api.getSubsession(999)).rejects.toThrow('404');
    });
  });

  // ── getDriverLaps ─────────────────────────────────────────────────────────

  describe('getDriverLaps', () => {
    it('calls GET with a customerId query param when provided', async () => {
      mockFetchOk({ subsessionId: 100, custId: 111, laps: [] });
      await api.getDriverLaps(100, 111);
      expect(fetch).toHaveBeenCalledWith(
        '/api/subsessions/100/laps?customerId=111',
        expect.objectContaining({})
      );
    });

    it('omits the query param when customerId is not provided', async () => {
      mockFetchOk({ subsessionId: 100, custId: 0, laps: [] });
      await api.getDriverLaps(100);
      expect(fetch).toHaveBeenCalledWith('/api/subsessions/100/laps', expect.objectContaining({}));
    });
  });

  // ── getSchedule ─────────────────────────────────────────────────────────────

  describe('getSchedule', () => {
    it('calls GET /api/series/:id/schedule and returns parsed data', async () => {
      const data = { seriesId: 444, seriesName: 'GT3 Cup', weeks: [] };
      mockFetchOk(data);
      const result = await api.getSchedule(444);
      expect(fetch).toHaveBeenCalledWith('/api/series/444/schedule', expect.objectContaining({}));
      expect(result).toEqual(data);
    });

    it('throws with status info on a non-ok response', async () => {
      mockFetchError({ status: 404, statusText: 'Not Found' });
      await expect(api.getSchedule(999)).rejects.toThrow('404');
    });
  });

  // ── getLeaderboard ────────────────────────────────────────────────────────

  describe('getLeaderboard', () => {
    it('calls GET /api/leaderboards with the categoryId query param', async () => {
      mockFetchOk([]);
      await api.getLeaderboard(5);
      expect(fetch).toHaveBeenCalledWith(
        '/api/leaderboards?categoryId=5',
        expect.objectContaining({})
      );
    });

    it('throws with status info on a non-ok response', async () => {
      mockFetchError({ status: 401, statusText: 'Unauthorized' });
      await expect(api.getLeaderboard(2)).rejects.toThrow('401');
    });
  });

  // ── getStandings ──────────────────────────────────────────────────────────

  describe('getStandings', () => {
    it('calls GET /api/series/:id/standings with a carClassId query param when provided', async () => {
      mockFetchOk({ seriesId: 444, standings: [] });
      await api.getStandings(444, 4091);
      expect(fetch).toHaveBeenCalledWith(
        '/api/series/444/standings?carClassId=4091',
        expect.objectContaining({})
      );
    });

    it('omits the query param when carClassId is not provided', async () => {
      mockFetchOk({ seriesId: 444, standings: [] });
      await api.getStandings(444);
      expect(fetch).toHaveBeenCalledWith('/api/series/444/standings', expect.objectContaining({}));
    });
  });

  // ── getRaceGuide ──────────────────────────────────────────────────────────

  describe('getRaceGuide', () => {
    it('calls GET /api/race-guide and returns parsed rows', async () => {
      mockFetchOk([]);
      const result = await api.getRaceGuide();
      expect(fetch).toHaveBeenCalledWith('/api/race-guide', expect.objectContaining({}));
      expect(result).toEqual([]);
    });

    it('throws with status info on a non-ok response', async () => {
      mockFetchError({ status: 503, statusText: 'Service Unavailable' });
      await expect(api.getRaceGuide()).rejects.toThrow('503');
    });
  });

  // ── setToken / clearToken ───────────────────────────────────────────────────

  // ── updateRole ──────────────────────────────────────────────────────────────

  describe('updateRole', () => {
    it('calls PUT /api/auth/role with role body and returns fresh JWT', async () => {
      mockFetchOk({ token: 'new-jwt', userId: 'u1', displayName: 'Jerry' });
      const result = await api.updateRole('Beta');
      expect(fetch).toHaveBeenCalledWith(
        '/api/auth/role',
        expect.objectContaining({
          method: 'PUT',
          body: JSON.stringify({ role: 'Beta' }),
        })
      );
      expect(result.token).toBe('new-jwt');
    });

    it('throws with server error body on failure', async () => {
      mockFetchError({ body: 'Invalid role.' });
      await expect(api.updateRole('Unknown')).rejects.toThrow('Invalid role.');
    });
  });

  // ── getFeatureFlags ─────────────────────────────────────────────────────────

  describe('getFeatureFlags', () => {
    it('calls GET /api/feature-flags and returns flag list', async () => {
      const data = [
        {
          id: 1,
          key: 'new-ui',
          name: 'New UI',
          description: null,
          isEnabled: true,
          minimumRole: 'Standard',
          createdAt: '',
          updatedAt: '',
        },
      ];
      mockFetchOk(data);
      const result = await api.getFeatureFlags();
      expect(fetch).toHaveBeenCalledWith('/api/feature-flags', expect.objectContaining({}));
      expect(result).toEqual(data);
    });

    it('throws on non-ok response', async () => {
      mockFetchError({ status: 401, statusText: 'Unauthorized' });
      await expect(api.getFeatureFlags()).rejects.toThrow('401');
    });
  });

  // ── getAdminUsers ───────────────────────────────────────────────────────────

  describe('getAdminUsers', () => {
    it('calls GET /api/admin/users and returns user list', async () => {
      const data = [
        { userId: 'u1', email: 'admin@example.com', displayName: 'Admin', role: 'Admin' },
      ];
      mockFetchOk(data);
      const result = await api.getAdminUsers();
      expect(fetch).toHaveBeenCalledWith('/api/admin/users', expect.objectContaining({}));
      expect(result).toEqual(data);
    });

    it('throws on non-ok response', async () => {
      mockFetchError({ status: 403, statusText: 'Forbidden' });
      await expect(api.getAdminUsers()).rejects.toThrow('403');
    });
  });

  // ── setAdminUserRole ────────────────────────────────────────────────────────

  describe('setAdminUserRole', () => {
    it('calls PUT /api/admin/users/:userId/role with role body', async () => {
      const data = { userId: 'u2', email: 'user@example.com', displayName: 'User', role: 'Beta' };
      mockFetchOk(data);
      const result = await api.setAdminUserRole('u2', 'Beta');
      expect(fetch).toHaveBeenCalledWith(
        '/api/admin/users/u2/role',
        expect.objectContaining({
          method: 'PUT',
          body: JSON.stringify({ role: 'Beta' }),
        })
      );
      expect(result.role).toBe('Beta');
    });

    it('throws with server error body on failure', async () => {
      mockFetchError({ body: 'User not found.' });
      await expect(api.setAdminUserRole('missing', 'Beta')).rejects.toThrow('User not found.');
    });
  });

  // ── getAdminFeatureFlags ────────────────────────────────────────────────────

  describe('getAdminFeatureFlags', () => {
    it('calls GET /api/admin/feature-flags and returns all flags', async () => {
      const data = [
        {
          id: 2,
          key: 'dark-mode',
          name: 'Dark Mode',
          description: null,
          isEnabled: false,
          minimumRole: 'Alpha',
          createdAt: '',
          updatedAt: '',
        },
      ];
      mockFetchOk(data);
      const result = await api.getAdminFeatureFlags();
      expect(fetch).toHaveBeenCalledWith('/api/admin/feature-flags', expect.objectContaining({}));
      expect(result).toEqual(data);
    });

    it('throws on non-ok response', async () => {
      mockFetchError({ status: 403, statusText: 'Forbidden' });
      await expect(api.getAdminFeatureFlags()).rejects.toThrow('403');
    });
  });

  // ── createFeatureFlag ───────────────────────────────────────────────────────

  describe('createFeatureFlag', () => {
    it('calls POST /api/admin/feature-flags with full flag body and returns created flag', async () => {
      const payload = {
        key: 'exp-lap',
        name: 'Experimental Lap',
        description: 'A test flag',
        isEnabled: true,
        minimumRole: 'Beta',
      };
      const created = { id: 5, ...payload, createdAt: '', updatedAt: '' };
      mockFetchOk(created);
      const result = await api.createFeatureFlag(payload);
      expect(fetch).toHaveBeenCalledWith(
        '/api/admin/feature-flags',
        expect.objectContaining({
          method: 'POST',
          body: JSON.stringify(payload),
        })
      );
      expect(result.id).toBe(5);
    });

    it('throws with server error body on failure', async () => {
      mockFetchError({ body: 'Key already exists.' });
      await expect(
        api.createFeatureFlag({
          key: 'dup',
          name: 'Dup',
          description: null,
          isEnabled: false,
          minimumRole: 'Standard',
        })
      ).rejects.toThrow('Key already exists.');
    });
  });

  // ── updateFeatureFlag ───────────────────────────────────────────────────────

  describe('updateFeatureFlag', () => {
    it('calls PUT /api/admin/feature-flags/:id with update body', async () => {
      const updateData = {
        name: 'Updated',
        description: null,
        isEnabled: false,
        minimumRole: 'Alpha',
      };
      const updated = { id: 5, key: 'exp-lap', ...updateData, createdAt: '', updatedAt: '' };
      mockFetchOk(updated);
      const result = await api.updateFeatureFlag(5, updateData);
      expect(fetch).toHaveBeenCalledWith(
        '/api/admin/feature-flags/5',
        expect.objectContaining({
          method: 'PUT',
          body: JSON.stringify(updateData),
        })
      );
      expect(result.name).toBe('Updated');
    });

    it('throws with server error body on failure', async () => {
      mockFetchError({ body: 'Flag not found.' });
      await expect(
        api.updateFeatureFlag(999, {
          name: 'x',
          description: null,
          isEnabled: true,
          minimumRole: 'Standard',
        })
      ).rejects.toThrow('Flag not found.');
    });
  });

  // ── deleteFeatureFlag ───────────────────────────────────────────────────────

  describe('deleteFeatureFlag', () => {
    it('calls DELETE /api/admin/feature-flags/:id', async () => {
      vi.mocked(fetch).mockResolvedValue({
        ok: true,
        status: 204,
        statusText: 'No Content',
        json: () => Promise.resolve(null),
        text: () => Promise.resolve(''),
      } as Response);
      await api.deleteFeatureFlag(5);
      expect(fetch).toHaveBeenCalledWith(
        '/api/admin/feature-flags/5',
        expect.objectContaining({ method: 'DELETE' })
      );
    });

    it('throws with status info on non-ok response', async () => {
      mockFetchError({ status: 404, statusText: 'Not Found' });
      await expect(api.deleteFeatureFlag(999)).rejects.toThrow(
        'DELETE /api/admin/feature-flags/999 → 404 Not Found'
      );
    });
  });

  // ── setToken / clearToken ───────────────────────────────────────────────────

  describe('setToken and clearToken', () => {
    it('setToken causes subsequent requests to include an Authorization header', async () => {
      setToken('my-jwt');
      mockFetchOk([]);
      await api.getSeries();
      expect(fetch).toHaveBeenCalledWith(
        '/api/series',
        expect.objectContaining({ headers: { Authorization: 'Bearer my-jwt' } })
      );
    });

    it('clearToken removes the Authorization header from subsequent requests', async () => {
      setToken('my-jwt');
      clearToken();
      mockFetchOk([]);
      await api.getSeries();
      expect(fetch).toHaveBeenCalledWith('/api/series', expect.objectContaining({ headers: {} }));
    });
  });

  // ── setRefreshToken / clearToken ─────────────────────────────────────────────

  describe('setRefreshToken and clearToken', () => {
    it('clearToken clears both access and refresh tokens', () => {
      setToken('t');
      setRefreshToken('r');
      clearToken();
      // After clearing, no Authorization header should be sent
      mockFetchOk([]);
      api.getSeries();
      const [, opts] = vi.mocked(fetch).mock.calls[0];
      const headers = (opts as RequestInit).headers as Record<string, string>;
      expect(headers?.Authorization).toBeUndefined();
    });
  });

  // ── 401 retry interceptor ────────────────────────────────────────────────────

  describe('401 retry interceptor', () => {
    beforeEach(() => {
      // Reset module-level state between interceptor tests
      clearToken();
      onTokenRefreshed(() => {});
      onSessionExpired(() => {});
    });

    it('retries GET once after a successful token refresh', async () => {
      setRefreshToken('rt');
      const newToken = 'new-access-token';
      const refreshResponse = {
        ok: true,
        status: 200,
        statusText: '200',
        json: () =>
          Promise.resolve({
            token: newToken,
            userId: 'u1',
            displayName: 'D',
            refreshToken: 'new-rt',
          }),
        text: () => Promise.resolve(''),
      } as unknown as Response;
      const retryResponse = {
        ok: true,
        status: 200,
        statusText: '200',
        json: () => Promise.resolve([]),
        text: () => Promise.resolve(''),
      } as unknown as Response;

      vi.mocked(fetch)
        .mockResolvedValueOnce({
          ok: false,
          status: 401,
          statusText: '401',
          json: () => Promise.resolve(null),
          text: () => Promise.resolve(''),
        } as unknown as Response)
        .mockResolvedValueOnce(refreshResponse)
        .mockResolvedValueOnce(retryResponse);

      await api.getSeries();

      expect(vi.mocked(fetch)).toHaveBeenCalledTimes(3);
      expect(vi.mocked(fetch).mock.calls[1][0]).toBe('/api/auth/refresh');
    });

    it('calls onSessionExpired when refresh returns non-2xx', async () => {
      setRefreshToken('rt');
      const expired = vi.fn();
      onSessionExpired(expired);

      vi.mocked(fetch)
        .mockResolvedValueOnce({
          ok: false,
          status: 401,
          statusText: '401',
          json: () => Promise.resolve(null),
          text: () => Promise.resolve(''),
        } as unknown as Response)
        .mockResolvedValueOnce({
          ok: false,
          status: 401,
          statusText: '401',
          json: () => Promise.resolve(null),
          text: () => Promise.resolve(''),
        } as unknown as Response);

      await expect(api.getSeries()).rejects.toThrow();
      expect(expired).toHaveBeenCalled();
    });

    it('throws without retry when no refresh token is set', async () => {
      clearToken(); // also clears refresh token

      vi.mocked(fetch).mockResolvedValueOnce({
        ok: false,
        status: 401,
        statusText: '401',
        json: () => Promise.resolve(null),
        text: () => Promise.resolve(''),
      } as unknown as Response);

      await expect(api.getSeries()).rejects.toThrow('401');
      expect(vi.mocked(fetch)).toHaveBeenCalledTimes(1);
    });

    it('deduplicates concurrent 401 retries into a single refresh call', async () => {
      setRefreshToken('rt');
      const refreshResponse = {
        ok: true,
        status: 200,
        statusText: '200',
        json: () =>
          Promise.resolve({
            token: 'new-t',
            userId: 'u1',
            displayName: 'D',
            refreshToken: 'new-rt',
          }),
        text: () => Promise.resolve(''),
      } as unknown as Response;
      const make401 = () =>
        ({
          ok: false,
          status: 401,
          statusText: '401',
          json: () => Promise.resolve(null),
          text: () => Promise.resolve(''),
        }) as unknown as Response;
      const makeOk = () =>
        ({
          ok: true,
          status: 200,
          statusText: '200',
          json: () => Promise.resolve([]),
          text: () => Promise.resolve(''),
        }) as unknown as Response;

      vi.mocked(fetch)
        .mockResolvedValueOnce(make401()) // first request 401
        .mockResolvedValueOnce(make401()) // second request 401
        .mockResolvedValueOnce(refreshResponse) // one shared refresh call
        .mockResolvedValueOnce(makeOk()) // first retry
        .mockResolvedValueOnce(makeOk()); // second retry

      await Promise.all([api.getSeries(), api.getSeries()]);

      // Only one POST to /api/auth/refresh
      const refreshCalls = vi.mocked(fetch).mock.calls.filter(c => c[0] === '/api/auth/refresh');
      expect(refreshCalls).toHaveLength(1);
    });

    it('calls onTokenRefreshed with new tokens after successful refresh', async () => {
      setRefreshToken('old-rt');
      const cb = vi.fn();
      onTokenRefreshed(cb);

      vi.mocked(fetch)
        .mockResolvedValueOnce({
          ok: false,
          status: 401,
          statusText: '401',
          json: () => Promise.resolve(null),
          text: () => Promise.resolve(''),
        } as unknown as Response)
        .mockResolvedValueOnce({
          ok: true,
          status: 200,
          statusText: '200',
          json: () =>
            Promise.resolve({
              token: 'new-t',
              userId: 'u1',
              displayName: 'D',
              refreshToken: 'new-rt',
            }),
          text: () => Promise.resolve(''),
        } as unknown as Response)
        .mockResolvedValueOnce({
          ok: true,
          status: 200,
          statusText: '200',
          json: () => Promise.resolve([]),
          text: () => Promise.resolve(''),
        } as unknown as Response);

      await api.getSeries();
      expect(cb).toHaveBeenCalledWith('new-t', 'new-rt');
    });

    it('handles refresh response with no refreshToken field (rotation disabled)', async () => {
      setRefreshToken('old-rt');
      const cb = vi.fn();
      onTokenRefreshed(cb);

      vi.mocked(fetch)
        .mockResolvedValueOnce({
          ok: false,
          status: 401,
          statusText: '401',
          json: () => Promise.resolve(null),
          text: () => Promise.resolve(''),
        } as unknown as Response)
        // No refreshToken in the response body
        .mockResolvedValueOnce({
          ok: true,
          status: 200,
          statusText: '200',
          json: () => Promise.resolve({ token: 'new-t', userId: 'u1', displayName: 'D' }),
          text: () => Promise.resolve(''),
        } as unknown as Response)
        .mockResolvedValueOnce({
          ok: true,
          status: 200,
          statusText: '200',
          json: () => Promise.resolve([]),
          text: () => Promise.resolve(''),
        } as unknown as Response);

      await api.getSeries();
      // onTokenRefreshed still called but with empty string for refreshToken
      expect(cb).toHaveBeenCalledWith('new-t', '');
    });

    it('calls onSessionExpired when fetch throws during refresh', async () => {
      setRefreshToken('rt');
      const expired = vi.fn();
      onSessionExpired(expired);

      vi.mocked(fetch)
        .mockResolvedValueOnce({
          ok: false,
          status: 401,
          statusText: '401',
          json: () => Promise.resolve(null),
          text: () => Promise.resolve(''),
        } as unknown as Response)
        .mockRejectedValueOnce(new Error('network failure'));

      await expect(api.getSeries()).rejects.toThrow();
      expect(expired).toHaveBeenCalled();
    });

    it('GET retry throws when retry response is not ok after refresh', async () => {
      setRefreshToken('rt');

      vi.mocked(fetch)
        .mockResolvedValueOnce({
          ok: false,
          status: 401,
          statusText: '401',
          json: () => Promise.resolve(null),
          text: () => Promise.resolve(''),
        } as unknown as Response)
        .mockResolvedValueOnce({
          ok: true,
          status: 200,
          statusText: '200',
          json: () =>
            Promise.resolve({
              token: 'new-t',
              userId: 'u1',
              displayName: 'D',
              refreshToken: 'new-rt',
            }),
          text: () => Promise.resolve(''),
        } as unknown as Response)
        .mockResolvedValueOnce({
          ok: false,
          status: 503,
          statusText: 'Service Unavailable',
          json: () => Promise.resolve(null),
          text: () => Promise.resolve(''),
        } as unknown as Response);

      await expect(api.getSeries()).rejects.toThrow('503');
    });

    it('POST retry (postAuthCallback) retries after 401 and succeeds', async () => {
      setRefreshToken('rt');

      vi.mocked(fetch)
        .mockResolvedValueOnce({
          ok: false,
          status: 401,
          statusText: '401',
          json: () => Promise.resolve(null),
          text: () => Promise.resolve(''),
        } as unknown as Response)
        .mockResolvedValueOnce({
          ok: true,
          status: 200,
          statusText: '200',
          json: () =>
            Promise.resolve({
              token: 'new-t',
              userId: 'u1',
              displayName: 'D',
              refreshToken: 'new-rt',
            }),
          text: () => Promise.resolve(''),
        } as unknown as Response)
        .mockResolvedValueOnce({
          ok: true,
          status: 200,
          statusText: '200',
          json: () => Promise.resolve({}),
          text: () => Promise.resolve(''),
        } as unknown as Response);

      await expect(api.postAuthCallback('code', 'state')).resolves.toBeDefined();
      expect(vi.mocked(fetch)).toHaveBeenCalledTimes(3);
    });

    it('POST retry (postAuthCallback) throws when retry fails after refresh', async () => {
      setRefreshToken('rt');

      vi.mocked(fetch)
        .mockResolvedValueOnce({
          ok: false,
          status: 401,
          statusText: '401',
          json: () => Promise.resolve(null),
          text: () => Promise.resolve(''),
        } as unknown as Response)
        .mockResolvedValueOnce({
          ok: true,
          status: 200,
          statusText: '200',
          json: () =>
            Promise.resolve({
              token: 'new-t',
              userId: 'u1',
              displayName: 'D',
              refreshToken: 'new-rt',
            }),
          text: () => Promise.resolve(''),
        } as unknown as Response)
        .mockResolvedValueOnce({
          ok: false,
          status: 403,
          statusText: 'Forbidden',
          json: () => Promise.resolve(null),
          text: () => Promise.resolve(''),
        } as unknown as Response);

      await expect(api.postAuthCallback('code', 'state')).rejects.toThrow('403');
    });

    it('PUT retry (updateProfile) retries after 401 and succeeds', async () => {
      setRefreshToken('rt');

      vi.mocked(fetch)
        .mockResolvedValueOnce({
          ok: false,
          status: 401,
          statusText: '401',
          json: () => Promise.resolve(null),
          text: () => Promise.resolve(''),
        } as unknown as Response)
        .mockResolvedValueOnce({
          ok: true,
          status: 200,
          statusText: '200',
          json: () =>
            Promise.resolve({
              token: 'new-t',
              userId: 'u1',
              displayName: 'D',
              refreshToken: 'new-rt',
            }),
          text: () => Promise.resolve(''),
        } as unknown as Response)
        .mockResolvedValueOnce({
          ok: true,
          status: 200,
          statusText: '200',
          json: () => Promise.resolve({ token: 'jwt', userId: 'u1', displayName: 'Updated' }),
          text: () => Promise.resolve(''),
        } as unknown as Response);

      const result = await api.updateProfile('Updated', null, 'a@b.com');
      expect(result.displayName).toBe('Updated');
    });

    it('PUT retry throws when retry fails after refresh', async () => {
      setRefreshToken('rt');

      vi.mocked(fetch)
        .mockResolvedValueOnce({
          ok: false,
          status: 401,
          statusText: '401',
          json: () => Promise.resolve(null),
          text: () => Promise.resolve(''),
        } as unknown as Response)
        .mockResolvedValueOnce({
          ok: true,
          status: 200,
          statusText: '200',
          json: () =>
            Promise.resolve({
              token: 'new-t',
              userId: 'u1',
              displayName: 'D',
              refreshToken: 'new-rt',
            }),
          text: () => Promise.resolve(''),
        } as unknown as Response)
        .mockResolvedValueOnce({
          ok: false,
          status: 422,
          statusText: 'Unprocessable',
          json: () => Promise.resolve(null),
          text: () => Promise.resolve('error body'),
        } as unknown as Response);

      await expect(api.updateProfile('X', null, 'a@b.com')).rejects.toThrow('error body');
    });

    it('DELETE retry (deleteFeatureFlag) retries after 401 and succeeds', async () => {
      setRefreshToken('rt');

      vi.mocked(fetch)
        .mockResolvedValueOnce({
          ok: false,
          status: 401,
          statusText: '401',
          json: () => Promise.resolve(null),
          text: () => Promise.resolve(''),
        } as unknown as Response)
        .mockResolvedValueOnce({
          ok: true,
          status: 200,
          statusText: '200',
          json: () =>
            Promise.resolve({
              token: 'new-t',
              userId: 'u1',
              displayName: 'D',
              refreshToken: 'new-rt',
            }),
          text: () => Promise.resolve(''),
        } as unknown as Response)
        .mockResolvedValueOnce({
          ok: true,
          status: 204,
          statusText: 'No Content',
          json: () => Promise.resolve(null),
          text: () => Promise.resolve(''),
        } as unknown as Response);

      await expect(api.deleteFeatureFlag(1)).resolves.toBeUndefined();
    });

    it('DELETE retry throws when retry fails after refresh', async () => {
      setRefreshToken('rt');

      vi.mocked(fetch)
        .mockResolvedValueOnce({
          ok: false,
          status: 401,
          statusText: '401',
          json: () => Promise.resolve(null),
          text: () => Promise.resolve(''),
        } as unknown as Response)
        .mockResolvedValueOnce({
          ok: true,
          status: 200,
          statusText: '200',
          json: () =>
            Promise.resolve({
              token: 'new-t',
              userId: 'u1',
              displayName: 'D',
              refreshToken: 'new-rt',
            }),
          text: () => Promise.resolve(''),
        } as unknown as Response)
        .mockResolvedValueOnce({
          ok: false,
          status: 404,
          statusText: 'Not Found',
          json: () => Promise.resolve(null),
          text: () => Promise.resolve(''),
        } as unknown as Response);

      await expect(api.deleteFeatureFlag(999)).rejects.toThrow('404');
    });

    it('postForm retry (uploadTelemetry) retries after 401 and succeeds', async () => {
      setRefreshToken('rt');
      const uploadResult = {
        totalLaps: 5,
        validLaps: 4,
        bestLapSeconds: 90,
        trackName: 'Spa',
        configName: 'Full',
        carName: 'GT3',
        customerId: 1,
        driverName: 'D',
      };

      vi.mocked(fetch)
        .mockResolvedValueOnce({
          ok: false,
          status: 401,
          statusText: '401',
          json: () => Promise.resolve(null),
          text: () => Promise.resolve(''),
        } as unknown as Response)
        .mockResolvedValueOnce({
          ok: true,
          status: 200,
          statusText: '200',
          json: () =>
            Promise.resolve({
              token: 'new-t',
              userId: 'u1',
              displayName: 'D',
              refreshToken: 'new-rt',
            }),
          text: () => Promise.resolve(''),
        } as unknown as Response)
        .mockResolvedValueOnce({
          ok: true,
          status: 200,
          statusText: '200',
          json: () => Promise.resolve(uploadResult),
          text: () => Promise.resolve(''),
        } as unknown as Response);

      const result = await api.uploadTelemetry(new File([''], 'test.ibt'));
      expect(result.trackName).toBe('Spa');
    });

    it('postForm retry throws when retry fails after refresh', async () => {
      setRefreshToken('rt');

      vi.mocked(fetch)
        .mockResolvedValueOnce({
          ok: false,
          status: 401,
          statusText: '401',
          json: () => Promise.resolve(null),
          text: () => Promise.resolve(''),
        } as unknown as Response)
        .mockResolvedValueOnce({
          ok: true,
          status: 200,
          statusText: '200',
          json: () =>
            Promise.resolve({
              token: 'new-t',
              userId: 'u1',
              displayName: 'D',
              refreshToken: 'new-rt',
            }),
          text: () => Promise.resolve(''),
        } as unknown as Response)
        .mockResolvedValueOnce({
          ok: false,
          status: 400,
          statusText: 'Bad Request',
          json: () => Promise.resolve(null),
          text: () => Promise.resolve('bad file'),
        } as unknown as Response);

      await expect(api.uploadTelemetry(new File([''], 'bad.ibt'))).rejects.toThrow('bad file');
    });

    it('postJson retry (login) retries after 401 and succeeds', async () => {
      setRefreshToken('rt');

      vi.mocked(fetch)
        .mockResolvedValueOnce({
          ok: false,
          status: 401,
          statusText: '401',
          json: () => Promise.resolve(null),
          text: () => Promise.resolve(''),
        } as unknown as Response)
        .mockResolvedValueOnce({
          ok: true,
          status: 200,
          statusText: '200',
          json: () =>
            Promise.resolve({
              token: 'new-t',
              userId: 'u1',
              displayName: 'D',
              refreshToken: 'new-rt',
            }),
          text: () => Promise.resolve(''),
        } as unknown as Response)
        .mockResolvedValueOnce({
          ok: true,
          status: 200,
          statusText: '200',
          json: () => Promise.resolve({ token: 'auth-t', userId: 'u1', displayName: 'Jerry' }),
          text: () => Promise.resolve(''),
        } as unknown as Response);

      const result = await api.login('a@b.com', 'pass');
      expect(result.token).toBe('auth-t');
    });

    it('postJson retry throws when retry fails after refresh', async () => {
      setRefreshToken('rt');

      vi.mocked(fetch)
        .mockResolvedValueOnce({
          ok: false,
          status: 401,
          statusText: '401',
          json: () => Promise.resolve(null),
          text: () => Promise.resolve(''),
        } as unknown as Response)
        .mockResolvedValueOnce({
          ok: true,
          status: 200,
          statusText: '200',
          json: () =>
            Promise.resolve({
              token: 'new-t',
              userId: 'u1',
              displayName: 'D',
              refreshToken: 'new-rt',
            }),
          text: () => Promise.resolve(''),
        } as unknown as Response)
        .mockResolvedValueOnce({
          ok: false,
          status: 403,
          statusText: 'Forbidden',
          json: () => Promise.resolve(null),
          text: () => Promise.resolve('forbidden body'),
        } as unknown as Response);

      await expect(api.login('a@b.com', 'pass')).rejects.toThrow('forbidden body');
    });
  });

  // ── refreshTokens / revokeToken ──────────────────────────────────────────────

  describe('refreshTokens', () => {
    it('calls POST /api/auth/refresh with refreshToken body', async () => {
      mockFetchOk({ token: 'new-jwt', userId: 'u1', displayName: 'Jerry', refreshToken: 'new-rt' });
      const result = await api.refreshTokens('old-rt');
      expect(fetch).toHaveBeenCalledWith(
        '/api/auth/refresh',
        expect.objectContaining({
          method: 'POST',
          body: JSON.stringify({ refreshToken: 'old-rt' }),
        })
      );
      expect(result.token).toBe('new-jwt');
      expect(result.refreshToken).toBe('new-rt');
    });
  });

  describe('revokeToken', () => {
    it('calls POST /api/auth/logout with refreshToken body and resolves without throwing', async () => {
      vi.mocked(fetch).mockResolvedValue({
        ok: true,
        status: 204,
        statusText: 'No Content',
        json: () => Promise.resolve(null),
        text: () => Promise.resolve(''),
      } as Response);
      await expect(api.revokeToken('rt')).resolves.toBeUndefined();
      expect(fetch).toHaveBeenCalledWith(
        '/api/auth/logout',
        expect.objectContaining({
          method: 'POST',
          body: JSON.stringify({ refreshToken: 'rt' }),
        })
      );
    });

    it('silently swallows network errors', async () => {
      vi.mocked(fetch).mockRejectedValue(new Error('network error'));
      await expect(api.revokeToken('rt')).resolves.toBeUndefined();
    });
  });
});
