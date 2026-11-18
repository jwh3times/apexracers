# Verification

How to tell that something is actually done or actually true. Every rule here came from a case in
this repository where a confident report, a green check, or a well-written issue was wrong while
nothing went red. Read it before calling a change verified, before accepting a "checked, nothing to
do", and before acting on an advisory or issue body.

## A green result must be able to go red

- **Ask whether the gate could ever have failed on this input.** A scan, filter or watcher reporting
  zero may be blind rather than clean. A secrets scan here passed for weeks because it only
  redacted absolute `https://` URLs, while the leaked values were relative paths, and a scanner
  allowlist silenced the one rule that would have caught them.
- **Build the positive control first.** Before trusting a gate or a probe, feed it an input that
  should fail and confirm it does. A control that uses a vendor's documentation example (often
  stopworded) proves nothing.
- **Treat every allowlist, ignore rule, exemption and `paths:` filter as a dated claim.** Read what it
  suppresses, not only its justification. Prove a suppression is removable by the counterfactual both
  ways: scan with it removed (clean because the data is clean), and scan the pre-fix content with the
  same rules (real findings). Prefer fixing the data so the suppression can be deleted.
- **A recorded reason for rejecting a tool version also ages.** Re-check each stated reason against
  upstream before honoring it.

## Tests must fail without the fix

- **Confirm a new test is red without the fix** by temporarily restoring the old code. Green-only
  proves nothing, especially for concurrency tests.
- **When a test exists to exercise a fallback, assert that the fallback ran.** A forced interleaving
  whose matcher silently stops matching leaves the test green with no concurrency in it (the
  `dotnet-api` agent's Tests section has the interceptor pattern). Check the matcher with a second
  counterfactual: deliberately mismatch it and confirm the assertion fails.
- **After any change to the test harness, diff coverage** rather than trusting a green run. A pooling
  change once removed the only collision a race test produced, and only branch coverage showed it.
- **Check that a filtered run ran something.** A test filter that matches zero tests exits green.
  Read the executed count.

## Check what the method could see

- **Before accepting "no change needed", ask whether the method could observe the target.** Git
  history says nothing about gitignored paths (`private/**`), CI says nothing about untracked files,
  and a grep over tracked files says nothing about either. A docs pass once skipped a companion file
  for several releases running, each time citing the previous skip as precedent.
- **A keyword match is not coverage.** A grep count answers "does this string appear", never "is this
  case covered". Read the section, and be most suspicious when a grep conveniently confirms that work
  can be skipped.
- **Before blaming pre-existing state, check whether this branch caused it.** A lint failure was once
  reported as old warnings in an untouched file; it was a new error from the branch itself, and
  warnings don't affect the exit code.
- **Key a CI or deploy watch on the commit SHA, never on a clock time.** A watcher filtered on
  `createdAt` matched no runs and reported "all complete" for a deploy it never observed. Treat a
  watcher that finished with no events as suspicious.

## Advisories, issues and procedures are proposals

Security advisories, this repository's own issue bodies, and the private wiki's operator procedures
are leads, not specifications. Several have prescribed fixes that would have done nothing, or made
things worse:

- a remediation that would have applied forwarded-header processing twice, creating the spoofing hole
  it warned about;
- redaction patterns added to a script that never reads the files in question;
- an issue whose two proposed mechanisms both failed, and whose headline effect was a no-op;
- an operator step that relied on a code path no production code calls.

**How to apply:** read the cited file and line before writing anything. Check that the proposed home
can actually reach its inputs. Trace the code path an irreversible operator step depends on. When the
finished fix departs from the advisory or issue, say so and why in the PR body and in the advisory's
`## Resolution`.

## Experiment when the answer is load-bearing

- **When what to build depends on what the framework actually does, measure it.** A five-minute
  throwaway app beats reasoning from familiarity, which was wrong three times in one session here.
  For example, an oversized `IFormFile` upload is a 400 from model state, not a 500 through the
  exception middleware.
  - Run `dotnet new web` in a scratch directory and delete its `Properties/launchSettings.json`,
    which overrides `--urls`.
  - Run the built executable rather than `dotnet run` when you need to stop it cleanly.
- **First price the failure.** Probe when being wrong is silent or expensive. Skip the probe when
  the real run fails loudly, cheaply and revertibly, for example a deploy job that just stops on a
  wrong OIDC subject while the old credential is still the rollback. A probe that needs wider
  production access buys certainty with exposure.

## Every check green, nothing dropped quietly

- **Every red check blocks, whether or not the ruleset requires it.** Say a check's required status
  only as context, never as a reason to stop. Prefer making a branch self-sufficient over a merge-order
  dependency between PRs.
- **Don't drop planned scope quietly.** Before deferring part of a slice, check the stated reason
  against the code; if the work is feasible, build it. A real deferral is stated plainly in the
  response and tracked as an actionable item, never left in a parenthetical.
- **Verify every remote or configuration write with a separate read**, including after a refusal. A
  write can exit 0 and apply nothing, apply a mangled value, or partly apply before it is refused.
