# Upstream-ready coverage observation counterexample

Draft only. The underlying profiler/JIT/export mechanism is unproven; this is not
a posted or maintainer-confirmed defect. Original reports and IL are linked from
docs/research/backend-async-coverage-drift.md and docs/testing/backend-async-411.json.

With unchanged AdminService product bytes, SDK 10.0.112/runtime 10.0.12 and
Microsoft dotnet-coverage 18.12.0, the retained strict SQLite controls call both
GetFlagsForRoleAsync and GetFlagsForUserAsync 16 times. Every call completes before
pumping its queued SynchronizationContext, with zero posted callbacks and the same
single enabled flag. Role-first reports 100%/50% at source lines 64/58; reversing
only the diagnostic call-order array reports 50%/100%. Product condition models
remain equal. Diagnostic DLLs differed because the declared array was compiled;
the current fixture parameterizes that order for future reproduction.

Natural MTP controls of the existing 39-case AdminServiceTests class also invert
those observations between two original invocations. Real PostgreSQL natural and
controlled pending-query probes returned correct flags, but did not distinguish
those report observations from the completed-query control. Await completion alone
is not a demonstrated explanation.

## Bounded reproduction

Use the repository's pinned SDK. Build the canonical Release tree once and preserve
its bytes. The diagnostic project references those compiled DLLs directly and does
not rebuild product projects or add itself to the solution/test denominator.

```bash
dotnet build --configuration Release
dotnet tool install dotnet-coverage --version 18.12.0 --tool-path /tmp/coverage411

dotnet build scripts/async-coverage-probe/Probe.csproj --configuration Release \
  -p:CanonicalDirectory="$PWD/src/ApexRacers.Tests/bin/Release/net10.0"

python3 scripts/probe-async-coverage-411.py --mode diagnostic \
  --tool /tmp/coverage411/dotnet-coverage \
  --build-root src/ApexRacers.Tests/bin/Release/net10.0 \
  --fixture-root scripts/async-coverage-probe/bin/Release/net10.0 \
  --diagnostic-mode sqlite-strict --call-order role-first \
  --output TestResults/async-order-role-first-new

python3 scripts/probe-async-coverage-411.py --mode diagnostic \
  --tool /tmp/coverage411/dotnet-coverage \
  --build-root src/ApexRacers.Tests/bin/Release/net10.0 \
  --fixture-root scripts/async-coverage-probe/bin/Release/net10.0 \
  --diagnostic-mode sqlite-strict --call-order user-first \
  --output TestResults/async-order-user-first-new
```

The final parameterized fixture/guard wrapper was compile/syntax checked after the
retained experiments; these commands are future reproduction instructions, not a
claim those additional invocations were executed. Use fresh output roots and retain
all outcomes. No full-suite campaign, production code modification, new denominator
exclusion or comparison tolerance is required for this question.

Question for investigation: what maps the exported condition `0`, type `jump`,
denominator 2 at these generated MoveNext await lines, and why does its observation
assignment invert in the declared order control? The IL contains get_IsCompleted
at offset49 and brtrue.s at54, but exact exported edge identity has not been decoded.
