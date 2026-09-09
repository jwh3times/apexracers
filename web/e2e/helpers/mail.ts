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

function readAll(): DroppedEmail[] {
  if (!existsSync(MAIL_DIR)) return [];
  return readdirSync(MAIL_DIR)
    .filter(name => name.endsWith('.json'))
    .map(name => JSON.parse(readFileSync(join(MAIL_DIR, name), 'utf8')) as DroppedEmail)
    .sort((a, b) => a.droppedAt.localeCompare(b.droppedAt));
}

/**
 * Polls the drop directory for the newest email to `recipient` whose subject matches, then returns
 * the `token` query value from the link in its text body. Polls because delivery is a file write
 * that races the HTTP response the test just observed.
 */
export async function waitForEmailedToken(
  recipient: string,
  subjectPattern: RegExp,
  timeoutMs = 15_000
): Promise<string> {
  const deadline = Date.now() + timeoutMs;

  while (Date.now() < deadline) {
    const match = readAll()
      .filter(e => e.to.toLowerCase() === recipient.toLowerCase() && subjectPattern.test(e.subject))
      .at(-1);

    if (match) {
      const link = /https?:\/\/\S+?\?[^\s"'<>]+/.exec(match.textBody);
      if (!link) throw new Error(`Email "${match.subject}" carried no link with a query string.`);
      const token = new URL(link[0]).searchParams.get('token');
      if (!token) throw new Error(`Email link carried no token: ${link[0]}`);
      return token;
    }
    await new Promise(resolve => setTimeout(resolve, 250));
  }

  throw new Error(
    `Timed out after ${timeoutMs}ms waiting for an email to ${recipient} matching ` +
      `${subjectPattern} in ${MAIL_DIR}. Is DEV_MAIL_DROP_PATH set on the API, and does ` +
      `E2E_MAIL_DIR point at the same directory from the host?`
  );
}
