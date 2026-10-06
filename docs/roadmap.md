# ApexRacers Roadmap

This is the public, high-level roadmap — the themes, not the task list.

**Individual remaining work is tracked as GitHub issues in this repository**, grouped by
milestone. Start there for anything concrete; this page only explains the shape of the work.

Private runbooks and historical implementation notes live in an optional private companion
repository checked out at `private/`, which this repository ignores and never requires.

## Current Status

ApexRacers has shipped its core local account flow, telemetry upload, synthetic/demo
data support, series/week browsing, percentile calculations, recommendations,
analytics, catalog pages, and operational hardening work such as health checks,
security headers, rate limiting, and CI quality gates.

The project is continuously deployed from `main`. Releases are tagged automatically
using the `<major>.<minor>.<build>` format.

## Active Themes

- **Driver authorization and publication:** integrate versioned personal/sharing consent,
  journal-first lifecycle transitions, protected HTTP dispatch and tracked-copy cleanup. Controlled
  synthetic modules precede provider proof, the production journal adapter, feature integration and
  the complete acceptance matrix; legacy Real Driver workflows remain unavailable meanwhile.
  Evidence writers now bind durable purpose/generation and source dependencies; remaining feature
  integrations, restore verification and deployed retention observations still gate live use.
  Controlled private upload attribution, dormant recovery and User-wide deletion now have synthetic
  PostgreSQL/HTTP evidence; ordinary real private workflows remain closed.
  Whole-cohort publication candidates add synthetic band/support and adversarial review evidence;
  actual catalog admission and atomic combined-release accounting still precede product publication.
- **Live iRacing data readiness:** keep the iRacing-backed surface gated until required
  service credentials and rollout checks are complete.
- **Sign in with iRacing:** replace self-entered customer IDs with a verified OAuth
  linking flow when a registered client is available.
- **Operations maturity:** continue improving deployment safety, monitoring, security
  checks, and release automation.
- **Product polish:** improve dashboard surfacing, notifications, accessibility
  coverage, and the demo-data preview experience.

## Parked Or Optional Work

- League-oriented features are parked until there is clear product demand.
- Broader visual-regression coverage and notification expansions are good follow-up
  candidates after core data availability is resolved.

## Documentation Policy

Public docs should describe product capabilities, setup, contribution workflow, and
safe architecture guidance. Private docs should hold deployment runbooks, raw API
samples, and archived implementation notes. Open, unfixed security findings are
tracked as draft GitHub security advisories on this repository, not in a private doc.
