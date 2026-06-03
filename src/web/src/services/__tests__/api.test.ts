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
      expect(fetch).toHaveBeenCalledWith('/api/series/7/weeks/12/cars', expect.objectContaining({}));
    });

    it('throws on non-ok response', async () => {
      mockFetchError({ status: 404, statusText: 'Not Found' });
      await expect(api.getCarsForWeek(1, 1)).rejects.toThrow('404');
    });
  });

  // ── getPercentile ───────────────────────────────────────────────────────────

  describe('getPercentile', () => {
    it('calls GET with seriesId, weekNumber, carId, and customerId query param', async () => {
      const data = { seriesId: 1, weekNumber: 5, carId: 3, customerId: 99, percentileRank: 75.0, sampleSize: 100, computedAt: '' };
      mockFetchOk(data);
      const result = await api.getPercentile(1, 5, 3, 99);
      expect(fetch).toHaveBeenCalledWith('/api/series/1/weeks/5/cars/3/percentile?customerId=99', expect.objectContaining({}));
      expect(result.percentileRank).toBe(75.0);
    });
  });

  // ── getRecommendations ──────────────────────────────────────────────────────

  describe('getRecommendations', () => {
    it('calls GET with seriesId and weekNumber query params', async () => {
      mockFetchOk([]);
      await api.getRecommendations(1, 4);
      expect(fetch).toHaveBeenCalledWith('/api/users/me/recommendations?seriesId=1&weekNumber=4', expect.objectContaining({}));
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

  // ── updateProfile ───────────────────────────────────────────────────────────

  describe('updateProfile', () => {
    it('calls PUT /api/auth/profile with display name and customer ID', async () => {
      mockFetchOk({ token: 'new-jwt', userId: 'u1', displayName: 'Updated Name' });
      const result = await api.updateProfile('Updated Name', 100042, 'driver@example.com');
      expect(fetch).toHaveBeenCalledWith('/api/auth/profile', expect.objectContaining({
        method: 'PUT',
        body: JSON.stringify({ displayName: 'Updated Name', iRacingCustomerId: 100042, email: 'driver@example.com' }),
      }));
      expect(result.displayName).toBe('Updated Name');
    });

    it('sends null iRacingCustomerId when not provided', async () => {
      mockFetchOk({ token: 'new-jwt', userId: 'u1', displayName: 'Updated Name' });
      await api.updateProfile('Updated Name', null, 'driver@example.com');
      expect(fetch).toHaveBeenCalledWith('/api/auth/profile', expect.objectContaining({
        body: JSON.stringify({ displayName: 'Updated Name', iRacingCustomerId: null, email: 'driver@example.com' }),
      }));
    });

    it('throws with server error body on failure', async () => {
      mockFetchError({ body: 'Display name too long.' });
      await expect(api.updateProfile('A'.repeat(100), null, 'driver@example.com')).rejects.toThrow('Display name too long.');
    });

    it('falls back to status line when body is empty', async () => {
      mockFetchError({ status: 400, statusText: 'Bad Request', body: '' });
      await expect(api.updateProfile('name', null, 'driver@example.com')).rejects.toThrow('PUT /api/auth/profile → 400');
    });
  });

  // ── getMyAnalytics ──────────────────────────────────────────────────────────

  describe('getMyAnalytics', () => {
    it('calls GET /api/users/me/analytics with seriesId query param when provided', async () => {
      mockFetchOk([]);
      await api.getMyAnalytics(3);
      expect(fetch).toHaveBeenCalledWith('/api/users/me/analytics?seriesId=3', expect.objectContaining({}));
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

  // ── setToken / clearToken ───────────────────────────────────────────────────

  // ── updateRole ──────────────────────────────────────────────────────────────

  describe('updateRole', () => {
    it('calls PUT /api/auth/role with role body and returns fresh JWT', async () => {
      mockFetchOk({ token: 'new-jwt', userId: 'u1', displayName: 'Jerry' });
      const result = await api.updateRole('Beta');
      expect(fetch).toHaveBeenCalledWith('/api/auth/role', expect.objectContaining({
        method: 'PUT',
        body: JSON.stringify({ role: 'Beta' }),
      }));
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
      const data = [{ id: 1, key: 'new-ui', name: 'New UI', description: null, isEnabled: true, minimumRole: 'Standard', createdAt: '', updatedAt: '' }];
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
      const data = [{ userId: 'u1', email: 'admin@example.com', displayName: 'Admin', role: 'Admin' }];
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
      expect(fetch).toHaveBeenCalledWith('/api/admin/users/u2/role', expect.objectContaining({
        method: 'PUT',
        body: JSON.stringify({ role: 'Beta' }),
      }));
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
      const data = [{ id: 2, key: 'dark-mode', name: 'Dark Mode', description: null, isEnabled: false, minimumRole: 'Alpha', createdAt: '', updatedAt: '' }];
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
      const payload = { key: 'exp-lap', name: 'Experimental Lap', description: 'A test flag', isEnabled: true, minimumRole: 'Beta' };
      const created = { id: 5, ...payload, createdAt: '', updatedAt: '' };
      mockFetchOk(created);
      const result = await api.createFeatureFlag(payload);
      expect(fetch).toHaveBeenCalledWith('/api/admin/feature-flags', expect.objectContaining({
        method: 'POST',
        body: JSON.stringify(payload),
      }));
      expect(result.id).toBe(5);
    });

    it('throws with server error body on failure', async () => {
      mockFetchError({ body: 'Key already exists.' });
      await expect(api.createFeatureFlag({ key: 'dup', name: 'Dup', description: null, isEnabled: false, minimumRole: 'Standard' }))
        .rejects.toThrow('Key already exists.');
    });
  });

  // ── updateFeatureFlag ───────────────────────────────────────────────────────

  describe('updateFeatureFlag', () => {
    it('calls PUT /api/admin/feature-flags/:id with update body', async () => {
      const updateData = { name: 'Updated', description: null, isEnabled: false, minimumRole: 'Alpha' };
      const updated = { id: 5, key: 'exp-lap', ...updateData, createdAt: '', updatedAt: '' };
      mockFetchOk(updated);
      const result = await api.updateFeatureFlag(5, updateData);
      expect(fetch).toHaveBeenCalledWith('/api/admin/feature-flags/5', expect.objectContaining({
        method: 'PUT',
        body: JSON.stringify(updateData),
      }));
      expect(result.name).toBe('Updated');
    });

    it('throws with server error body on failure', async () => {
      mockFetchError({ body: 'Flag not found.' });
      await expect(api.updateFeatureFlag(999, { name: 'x', description: null, isEnabled: true, minimumRole: 'Standard' }))
        .rejects.toThrow('Flag not found.');
    });
  });

  // ── deleteFeatureFlag ───────────────────────────────────────────────────────

  describe('deleteFeatureFlag', () => {
    it('calls DELETE /api/admin/feature-flags/:id', async () => {
      vi.mocked(fetch).mockResolvedValue({
        ok: true, status: 204, statusText: 'No Content',
        json: () => Promise.resolve(null),
        text: () => Promise.resolve(''),
      } as Response);
      await api.deleteFeatureFlag(5);
      expect(fetch).toHaveBeenCalledWith('/api/admin/feature-flags/5', expect.objectContaining({ method: 'DELETE' }));
    });

    it('throws with status info on non-ok response', async () => {
      mockFetchError({ status: 404, statusText: 'Not Found' });
      await expect(api.deleteFeatureFlag(999)).rejects.toThrow('DELETE /api/admin/feature-flags/999 → 404 Not Found');
    });
  });

  // ── setToken / clearToken ───────────────────────────────────────────────────

  describe('setToken and clearToken', () => {
    it('setToken causes subsequent requests to include an Authorization header', async () => {
      const { setToken } = await import('../api');
      setToken('my-jwt');
      mockFetchOk([]);
      await api.getSeries();
      expect(fetch).toHaveBeenCalledWith(
        '/api/series',
        expect.objectContaining({ headers: { Authorization: 'Bearer my-jwt' } }),
      );
    });

    it('clearToken removes the Authorization header from subsequent requests', async () => {
      const { setToken, clearToken } = await import('../api');
      setToken('my-jwt');
      clearToken();
      mockFetchOk([]);
      await api.getSeries();
      expect(fetch).toHaveBeenCalledWith(
        '/api/series',
        expect.objectContaining({ headers: {} }),
      );
    });
  });
});
