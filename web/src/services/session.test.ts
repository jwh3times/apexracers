import { describe, it, expect, vi } from 'vitest';
import {
  createSession,
  decodeJwt,
  isTokenExpired,
  ACCESS_TOKEN_KEY,
  REFRESH_TOKEN_KEY,
  type KeyValueStore,
  type SessionTokens,
} from './session';

// ── Helpers ───────────────────────────────────────────────────────────────────

/** Builds an unsigned JWT — only the payload matters, nothing here verifies a signature. */
function jwt(payload: Record<string, unknown>): string {
  const b64 = (o: unknown) =>
    btoa(JSON.stringify(o)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
  return `${b64({ alg: 'none' })}.${b64(payload)}.sig`;
}

const CLAIMS = {
  sub: 'user-1',
  email: 'driver@example.com',
  name: 'Test Driver',
  role: 'Standard',
};

const FUTURE = Math.floor(Date.now() / 1000) + 3600;
const PAST = Math.floor(Date.now() / 1000) - 3600;

/** In-memory store — the second real adapter behind the storage seam. */
function memoryStore(seed: Record<string, unknown> = {}): KeyValueStore & {
  data: Map<string, unknown>;
} {
  const data = new Map<string, unknown>(Object.entries(seed));
  return {
    data,
    get: <T>(k: string) => Promise.resolve(data.get(k) as T | undefined),
    set: (k: string, v: unknown) => {
      data.set(k, v);
      return Promise.resolve();
    },
    remove: (k: string) => {
      data.delete(k);
      return Promise.resolve();
    },
  };
}

function build(
  seed: Record<string, unknown> = {},
  transport: (rt: string) => Promise<SessionTokens | null> = () => Promise.resolve(null)
) {
  const store = memoryStore(seed);
  const refreshTransport = vi.fn(transport);
  const session = createSession({ store, refreshTransport });
  return { session, store, refreshTransport };
}

// ── decodeJwt / isTokenExpired ────────────────────────────────────────────────

describe('decodeJwt', () => {
  it('decodes a well-formed payload', () => {
    expect(decodeJwt(jwt(CLAIMS))).toMatchObject(CLAIMS);
  });

  it('handles base64url padding and substitutions', () => {
    const claims = { ...CLAIMS, name: 'Ünïcøde Dríver ????' };
    expect(decodeJwt(jwt(claims))?.name).toBe(claims.name);
  });

  it('returns null for a malformed token rather than throwing', () => {
    expect(decodeJwt('not-a-jwt')).toBeNull();
    expect(decodeJwt('')).toBeNull();
    expect(decodeJwt('a.!!!not-base64!!!.c')).toBeNull();
  });
});

describe('isTokenExpired', () => {
  it('is false for a token expiring in the future', () => {
    expect(isTokenExpired(jwt({ ...CLAIMS, exp: FUTURE }))).toBe(false);
  });

  it('is true for a token already past its exp', () => {
    expect(isTokenExpired(jwt({ ...CLAIMS, exp: PAST }))).toBe(true);
  });

  it('treats a token with no exp as live — the server is still the authority', () => {
    expect(isTokenExpired(jwt(CLAIMS))).toBe(false);
  });

  it('compares against the supplied clock', () => {
    const token = jwt({ ...CLAIMS, exp: 1000 });
    expect(isTokenExpired(token, 999_000)).toBe(false);
    expect(isTokenExpired(token, 1_001_000)).toBe(true);
  });
});

// ── restore ───────────────────────────────────────────────────────────────────

describe('restore', () => {
  it('adopts a stored, unexpired access token', async () => {
    const token = jwt({ ...CLAIMS, exp: FUTURE });
    const { session } = build({ [ACCESS_TOKEN_KEY]: token, [REFRESH_TOKEN_KEY]: 'rt' });

    const snapshot = await session.restore();

    expect(session.accessToken).toBe(token);
    expect(session.refreshToken).toBe('rt');
    expect(snapshot.claims).toMatchObject(CLAIMS);
  });

  it('exchanges the refresh token when the stored access token has expired', async () => {
    const fresh = jwt({ ...CLAIMS, exp: FUTURE });
    const { session, refreshTransport } = build(
      { [ACCESS_TOKEN_KEY]: jwt({ ...CLAIMS, exp: PAST }), [REFRESH_TOKEN_KEY]: 'rt' },
      () => Promise.resolve({ accessToken: fresh, refreshToken: 'rt-2' })
    );

    await session.restore();

    expect(refreshTransport).toHaveBeenCalledWith('rt');
    expect(session.accessToken).toBe(fresh);
    expect(session.refreshToken).toBe('rt-2');
  });

  it('exchanges the refresh token when no access token was stored at all', async () => {
    const fresh = jwt({ ...CLAIMS, exp: FUTURE });
    const { session } = build({ [REFRESH_TOKEN_KEY]: 'rt' }, () =>
      Promise.resolve({ accessToken: fresh })
    );

    await session.restore();

    expect(session.accessToken).toBe(fresh);
  });

  it('ends signed out when the refresh token is spent, and clears storage', async () => {
    const { session, store } = build(
      { [ACCESS_TOKEN_KEY]: jwt({ ...CLAIMS, exp: PAST }), [REFRESH_TOKEN_KEY]: 'rt' },
      () => Promise.resolve(null)
    );

    await session.restore();

    expect(session.accessToken).toBeNull();
    expect(session.refreshToken).toBeNull();
    expect(store.data.has(ACCESS_TOKEN_KEY)).toBe(false);
    expect(store.data.has(REFRESH_TOKEN_KEY)).toBe(false);
  });

  it('resolves signed out when nothing is stored, without calling the transport', async () => {
    const { session, refreshTransport } = build();

    const snapshot = await session.restore();

    expect(snapshot).toEqual({ accessToken: null, claims: null });
    expect(refreshTransport).not.toHaveBeenCalled();
  });

  it('resolves rather than rejecting when storage itself fails', async () => {
    const store = memoryStore();
    store.get = () => Promise.reject(new Error('IndexedDB unavailable'));
    const session = createSession({ store, refreshTransport: () => Promise.resolve(null) });

    await expect(session.restore()).resolves.toEqual({ accessToken: null, claims: null });
  });

  it('is awaitable, so a caller can order requests after the token is installed', async () => {
    // The bug this closes: the refresh token used to be installed inside a .then() on an async
    // IndexedDB read, so anything issued before that resolved got a 401 with no retry.
    const token = jwt({ ...CLAIMS, exp: FUTURE });
    const { session } = build({ [ACCESS_TOKEN_KEY]: token });

    expect(session.accessToken).toBeNull();
    await session.restore();
    expect(session.accessToken).toBe(token);
  });
});

// ── adopt / clear ─────────────────────────────────────────────────────────────

describe('adopt', () => {
  it('installs the pair, decodes claims, and persists both', async () => {
    const token = jwt({ ...CLAIMS, exp: FUTURE });
    const { session, store } = build();

    await session.adopt({ accessToken: token, refreshToken: 'rt' });

    expect(session.accessToken).toBe(token);
    expect(session.claims).toMatchObject(CLAIMS);
    expect(store.data.get(ACCESS_TOKEN_KEY)).toBe(token);
    expect(store.data.get(REFRESH_TOKEN_KEY)).toBe('rt');
  });

  it('leaves the existing refresh token alone when none is supplied (rotation disabled)', async () => {
    const { session, store } = build();
    await session.adopt({ accessToken: jwt(CLAIMS), refreshToken: 'rt-original' });

    await session.adopt({ accessToken: jwt({ ...CLAIMS, name: 'Renamed' }) });

    expect(session.refreshToken).toBe('rt-original');
    expect(store.data.get(REFRESH_TOKEN_KEY)).toBe('rt-original');
    expect(session.claims?.name).toBe('Renamed');
  });
});

describe('clear', () => {
  it('drops both tokens from memory and storage', async () => {
    const { session, store } = build();
    await session.adopt({ accessToken: jwt(CLAIMS), refreshToken: 'rt' });

    await session.clear();

    expect(session.accessToken).toBeNull();
    expect(session.refreshToken).toBeNull();
    expect(session.claims).toBeNull();
    expect(store.data.size).toBe(0);
  });
});

// ── refresh ───────────────────────────────────────────────────────────────────

describe('refresh', () => {
  it('returns false without calling the transport when there is no refresh token', async () => {
    const { session, refreshTransport } = build();

    await expect(session.refresh()).resolves.toBe(false);
    expect(refreshTransport).not.toHaveBeenCalled();
  });

  it('installs the new pair and returns true', async () => {
    const fresh = jwt({ ...CLAIMS, exp: FUTURE });
    const { session, store } = build({}, () =>
      Promise.resolve({ accessToken: fresh, refreshToken: 'rt-2' })
    );
    await session.adopt({ accessToken: jwt(CLAIMS), refreshToken: 'rt-1' });

    await expect(session.refresh()).resolves.toBe(true);
    expect(session.accessToken).toBe(fresh);
    expect(store.data.get(REFRESH_TOKEN_KEY)).toBe('rt-2');
  });

  it('deduplicates concurrent attempts into one transport call', async () => {
    let release!: (v: SessionTokens) => void;
    const pending = new Promise<SessionTokens>(r => {
      release = r;
    });
    const { session, refreshTransport } = build({}, () => pending);
    await session.adopt({ accessToken: jwt(CLAIMS), refreshToken: 'rt' });

    const all = Promise.all([session.refresh(), session.refresh(), session.refresh()]);
    release({ accessToken: jwt({ ...CLAIMS, exp: FUTURE }) });

    expect(await all).toEqual([true, true, true]);
    expect(refreshTransport).toHaveBeenCalledTimes(1);
  });

  it('allows a fresh attempt after the previous one settles', async () => {
    const { session, refreshTransport } = build({}, () =>
      Promise.resolve({ accessToken: jwt({ ...CLAIMS, exp: FUTURE }), refreshToken: 'rt' })
    );
    await session.adopt({ accessToken: jwt(CLAIMS), refreshToken: 'rt' });

    await session.refresh();
    await session.refresh();

    expect(refreshTransport).toHaveBeenCalledTimes(2);
  });

  it('clears the session when the transport reports the token is spent', async () => {
    const { session, store } = build({}, () => Promise.resolve(null));
    await session.adopt({ accessToken: jwt(CLAIMS), refreshToken: 'rt' });

    await expect(session.refresh()).resolves.toBe(false);
    expect(session.accessToken).toBeNull();
    expect(store.data.size).toBe(0);
  });

  it('clears the session when the transport throws', async () => {
    const { session } = build({}, () => Promise.reject(new Error('network down')));
    await session.adopt({ accessToken: jwt(CLAIMS), refreshToken: 'rt' });

    await expect(session.refresh()).resolves.toBe(false);
    expect(session.accessToken).toBeNull();
  });
});

// ── subscribe ─────────────────────────────────────────────────────────────────
//
// The defect this closes: the old interceptor exposed two single callback slots, so a second
// registration silently replaced the first — which any StrictMode double-invoke or multi-provider
// test does.

describe('subscribe', () => {
  it('notifies every listener, not just the most recent', async () => {
    const { session } = build();
    const first = vi.fn();
    const second = vi.fn();
    session.subscribe(first);
    session.subscribe(second);

    await session.adopt({ accessToken: jwt(CLAIMS) });

    expect(first).toHaveBeenCalledTimes(1);
    expect(second).toHaveBeenCalledTimes(1);
  });

  it('stops notifying after unsubscribe, leaving others intact', async () => {
    const { session } = build();
    const kept = vi.fn();
    const dropped = vi.fn();
    session.subscribe(kept);
    const unsubscribe = session.subscribe(dropped);

    unsubscribe();
    await session.adopt({ accessToken: jwt(CLAIMS) });

    expect(dropped).not.toHaveBeenCalled();
    expect(kept).toHaveBeenCalledTimes(1);
  });

  it('passes the new token and claims on adopt', async () => {
    const token = jwt({ ...CLAIMS, exp: FUTURE });
    const { session } = build();
    const listener = vi.fn();
    session.subscribe(listener);

    await session.adopt({ accessToken: token });

    expect(listener).toHaveBeenCalledWith({
      accessToken: token,
      claims: expect.objectContaining(CLAIMS),
    });
  });

  it('passes a signed-out snapshot on clear', async () => {
    const { session } = build();
    await session.adopt({ accessToken: jwt(CLAIMS), refreshToken: 'rt' });
    const listener = vi.fn();
    session.subscribe(listener);

    await session.clear();

    expect(listener).toHaveBeenCalledWith({ accessToken: null, claims: null });
  });

  it('notifies on a failed refresh so the app can drop the user', async () => {
    const { session } = build({}, () => Promise.resolve(null));
    await session.adopt({ accessToken: jwt(CLAIMS), refreshToken: 'rt' });
    const listener = vi.fn();
    session.subscribe(listener);

    await session.refresh();

    expect(listener).toHaveBeenCalledWith({ accessToken: null, claims: null });
  });

  it('tolerates a listener unsubscribing itself mid-notification', async () => {
    const { session } = build();
    const other = vi.fn();
    const unsubscribe = session.subscribe(() => unsubscribe());
    session.subscribe(other);

    await expect(session.adopt({ accessToken: jwt(CLAIMS) })).resolves.toBeUndefined();
    expect(other).toHaveBeenCalledTimes(1);
  });
});
