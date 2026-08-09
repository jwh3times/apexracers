import { dbGet, dbSet, dbRemove } from './db';

/**
 * The one owner of the signed-in session: the token pair, the claims decoded from it, its
 * persistence, and the silent refresh.
 *
 * Before this module the same session lived in two places — `api.ts` held `_token`/`_refreshToken`
 * and `tryRefresh`, while `AuthProvider` held `user.token`/`refreshTokenRef` and the IndexedDB
 * writes — kept in step by five imperative setters called from ten sites plus two global callback
 * slots. Neither half was a pass-through, so neither could simply be deleted: they were one module
 * wearing two hats, and the bug surface was the handshake between them, which nothing tested.
 *
 * Three concrete defects that shape fixes:
 *
 *  1. `onTokenRefreshed`/`onSessionExpired` were single slots — a second registration silently
 *     replaced the first, which any StrictMode double-invoke or multi-provider test does.
 *     `subscribe` keeps a list and hands back an unsubscribe.
 *  2. The refresh token was installed inside a `.then()` on an async IndexedDB read, so a request
 *     issued before that resolved got a 401 with no retry. `restore()` is awaitable, so callers
 *     can order themselves against it.
 *  3. `login` wrote five things, `updateSession` three, `logout` cleared two — a caller had to
 *     know which. `adopt` and `clear` are now the only writers.
 */

export type JwtClaims = {
  sub: string;
  email: string;
  name: string;
  iracing_id?: string;
  role?: string;
  theme_preference?: string;
  exp?: number;
};

export type SessionTokens = { accessToken: string; refreshToken?: string | null };

export type SessionSnapshot = { accessToken: string | null; claims: JwtClaims | null };

/** Storage seam. Two real adapters: IndexedDB in the app, in-memory in tests. */
export interface KeyValueStore {
  get<T>(key: string): Promise<T | undefined>;
  set(key: string, value: unknown): Promise<void>;
  remove(key: string): Promise<void>;
}

/**
 * Exchanges a refresh token for a new pair, or null when the session is over.
 *
 * Injected rather than imported so this module has no dependency on `api.ts` — and, more
 * importantly, so the refresh call cannot be routed through the intercepting http client, which
 * would call back into `refresh()` on a 401 and recurse.
 */
export type RefreshTransport = (refreshToken: string) => Promise<SessionTokens | null>;

export interface Session {
  readonly accessToken: string | null;
  readonly refreshToken: string | null;
  readonly claims: JwtClaims | null;
  /**
   * Loads the persisted pair. If the access token is missing or expired but a refresh token
   * survives, exchanges it. Resolves once the session can authorize requests — await this before
   * issuing any.
   */
  restore(): Promise<SessionSnapshot>;
  /** Installs a new pair and persists it. An absent `refreshToken` leaves the current one alone. */
  adopt(tokens: SessionTokens): Promise<void>;
  clear(): Promise<void>;
  /** Attempts a silent refresh. Concurrent calls share one in-flight attempt. */
  refresh(): Promise<boolean>;
  subscribe(listener: (snapshot: SessionSnapshot) => void): () => void;
}

export const ACCESS_TOKEN_KEY = 'ar_token';
export const REFRESH_TOKEN_KEY = 'ar_refresh_token';

/** Decodes a JWT payload without verifying it — the server is the authority; this is for display. */
export function decodeJwt(token: string): JwtClaims | null {
  try {
    const payload = token.split('.')[1];
    return JSON.parse(atob(payload.replace(/-/g, '+').replace(/_/g, '/'))) as JwtClaims;
  } catch {
    return null;
  }
}

/** A token with no `exp` is treated as live — the server still rejects it if it isn't. */
export function isTokenExpired(token: string, now: number = Date.now()): boolean {
  const claims = decodeJwt(token);
  if (!claims?.exp) return false;
  return claims.exp * 1000 < now;
}

export type SessionDeps = {
  store: KeyValueStore;
  refreshTransport: RefreshTransport;
  now?: () => number;
};

export function createSession({ store, refreshTransport, now = Date.now }: SessionDeps): Session {
  let accessToken: string | null = null;
  let refreshToken: string | null = null;
  let claims: JwtClaims | null = null;
  let inFlight: Promise<boolean> | null = null;
  const listeners = new Set<(snapshot: SessionSnapshot) => void>();

  function snapshot(): SessionSnapshot {
    return { accessToken, claims };
  }

  function notify(): void {
    const s = snapshot();
    // Copy first: a listener may unsubscribe itself while being notified.
    for (const listener of [...listeners]) listener(s);
  }

  async function write(tokens: SessionTokens): Promise<void> {
    accessToken = tokens.accessToken;
    claims = decodeJwt(tokens.accessToken);
    await store.set(ACCESS_TOKEN_KEY, tokens.accessToken);
    if (tokens.refreshToken) {
      refreshToken = tokens.refreshToken;
      await store.set(REFRESH_TOKEN_KEY, tokens.refreshToken);
    }
  }

  async function wipe(): Promise<void> {
    accessToken = null;
    refreshToken = null;
    claims = null;
    await Promise.all([store.remove(ACCESS_TOKEN_KEY), store.remove(REFRESH_TOKEN_KEY)]).catch(
      () => {}
    );
  }

  return {
    get accessToken() {
      return accessToken;
    },
    get refreshToken() {
      return refreshToken;
    },
    get claims() {
      return claims;
    },

    async restore(): Promise<SessionSnapshot> {
      const [storedAccess, storedRefresh] = await Promise.all([
        store.get<string>(ACCESS_TOKEN_KEY),
        store.get<string>(REFRESH_TOKEN_KEY),
      ]).catch(() => [undefined, undefined] as const);

      refreshToken = storedRefresh ?? null;

      if (storedAccess && !isTokenExpired(storedAccess, now())) {
        accessToken = storedAccess;
        claims = decodeJwt(storedAccess);
      } else if (refreshToken) {
        await this.refresh();
      }

      notify();
      return snapshot();
    },

    async adopt(tokens: SessionTokens): Promise<void> {
      await write(tokens);
      notify();
    },

    async clear(): Promise<void> {
      await wipe();
      notify();
    },

    refresh(): Promise<boolean> {
      if (!refreshToken) return Promise.resolve(false);
      if (inFlight) return inFlight;

      inFlight = (async (): Promise<boolean> => {
        try {
          const tokens = await refreshTransport(refreshToken!);
          if (!tokens) {
            await wipe();
            notify();
            return false;
          }
          await write(tokens);
          notify();
          return true;
        } catch {
          await wipe();
          notify();
          return false;
        } finally {
          inFlight = null;
        }
      })();

      return inFlight;
    },

    subscribe(listener: (snapshot: SessionSnapshot) => void): () => void {
      listeners.add(listener);
      return () => {
        listeners.delete(listener);
      };
    },
  };
}

// ── App wiring ────────────────────────────────────────────────────────────────

/** IndexedDB adapter — the production half of the storage seam. */
export const indexedDbStore: KeyValueStore = {
  get: dbGet,
  set: dbSet,
  remove: dbRemove,
};

/**
 * Raw `fetch` on purpose: routing this through the http client would re-enter `refresh()` on a
 * 401 and recurse. A non-2xx here means the refresh token is spent, which is a `null`, not a throw.
 */
const httpRefreshTransport: RefreshTransport = async refreshToken => {
  const res = await fetch('/api/auth/refresh', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ refreshToken }),
  });
  if (!res.ok) return null;
  const data = (await res.json()) as { token: string; refreshToken?: string | null };
  return { accessToken: data.token, refreshToken: data.refreshToken ?? null };
};

/** The app-wide session. Tests build their own with `createSession`. */
export const session = createSession({
  store: indexedDbStore,
  refreshTransport: httpRefreshTransport,
});
