import { execFileSync } from 'node:child_process';

/**
 * Runs one SQL statement against the E2E database and returns psql's stdout.
 * - CI: set E2E_DATABASE_URL (postgresql://user:pass@host:port/db) — uses the
 *   runner's psql against the postgres service container.
 * - Local: falls back to `docker compose exec -T postgres psql`, i.e. the same
 *   DB the reused :8080 compose server is attached to.
 * Tests execute with cwd = web/, so the compose fallback runs from the repo root.
 */
export function runSql(sql: string): string {
  const url = process.env.E2E_DATABASE_URL;
  if (url) {
    return execFileSync('psql', [url, '-v', 'ON_ERROR_STOP=1', '-c', sql], {
      encoding: 'utf8',
    });
  }
  return execFileSync(
    'docker',
    [
      'compose',
      'exec',
      '-T',
      'postgres',
      'psql',
      '-U',
      'apexracers',
      '-d',
      'apexracers',
      '-v',
      'ON_ERROR_STOP=1',
      '-c',
      sql,
    ],
    { encoding: 'utf8', cwd: '..' }
  );
}
