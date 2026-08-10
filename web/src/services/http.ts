/**
 * The HTTP core the whole API surface is built on.
 *
 * This module owns everything a caller must get right about talking to the API — auth headers,
 * the one silent-refresh retry on 401, RFC-7807 error mapping, the typed 409 not-linked contract,
 * and 204 handling — behind a single `request<T>` method.
 *
 * It is a separate module from `api.ts` so that behaviour is reachable *directly* from a test.
 * While `request` was private, the only way to exercise its 401-retry branch was through one of
 * the 50-odd methods on `api`, which is why the suite grew ten near-identical copies of the same
 * retry test (one per HTTP verb) for five lines of implementation. `fetch` and `refresh` are
 * injected rather than reached for, so a test supplies its own without touching globals.
 *
 * Deliberately knows nothing about *where* tokens live: it asks for the access token and asks
 * whether a refresh succeeded. Session ownership stays with the caller.
 */

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

/** HTTP failure carrying the response status, for status-aware handling (e.g. 503 = unavailable). */
export class ApiError extends Error {
  readonly status: number;
  constructor(status: number, message: string) {
    super(message);
    this.status = status;
    this.name = 'ApiError';
  }
}

type ProblemBody = { code?: string; message?: string; detail?: string; title?: string };

function tryParseJson(raw: string): unknown {
  try {
    return raw ? JSON.parse(raw) : null;
  } catch {
    return null;
  }
}

function asProblemBody(value: unknown): ProblemBody | null {
  return typeof value === 'object' && value !== null && !Array.isArray(value) ? value : null;
}

/**
 * Picks the most useful *human-readable* message from an error response.
 *
 * The raw body is only a safe fallback when it is not a JSON object. ASP.NET Core fills in
 * automatic ProblemDetails for a bare status result (e.g. `Unauthorized()`), producing a
 * well-formed object carrying only type/title/status/traceId — printing that verbatim put a
 * raw JSON blob, traceId and all, in front of the user on every failed login. For a JSON
 * object we therefore stop at `title`; for valid JSON in any other shape we show nothing but
 * the status line.
 *
 * Exported so its cases can be asserted directly — the reason this is subtle is that the
 * *shape* of the body, not the status, decides which branch runs.
 */
export function humanMessageFor(parsed: unknown, raw: string, statusLine: string): string {
  const problem = asProblemBody(parsed);
  if (problem) return problem.detail || problem.message || problem.title || statusLine;
  // A JSON string body is itself the message (AuthController's 423 lockout).
  if (typeof parsed === 'string') return parsed.trim() || statusLine;
  // Not JSON at all — a text/plain body is already human-readable.
  if (parsed === null) return raw.trim() || statusLine;
  return statusLine;
}

/**
 * Maps a non-ok response to the right thrown error: a typed IRacingNotLinkedError for the 409
 * not-linked contract, otherwise an ApiError carrying the message chosen by humanMessageFor.
 *
 * Note the status line is only ever a *fallback* message. Pages must branch on
 * `ApiError.status`, never on the text — a bare `NotFound()` arrives as ProblemDetails whose
 * title wins, so the status never appears in the message at all.
 */
export async function throwForResponse(
  res: Response,
  path: string,
  method: string
): Promise<never> {
  const raw = await res.text().catch(() => '');
  const parsed = tryParseJson(raw);
  const problem = asProblemBody(parsed);

  if (res.status === 409 && problem?.code === 'IRACING_NOT_LINKED') {
    throw new IRacingNotLinkedError(problem.message ?? 'iRacing account not linked.');
  }

  const statusLine = `${method} ${path} → ${res.status} ${res.statusText}`;
  throw new ApiError(res.status, humanMessageFor(parsed, raw, statusLine));
}

export type ReqInit = {
  method?: string;
  body?: BodyInit;
  json?: unknown;
  signal?: AbortSignal;
};

export interface HttpClient {
  /**
   * Issues a request and returns the parsed body.
   *
   * Attaches the bearer token when one is available; on a 401, attempts one silent refresh and
   * replays the request once (never more — a second 401 is a real failure). Throws
   * `IRacingNotLinkedError` for the typed 409, `ApiError` for every other non-ok response.
   * Returns `undefined` for 204. Pass a JSON payload via `json`, a raw body (e.g. FormData)
   * via `body`, and an `AbortSignal` via `signal`; the same signal is preserved on a 401 replay.
   */
  request<T>(path: string, init?: ReqInit): Promise<T>;
}

export type HttpDeps = {
  /** Injected so tests supply their own without stubbing a global. */
  fetch: typeof globalThis.fetch;
  /** Current access token, or null when signed out. Read per request, never cached. */
  getAccessToken: () => string | null;
  /** Attempts a silent refresh; `true` means the request is worth replaying. */
  refresh: () => Promise<boolean>;
};

export function createHttpClient(deps: HttpDeps): HttpClient {
  return {
    async request<T>(path: string, init: ReqInit = {}): Promise<T> {
      const build = (): RequestInit => {
        const token = deps.getAccessToken();
        const headers: Record<string, string> = token ? { Authorization: `Bearer ${token}` } : {};
        let body = init.body;
        if (init.json !== undefined) {
          headers['Content-Type'] = 'application/json';
          body = JSON.stringify(init.json);
        }
        return { method: init.method ?? 'GET', headers, body, signal: init.signal };
      };

      let res = await deps.fetch(path, build());
      if (res.status === 401 && (await deps.refresh())) {
        res = await deps.fetch(path, build());
      }
      if (!res.ok) return throwForResponse(res, path, init.method ?? 'GET');
      if (res.status === 204) return undefined as T;
      return res.json() as Promise<T>;
    },
  };
}
