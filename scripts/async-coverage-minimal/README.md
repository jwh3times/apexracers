# Independent compiler-cache control

Research-only package-free wrappers for #416. This project is outside the solution and backend coverage denominator. It neither replaces real AdminService/provider tests nor changes production code.

Build once, then compare both orders using the same bytes:

```bash
dotnet build scripts/async-coverage-minimal/Minimal.csproj -c Release --disable-build-servers
python3 scripts/probe-async-coverage-416.py \
  --tool /path/to/pinned-18.12.0/dotnet-coverage \
  --collection-format cobertura --output TestResults/async-416-new-direct
```

The command declares six independent fresh-process controls: cold cache, helper-prewarmed cache and explicit fresh delegates, each in both orders. `--collection-format coverage` retains native reports and exports the same native input to XML/Cobertura; those observed exports lacked branch rows, so direct Cobertura supplies the branch comparison.

`RealWarm.csproj` is a separate reflection launcher for the already-built #411 real AdminService diagnostic. It populates the exact canonical compiler cache before the unchanged diagnostic runs. It requires the canonical product with field token `0x040004F1`; a different token is rejected rather than guessed.

```bash
dotnet build scripts/async-coverage-minimal/RealWarm.csproj -c Release --disable-build-servers \
  -p:BaseIntermediateOutputPath=obj/realwarm/ -p:OutputPath=bin/realwarm/
python3 scripts/probe-async-coverage-416.py --real-cache-warm \
  --tool /path/to/pinned-18.12.0/dotnet-coverage \
  --build-root scripts/async-coverage-minimal/bin/realwarm \
  --canonical /path/to/frozen/canonical \
  --fixture-root /path/to/frozen/411-diagnostic \
  --settings /path/to/same/product-settings.config \
  --output TestResults/async-416-new-real-warm
```

Outputs must be new directories. Commands are bounded to 30 seconds with bounded process-group termination on timeout. These package-free/SQLite probes create no PostgreSQL or Docker resources. Collection failures and comparison failures retain originals. See the research report for exact attribution and provenance limits; no upstream tool defect was established.
