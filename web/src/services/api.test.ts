import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { api, IRacingNotLinkedError, ApiError } from './api';

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

  // ── ApiError (T17) ──────────────────────────────────────────────────────────

  describe('ApiError', () => {
    it('throws an ApiError carrying the response status and problem detail on a non-ok response', async () => {
      mockFetchError({
        status: 503,
        statusText: 'Service Unavailable',
        body: JSON.stringify({ status: 503, title: 'Service Unavailable', detail: 'Try again.' }),
      });
      let caught: unknown;
      try {
        await api.getSeries();
      } catch (err) {
        caught = err;
      }
      expect(caught).toBeInstanceOf(ApiError);
      expect(caught).toBeInstanceOf(Error);
      expect((caught as ApiError).status).toBe(503);
      expect((caught as ApiError).message).toBe('Try again.');
    });

    it('never surfaces a raw ProblemDetails body when it carries no detail', async () => {
      // ASP.NET Core's automatic ProblemDetails for a bare status result (e.g.
      // `Unauthorized()`) is a well-formed object with only type/title/status/traceId.
      // Falling back to the raw body printed that whole blob — traceId included — in
      // front of the user on every failed login.
      mockFetchError({
        status: 401,
        statusText: 'Unauthorized',
        body: JSON.stringify({
          type: 'https://tools.ietf.org/html/rfc9110#section-15.5.2',
          title: 'Unauthorized',
          status: 401,
          traceId: '00-fcbe51ceeccbb4ca69132acfb040a028-de2b766d85701147-00',
        }),
      });

      let caught: unknown;
      try {
        await api.getSeries();
      } catch (err) {
        caught = err;
      }

      const message = (caught as ApiError).message;
      expect(message).not.toContain('traceId');
      expect(message).not.toContain('{');
      expect(message).toBe('Unauthorized');
    });

    it('prefers detail over title when both are present', async () => {
      mockFetchError({
        status: 401,
        statusText: 'Unauthorized',
        body: JSON.stringify({
          title: 'Unauthorized',
          status: 401,
          detail: 'Invalid email or password.',
        }),
      });
      await expect(api.getSeries()).rejects.toThrow('Invalid email or password.');
    });

    it('unwraps a JSON-encoded string body without leaving quotes in the message', async () => {
      // AuthController returns the 423 lockout as a bare string; if it is serialized as
      // JSON rather than text/plain, the raw body carries surrounding quotes.
      mockFetchError({
        status: 423,
        statusText: 'Locked',
        body: JSON.stringify('Account temporarily locked. Try again later.'),
      });

      let caught: unknown;
      try {
        await api.getSeries();
      } catch (err) {
        caught = err;
      }

      expect((caught as ApiError).message).toBe('Account temporarily locked. Try again later.');
    });

    it('still surfaces a plain-text (non-JSON) error body as-is', async () => {
      // Regression guard: the raw fallback is only unsafe for JSON objects that lack a
      // human-readable field. A text/plain body is already the message.
      mockFetchError({
        status: 423,
        statusText: 'Locked',
        body: 'Account temporarily locked. Try again later.',
      });
      await expect(api.getSeries()).rejects.toThrow('Account temporarily locked. Try again later.');
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

  // ── getMyWeekPercentiles ────────────────────────────────────────────────────

  describe('getMyWeekPercentiles', () => {
    it('calls GET /api/series/:id/weeks/:n/my-percentiles', async () => {
      const data = [{ carId: 1, percentileRank: 92 }];
      mockFetchOk(data);
      const result = await api.getMyWeekPercentiles(7, 12);
      expect(fetch).toHaveBeenCalledWith(
        '/api/series/7/weeks/12/my-percentiles',
        expect.objectContaining({})
      );
      expect(result).toEqual(data);
    });

    it('serializes the shared Personal Best evidence options', async () => {
      mockFetchOk([]);

      await api.getMyWeekPercentiles(7, 12, {
        includeUploadedLaps: true,
        uploadedLapTypes: ['Race', 'Practice'],
      });

      expect(fetch).toHaveBeenCalledWith(
        '/api/series/7/weeks/12/my-percentiles?includeUploadedLaps=true&uploadedLapTypes=Race&uploadedLapTypes=Practice',
        expect.objectContaining({})
      );
    });

    it('throws IRacingNotLinkedError on a 409 carrying the not-linked code', async () => {
      mockFetchError({
        status: 409,
        statusText: 'Conflict',
        body: JSON.stringify({ code: 'IRACING_NOT_LINKED', message: 'Link your iRacing ID.' }),
      });
      await expect(api.getMyWeekPercentiles(1, 1)).rejects.toBeInstanceOf(IRacingNotLinkedError);
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

    it('treats blend with no selected types as all Uploaded Lap types', async () => {
      mockFetchOk({});

      await api.getPercentile(1, 5, 3, 99, { includeUploadedLaps: true });

      expect(fetch).toHaveBeenCalledWith(
        '/api/series/1/weeks/5/cars/3/percentile?customerId=99&includeUploadedLaps=true',
        expect.objectContaining({})
      );
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

    it('serializes selected Uploaded Lap types through the shared evidence contract', async () => {
      mockFetchOk([]);

      await api.getRecommendations(1, 4, {
        includeUploadedLaps: true,
        uploadedLapTypes: ['Qualifying'],
      });

      expect(fetch).toHaveBeenCalledWith(
        '/api/users/me/recommendations?seriesId=1&weekNumber=4&includeUploadedLaps=true&uploadedLapTypes=Qualifying',
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

  // ── changePassword ──────────────────────────────────────────────────────────

  describe('changePassword', () => {
    it('calls POST /api/auth/change-password with current and new password', async () => {
      vi.mocked(fetch).mockResolvedValue({
        ok: true,
        status: 204,
        statusText: 'No Content',
        json: () => Promise.resolve(null),
        text: () => Promise.resolve(''),
      } as Response);
      await expect(api.changePassword('OldPass1', 'NewPass2')).resolves.toBeUndefined();
      expect(fetch).toHaveBeenCalledWith(
        '/api/auth/change-password',
        expect.objectContaining({
          method: 'POST',
          body: JSON.stringify({ currentPassword: 'OldPass1', newPassword: 'NewPass2' }),
        })
      );
    });

    it('throws with the server error body on failure', async () => {
      mockFetchError({ status: 400, body: 'Incorrect password.' });
      await expect(api.changePassword('bad', 'NewPass2')).rejects.toThrow('Incorrect password.');
    });
  });

  // ── forgotPassword ──────────────────────────────────────────────────────────

  describe('forgotPassword', () => {
    it('calls POST /api/auth/forgot-password with email and returns the acknowledgement', async () => {
      mockFetchOk({ message: 'If an account exists, a link was sent.', resetToken: 'tok-123' });
      const result = await api.forgotPassword('driver@example.com');
      expect(fetch).toHaveBeenCalledWith(
        '/api/auth/forgot-password',
        expect.objectContaining({
          method: 'POST',
          body: JSON.stringify({ email: 'driver@example.com' }),
        })
      );
      expect(result.resetToken).toBe('tok-123');
    });
  });

  // ── resetPassword ───────────────────────────────────────────────────────────

  describe('resetPassword', () => {
    it('calls POST /api/auth/reset-password with email, token, and new password', async () => {
      vi.mocked(fetch).mockResolvedValue({
        ok: true,
        status: 204,
        statusText: 'No Content',
        json: () => Promise.resolve(null),
        text: () => Promise.resolve(''),
      } as Response);
      await expect(api.resetPassword('a@b.com', 'tok', 'NewPass99')).resolves.toBeUndefined();
      expect(fetch).toHaveBeenCalledWith(
        '/api/auth/reset-password',
        expect.objectContaining({
          method: 'POST',
          body: JSON.stringify({ email: 'a@b.com', token: 'tok', newPassword: 'NewPass99' }),
        })
      );
    });

    it('throws with the server error body on an invalid token', async () => {
      mockFetchError({ status: 400, body: 'Invalid token.' });
      await expect(api.resetPassword('a@b.com', 'bad', 'NewPass99')).rejects.toThrow(
        'Invalid token.'
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

  // ── getMyUploadedBests ───────────────────────────────────────────────────────────────

  describe('getMyUploadedBests', () => {
    it('calls GET /api/telemetry/laps', async () => {
      mockFetchOk([]);
      await api.getMyUploadedBests();
      expect(fetch).toHaveBeenCalledWith('/api/telemetry/laps', expect.objectContaining({}));
    });
  });

  // ── updateProfile ───────────────────────────────────────────────────────────

  describe('updateProfile', () => {
    it('calls PUT /api/auth/profile with display name and customer ID', async () => {
      mockFetchOk({ token: 'new-jwt', userId: 'u1', displayName: 'Updated Name' });
      const result = await api.updateProfile('Updated Name', 100042);
      expect(fetch).toHaveBeenCalledWith(
        '/api/auth/profile',
        expect.objectContaining({
          method: 'PUT',
          body: JSON.stringify({
            displayName: 'Updated Name',
            iRacingCustomerId: 100042,
          }),
        })
      );
      expect(result.displayName).toBe('Updated Name');
    });

    it('sends null iRacingCustomerId when not provided', async () => {
      mockFetchOk({ token: 'new-jwt', userId: 'u1', displayName: 'Updated Name' });
      await api.updateProfile('Updated Name', null);
      expect(fetch).toHaveBeenCalledWith(
        '/api/auth/profile',
        expect.objectContaining({
          body: JSON.stringify({
            displayName: 'Updated Name',
            iRacingCustomerId: null,
          }),
        })
      );
    });

    it('throws with server error body on failure', async () => {
      mockFetchError({ body: 'Display name too long.' });
      await expect(api.updateProfile('A'.repeat(100), null)).rejects.toThrow(
        'Display name too long.'
      );
    });

    it('falls back to status line when body is empty', async () => {
      mockFetchError({ status: 400, statusText: 'Bad Request', body: '' });
      await expect(api.updateProfile('name', null)).rejects.toThrow('PUT /api/auth/profile → 400');
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

    it('serializes evidence options for the all-series analytics view', async () => {
      mockFetchOk([]);

      await api.getMyAnalytics(undefined, {
        includeUploadedLaps: true,
        uploadedLapTypes: ['TimeTrial'],
      });

      expect(fetch).toHaveBeenCalledWith(
        '/api/users/me/analytics?includeUploadedLaps=true&uploadedLapTypes=TimeTrial',
        expect.objectContaining({})
      );
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
      const data = { customerId: 691062, driverName: 'Jerry', licenses: [], career: [] };
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

  // ── getAchievements ───────────────────────────────────────────────────────

  describe('getAchievements', () => {
    it('calls GET /api/users/me/achievements and returns parsed data', async () => {
      const data = { customerId: 691062, awardCount: 1, awards: [] };
      mockFetchOk(data);
      const result = await api.getAchievements();
      expect(fetch).toHaveBeenCalledWith('/api/users/me/achievements', expect.objectContaining({}));
      expect(result).toEqual(data);
    });

    it('throws IRacingNotLinkedError on a 409 carrying the not-linked code', async () => {
      mockFetchError({
        status: 409,
        statusText: 'Conflict',
        body: JSON.stringify({ code: 'IRACING_NOT_LINKED', message: 'Link your iRacing ID.' }),
      });
      await expect(api.getAchievements()).rejects.toBeInstanceOf(IRacingNotLinkedError);
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
      mockFetchOk({ subsessionId: 100, customerId: 111, laps: [] });
      await api.getDriverLaps(100, 111);
      expect(fetch).toHaveBeenCalledWith(
        '/api/subsessions/100/laps?customerId=111',
        expect.objectContaining({})
      );
    });

    it('omits the query param when customerId is not provided', async () => {
      mockFetchOk({ subsessionId: 100, customerId: 0, laps: [] });
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

  // ── getWeekStrategy ─────────────────────────────────────────────────────────

  describe('getWeekStrategy', () => {
    it('calls GET /api/series/:id/weeks/:n/strategy and returns parsed data', async () => {
      const data = { seriesId: 444, seriesName: 'GT3 Cup', weekNumber: 3, cars: [] };
      mockFetchOk(data);
      const result = await api.getWeekStrategy(444, 3);
      expect(fetch).toHaveBeenCalledWith(
        '/api/series/444/weeks/3/strategy',
        expect.objectContaining({})
      );
      expect(result).toEqual(data);
    });

    it('throws with status info on a non-ok response', async () => {
      mockFetchError({ status: 404, statusText: 'Not Found' });
      await expect(api.getWeekStrategy(999, 1)).rejects.toThrow('404');
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

  // ── getTtStandings ────────────────────────────────────────────────────────

  describe('getTtStandings', () => {
    it('calls GET /api/series/:id/tt-standings with a carClassId query param when provided', async () => {
      mockFetchOk({ seriesId: 444, standings: [] });
      await api.getTtStandings(444, 4091);
      expect(fetch).toHaveBeenCalledWith(
        '/api/series/444/tt-standings?carClassId=4091',
        expect.objectContaining({})
      );
    });

    it('omits the query param when carClassId is not provided', async () => {
      mockFetchOk({ seriesId: 444, standings: [] });
      await api.getTtStandings(444);
      expect(fetch).toHaveBeenCalledWith(
        '/api/series/444/tt-standings',
        expect.objectContaining({})
      );
    });
  });

  // ── getQualifyResults ─────────────────────────────────────────────────────

  describe('getQualifyResults', () => {
    it('includes carClassId and weekNumber query params when both provided', async () => {
      mockFetchOk({ seriesId: 444, results: [] });
      await api.getQualifyResults(444, 4091, 2);
      expect(fetch).toHaveBeenCalledWith(
        '/api/series/444/qualify-results?carClassId=4091&weekNumber=2',
        expect.objectContaining({})
      );
    });

    it('includes only weekNumber when carClassId is omitted', async () => {
      mockFetchOk({ seriesId: 444, results: [] });
      await api.getQualifyResults(444, undefined, 0);
      expect(fetch).toHaveBeenCalledWith(
        '/api/series/444/qualify-results?weekNumber=0',
        expect.objectContaining({})
      );
    });

    it('omits the query string entirely when no optional args are provided', async () => {
      mockFetchOk({ seriesId: 444, results: [] });
      await api.getQualifyResults(444);
      expect(fetch).toHaveBeenCalledWith(
        '/api/series/444/qualify-results',
        expect.objectContaining({})
      );
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

  // ── rivals & compare (3.1) ──────────────────────────────────────────────────

  describe('rivals', () => {
    it('getRivals calls GET /api/users/me/rivals', async () => {
      mockFetchOk([]);
      await api.getRivals();
      expect(fetch).toHaveBeenCalledWith('/api/users/me/rivals', expect.objectContaining({}));
    });

    it('addRival POSTs the cust id and display name', async () => {
      mockFetchOk({
        customerId: 200,
        driverName: 'Max Power',
        createdAt: '2026-01-01T00:00:00Z',
      });
      const result = await api.addRival(200, 'Max Power');
      expect(fetch).toHaveBeenCalledWith(
        '/api/users/me/rivals',
        expect.objectContaining({
          method: 'POST',
          body: JSON.stringify({ custId: 200, displayName: 'Max Power' }),
        })
      );
      expect(result.customerId).toBe(200);
    });

    it('removeRival DELETEs the cust id and resolves on 204', async () => {
      vi.mocked(fetch).mockResolvedValue({
        ok: true,
        status: 204,
        statusText: 'No Content',
        json: () => Promise.resolve(null),
        text: () => Promise.resolve(''),
      } as Response);
      await expect(api.removeRival(200)).resolves.toBeUndefined();
      expect(fetch).toHaveBeenCalledWith(
        '/api/users/me/rivals/200',
        expect.objectContaining({ method: 'DELETE' })
      );
    });

    it('searchDrivers encodes the term in the query string', async () => {
      mockFetchOk([]);
      await api.searchDrivers('max power');
      expect(fetch).toHaveBeenCalledWith(
        '/api/users/me/rivals/search?term=max%20power',
        expect.objectContaining({})
      );
    });

    it('getRivalSuggestions calls GET /api/users/me/rivals/suggestions', async () => {
      mockFetchOk([]);
      await api.getRivalSuggestions();
      expect(fetch).toHaveBeenCalledWith(
        '/api/users/me/rivals/suggestions',
        expect.objectContaining({})
      );
    });

    it('getRivalSuggestions throws a typed error when not linked (409)', async () => {
      mockFetchError({ status: 409, body: JSON.stringify({ code: 'IRACING_NOT_LINKED' }) });
      await expect(api.getRivalSuggestions()).rejects.toBeInstanceOf(IRacingNotLinkedError);
    });
  });

  describe('compareRival', () => {
    it('calls GET /api/users/me/compare with the rival cust id', async () => {
      mockFetchOk({ you: {}, rival: {}, shared: {} });
      await api.compareRival(200);
      expect(fetch).toHaveBeenCalledWith(
        '/api/users/me/compare?rivalCustId=200',
        expect.objectContaining({})
      );
    });

    it('throws a typed error when not linked (409)', async () => {
      mockFetchError({ status: 409, body: JSON.stringify({ code: 'IRACING_NOT_LINKED' }) });
      await expect(api.compareRival(200)).rejects.toBeInstanceOf(IRacingNotLinkedError);
    });
  });

  // ── catalog (3.5) ───────────────────────────────────────────────────────────

  describe('catalog', () => {
    it('getCars calls GET /api/cars', async () => {
      mockFetchOk([]);
      await api.getCars();
      expect(fetch).toHaveBeenCalledWith('/api/cars', expect.objectContaining({}));
    });

    it('getCar calls GET /api/cars/:id', async () => {
      mockFetchOk({ carId: 132 });
      await api.getCar(132);
      expect(fetch).toHaveBeenCalledWith('/api/cars/132', expect.objectContaining({}));
    });

    it('getTracks calls GET /api/tracks', async () => {
      mockFetchOk([]);
      await api.getTracks();
      expect(fetch).toHaveBeenCalledWith('/api/tracks', expect.objectContaining({}));
    });

    it('getTrack calls GET /api/tracks/:id', async () => {
      mockFetchOk({ trackId: 18 });
      await api.getTrack(18);
      expect(fetch).toHaveBeenCalledWith('/api/tracks/18', expect.objectContaining({}));
    });

    it('getCars throws with status info when the catalog is unavailable (503)', async () => {
      mockFetchError({ status: 503, statusText: 'Service Unavailable' });
      await expect(api.getCars()).rejects.toThrow('503');
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

  // Token storage and the silent-refresh mechanics (dedup, listener notification, rotation)
  // moved to services/session.ts when the session became one module; they are covered directly
  // in session.test.ts. The 401 *retry* branch is covered in http.test.ts. What remains in this
  // file is what it says on the tin: that each api method calls the right endpoint.

  describe('requestEmailChange', () => {
    it('requestEmailChange posts the new email', async () => {
      const fetchMock = vi.mocked(fetch);
      fetchMock.mockResolvedValue({
        ok: true,
        status: 200,
        statusText: 'OK',
        json: () => Promise.resolve({ message: 'ok' }),
        text: () => Promise.resolve(''),
      } as Response);
      await api.requestEmailChange('new@example.com');
      expect(fetchMock).toHaveBeenCalledWith(
        '/api/auth/request-email-change',
        expect.objectContaining({ method: 'POST' })
      );
      const body = JSON.parse((fetchMock.mock.calls[0][1] as RequestInit).body as string);
      expect(body).toEqual({ newEmail: 'new@example.com' });
    });
  });

  // ── confirmEmailChange ──────────────────────────────────────────────────────

  describe('confirmEmailChange', () => {
    it('confirmEmailChange posts userId, newEmail, token', async () => {
      const fetchMock = vi.mocked(fetch);
      fetchMock.mockResolvedValue({
        ok: true,
        status: 204,
        statusText: 'No Content',
        json: () => Promise.resolve(null),
        text: () => Promise.resolve(''),
      } as Response);
      await api.confirmEmailChange('uid-1', 'new@example.com', 'tok');
      const body = JSON.parse((fetchMock.mock.calls[0][1] as RequestInit).body as string);
      expect(body).toEqual({ userId: 'uid-1', newEmail: 'new@example.com', token: 'tok' });
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
