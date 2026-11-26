### Changed

- Contributor docs: three gaps left by the move to changelog fragments are closed (#545).
  - The merging guide no longer says the `[skip ci]` release commit runs no workflow. No workflow in `.github/workflows/` runs on it, so nothing deploys it, but GitHub's CodeQL default setup still scans it.
  - The penetration-tester agent's CI/CD supply-chain checks now cover the release GitHub App, the one credential that can push to `main` past the ruleset, with a grep test for misuse.
  - The doc map now says to update the code-reviewer and penetration-tester agents when a workflow gains a privileged credential or a ruleset bypass, and `CONTRIBUTING.md` when the changelog scheme changes.
