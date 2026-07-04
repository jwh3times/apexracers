import { expect } from '@playwright/test';
import { runSql } from './db';

/**
 * Promotes a registered user to Admin by swapping their single role row
 * (single-role model: exactly one identity."UserRoles" row per user, enforced
 * by a unique index). The caller must re-login afterwards — the JWT still
 * carries the old role claim until a fresh token is minted.
 *
 * ADMIN_SEED_EMAILS can't do this in E2E: it only promotes accounts that
 * already exist when the API boots, and E2E users register after boot.
 */
export function promoteToAdmin(email: string): void {
  // uniqueEmail() emits only [a-z0-9.@-]; assert so the inlined SQL stays injection-safe.
  expect(email).toMatch(/^[a-z0-9.@-]+$/i);

  const out = runSql(
    `UPDATE identity."UserRoles" ` +
      `SET "RoleId" = (SELECT "Id" FROM identity."Roles" WHERE "Name" = 'Admin') ` +
      `WHERE "UserId" = (SELECT "Id" FROM identity."Users" WHERE "NormalizedEmail" = '${email.toUpperCase()}');`
  );

  expect(out).toContain('UPDATE 1');
}
