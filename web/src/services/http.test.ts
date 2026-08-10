import { describe, it, expect, vi } from 'vitest';
import {
  createHttpClient,
  humanMessageFor,
  ApiError,
  IRacingNotLinkedError,
  type HttpDeps,
} from './http';

// ── Response helpers ──────────────────────────────────────────────────────────

type ResponseOpts = { status?: number; statusText?: string; body?: string; json?: unknown };

function response({ status = 200, statusText = 'OK', body = '', json }: ResponseOpts = {}) {
  return {
    ok: status >= 200 && status < 300,
    status,
    statusText,
    json: () => Promise.resolve(json ?? null),
    text: () => Promise.resolve(body),
  } as Response;
}

/**
 * Builds a client over a scripted fetch. `refresh` defaults to failing, so a test that cares
 * about the retry path has to say so — the opposite default would let a broken retry pass.
 */
function harness(responses: Response[], refreshResult = false) {
  const fetchMock = vi.fn<HttpDeps['fetch']>();
  for (const r of responses) fetchMock.mockResolvedValueOnce(r);
  const refresh = vi.fn<HttpDeps['refresh']>().mockResolvedValue(refreshResult);
  let token: string | null = null;
  const client = createHttpClient({
    fetch: fetchMock,
    getAccessToken: () => token,
    refresh,
  });
  return {
    client,
    fetchMock,
    refresh,
    setToken: (t: string | null) => {
      token = t;
    },
    /** The RequestInit fetch was called with on the nth (0-based) call. */
    initOf: (n: number) => fetchMock.mock.calls[n][1] as RequestInit,
    headersOf: (n: number) =>
      (fetchMock.mock.calls[n][1] as RequestInit).headers as Record<string, string>,
  };
}

describe('createHttpClient', () => {
  // ── Request shaping ─────────────────────────────────────────────────────────

  describe('request shaping', () => {
    it('defaults to GET with no body and no Content-Type', async () => {
      const h = harness([response({ json: { ok: 1 } })]);
      await h.client.request('/api/thing');

      expect(h.fetchMock).toHaveBeenCalledWith('/api/thing', expect.objectContaining({}));
      const init = h.initOf(0);
      expect(init.method).toBe('GET');
      expect(init.body).toBeUndefined();
      expect(h.headersOf(0)['Content-Type']).toBeUndefined();
    });

    it('attaches the bearer token when one is available', async () => {
      const h = harness([response({ json: {} })]);
      h.setToken('tok-abc');
      await h.client.request('/api/thing');

      expect(h.headersOf(0).Authorization).toBe('Bearer tok-abc');
    });

    it('omits Authorization entirely when signed out', async () => {
      const h = harness([response({ json: {} })]);
      await h.client.request('/api/thing');

      expect(h.headersOf(0)).not.toHaveProperty('Authorization');
    });

    it('reads the token per request rather than caching it', async () => {
      const h = harness([response({ json: {} }), response({ json: {} })]);
      h.setToken('first');
      await h.client.request('/api/a');
      h.setToken('second');
      await h.client.request('/api/b');

      expect(h.headersOf(0).Authorization).toBe('Bearer first');
      expect(h.headersOf(1).Authorization).toBe('Bearer second');
    });

    it('serializes `json` and sets Content-Type', async () => {
      const h = harness([response({ json: {} })]);
      await h.client.request('/api/thing', { method: 'POST', json: { a: 1 } });

      const init = h.initOf(0);
      expect(init.method).toBe('POST');
      expect(init.body).toBe('{"a":1}');
      expect(h.headersOf(0)['Content-Type']).toBe('application/json');
    });

    it('passes a raw body through untouched and sets no Content-Type', async () => {
      const h = harness([response({ json: {} })]);
      const form = new FormData();
      form.append('file', 'x');
      await h.client.request('/api/upload', { method: 'POST', body: form });

      expect(h.initOf(0).body).toBe(form);
      expect(h.headersOf(0)['Content-Type']).toBeUndefined();
    });

    it('passes the abort signal through to fetch', async () => {
      const h = harness([response({ json: {} })]);
      const controller = new AbortController();

      await h.client.request('/api/thing', { signal: controller.signal });

      expect(h.initOf(0).signal).toBe(controller.signal);
    });
  });

  // ── Responses ───────────────────────────────────────────────────────────────

  describe('responses', () => {
    it('returns the parsed body on success', async () => {
      const h = harness([response({ json: { id: 7 } })]);
      await expect(h.client.request('/api/thing')).resolves.toEqual({ id: 7 });
    });

    it('returns undefined for 204 without parsing a body', async () => {
      const res = response({ status: 204, statusText: 'No Content' });
      const jsonSpy = vi.spyOn(res, 'json');
      const h = harness([res]);

      await expect(h.client.request('/api/thing')).resolves.toBeUndefined();
      expect(jsonSpy).not.toHaveBeenCalled();
    });
  });

  // ── The 401 retry branch ────────────────────────────────────────────────────
  //
  // These replace ten near-identical per-verb copies that reached this branch through an `api`
  // method. The branch does not know what verb it is on, so the coverage is verb-independent
  // and the method used is irrelevant.

  describe('401 retry', () => {
    it('refreshes once and replays the request', async () => {
      const h = harness(
        [response({ status: 401, statusText: 'Unauthorized' }), response({ json: { ok: true } })],
        true
      );

      await expect(h.client.request('/api/thing')).resolves.toEqual({ ok: true });
      expect(h.refresh).toHaveBeenCalledTimes(1);
      expect(h.fetchMock).toHaveBeenCalledTimes(2);
    });

    it('replays with the token the refresh installed, not the stale one', async () => {
      const h = harness(
        [response({ status: 401, statusText: 'Unauthorized' }), response({ json: {} })],
        true
      );
      h.setToken('stale');
      h.refresh.mockImplementation(async () => {
        h.setToken('fresh');
        return true;
      });

      await h.client.request('/api/thing');

      expect(h.headersOf(0).Authorization).toBe('Bearer stale');
      expect(h.headersOf(1).Authorization).toBe('Bearer fresh');
    });

    it('preserves method and body on the replay', async () => {
      const h = harness(
        [response({ status: 401, statusText: 'Unauthorized' }), response({ json: {} })],
        true
      );

      await h.client.request('/api/thing', { method: 'PUT', json: { a: 1 } });

      expect(h.initOf(1).method).toBe('PUT');
      expect(h.initOf(1).body).toBe('{"a":1}');
    });

    it('preserves the abort signal on the replay', async () => {
      const h = harness(
        [response({ status: 401, statusText: 'Unauthorized' }), response({ json: {} })],
        true
      );
      const controller = new AbortController();

      await h.client.request('/api/thing', { signal: controller.signal });

      expect(h.initOf(0).signal).toBe(controller.signal);
      expect(h.initOf(1).signal).toBe(controller.signal);
    });

    it('throws without retrying when the refresh fails', async () => {
      const h = harness([response({ status: 401, statusText: 'Unauthorized' })], false);

      await expect(h.client.request('/api/thing')).rejects.toBeInstanceOf(ApiError);
      expect(h.fetchMock).toHaveBeenCalledTimes(1);
    });

    it('retries at most once — a second 401 throws rather than looping', async () => {
      const h = harness(
        [
          response({ status: 401, statusText: 'Unauthorized' }),
          response({ status: 401, statusText: 'Unauthorized' }),
        ],
        true
      );

      await expect(h.client.request('/api/thing')).rejects.toMatchObject({ status: 401 });
      expect(h.fetchMock).toHaveBeenCalledTimes(2);
      expect(h.refresh).toHaveBeenCalledTimes(1);
    });

    it('does not attempt a refresh on a non-401 failure', async () => {
      const h = harness([response({ status: 500, statusText: 'Server Error' })], true);

      await expect(h.client.request('/api/thing')).rejects.toBeInstanceOf(ApiError);
      expect(h.refresh).not.toHaveBeenCalled();
    });
  });

  // ── Error mapping ───────────────────────────────────────────────────────────

  describe('error mapping', () => {
    it('throws ApiError carrying the status', async () => {
      const h = harness([response({ status: 503, statusText: 'Service Unavailable' })]);

      await expect(h.client.request('/api/thing')).rejects.toMatchObject({
        name: 'ApiError',
        status: 503,
      });
    });

    it('throws the typed IRacingNotLinkedError for the 409 contract', async () => {
      const h = harness([
        response({
          status: 409,
          statusText: 'Conflict',
          body: JSON.stringify({ code: 'IRACING_NOT_LINKED', message: 'Link your account.' }),
        }),
      ]);

      const err = await h.client.request('/api/thing').catch((e: unknown) => e);
      expect(err).toBeInstanceOf(IRacingNotLinkedError);
      expect((err as IRacingNotLinkedError).message).toBe('Link your account.');
    });

    it('falls back to a default message when the 409 body carries no message', async () => {
      const h = harness([
        response({
          status: 409,
          statusText: 'Conflict',
          body: JSON.stringify({ code: 'IRACING_NOT_LINKED' }),
        }),
      ]);

      await expect(h.client.request('/api/thing')).rejects.toThrow('iRacing account not linked.');
    });

    it('treats a 409 without the code as an ordinary ApiError', async () => {
      const h = harness([
        response({
          status: 409,
          statusText: 'Conflict',
          body: JSON.stringify({ title: 'Conflict' }),
        }),
      ]);

      const err = await h.client.request('/api/thing').catch((e: unknown) => e);
      expect(err).toBeInstanceOf(ApiError);
      expect(err).not.toBeInstanceOf(IRacingNotLinkedError);
    });

    it('never puts the status line in the message when the body is ProblemDetails', async () => {
      // The regression behind the percentile page's dead 404 branch: a bare NotFound() arrives
      // as ProblemDetails, so `title` wins and the status never reaches the message. Pages must
      // branch on `.status`.
      const h = harness([
        response({
          status: 404,
          statusText: 'Not Found',
          body: JSON.stringify({ title: 'Not Found', status: 404, traceId: '00-abc' }),
        }),
      ]);

      const err = (await h.client.request('/api/thing').catch((e: unknown) => e)) as ApiError;
      expect(err.status).toBe(404);
      expect(err.message).toBe('Not Found');
      expect(err.message).not.toContain('→ 404');
    });

    it('uses the request method in the status-line fallback', async () => {
      const h = harness([response({ status: 500, statusText: 'Server Error' })]);

      await expect(h.client.request('/api/thing', { method: 'DELETE' })).rejects.toThrow(
        'DELETE /api/thing → 500 Server Error'
      );
    });

    it('survives a body that cannot be read', async () => {
      const res = response({ status: 500, statusText: 'Server Error' });
      vi.spyOn(res, 'text').mockRejectedValue(new Error('stream broken'));
      const h = harness([res]);

      await expect(h.client.request('/api/thing')).rejects.toThrow(
        'GET /api/thing → 500 Server Error'
      );
    });
  });
});

// ── humanMessageFor ───────────────────────────────────────────────────────────
//
// Directly testable now that it is exported: which branch runs depends on the *shape* of the
// body, not the status, which is exactly what makes it easy to get wrong.

describe('humanMessageFor', () => {
  const line = 'GET /api/x → 500 Server Error';

  it('prefers detail over every other field', () => {
    expect(humanMessageFor({ detail: 'd', message: 'm', title: 't' }, '', line)).toBe('d');
  });

  it('falls back to message, then title', () => {
    expect(humanMessageFor({ message: 'm', title: 't' }, '', line)).toBe('m');
    expect(humanMessageFor({ title: 't' }, '', line)).toBe('t');
  });

  it('falls back to the status line for a JSON object with no usable field', () => {
    expect(humanMessageFor({ traceId: '00-abc' }, '{"traceId":"00-abc"}', line)).toBe(line);
  });

  it('unwraps a JSON string body (the 423 lockout case)', () => {
    expect(humanMessageFor('Account locked.', '"Account locked."', line)).toBe('Account locked.');
  });

  it('shows a plain-text body as-is', () => {
    expect(humanMessageFor(null, 'Something broke', line)).toBe('Something broke');
  });

  it('falls back to the status line for an empty body', () => {
    expect(humanMessageFor(null, '   ', line)).toBe(line);
  });

  it('shows nothing but the status line for JSON of another shape', () => {
    expect(humanMessageFor([1, 2, 3], '[1,2,3]', line)).toBe(line);
  });
});
