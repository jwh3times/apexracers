# Account Emails Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver real transactional email via Azure Communication Services for password reset, and add verify-then-apply email-change verification.

**Architecture:** A small `IEmailSender` abstraction (ACS implementation + logging fallback when unconfigured) plus a pure `AccountEmailTemplates` builder. `AuthService` composes absolute links and sends through `IEmailSender`. Two new auth endpoints add email-change verification; the profile-update endpoint stops mutating email. Frontend gains a public `/verify-email` page and a Settings email-change flow.

**Tech Stack:** .NET 10 / ASP.NET Core, EF Core 10, ASP.NET Identity, `Azure.Communication.Email`; React + TypeScript + Vite + Vitest; xUnit.

**Spec:** `docs/superpowers/specs/2026-06-22-account-emails-design.md`

## Global Constraints

- All work on branch `feat/account-emails` (repo default branch is `main`; never commit there directly).
- Commit messages use Conventional Commits and end with the trailer: `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.
- Backend coverage gate: **≥85% line AND branch** (controllers excluded). Frontend gate: **≥85%** statements/branches/functions/lines.
- NuGet versions are centrally managed — add packages via `dotnet add package` (writes to `Directory.Packages.props`); never put `Version=` in a `.csproj`.
- Never serialize raw Azure SDK types past `AcsEmailSender`; use the `OutboundEmail` DTO.
- Do not log reset/verification tokens or links.
- All tokens and emails embedded in URLs use `Uri.EscapeDataString`.
- Sender address default `noreply@apexracers.gg`; base-URL default `https://apexracers.gg`.
- Frontend: route all HTTP through the `api.ts` `request<T>` helper; format via existing utilities; use fluid/design-token classes (the public auth pages mirror `ResetPasswordPage`'s existing class set).
- Run `cd src/web && npx prettier --write .` before any frontend commit (CI checks `--check`).

---

## Phase A — Email foundation

### Task A1: `OutboundEmail` DTO, `IEmailSender`, and the ACS package

**Files:**

- Modify: `Directory.Packages.props` (via `dotnet add package`)
- Modify: `src/ApexRacers.Api/ApexRacers.Api.csproj` (via `dotnet add package`)
- Create: `src/ApexRacers.Api/Services/Email/OutboundEmail.cs`
- Create: `src/ApexRacers.Api/Services/Email/IEmailSender.cs`

**Interfaces:**

- Produces: `record OutboundEmail(string To, string? ToName, string Subject, string HtmlBody, string TextBody)` in `ApexRacers.Api.Services.Email`; `interface IEmailSender { Task SendAsync(OutboundEmail email, CancellationToken ct = default); }`.

- [ ] **Step 1: Add the ACS package**

Run:

```bash
dotnet add src/ApexRacers.Api package Azure.Communication.Email
```

Expected: `Directory.Packages.props` gains a `<PackageVersion Include="Azure.Communication.Email" Version="..." />` and the csproj a version-less `<PackageReference>`.

- [ ] **Step 2: Create `OutboundEmail.cs`**

```csharp
namespace ApexRacers.Api.Services.Email;

/// <summary>
/// Provider-agnostic outbound email. ACS SDK types never leak past <see cref="IEmailSender"/>.
/// </summary>
public record OutboundEmail(string To, string? ToName, string Subject, string HtmlBody, string TextBody);
```

- [ ] **Step 3: Create `IEmailSender.cs`**

```csharp
namespace ApexRacers.Api.Services.Email;

/// <summary>Sends transactional account emails. Backed by ACS in production, a no-op logger otherwise.</summary>
public interface IEmailSender
{
    Task SendAsync(OutboundEmail email, CancellationToken ct = default);
}
```

- [ ] **Step 4: Build**

Run: `dotnet build src/ApexRacers.Api`
Expected: build succeeds.

- [ ] **Step 5: Commit**

```bash
git add Directory.Packages.props src/ApexRacers.Api
git commit -m "feat(email): add OutboundEmail DTO and IEmailSender abstraction

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task A2: `AccountEmailTemplates` (pure) + tests

**Files:**

- Create: `src/ApexRacers.Api/Services/Email/AccountEmailTemplates.cs`
- Test: `src/ApexRacers.Tests/Services/AccountEmailTemplatesTests.cs`

**Interfaces:**

- Consumes: `OutboundEmail` (Task A1).
- Produces: `static class AccountEmailTemplates` with `OutboundEmail PasswordReset(string toEmail, string resetUrl)` and `OutboundEmail EmailChangeVerification(string toEmail, string verifyUrl)`.

- [ ] **Step 1: Write the failing tests**

```csharp
using ApexRacers.Api.Services.Email;
using Xunit;

namespace ApexRacers.Tests.Services;

public class AccountEmailTemplatesTests
{
    [Fact]
    public void PasswordReset_SetsRecipientSubjectAndEmbedsUrl()
    {
        var email = AccountEmailTemplates.PasswordReset("driver@example.com", "https://apexracers.gg/reset-password?token=abc%20123");

        Assert.Equal("driver@example.com", email.To);
        Assert.Equal("Reset your ApexRacers password", email.Subject);
        Assert.Contains("https://apexracers.gg/reset-password?token=abc%20123", email.HtmlBody);
        Assert.Contains("https://apexracers.gg/reset-password?token=abc%20123", email.TextBody);
        Assert.Contains("ignore", email.TextBody, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EmailChangeVerification_SetsRecipientSubjectAndEmbedsUrl()
    {
        var email = AccountEmailTemplates.EmailChangeVerification("new@example.com", "https://apexracers.gg/verify-email?token=xyz");

        Assert.Equal("new@example.com", email.To);
        Assert.Equal("Confirm your new ApexRacers email", email.Subject);
        Assert.Contains("https://apexracers.gg/verify-email?token=xyz", email.HtmlBody);
        Assert.Contains("https://apexracers.gg/verify-email?token=xyz", email.TextBody);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~AccountEmailTemplatesTests`
Expected: FAIL — `AccountEmailTemplates` does not exist.

- [ ] **Step 3: Implement `AccountEmailTemplates.cs`**

```csharp
namespace ApexRacers.Api.Services.Email;

/// <summary>Pure builders for account-security emails (branded HTML + plain-text). Unit-tested directly.</summary>
public static class AccountEmailTemplates
{
    private const string BrandName = "ApexRacers";

    public static OutboundEmail PasswordReset(string toEmail, string resetUrl)
    {
        const string subject = "Reset your ApexRacers password";
        var html = Layout(
            "Reset your password",
            "We received a request to reset your ApexRacers password. Use the button below to choose a new one. " +
            "The link expires shortly and can be used once.",
            "Reset password", resetUrl,
            "If you didn't request this, you can safely ignore this email — your password won't change.");
        var text =
            $"Reset your {BrandName} password\n\n" +
            $"We received a request to reset your password. Open this link to choose a new one:\n{resetUrl}\n\n" +
            "If you didn't request this, ignore this email — your password won't change.";
        return new OutboundEmail(toEmail, null, subject, html, text);
    }

    public static OutboundEmail EmailChangeVerification(string toEmail, string verifyUrl)
    {
        const string subject = "Confirm your new ApexRacers email";
        var html = Layout(
            "Confirm your email change",
            "You asked to change the email on your ApexRacers account to this address. Use the button below to confirm. " +
            "Your email won't change until you do.",
            "Confirm email", verifyUrl,
            "If you didn't request this, you can safely ignore this email.");
        var text =
            $"Confirm your new {BrandName} email\n\n" +
            $"You asked to change your account email to this address. Open this link to confirm:\n{verifyUrl}\n\n" +
            "If you didn't request this, ignore this email.";
        return new OutboundEmail(toEmail, null, subject, html, text);
    }

    private static string Layout(string heading, string body, string cta, string url, string footnote) =>
        $$"""
        <!DOCTYPE html>
        <html>
          <body style="margin:0;background:#0b0f14;font-family:Segoe UI,Roboto,Helvetica,Arial,sans-serif;color:#e6edf3;">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#0b0f14;padding:32px 0;">
              <tr><td align="center">
                <table role="presentation" width="480" cellpadding="0" cellspacing="0" style="background:#11161d;border:1px solid #1f2730;border-radius:14px;overflow:hidden;">
                  <tr><td style="padding:24px 32px;border-bottom:1px solid #1f2730;font-size:18px;font-weight:700;color:#00e0ff;letter-spacing:-0.3px;">{{BrandName}}</td></tr>
                  <tr><td style="padding:32px;">
                    <h1 style="margin:0 0 16px;font-size:20px;color:#e6edf3;">{{heading}}</h1>
                    <p style="margin:0 0 24px;font-size:14px;line-height:1.6;color:#aab4c0;">{{body}}</p>
                    <a href="{{url}}" style="display:inline-block;background:#00e0ff;color:#04222a;font-weight:700;font-size:14px;text-decoration:none;padding:12px 24px;border-radius:10px;">{{cta}}</a>
                    <p style="margin:24px 0 0;font-size:12px;line-height:1.6;color:#6b7785;">Or paste this link into your browser:<br><span style="color:#9fb0c0;word-break:break-all;">{{url}}</span></p>
                    <p style="margin:24px 0 0;font-size:12px;line-height:1.6;color:#6b7785;">{{footnote}}</p>
                  </td></tr>
                </table>
              </td></tr>
            </table>
          </body>
        </html>
        """;
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test --filter FullyQualifiedName~AccountEmailTemplatesTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add src/ApexRacers.Api/Services/Email/AccountEmailTemplates.cs src/ApexRacers.Tests/Services/AccountEmailTemplatesTests.cs
git commit -m "feat(email): add pure AccountEmailTemplates with tests

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task A3: ACS + logging senders and DI registration

**Files:**

- Create: `src/ApexRacers.Api/Services/Email/AcsEmailSender.cs`
- Create: `src/ApexRacers.Api/Services/Email/LoggingEmailSender.cs`
- Modify: `src/ApexRacers.Api/Program.cs` (add `using`s + conditional DI near `AddScoped<AuthService>()`, currently line 152)

**Interfaces:**

- Consumes: `IEmailSender`, `OutboundEmail` (A1).
- Produces: `AcsEmailSender : IEmailSender`, `LoggingEmailSender : IEmailSender`; DI binds `IEmailSender` to ACS when `ACS_CONNECTION_STRING` is set, else logging.

Both concrete senders are I/O glue and carry `[ExcludeFromCodeCoverage]` (mirrors `ExternalDataCacheCleanupService`'s loop); behavior is covered through `IEmailSender` fakes in later tasks.

- [ ] **Step 1: Create `AcsEmailSender.cs`**

```csharp
using System.Diagnostics.CodeAnalysis;
using Azure;
using Azure.Communication.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ApexRacers.Api.Services.Email;

/// <summary>Sends email via Azure Communication Services. Thin SDK glue — excluded from coverage.</summary>
[ExcludeFromCodeCoverage]
public sealed class AcsEmailSender(EmailClient client, IConfiguration config, ILogger<AcsEmailSender> logger)
    : IEmailSender
{
    private string SenderAddress => config["ACS_SENDER_ADDRESS"] ?? "noreply@apexracers.gg";

    public async Task SendAsync(OutboundEmail email, CancellationToken ct = default)
    {
        var content = new EmailContent(email.Subject) { PlainText = email.TextBody, Html = email.HtmlBody };
        var recipient = email.ToName is null
            ? new EmailAddress(email.To)
            : new EmailAddress(email.To, email.ToName);
        var message = new EmailMessage(SenderAddress, new EmailRecipients([recipient]), content);

        try
        {
            var op = await client.SendAsync(WaitUntil.Completed, message, ct);
            logger.LogInformation("Email sent (subject {Subject}); status {Status}", email.Subject, op.Value.Status);
        }
        catch (RequestFailedException ex)
        {
            logger.LogError(ex, "ACS email send failed (subject {Subject}); error {Code}", email.Subject, ex.ErrorCode);
            throw;
        }
    }
}
```

- [ ] **Step 2: Create `LoggingEmailSender.cs`**

```csharp
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace ApexRacers.Api.Services.Email;

/// <summary>Fallback used when ACS is unconfigured. Logs metadata only — never the link/token.</summary>
[ExcludeFromCodeCoverage]
public sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(OutboundEmail email, CancellationToken ct = default)
    {
        logger.LogWarning(
            "Email delivery not configured (ACS_CONNECTION_STRING missing). Would have sent '{Subject}'.",
            email.Subject);
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 3: Register in `Program.cs`**

Add to the `using` block at the top:

```csharp
using ApexRacers.Api.Services.Email;
using Azure.Communication.Email;
```

Immediately after `builder.Services.AddScoped<AuthService>();` (line 152), add:

```csharp
var acsConnectionString = builder.Configuration["ACS_CONNECTION_STRING"];
if (!string.IsNullOrWhiteSpace(acsConnectionString))
{
    builder.Services.AddSingleton(new EmailClient(acsConnectionString));
    builder.Services.AddScoped<IEmailSender, AcsEmailSender>();
}
else
{
    builder.Services.AddScoped<IEmailSender, LoggingEmailSender>();
}
```

- [ ] **Step 4: Build**

Run: `dotnet build src/ApexRacers.Api`
Expected: build succeeds.

- [ ] **Step 5: Commit**

```bash
git add src/ApexRacers.Api/Services/Email/AcsEmailSender.cs src/ApexRacers.Api/Services/Email/LoggingEmailSender.cs src/ApexRacers.Api/Program.cs
git commit -m "feat(email): add ACS + logging senders with conditional DI

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Phase B — Password-reset email

### Task B1: Send the reset email; wire `AuthService` + controller

**Files:**

- Create: `src/ApexRacers.Tests/Helpers/FakeEmailSender.cs`
- Modify: `src/ApexRacers.Api/Services/AuthService.cs` (ctor line 14; method `GeneratePasswordResetTokenAsync` lines 235-239; doc comment line 242)
- Modify: `src/ApexRacers.Api/Controllers/AuthController.cs:98`
- Modify: `src/ApexRacers.Tests/Services/AuthServiceTests.cs` (`BuildService` helper line 44-55; existing reset tests)

**Interfaces:**

- Consumes: `IEmailSender`, `AccountEmailTemplates` (A1/A2).
- Produces: `AuthService.RequestPasswordResetAsync(string email, CancellationToken ct = default) : Task<string?>` (replaces `GeneratePasswordResetTokenAsync`); `AuthService` ctor now `(UserManager<ApplicationUser>, IConfiguration, AppDbContext, IEmailSender)`; `FakeEmailSender` test double with `IReadOnlyList<OutboundEmail> Sent` and `OutboundEmail? Last`.

- [ ] **Step 1: Create the `FakeEmailSender` test double**

```csharp
using ApexRacers.Api.Services.Email;

namespace ApexRacers.Tests.Helpers;

/// <summary>Captures sent emails so service tests can assert delivery without ACS.</summary>
public sealed class FakeEmailSender : IEmailSender
{
    private readonly List<OutboundEmail> _sent = [];
    public IReadOnlyList<OutboundEmail> Sent => _sent;
    public OutboundEmail? Last => _sent.Count > 0 ? _sent[^1] : null;

    public Task SendAsync(OutboundEmail email, CancellationToken ct = default)
    {
        _sent.Add(email);
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 2: Update `AuthServiceTests.BuildService` to inject a sender, then write the failing test**

Replace the `BuildService` helper (lines 44-55) with:

```csharp
    private static AuthService BuildService(ServiceProvider provider, IEmailSender? emailSender = null)
    {
        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        var db          = provider.GetRequiredService<AppDbContext>();
        var config      = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JWT_SIGNING_KEY"] = "unit-test-signing-key-minimum-32-bytes-long!",
                ["APP_BASE_URL"]    = "https://test.apexracers.gg"
            })
            .Build();
        return new AuthService(userManager, config, db, emailSender ?? new FakeEmailSender());
    }
```

Add `using ApexRacers.Api.Services.Email;` and `using ApexRacers.Tests.Helpers;` to the test file's usings.

Add this test:

```csharp
    [Fact]
    public async Task RequestPasswordResetAsync_KnownUser_SendsEmailWithTokenLink()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var emails = new FakeEmailSender();
        var svc = BuildService(provider, emails);
        await svc.RegisterAsync(new RegisterRequest("reset@example.com", "Pass1234"), TestContext.Current.CancellationToken);

        var token = await svc.RequestPasswordResetAsync("reset@example.com", TestContext.Current.CancellationToken);

        Assert.NotNull(token);
        Assert.NotNull(emails.Last);
        Assert.Equal("reset@example.com", emails.Last!.To);
        Assert.Contains("https://test.apexracers.gg/reset-password", emails.Last.HtmlBody);
        Assert.Contains(Uri.EscapeDataString(token!), emails.Last.HtmlBody);
    }

    [Fact]
    public async Task RequestPasswordResetAsync_UnknownUser_ReturnsNullAndSendsNothing()
    {
        await using var provider = BuildProvider();
        var emails = new FakeEmailSender();
        var svc = BuildService(provider, emails);

        var token = await svc.RequestPasswordResetAsync("nobody@example.com", TestContext.Current.CancellationToken);

        Assert.Null(token);
        Assert.Empty(emails.Sent);
    }
```

In existing reset tests, replace any call to `svc.GeneratePasswordResetTokenAsync(` with `svc.RequestPasswordResetAsync(` (same signature/return).

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test --filter FullyQualifiedName~AuthServiceTests`
Expected: FAIL — `RequestPasswordResetAsync` not defined / ctor arity mismatch.

- [ ] **Step 4: Update `AuthService`**

Change the ctor (line 14) to:

```csharp
public class AuthService(UserManager<ApplicationUser> userManager, IConfiguration config, AppDbContext db, IEmailSender emailSender)
```

Add `using ApexRacers.Api.Services.Email;` to the file's usings.

Add this property in the constants region (after line 24):

```csharp
    private string BaseUrl => config["APP_BASE_URL"]?.TrimEnd('/') ?? "https://apexracers.gg";
```

Replace `GeneratePasswordResetTokenAsync` (lines 235-239) with:

```csharp
    /// <summary>
    /// Generates a single-use reset token for the account and emails the reset link. Returns the token
    /// (for Development-only echoing) or null when no account exists for the email.
    /// </summary>
    public async Task<string?> RequestPasswordResetAsync(string email, CancellationToken ct = default)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
            return null;

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var url = $"{BaseUrl}/reset-password?email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}";
        await emailSender.SendAsync(AccountEmailTemplates.PasswordReset(email, url), ct);
        return token;
    }
```

Update the doc comment on `ResetPasswordAsync` (line 242) that references `GeneratePasswordResetTokenAsync` to read `RequestPasswordResetAsync`.

- [ ] **Step 5: Update the controller**

In `AuthController.ForgotPasswordAsync` change line 98 from
`var token = await auth.GeneratePasswordResetTokenAsync(request.Email, ct);` to
`var token = await auth.RequestPasswordResetAsync(request.Email, ct);`.

- [ ] **Step 6: Run tests to verify pass**

Run: `dotnet test --filter FullyQualifiedName~AuthServiceTests`
Expected: PASS (existing + 2 new).

- [ ] **Step 7: Commit**

```bash
git add src/ApexRacers.Api/Services/AuthService.cs src/ApexRacers.Api/Controllers/AuthController.cs src/ApexRacers.Tests
git commit -m "feat(auth): email the password-reset link via IEmailSender

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Phase C — Email-change verification (backend)

### Task C1: DTOs + `RequestEmailChangeAsync`; drop email from profile update

**Files:**

- Modify: `src/ApexRacers.Api/Dtos/RequestDtos.cs:5` and add two records
- Modify: `src/ApexRacers.Api/Dtos/ResponseDtos.cs` (add `MessageResponse`)
- Modify: `src/ApexRacers.Api/Services/AuthService.cs` (`UpdateProfileAsync` lines 92-106; add `RequestEmailChangeAsync`)
- Modify: `src/ApexRacers.Tests/Services/AuthServiceTests.cs`

**Interfaces:**

- Produces: `record RequestEmailChangeRequest(string NewEmail)`; `record ConfirmEmailChangeRequest(Guid UserId, string NewEmail, string Token)`; `record MessageResponse(string Message)`; `AuthService.RequestEmailChangeAsync(Guid userId, string newEmail, CancellationToken ct = default) : Task`; `UpdateProfileRequest` no longer has `Email`.

- [ ] **Step 1: Write the failing tests**

```csharp
    [Fact]
    public async Task RequestEmailChangeAsync_NewAddress_SendsVerificationToNewEmail()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var emails = new FakeEmailSender();
        var svc = BuildService(provider, emails);
        var reg = await svc.RegisterAsync(new RegisterRequest("old@example.com", "Pass1234"), TestContext.Current.CancellationToken);

        await svc.RequestEmailChangeAsync(reg.UserId, "new@example.com", TestContext.Current.CancellationToken);

        Assert.NotNull(emails.Last);
        Assert.Equal("new@example.com", emails.Last!.To);
        Assert.Contains("https://test.apexracers.gg/verify-email", emails.Last.HtmlBody);
        Assert.Contains(reg.UserId.ToString(), emails.Last.HtmlBody);
    }

    [Fact]
    public async Task RequestEmailChangeAsync_AddressUsedByAnother_SendsNothing()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var emails = new FakeEmailSender();
        var svc = BuildService(provider, emails);
        await svc.RegisterAsync(new RegisterRequest("taken@example.com", "Pass1234"), TestContext.Current.CancellationToken);
        var reg = await svc.RegisterAsync(new RegisterRequest("me@example.com", "Pass1234"), TestContext.Current.CancellationToken);

        await svc.RequestEmailChangeAsync(reg.UserId, "taken@example.com", TestContext.Current.CancellationToken);

        Assert.Empty(emails.Sent);
    }
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test --filter FullyQualifiedName~AuthServiceTests`
Expected: FAIL — `RequestEmailChangeAsync` not defined.

- [ ] **Step 3: Update DTOs**

In `RequestDtos.cs` replace line 5 with:

```csharp
public record UpdateProfileRequest(string DisplayName, long? IRacingCustomerId = null, string? ThemePreference = null);
```

and add:

```csharp
public record RequestEmailChangeRequest(string NewEmail);
public record ConfirmEmailChangeRequest(Guid UserId, string NewEmail, string Token);
```

In `ResponseDtos.cs` add near `ForgotPasswordResponse` (line 67):

```csharp
public record MessageResponse(string Message);
```

- [ ] **Step 4: Update `AuthService`**

In `UpdateProfileAsync`, delete the email block (lines 92-106 — the entire `if (!string.IsNullOrWhiteSpace(request.Email)) { ... }`). Display name, iRacing ID, and theme handling remain.

Add (next to the password-reset method):

```csharp
    /// <summary>
    /// Begins a verify-then-apply email change: emails a confirmation link to the new address. The account
    /// email is unchanged until <see cref="ConfirmEmailChangeAsync"/> runs. Enumeration-safe — if the target
    /// address already belongs to another account, nothing is sent.
    /// </summary>
    public async Task RequestEmailChangeAsync(Guid userId, string newEmail, CancellationToken ct = default)
    {
        newEmail = newEmail?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(newEmail))
            throw new InvalidOperationException("Email address cannot be empty.");

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return;

        var existing = await userManager.FindByEmailAsync(newEmail);
        if (existing is not null && existing.Id != userId)
            return;

        var token = await userManager.GenerateChangeEmailTokenAsync(user, newEmail);
        var url = $"{BaseUrl}/verify-email?userId={userId}&email={Uri.EscapeDataString(newEmail)}&token={Uri.EscapeDataString(token)}";
        await emailSender.SendAsync(AccountEmailTemplates.EmailChangeVerification(newEmail, url), ct);
    }
```

- [ ] **Step 5: Run tests**

Run: `dotnet test --filter FullyQualifiedName~AuthServiceTests`
Expected: PASS. If any existing profile-update test referenced `request.Email` / passed an email, update it to the new `UpdateProfileRequest` shape.

- [ ] **Step 6: Commit**

```bash
git add src/ApexRacers.Api/Dtos src/ApexRacers.Api/Services/AuthService.cs src/ApexRacers.Tests
git commit -m "feat(auth): request-email-change flow; profile update no longer mutates email

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task C2: `ConfirmEmailChangeAsync` + controller endpoints

**Files:**

- Modify: `src/ApexRacers.Api/Services/AuthService.cs` (add `ConfirmEmailChangeAsync`)
- Modify: `src/ApexRacers.Api/Controllers/AuthController.cs` (add two actions after `ResetPasswordAsync`, line 114; remove email mention from the `UpdateProfileRequest` usage is automatic)
- Modify: `src/ApexRacers.Tests/Services/AuthServiceTests.cs`

**Interfaces:**

- Consumes: `RequestEmailChangeRequest`, `ConfirmEmailChangeRequest`, `MessageResponse` (C1).
- Produces: `AuthService.ConfirmEmailChangeAsync(Guid userId, string newEmail, string token, CancellationToken ct = default) : Task`; endpoints `POST /api/auth/request-email-change` ([Authorize]) and `POST /api/auth/confirm-email-change` (public).

- [ ] **Step 1: Write the failing tests**

```csharp
    [Fact]
    public async Task ConfirmEmailChangeAsync_ValidToken_ChangesEmailAndUsername()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var emails = new FakeEmailSender();
        var svc = BuildService(provider, emails);
        var reg = await svc.RegisterAsync(new RegisterRequest("old@example.com", "Pass1234"), TestContext.Current.CancellationToken);
        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByIdAsync(reg.UserId.ToString());
        var token = await userManager.GenerateChangeEmailTokenAsync(user!, "new@example.com");

        await svc.ConfirmEmailChangeAsync(reg.UserId, "new@example.com", token, TestContext.Current.CancellationToken);

        var updated = await userManager.FindByIdAsync(reg.UserId.ToString());
        Assert.Equal("new@example.com", updated!.Email);
        Assert.Equal("new@example.com", updated.UserName);
    }

    [Fact]
    public async Task ConfirmEmailChangeAsync_BadToken_Throws()
    {
        await using var provider = BuildProvider();
        await SeedRolesAsync(provider);
        var svc = BuildService(provider);
        var reg = await svc.RegisterAsync(new RegisterRequest("old@example.com", "Pass1234"), TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.ConfirmEmailChangeAsync(reg.UserId, "new@example.com", "not-a-real-token", TestContext.Current.CancellationToken));
    }
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test --filter FullyQualifiedName~AuthServiceTests`
Expected: FAIL — `ConfirmEmailChangeAsync` not defined.

- [ ] **Step 3: Implement `ConfirmEmailChangeAsync`**

```csharp
    /// <summary>
    /// Applies a pending email change using a token from <see cref="RequestEmailChangeAsync"/>. Keeps the
    /// username in sync (login is by email) and revokes all active refresh tokens (account-recovery action).
    /// </summary>
    public async Task ConfirmEmailChangeAsync(Guid userId, string newEmail, string token, CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString())
            ?? throw new InvalidOperationException("Invalid or expired email change request.");

        var trimmed = newEmail.Trim();
        var result = await userManager.ChangeEmailAsync(user, trimmed, token);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join(" ", result.Errors.Select(e => e.Description)));

        await userManager.SetUserNameAsync(user, trimmed);
        await RevokeAllActiveTokensAsync(user.Id, ct);
    }
```

- [ ] **Step 4: Add controller endpoints** (after `ResetPasswordAsync`, line 114)

```csharp
    [HttpPost("request-email-change")]
    [Authorize]
    public async Task<IActionResult> RequestEmailChangeAsync([FromBody] RequestEmailChangeRequest request, CancellationToken ct)
    {
        var userIdStr = User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (!Guid.TryParse(userIdStr, out var userId))
            return Unauthorized();

        await auth.RequestEmailChangeAsync(userId, request.NewEmail, ct);
        // Generic response — never reveals whether the target address is already in use.
        return Ok(new MessageResponse("If that address is available, a confirmation email has been sent."));
    }

    [HttpPost("confirm-email-change")]
    public async Task<IActionResult> ConfirmEmailChangeAsync([FromBody] ConfirmEmailChangeRequest request, CancellationToken ct)
    {
        await auth.ConfirmEmailChangeAsync(request.UserId, request.NewEmail, request.Token, ct);
        return NoContent();
    }
```

- [ ] **Step 5: Run tests**

Run: `dotnet test --filter FullyQualifiedName~AuthServiceTests`
Expected: PASS.

- [ ] **Step 6: Full backend build + coverage check**

Run:

```bash
dotnet build
dotnet-coverage collect "dotnet test" -f xml -o coverage.xml
reportgenerator -reports:coverage.xml -targetdir:coverage-report -reporttypes:TextSummary
```

Expected: build + tests pass; line and branch coverage ≥85%. (Pure templates + AuthService paths are covered; the two senders are `[ExcludeFromCodeCoverage]`.)

- [ ] **Step 7: Commit**

```bash
git add src/ApexRacers.Api/Services/AuthService.cs src/ApexRacers.Api/Controllers/AuthController.cs src/ApexRacers.Tests
git commit -m "feat(auth): confirm-email-change endpoint + service

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Phase D — Frontend

### Task D1: api.ts methods + drop email from updateProfile

**Files:**

- Modify: `src/web/src/services/api.ts` (`updateProfile` lines 813-822; add two methods after `resetPassword` line 849)
- Modify: `src/web/src/services/__tests__/api.test.ts`
- Modify: any caller of `updateProfile` (SettingsPage — updated in D3; adjust the call here so the build stays green)

**Interfaces:**

- Produces: `api.updateProfile(displayName: string, iRacingCustomerId: number | null): Promise<AuthResult>`; `api.requestEmailChange(newEmail: string): Promise<{ message: string }>`; `api.confirmEmailChange(userId: string, email: string, token: string): Promise<void>`.

- [ ] **Step 1: Write failing api tests**

Add to `api.test.ts` (mirror existing `forgotPassword`/`resetPassword` request-shape tests):

```ts
it("requestEmailChange posts the new email", async () => {
  const fetchMock = mockFetchJson({ message: "ok" });
  await api.requestEmailChange("new@example.com");
  expect(fetchMock).toHaveBeenCalledWith(
    "/api/auth/request-email-change",
    expect.objectContaining({ method: "POST" }),
  );
  const body = JSON.parse(
    (fetchMock.mock.calls[0][1] as RequestInit).body as string,
  );
  expect(body).toEqual({ newEmail: "new@example.com" });
});

it("confirmEmailChange posts userId, newEmail, token", async () => {
  const fetchMock = mockFetchJson(undefined, 204);
  await api.confirmEmailChange("uid-1", "new@example.com", "tok");
  const body = JSON.parse(
    (fetchMock.mock.calls[0][1] as RequestInit).body as string,
  );
  expect(body).toEqual({
    userId: "uid-1",
    newEmail: "new@example.com",
    token: "tok",
  });
});
```

> Match the existing helper names in `api.test.ts` (e.g. the fetch-mock + `JSON.parse(body)` pattern already used for `forgotPassword`); reuse them rather than introducing `mockFetchJson` if the file names them differently.

- [ ] **Step 2: Run to verify failure**

Run: `cd src/web && npx vitest run src/services/__tests__/api.test.ts`
Expected: FAIL — methods undefined.

- [ ] **Step 3: Update `api.ts`**

Replace `updateProfile` (lines 813-822) with:

```ts
  /** PUT /api/auth/profile — update display name and optional iRacing customer ID, returns fresh JWT */
  updateProfile(displayName: string, iRacingCustomerId: number | null): Promise<AuthResult> {
    return request('/api/auth/profile', {
      method: 'PUT',
      json: { displayName, iRacingCustomerId },
    });
  },
```

After `resetPassword` (line 849) add:

```ts
  /** POST /api/auth/request-email-change — send a verification link to the new address */
  requestEmailChange(newEmail: string): Promise<{ message: string }> {
    return request('/api/auth/request-email-change', { method: 'POST', json: { newEmail } });
  },

  /** POST /api/auth/confirm-email-change — apply a pending email change from the emailed link */
  confirmEmailChange(userId: string, email: string, token: string): Promise<void> {
    return request('/api/auth/confirm-email-change', {
      method: 'POST',
      json: { userId, newEmail: email, token },
    });
  },
```

- [ ] **Step 4: Run tests**

Run: `cd src/web && npx vitest run src/services/__tests__/api.test.ts`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
cd src/web && npx prettier --write src/services/api.ts src/services/__tests__/api.test.ts
git add src/web/src/services/api.ts src/web/src/services/__tests__/api.test.ts
git commit -m "feat(web): api methods for email change; drop email from updateProfile

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task D2: `VerifyEmailPage` + public route

**Files:**

- Create: `src/web/src/pages/VerifyEmailPage.tsx`
- Create: `src/web/src/pages/__tests__/VerifyEmailPage.test.tsx`
- Modify: `src/web/src/App.tsx` (add a public route next to `/reset-password`)

**Interfaces:**

- Consumes: `api.confirmEmailChange` (D1).
- Produces: default-exported `VerifyEmailPage`; route `/verify-email` (public, no AppShell).

- [ ] **Step 1: Write the failing test**

```tsx
import { render, screen, waitFor } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { describe, it, expect, vi, beforeEach } from "vitest";
import VerifyEmailPage from "../VerifyEmailPage";
import { api } from "../../services/api";

vi.mock("../../services/api", () => ({ api: { confirmEmailChange: vi.fn() } }));

function renderAt(search: string) {
  return render(
    <MemoryRouter initialEntries={[`/verify-email${search}`]}>
      <VerifyEmailPage />
    </MemoryRouter>,
  );
}

describe("VerifyEmailPage", () => {
  beforeEach(() => vi.clearAllMocks());

  it("confirms the change and shows success", async () => {
    (api.confirmEmailChange as ReturnType<typeof vi.fn>).mockResolvedValue(
      undefined,
    );
    renderAt("?userId=u1&email=new@example.com&token=tok");
    await waitFor(() =>
      expect(api.confirmEmailChange).toHaveBeenCalledWith(
        "u1",
        "new@example.com",
        "tok",
      ),
    );
    expect(await screen.findByText(/email.*updated/i)).toBeInTheDocument();
  });

  it("shows an error for an invalid link (missing params)", () => {
    renderAt("");
    expect(screen.getByText(/invalid or has expired/i)).toBeInTheDocument();
    expect(api.confirmEmailChange).not.toHaveBeenCalled();
  });

  it("shows an error when confirmation fails", async () => {
    (api.confirmEmailChange as ReturnType<typeof vi.fn>).mockRejectedValue(
      new Error("expired"),
    );
    renderAt("?userId=u1&email=new@example.com&token=bad");
    expect(await screen.findByText(/expired/i)).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run to verify failure**

Run: `cd src/web && npx vitest run src/pages/__tests__/VerifyEmailPage.test.tsx`
Expected: FAIL — module not found.

- [ ] **Step 3: Create `VerifyEmailPage.tsx`** (mirrors `ResetPasswordPage`'s layout/classes)

```tsx
import { useEffect, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { api } from "../services/api";

export default function VerifyEmailPage() {
  const [params] = useSearchParams();
  const userId = params.get("userId") ?? "";
  const email = params.get("email") ?? "";
  const token = params.get("token") ?? "";
  const linkValid = userId !== "" && email !== "" && token !== "";

  const [status, setStatus] = useState<"pending" | "done" | "error">(
    linkValid ? "pending" : "error",
  );
  const [error, setError] = useState<string | null>(
    linkValid
      ? null
      : "This email verification link is invalid or has expired.",
  );

  useEffect(() => {
    if (!linkValid) return;
    let cancelled = false;
    (async () => {
      try {
        await api.confirmEmailChange(userId, email, token);
        if (!cancelled) setStatus("done");
      } catch (err) {
        if (!cancelled) {
          setError(
            err instanceof Error
              ? err.message
              : "This link is invalid or has expired.",
          );
          setStatus("error");
        }
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [linkValid, userId, email, token]);

  return (
    <div className="bg-background text-on-background antialiased min-h-screen flex items-center justify-center p-4">
      <main className="relative z-10 w-full max-w-md bg-surface border border-line-2 rounded-xl shadow-2xl p-8 md:p-10">
        <h1 className="font-headline-md text-headline-md text-primary-fixed-dim font-extrabold tracking-tighter mb-2">
          Confirm Email Change
        </h1>

        {status === "pending" ? (
          <p className="font-body-sm text-body-sm text-on-surface-variant mt-4">
            Confirming your new email…
          </p>
        ) : status === "done" ? (
          <div className="space-y-6 mt-4">
            <div className="p-4 bg-surface-container-high border border-line rounded-lg font-body-sm text-body-sm text-on-surface">
              Your account email has been updated to{" "}
              <span className="text-on-surface font-semibold">{email}</span>.
              For security, you've been signed out everywhere — please sign in
              again.
            </div>
            <Link
              to="/login"
              className="block text-center w-full bg-primary-fixed-dim text-on-primary-fixed font-headline-sm text-headline-sm py-3 rounded-lg hover:bg-primary-fixed transition-all"
            >
              Continue to Sign In
            </Link>
          </div>
        ) : (
          <div className="space-y-6 mt-4">
            <div className="p-4 bg-error-container rounded-lg font-body-sm text-body-sm text-on-error-container">
              {error}
            </div>
            <Link
              to="/settings"
              className="block text-center w-full bg-primary-fixed-dim text-on-primary-fixed font-headline-sm text-headline-sm py-3 rounded-lg hover:bg-primary-fixed transition-all"
            >
              Back to Settings
            </Link>
          </div>
        )}
      </main>
    </div>
  );
}
```

- [ ] **Step 4: Register the route in `App.tsx`**

Find the `/reset-password` route registration and add a sibling. If routes are statically imported, add at the top:

```tsx
import VerifyEmailPage from "./pages/VerifyEmailPage";
```

and next to the reset-password `<Route>`:

```tsx
<Route path="/verify-email" element={<VerifyEmailPage />} />
```

(If `ResetPasswordPage` is lazy-loaded, mirror that lazy import form instead.)

- [ ] **Step 5: Run tests**

Run: `cd src/web && npx vitest run src/pages/__tests__/VerifyEmailPage.test.tsx`
Expected: PASS (3 tests).

- [ ] **Step 6: Commit**

```bash
cd src/web && npx prettier --write src/pages/VerifyEmailPage.tsx src/pages/__tests__/VerifyEmailPage.test.tsx src/App.tsx
git add src/web/src/pages/VerifyEmailPage.tsx src/web/src/pages/__tests__/VerifyEmailPage.test.tsx src/web/src/App.tsx
git commit -m "feat(web): public /verify-email page for email-change confirmation

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task D3: SettingsPage email-change UI

**Files:**

- Modify: `src/web/src/pages/SettingsPage.tsx` (state ~lines 19-28; `saveProfile` lines 67-71; email field JSX lines 176-190)
- Modify: `src/web/src/pages/__tests__/SettingsPage.test.tsx`

**Interfaces:**

- Consumes: `api.updateProfile(displayName, iRacingCustomerId)` and `api.requestEmailChange` (D1).

- [ ] **Step 1: Write the failing test**

Add to `SettingsPage.test.tsx` (match its existing mock/render harness — it already mocks `api` and `useAuth`):

```tsx
it("requests an email change and shows a pending notice", async () => {
  (api.requestEmailChange as ReturnType<typeof vi.fn>).mockResolvedValue({
    message: "ok",
  });
  renderSettings(); // existing helper in this file
  const emailInput = screen.getByLabelText(/email address/i);
  fireEvent.change(emailInput, { target: { value: "new@example.com" } });
  fireEvent.click(screen.getByRole("button", { name: /verify new email/i }));
  await waitFor(() =>
    expect(api.requestEmailChange).toHaveBeenCalledWith("new@example.com"),
  );
  expect(await screen.findByText(/pending verification/i)).toBeInTheDocument();
});
```

Ensure `requestEmailChange` is part of the file's `api` mock. If a profile-save test asserts `updateProfile` was called with an email argument, update it to the two-arg signature `(displayName, iRacingCustomerId)`.

- [ ] **Step 2: Run to verify failure**

Run: `cd src/web && npx vitest run src/pages/__tests__/SettingsPage.test.tsx`
Expected: FAIL.

- [ ] **Step 3: Update SettingsPage**

Add state near the other profile state (after line 28):

```tsx
const [emailSaving, setEmailSaving] = useState(false);
const [emailPending, setEmailPending] = useState<string | null>(null);
const [emailError, setEmailError] = useState<string | null>(null);
```

Change the `updateProfile` call in `saveProfile` (lines 67-71) to drop `email`:

```tsx
const result = await api.updateProfile(
  displayName,
  iRacingCustomerId ? Number(iRacingCustomerId) : null,
);
```

Add an email-change handler after `saveProfile`:

```tsx
async function submitEmailChange(e: React.SubmitEvent<HTMLFormElement>) {
  e.preventDefault();
  setEmailError(null);
  if (!email || email === user?.email) {
    setEmailError("Enter a different email address.");
    return;
  }
  setEmailSaving(true);
  try {
    await api.requestEmailChange(email);
    setEmailPending(email);
  } catch (err) {
    setEmailError(
      err instanceof Error ? err.message : "Failed to request email change.",
    );
  } finally {
    setEmailSaving(false);
  }
}
```

Remove the email `<div>` (lines 176-190) from inside the profile `<form onSubmit={saveProfile}>` and add a separate form immediately after that form closes:

```tsx
<form
  onSubmit={submitEmailChange}
  className="space-y-4 mt-6 pt-6 border-t border-line"
>
  <div>
    <label
      htmlFor="profile-email"
      className="block font-label-caps text-label-caps text-on-surface-variant mb-2"
    >
      Email Address
    </label>
    <input
      id="profile-email"
      type="email"
      value={email}
      onChange={(e) => setEmail(e.target.value)}
      className="w-full bg-surface-container-high border border-line-2 rounded text-on-surface font-body-sm text-body-sm px-3 py-2 focus:outline-none focus:border-primary-fixed-dim focus:ring-1 focus:ring-primary-fixed-dim transition-colors"
    />
    {emailPending && (
      <p className="mt-1.5 font-body-sm text-[12px] text-primary-fixed-dim">
        Pending verification: {emailPending}. Check that inbox to confirm the
        change.
      </p>
    )}
    <p className="mt-1.5 font-body-sm text-[12px] text-on-surface-variant/60">
      Changing your email sends a confirmation link to the new address. Your
      sign-in email won't change until you confirm it.
    </p>
  </div>
  {emailError && (
    <p className="font-body-sm text-body-sm text-error">{emailError}</p>
  )}
  <div className="pt-2">
    <button
      type="submit"
      disabled={emailSaving}
      className="bg-primary-fixed-dim text-on-primary-fixed font-headline-sm text-headline-sm px-4 py-2 rounded-lg hover:bg-primary-fixed transition-all disabled:opacity-60 disabled:cursor-not-allowed"
    >
      {emailSaving ? "Sending…" : "Verify new email"}
    </button>
  </div>
</form>
```

- [ ] **Step 4: Run tests**

Run: `cd src/web && npx vitest run src/pages/__tests__/SettingsPage.test.tsx`
Expected: PASS.

- [ ] **Step 5: Full frontend gate**

Run: `cd src/web && npx prettier --check . && npm run lint && npx vitest run --coverage`
Expected: prettier clean, lint clean, all four coverage metrics ≥85%.

- [ ] **Step 6: Commit**

```bash
git add src/web/src/pages/SettingsPage.tsx src/web/src/pages/__tests__/SettingsPage.test.tsx
git commit -m "feat(web): Settings email-change flow with pending-verification state

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Phase E — Infrastructure & docs

### Task E1: Provision ACS + document the runbook

**Files:**

- Modify: `private/deployTODO.md` (new section), `CLAUDE.md` / `private/PRD.md` infra tables if a resource row is added.

Provisioning runs through the `azure-infrastructure` agent (it owns `az` calls + Key Vault). This task pauses on Jerry's registrar DNS step.

- [ ] **Step 1: Create the Email Communication Service + custom domain**

Dispatch the `azure-infrastructure` agent to:

- `az communication email create` (Email Communication Service in `apexracers-rg`).
- Add custom domain `apexracers.gg`; capture the verification TXT, SPF TXT, and DKIM CNAME records.
- Output those records verbatim for Jerry to add at the registrar. **Stop and wait** for verification to go green (do not invent the record values — read them from the resource).

- [ ] **Step 2: Create/link the Communication Services resource**

- `az communication create` (or reuse) and link the verified email domain; configure MailFrom `noreply@apexracers.gg` (display name "ApexRacers").

- [ ] **Step 3: Wire secrets + app settings**

- `az keyvault secret set --vault-name apexracers-kv --name ACS-CONNECTION-STRING --value "<conn>"`.
- `az webapp config appsettings set --name apexracers-api --resource-group apexracers-rg --settings ACS_SENDER_ADDRESS=noreply@apexracers.gg APP_BASE_URL=https://apexracers.gg`.
- Restart `apexracers-api`; confirm DI binds `AcsEmailSender` (no "Email delivery not configured" warning at startup).

- [ ] **Step 4: Smoke test + document**

- Trigger a forgot-password against the deployed API for a test account; confirm the email arrives from `noreply@apexracers.gg`.
- Add a "Azure Communication Services (transactional email)" section to `deployTODO.md` covering steps 1-3 (with the DNS-record table) and the new `ACS-CONNECTION-STRING` row in the Key Vault secrets summary.

- [ ] **Step 5: Commit (docs only; private/ is gitignored so only tracked docs commit)**

```bash
git add CLAUDE.md private 2>/dev/null; git add CLAUDE.md
git commit -m "docs(deploy): ACS transactional email provisioning runbook

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

> `private/` is gitignored — `deployTODO.md` changes won't be committed; that's expected. Commit only tracked doc edits.

---

### Task E2: Update project docs

**Files:**

- Modify: `CLAUDE.md`, `private/PRD.md`, `private/ROADMAP.md`, `README.md`

- [ ] **Step 1: CLAUDE.md**

- AuthController bullet: add `request-email-change` (Authorize) + `confirm-email-change` (public); note profile update no longer changes email.
- AuthService bullet: note reset now emails the link; add the email-change methods.
- Add the `Email/` services (`IEmailSender`, `AcsEmailSender`, `LoggingEmailSender`, pure `AccountEmailTemplates`) to the services list and test-files list.
- Config: document `ACS_CONNECTION_STRING`, `ACS_SENDER_ADDRESS`, `APP_BASE_URL`.
- Frontend: add the `/verify-email` route.

- [ ] **Step 2: PRD**

- §2 auth/onboarding: reset email is now real; add verify-then-apply email change. Settings (§10): email change is now verification-gated. Screen inventory: add `/verify-email` (public). Version bump.

- [ ] **Step 3: ROADMAP**

- Move "Real email delivery for password reset" and the email-change verification follow-up from Backlog into Completed (dated). Note the deferred old-address notice.

- [ ] **Step 4: README**

- If env vars are documented there, add `ACS_CONNECTION_STRING` / `ACS_SENDER_ADDRESS` / `APP_BASE_URL` and the local-dev note (logging fallback when unset).

- [ ] **Step 5: Commit**

```bash
git add CLAUDE.md README.md
git commit -m "docs: account-emails feature (reset email + email-change verification)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Self-review notes

- **Spec coverage:** Foundation (A1-A3) ✓; password-reset email (B1) ✓; profile-update email removal (C1) ✓; request/confirm email change (C1/C2) ✓; enumeration-safe (C1) ✓; revoke-on-change (C2) ✓; frontend api/page/settings (D1-D3) ✓; ACS provisioning + custom domain (E1) ✓; docs (E2) ✓. Deferred old-address notice is explicitly out of scope.
- **Type consistency:** `RequestPasswordResetAsync`, `RequestEmailChangeAsync`, `ConfirmEmailChangeAsync`, `OutboundEmail`, `IEmailSender.SendAsync`, `MessageResponse`, and the api.ts method signatures are used identically across tasks.
- **No placeholders:** every code step shows complete code; the only deliberately-unspecified values are the ACS DNS records (must be read from the provisioned resource, per the Ground Rules — never invented).
