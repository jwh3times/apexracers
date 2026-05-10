import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { api } from '../api';

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

function mockFetchError({ ok = false, status = 500, statusText = 'Server Error', body = '' }: MockResponseOpts = {}) {
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
      const data = [{ id: 1, name: 'GT3 Cup', seasonId: 10, currentWeekId: 5 }];
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
      expect(fetch).toHaveBeenCalledWith('/api/series/7/weeks/12/cars', expect.objectContaining({}));
    });

    it('throws on non-ok response', async () => {
      mockFetchError({ status: 404, statusText: 'Not Found' });
      await expect(api.getCarsForWeek(1, 1)).rejects.toThrow('404');
    });
  });

  // ── getPercentile ───────────────────────────────────────────────────────────

  describe('getPercentile', () => {
    it('calls GET with seriesId, weekId, carId, and customerId query param', async () => {
      const data = { seriesId: 1, weekId: 5, carId: 3, customerId: 99, percentileRank: 75.0, sampleSize: 100, computedAt: '' };
      mockFetchOk(data);
      const result = await api.getPercentile(1, 5, 3, 99);
      expect(fetch).toHaveBeenCalledWith('/api/series/1/weeks/5/cars/3/percentile?customerId=99', expect.objectContaining({}));
      expect(result.percentileRank).toBe(75.0);
    });
  });

  // ── getRecommendations ──────────────────────────────────────────────────────

  describe('getRecommendations', () => {
    it('calls GET with weekId query param', async () => {
      mockFetchOk([]);
      await api.getRecommendations(10);
      expect(fetch).toHaveBeenCalledWith('/api/users/me/recommendations?weekId=10', expect.objectContaining({}));
    });
  });

  // ── login ───────────────────────────────────────────────────────────────────

  describe('login', () => {
    it('calls POST /api/auth/login with JSON body and correct headers', async () => {
      mockFetchOk({ token: 'jwt', userId: 'u1', displayName: 'Jerry' });
      const result = await api.login('jerry@example.com', 'password123');
      expect(fetch).toHaveBeenCalledWith('/api/auth/login', expect.objectContaining({
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ email: 'jerry@example.com', password: 'password123' }),
      }));
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
      expect(fetch).toHaveBeenCalledWith('/api/auth/register', expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({ email: 'new@example.com', password: 'secret' }),
      }));
    });

    it('throws with server error body on failure', async () => {
      mockFetchError({ body: 'Email already registered.' });
      await expect(api.register('dup@example.com', 'pass')).rejects.toThrow('Email already registered.');
    });
  });

  // ── postAuthCallback ────────────────────────────────────────────────────────

  describe('postAuthCallback', () => {
    it('calls POST with code and state as query params', async () => {
      mockFetchOk({});
      await api.postAuthCallback('auth-code-123', 'state-abc');
      expect(fetch).toHaveBeenCalledWith(
        '/api/auth/callback?code=auth-code-123&state=state-abc',
        expect.objectContaining({ method: 'POST' }),
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
        totalLaps: 10, validLaps: 8, bestLapSeconds: 130.5,
        trackName: 'Spa', configName: 'Full', carName: 'Porsche',
        customerId: 12345, driverName: 'Jerry',
      };
      mockFetchOk(result);
      const file = new File(['data'], 'session.ibt');
      const output = await api.uploadTelemetry(file);
      expect(fetch).toHaveBeenCalledWith(
        '/api/telemetry/upload',
        expect.objectContaining({ method: 'POST', body: expect.any(FormData) }),
      );
      expect(output.driverName).toBe('Jerry');
    });

    it('throws with server error body on failure', async () => {
      mockFetchError({ body: 'Invalid file format' });
      await expect(api.uploadTelemetry(new File([''], 'bad.ibt'))).rejects.toThrow('Invalid file format');
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
});
