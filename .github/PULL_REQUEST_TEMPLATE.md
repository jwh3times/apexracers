# Pull Request Template

<!--
Thanks for contributing to ApexRacers! Please fill out the sections below.
Keep PRs focused — one logical change per PR is easier to review.
See CONTRIBUTING.md and CLAUDE.md for conventions and quality gates.
-->

## Summary

<!-- What does this PR change, and why? Give reviewers the context they need. -->

## Related issue

<!-- Link the issue this closes, e.g. "Closes #123". Use "Refs #123" if it only relates. -->

Closes #

## Type of change

<!-- Check all that apply. -->

- [ ] Bug fix
- [ ] New feature
- [ ] Refactor / tech debt
- [ ] Documentation
- [ ] CI / build / infrastructure

## How was this tested?

<!-- Describe the tests you ran and how a reviewer can reproduce them.
     Note any manual verification (e.g. Swagger, the UI, a specific endpoint). -->

## Checklist

- [ ] Follows the conventions in [CONTRIBUTING.md](../CONTRIBUTING.md) and [CLAUDE.md](../CLAUDE.md)
- [ ] Backend: `dotnet test` passes; line **and** branch coverage stay above 85%
- [ ] Frontend (`web/`): `npm run lint`, `npx prettier --check .`, and `npx vitest run --coverage` pass
- [ ] Added/updated tests for new logic
- [ ] Added/updated documentation where relevant (README, CLAUDE.md, etc.)
- [ ] Database changes include an EF Core migration (if applicable)
- [ ] No secrets, credentials, or `.env` values are committed

## Screenshots / notes

<!-- Optional: UI screenshots, before/after, or anything else reviewers should know. -->
