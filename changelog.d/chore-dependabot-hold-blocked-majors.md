### Changed

- Dependabot no longer opens major-version PRs for `Npgsql.OpenTelemetry` or the local `postgres`
  image. `Npgsql.OpenTelemetry` 11 brings the Npgsql 11 driver, which the 10.x Npgsql EF Core
  provider cannot run against, so its major now waits with the provider's for the .NET 11
  migration. The `docker-compose.yml` PostgreSQL image stays on production's major 18 until the
  production database move chooses the next one; a new major also refuses the existing local data
  volume. `Npgsql.OpenTelemetry` minor and patch updates still arrive. `docs/agents/dependabot.md`
  describes both holds. No product behavior changes.
