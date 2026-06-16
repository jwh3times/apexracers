# Security Policy

Thanks for helping keep ApexRacers and its users safe. This document explains how
to report a security vulnerability and what to expect after you do.

## Supported versions

ApexRacers is a continuously deployed web application. Only the latest code on the
`main` branch and the version currently deployed to production
(`https://apexracers.gg`) are supported and will receive security fixes. There are
no long-term support branches or backports to older commits.

| Version | Supported |
| ------- | --------- |
| `main` (latest) | ✅ |
| Older commits / tags | ❌ |

## Reporting a vulnerability

**Please do not open a public GitHub issue, pull request, or discussion for security
vulnerabilities.** Public reports expose users before a fix is available.

Instead, use one of the following private channels:

1. **GitHub private vulnerability reporting (preferred).** Go to the
   [**Security** tab](https://github.com/jwh3times/apexracers/security/advisories/new)
   of this repository and click **Report a vulnerability**. This opens a private
   advisory visible only to you and the maintainers.
2. **Email.** If you cannot use GitHub, email **<security@apexracers.gg>** with the
   subject line `[SECURITY] ApexRacers`.

To help us triage quickly, please include as much of the following as you can:

- A description of the vulnerability and its potential impact.
- The component affected (API, ingestion worker, frontend, infrastructure, etc.).
- Step-by-step reproduction instructions or a proof of concept.
- Affected URL(s), endpoint(s), or file path(s) and the commit/branch you tested.
- Any logs, screenshots, or sample requests that demonstrate the issue.
- Your assessment of severity, if you have one.

## What to expect

- **Acknowledgement** within **3 business days** of your report.
- An initial **assessment and triage** within **7 business days**, including whether
  we accept the report and an expected timeline for a fix.
- Ongoing updates as we work toward a resolution.
- **Coordinated disclosure:** we will work with you on a disclosure timeline and aim
  to ship a fix within **90 days**. We are happy to credit you in the advisory and
  release notes once the issue is resolved — let us know how you would like to be
  attributed (or if you prefer to remain anonymous).

If you do not receive a response within the windows above, please send a follow-up —
messages occasionally get missed.

## Scope

This project is maintained by a single developer as a side project, so please size
your expectations accordingly. The following are **in scope**:

- The ApexRacers Web API (`src/ApexRacers.Api`) — authentication (JWT, refresh
  tokens, iRacing OAuth), authorization/RBAC, input validation, and API endpoints.
- The React frontend (`src/web`) — auth/token handling, XSS, and client-side data
  exposure.
- The ingestion worker (`src/ApexRacers.Ingestion`) and seeder
  (`src/ApexRacers.Seeder`).
- Telemetry file upload handling (`.ibt` parsing).
- Database access patterns, EF Core queries, and migrations.
- Container and deployment configuration in this repository.

The following are **out of scope**:

- Vulnerabilities in third-party dependencies that have no demonstrated impact on
  ApexRacers (report those upstream; we track dependency advisories via Dependabot).
- The iRacing platform, the iRacing API, or any third-party service we integrate
  with (report those to the respective vendor).
- Reports generated solely by automated scanners without a working proof of concept.
- Denial-of-service, volumetric, brute-force, or rate-limiting findings without a
  concrete, non-volumetric exploit.
- Social engineering, phishing, or physical attacks against the maintainer or
  infrastructure providers.
- Missing security headers or best-practice recommendations with no demonstrable
  exploit (these are welcome as regular issues, not security reports).
- Self-XSS and issues requiring a fully compromised device or browser.

## Testing guidelines (safe harbor)

We support good-faith security research. If you make a genuine effort to comply with
this policy, we will consider your research authorized, will not pursue or support
legal action against you, and will work with you to understand and resolve the issue.

When testing, please:

- Only test against your **own** account and a **local development environment** that
  you control (`docker compose up` — see the [README](README.md)). Do **not** test
  against the production environment (`apexracers.gg`) or other users' data.
- Never access, modify, or destroy data that does not belong to you.
- Do not run automated scanners, fuzzers, or load tests against production.
- Stop immediately and report if you encounter any third-party or personal data.
- Keep details of any vulnerability confidential until a fix has been released and we
  have agreed on disclosure.

## A note on secrets

If you discover credentials, API keys, tokens, or other secrets committed to the
repository or exposed by the application, please report them privately as described
above and do **not** use them. Application secrets are managed through Azure Key
Vault and environment variables (`.env`, which is gitignored); a leaked secret should
be treated as a vulnerability.

Thank you for helping keep ApexRacers secure.
