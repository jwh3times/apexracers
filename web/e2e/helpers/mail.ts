import { readdirSync, readFileSync, existsSync } from 'node:fs';
import { join } from 'node:path';

/**
 * Reads emails back out of the API's Development mail drop.
 *
 * The API no longer echoes password-reset tokens in the forgot-password response — doing so handed
 * a live credential to any unauthenticated caller who knew an email address (GHSA-qmqp-gxpr-867g).
 * A Development stack instead writes each outbound email as JSON to DEV_MAIL_DROP_PATH, and this
 * helper reads the delivered message the way a real recipient would.
 *
 * - CI: the API runs on the runner, so the drop directory is a plain local path.
 * - Local: the API runs in compose, which bind-mounts the container's drop directory onto
 *   TestResults/mail in the repo root. Tests execute with cwd = web/, hence the `../` default.
 */
const MAIL_DIR = process.env.E2E_MAIL_DIR ?? '../TestResults/mail';

interface DroppedEmail {
  to: string;
  toName: string | null;
  subject: string;
  htmlBody: string;
  textBody: string;
  droppedAt: string;
}

/**
 * Every email the drop currently holds, oldest first.
 *
 * Unreadable entries are skipped rather than thrown on. The directory is shared by all Playwright
 * workers and the API writes into it while they read: a file listed by `readdirSync` can still be
 * mid-write (truncated JSON) or already gone. Since registration emails a confirmation link, most
 * tests drop mail now, so that window is hit regularly — and every caller is inside a polling loop
 * that will see the finished file on its next pass anyway.
 */
function readAll(): DroppedEmail[] {
  if (!existsSync(MAIL_DIR)) return [];
  const emails: DroppedEmail[] = [];
  for (const name of readdirSync(MAIL_DIR)) {
    if (!name.endsWith('.json')) continue;
    try {
      emails.push(JSON.parse(readFileSync(join(MAIL_DIR, name), 'utf8')) as DroppedEmail);
    } catch {
      continue;
    }
  }
  return emails.sort((a, b) => a.droppedAt.localeCompare(b.droppedAt));
}

/**
 * Polls the drop directory for the newest email to `recipient` whose subject matches, then returns
 * the link from its text body as a parsed URL. Polls because delivery is a file write that races
 * the HTTP response the test just observed.
 *
 * The returned URL is built from APP_BASE_URL, which is the production host rather than the stack
 * under test — so callers navigate with its `searchParams` against their own origin rather than
 * following the href.
 */
export async function waitForEmailedLink(
  recipient: string,
  subjectPattern: RegExp,
  timeoutMs = 15_000
): Promise<URL> {
  const deadline = Date.now() + timeoutMs;

  while (Date.now() < deadline) {
    const match = readAll()
      .filter(e => e.to.toLowerCase() === recipient.toLowerCase() && subjectPattern.test(e.subject))
      .at(-1);

    if (match) {
      const link = /https?:\/\/\S+?\?[^\s"'<>]+/.exec(match.textBody);
      if (!link) throw new Error(`Email "${match.subject}" carried no link with a query string.`);
      return new URL(link[0]);
    }
    await new Promise(resolve => setTimeout(resolve, 250));
  }

  throw new Error(
    `Timed out after ${timeoutMs}ms waiting for an email to ${recipient} matching ` +
      `${subjectPattern} in ${MAIL_DIR}. Is DEV_MAIL_DROP_PATH set on the API, and does ` +
      `E2E_MAIL_DIR point at the same directory from the host?`
  );
}

/** The `token` query value from the emailed link — see {@link waitForEmailedLink}. */
export async function waitForEmailedToken(
  recipient: string,
  subjectPattern: RegExp,
  timeoutMs = 15_000
): Promise<string> {
  const link = await waitForEmailedLink(recipient, subjectPattern, timeoutMs);
  const token = link.searchParams.get('token');
  if (!token) throw new Error(`Email link carried no token: ${link.href}`);
  return token;
}
