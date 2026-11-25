### Changed

- Landing a pull request now waits for the production deploy that its merge starts (`deploy.yml`)
  and reports its outcome, alongside the release tag. The agent docs map also lists
  `.github/dependabot.yml`, so its update groups and held majors stay in step with
  `docs/agents/dependabot.md`. No product behavior changes.
