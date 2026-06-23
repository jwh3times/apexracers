# Account Emails — Password Reset Delivery & Email-Change Verification

**Date:** 2026-06-22
**Status:** Approved (pending written-spec review)
**Author:** brainstorming session (Claude + Jerry)

---

## 1. Problem & context

Two account-security email flows are currently incomplete in the live product:

1. **Password reset.** The flow exists end-to-end (`AuthService.GeneratePasswordResetTokenAsync`,
   `AuthController.ForgotPasswordAsync`/`ResetPasswordAsync`, `ForgotPasswordPage`/`ResetPasswordPage`,
   the `/reset-password?email=&token=` link), but **no email is ever sent** — the token is returned in
   the response body only in the Development environment and never logged. In production a user
   **cannot complete a reset.** This is the highest-value unblocked work (it fixes the auth surface
   that is live today, independent of the iRacing-creds blocker).
2. **Email-change verification.** Today `PUT /api/auth/profile` mutates the account email immediately,
   with no verification. A typo or a hijacked session changes the login email instantly.

This spec adds real email delivery via **Azure Communication Services (ACS)** and builds both flows on a
shared email-sender foundation.

## 2. Decisions (locked during brainstorming)

| Decision                                 | Choice                                                                                                                               |
| ---------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------ |
| Email provider                           | **Azure Communication Services** (consistent with the Azure stack; connection string in Key Vault like `DATABASE-CONNECTION-STRING`) |
| Scope                                    | Password-reset email **and** email-change verification, on one shared sender foundation                                              |
| Sender domain                            | **Custom domain `apexracers.gg`**, sender `noreply@apexracers.gg` (requires registrar DNS records)                                   |
| Email-change model                       | **Verify-then-apply** — new address held pending until verified via a link sent to the new address                                   |
| Provisioning                             | **Provision ACS as part of this work** (via the `azure-infrastructure` agent, during implementation)                                 |
| Email already in use (on change request) | **Enumeration-safe** — generic 200, silently skip the send                                                                           |
| On successful email change               | **Revoke all active refresh tokens** (treat like a password reset)                                                                   |
| Old-address notification                 | **Deferred** — one verification email (to the new address) this build                                                                |
| Email template fidelity                  | Branded HTML (dark/cyan, CTA button + raw-link fallback) **plus** a plain-text alternate                                             |

## 3. Verified external contract (ACS Email .NET SDK)

Confirmed against current Microsoft Learn docs (NuGet `Azure.Communication.Email`):

```csharp
var emailClient = new EmailClient(connectionString);
var content = new EmailContent(subject) { PlainText = text, Html = html };
var recipients = new EmailRecipients(new[] { new EmailAddress(toAddress, toDisplayName) });
var message = new EmailMessage(senderAddress, recipients, content); // sender is a string address
EmailSendOperation op = await emailClient.SendAsync(WaitUntil.Completed, message);
// op.Value.Status on success; throws RequestFailedException on failure (ErrorCode + message).
```

Auth is via connection string (matches the repo's existing Key-Vault-secret pattern). Sender address must
be a verified MailFrom on the linked domain.

## 4. Architecture

### 4.1 Email foundation (shared) — `src/ApexRacers.Api/Services/Email/`

- **`OutboundEmail`** record: `(string To, string? ToName, string Subject, string HtmlBody, string TextBody)`.
  Our own DTO — ACS SDK types never leak past `AcsEmailSender` (same principle as not caching raw SDK types).
- **`IEmailSender`**: `Task SendAsync(OutboundEmail email, CancellationToken ct = default)`.
- **`AcsEmailSender : IEmailSender`** — wraps `EmailClient`; reads sender from `ACS_SENDER_ADDRESS`
  (default `noreply@apexracers.gg`, display name "ApexRacers"); maps `OutboundEmail` → ACS `EmailMessage`;
  sends `WaitUntil.Completed`; logs and rethrows `RequestFailedException`. The thin SDK-call body is
  `[ExcludeFromCodeCoverage]` (mirrors `ExternalDataCacheCleanupService`'s loop); any pure mapping is testable.
- **`LoggingEmailSender : IEmailSender`** — fallback used when `ACS_CONNECTION_STRING` is absent. Logs
  recipient + subject only (**never** the link/token). Lets the app run locally and pre-provisioning, the
  same way the iRacing features degrade when creds are missing.
- **DI (`Program.cs`)**: register `AcsEmailSender` when `ACS_CONNECTION_STRING` is present, else
  `LoggingEmailSender`.
- **`AccountEmailTemplates`** (pure static): `PasswordReset(string resetUrl) → OutboundEmail`-parts and
  `EmailChangeVerification(string verifyUrl) → OutboundEmail`-parts. Branded dark/cyan HTML with a CTA
  button, a raw-link fallback line, an "ignore this if you didn't request it" note, and a plain-text
  alternate. Unit-tested directly (assert the URL is embedded and the subject is correct) — mirrors
  `AchievementsMapper`/`CarCatalogMapper`.
- **Config**: `APP_BASE_URL` for absolute links (prod `https://apexracers.gg`, dev `http://localhost:5173`).
  All link tokens/emails are `Uri.EscapeDataString`-encoded.

### 4.2 Password-reset email (flow B)

- `AuthService.RequestPasswordResetAsync(string email, CancellationToken)` (renamed from
  `GeneratePasswordResetTokenAsync`): generates the Identity token (null when the user doesn't exist),
  and when non-null builds `${APP_BASE_URL}/reset-password?email=<enc>&token=<enc>` and sends it via
  `IEmailSender`. Returns the token so the controller can still surface it **in Development only**.
- `AuthController.ForgotPasswordAsync` is otherwise unchanged: always a generic 200, dev-only token in
  body, token never logged.
- **No frontend change** — the reset pages and link already exist.

### 4.3 Email-change verification (flow C, verify-then-apply)

- `PUT /api/auth/profile` (`UpdateProfileAsync`) **stops mutating email**. `UpdateProfileRequest.Email`
  is ignored/removed; display name, iRacing ID, and theme still apply immediately.
- **`POST /api/auth/request-email-change`** (`[Authorize]`) `{ newEmail }`:
  - If `newEmail` already belongs to another account → return generic 200, **skip send** (enumeration-safe).
  - Else `GenerateChangeEmailTokenAsync(user, newEmail)`, build
    `${APP_BASE_URL}/verify-email?userId=<id>&email=<newEnc>&token=<enc>`, send to **the new address**,
    return generic 200. The account email is **not** changed yet.
- **`POST /api/auth/confirm-email-change`** (public — the user may be logged out when clicking the link)
  `{ userId, newEmail, token }`:
  - Find user by id; `ChangeEmailAsync(user, newEmail, token)`; on success also `SetUserNameAsync` to the
    new email (login is by email); then `RevokeAllActiveTokensAsync(user.Id)`. Returns 200.
  - Invalid/expired token → `InvalidOperationException` → 400 via the existing exception middleware.
- New request DTOs: `RequestEmailChangeRequest(string NewEmail)`,
  `ConfirmEmailChangeRequest(Guid UserId, string NewEmail, string Token)`.
- Identity change-email tokens use the default token provider already registered via
  `AddDefaultTokenProviders()` (the same provider reset tokens use).

### 4.4 Frontend

- `api.ts`: `requestEmailChange(newEmail)`, `confirmEmailChange({ userId, email, token })`. (Profile
  update no longer sends `email`.)
- `SettingsPage`: email field change calls `requestEmailChange` (not profile update); show a
  "Pending verification: new@email" state and a hint that the change applies after the new address is
  confirmed. Other fields keep saving via profile update.
- New public route `/verify-email` → `VerifyEmailPage` (mirrors `ResetPasswordPage`): reads
  `userId/email/token` from the query, calls `confirmEmailChange`, shows success / expired-or-invalid /
  loading. On success, prompt re-login (sessions were revoked).
- Routing: `/verify-email` is public (no AppShell), alongside `/reset-password`.

## 5. Config, secrets & infrastructure

| Item             | Value / location                                                                          |
| ---------------- | ----------------------------------------------------------------------------------------- |
| Key Vault secret | `ACS-CONNECTION-STRING` → `ACS_CONNECTION_STRING` (via `HyphenToUnderscoreSecretManager`) |
| App setting      | `ACS_SENDER_ADDRESS` = `noreply@apexracers.gg`                                            |
| App setting      | `APP_BASE_URL` = `https://apexracers.gg` (dev: `http://localhost:5173`)                   |
| NuGet            | `Azure.Communication.Email` (added via `dotnet add package` → `Directory.Packages.props`) |

**Provisioning (azure-infrastructure agent, during implementation):**

1. Create an **Email Communication Service** resource.
2. Add custom domain **`apexracers.gg`**; capture the required DNS records and hand them to Jerry to add
   at the registrar:
   - TXT — domain ownership verification
   - TXT — SPF
   - CNAME ×2 — DKIM (`selector1`/`selector2`-style)
   - _(plus the MailFrom/`bounce` records ACS specifies)_
     Wait for domain + sender verification to go green. **This step pauses on Jerry's DNS work.**
3. Create (or reuse) a **Communication Services** resource and **link** the verified email domain.
4. Configure MailFrom sender `noreply@apexracers.gg` with display name "ApexRacers".
5. Put the connection string in Key Vault as `ACS-CONNECTION-STRING`; set the two app settings on the
   `apexracers-api` App Service.
6. Document all of the above as a new section in `deployTODO.md` and update the infra tables in
   `CLAUDE.md`/`PRD.md` if a new resource row is warranted.

Until provisioning is complete, the API runs with `LoggingEmailSender` (no secret present) and behaves
exactly as today (dev-only token in the reset response). Email-change request/confirm endpoints still
function; the verification email is logged-only until ACS is live.

## 6. Testing

**Backend (≥85% line & branch):**

- `AccountEmailTemplatesTests` (pure) — URL embedded, subject/text/html present for both templates.
- `AuthServiceTests` additions — `RequestPasswordResetAsync` sends via a mock `IEmailSender` with a link
  containing the token; unknown email → no send, null token. `RequestEmailChangeAsync` — sends to new
  address; in-use address → generic success, no send. `ConfirmEmailChangeAsync` — success changes email
  - username + revokes tokens; bad token → throws.
- `AcsEmailSender` SDK glue excluded from coverage; any pure `OutboundEmail`→ACS mapping is tested.

**Frontend (≥85% stmts/branches/fns/lines):**

- `SettingsPage` — email change triggers `requestEmailChange` and renders the pending state; other fields
  still save.
- `VerifyEmailPage` — success, expired/invalid, loading.
- `api.ts` — `requestEmailChange`/`confirmEmailChange` request shapes (extend `api.test.ts`).

## 7. Docs to update (on completion)

- `CLAUDE.md` — AuthController endpoints (`request-email-change`, `confirm-email-change`), the new
  `Email/` services + `AccountEmailTemplates`, config keys, frontend route/page.
- `private/PRD.md` — auth/onboarding section (reset email now real; verify-then-apply email change),
  Settings + screen inventory (`/verify-email`), version bump.
- `private/ROADMAP.md` — move "real email delivery for password reset" and the email-change
  verification follow-up out of Backlog into Completed.
- `private/deployTODO.md` — the ACS provisioning section above.
- `README.md` — env vars / local-dev note if relevant.

## 8. Out of scope / deferred

- Old-address "your email was changed" notification (fast follow-up).
- Any other transactional emails (welcome, race alerts) — the foundation supports them later.
- Email open/click tracking, retries/queueing, a templating engine.
- Managed-identity auth to ACS (connection string chosen for parity with existing secrets).

## 9. Risks / notes

- **DNS verification latency** blocks real sending; the logging fallback keeps everything else shippable.
- **Public `confirm-email-change`** must not leak: it only acts on a valid `(userId, newEmail, token)`
  triple; an invalid token is an opaque 400.
- **Username/email coupling**: login is by email, so a confirmed change must update both `Email` and
  `UserName` atomically, then revoke sessions.
- **Link encoding**: Identity tokens contain URL-unsafe characters — always `Uri.EscapeDataString`.
