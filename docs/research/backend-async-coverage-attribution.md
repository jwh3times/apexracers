# Shared delegate-cache attribution (#416)

The current focused AdminService observation drift comes from a shared compiler-generated static method-group delegate cache. It is an execution-order difference at the `Select(ToDto)` projections on source lines 58 and 64. The controlled evidence does not establish a collector defect or an `await IsCompleted` branch difference.

No product source, coverage exclusion, comparison tolerance, privacy case, meaningful time window or production workflow changed. This investigation does not reopen the rejected #405 sharding topology or remove its independent timing miss.

## Same-binary RED and predictions

Fresh processes ran the parameterized #411 fixture against the same frozen product and diagnostic DLLs. SDK 10.0.112, runtime 10.0.12 and dotnet-coverage 18.12.0 were pinned; canonical product dependencies, diagnostic bytes, settings and helper inputs matched before/after. Every run made 32 real AdminService calls using actual EF/SQLite queries and returned the expected flag. Both methods completed synchronously under the queued synchronization-context control.

| Actual service control | Role projection | User projection |
| --- | --- | --- |
| Cold, role first | 100% (2/2) | 50% (1/2) |
| Cold, user first | 50% (1/2) | 100% (2/2) |
| Shared cache prewarmed, role first | 50% (1/2) | 50% (1/2) |
| Shared cache prewarmed, user first | 50% (1/2) | 50% (1/2) |

The explicit feedback command exits **1**, reporting `sameBinary=true` and `targetObservationsEqual=false`:

```bash
python3 scripts/probe-async-coverage-416.py --verify-real-pair \
  TestResults/async-416-real-role-first-1/context.json \
  TestResults/async-416-real-user-first-1/context.json
```

Before instrumentation, the ranked predictions were: shared cache initialization (independent prewarming removes order dependence); await completion (changing completed/pending awaits changes the affected edge); collector/export attribution (drift survives removing cache or differs by report writer); input drift (material byte bindings differ). The prewarming and independent reduction confirm the first prediction. Earlier #411 real PostgreSQL pending-query controls also did not make the projection observation depend on await completion; they remain separately scoped evidence.

## Exact compiler mapping and causal control

Microsoft documents reuse of static method-group delegates beginning in C# 11, through a generated `<>O` cache and null-check initialization. Our canonical IL establishes this particular shared field, rather than assuming every method-group conversion shares storage. [Microsoft: delegate costs and C# 11](https://devblogs.microsoft.com/dotnet/understanding-the-cost-of-csharp-delegates/).

Both canonical 217-byte `MoveNext` methods load and store `AdminService+<>O.<0>__ToDto`, field token **0x040004F1**. The uninstrumented portable PDB maps cache load offset 131, conditional branch 137 and store 153 to the visible projection: line 64 for Role, line 58 for User. `get_IsCompleted` at offset 49 and its branch at 54 lie in a **hidden** sequence-point region. The actual awaits occur on lines 63 and 57. Instrumented reflection IL has different offsets and cannot be mapped to the uninstrumented PDB by reusing offset numbers.

The IL and cache controls establish initialization on the first use and reuse after initialization. In the exported observations, the first-invoked wrapper receives 100% and the second 50%; swapping order swaps that assignment. These percentages do not identify individual branch edges or prove that their numerator counts individually decoded edge executions. The independent Warm helper itself reports 100% after one declared call, reinforcing this interpretation limit without changing the causal effect of prewarming on the two wrappers.

The research-only `RealCacheWarm` launcher resolves that exact private compiler field and actual `ToDto` method through reflection. It verifies the field is initially null, installs a delegate of its exact declared type, records null→nonnull and invokes the unchanged #411 fixture. The same launcher binary serves both orders. Both projections then show 50% in both orders, with all 32 real query results correct, zero queued posts and completed tasks. Canonical product DLL/PDB, copied diagnostic dependencies, launcher, tool and runtime hashes remain unchanged. This is a causal intervention on the compiled cache, not a product fix or a proposal to prewarm production.

## Independent reduction

The package-free `scripts/async-coverage-minimal` project retains two async wrappers, completed tasks, list projection and the same static method-group mapper. It has no project or package references. This independent extraction removes EF/Identity/provider dependencies together; it proves this compiler pattern is sufficient, rather than separately proving each removed dependency irrelevant through a one-variable deletion sequence.

| Minimal control | Role first | User first |
| --- | --- | --- |
| Cold shared method-group cache | Role100/User50 | Role50/User100 |
| Independent helper warms the shared cache | Role50/User50 | Role50/User50 |
| Explicit `new Func<Flag, Dto>(ToDto)` in both wrappers | No visible projection cache branch | No visible projection cache branch |

Every control calls each wrapper 16 times, returns the expected single DTO and observes completed tasks. The cold/warm controls use the same binary and vary only process arguments. The fresh-delegate methods live in the same binary and preserve the await and mapper while replacing implicit cached conversion with explicit delegate construction. Their uninstrumented IL contains the await conditional but no cache conditional. The invoked fresh wrappers have positive line coverage; uncalled cached wrappers remain at zero.

In the minimal uninstrumented IL, both cached wrappers and the warm helper use field **0x0400002E**. The wrappers' cache conditional at offset 122 maps to visible projection lines 84/89; the await conditional at 47 maps to hidden sequence point 40. Source-bound originals retain these historical line numbers if the runner is later improved.

## Formats, provenance and limits

Six original native `.coverage` collections were retained and each exported to detailed XML and Cobertura without changing its SHA-256. Those exports contain positive line/block observations but **no branch rows** in these controls. Six separately declared direct-Cobertura executions expose the branch rows above. The direct reports are not conversions of the native executions. The documented tool supports these collection/export formats; the observed native-export limitation does not establish a general conversion defect or an undocumented configuration switch. [Microsoft: dotnet-coverage](https://learn.microsoft.com/en-us/dotnet/core/additional-tools/dotnet-coverage).

The current canonical product build is bound to source `1fa1664c604e0b848bc1c4ab4ae36a47c0a6d56e` and xUnit MTP 4.0.2. Historical #405 pair 3 used source `2b5e50920fbad7e426d64d3236859faee6bf4b75` and xUnit 4.0.1, with missing after-bindings. The current causal result explains the matched compiler pattern; it cannot recover or prove the historical missing bindings. The original rejection and every old report stay intact.

The original real-prewarm context inherited an inaccurate minimal scope/native collection-format label; its command arguments and actual artifacts establish actual AdminService/direct-Cobertura execution. That original context is retained unchanged. Its before/after guard includes compiled diagnostic dependencies, but not the diagnostic source file itself. Its runtime byte guard inventories NETCore only, while the canonical runtimeconfig also references ASP.NETCore; the future runner captures every shared framework and runtime host. The future runner also adds the diagnostic source guard without retroactively upgrading the old record. Early minimal runner/source snapshots were recovered and SHA-verified against their original before-bindings after collection, retained separately, and do not rewrite the original contexts.

## Evidence and bounded outcome

Publication of the [raw evidence archive](https://github.com/jwh3times/apexracers/releases/download/v14.0.14/backend-async-416-evidence.tar.gz) and [SHA-256 sidecar](https://github.com/jwh3times/apexracers/releases/download/v14.0.14/backend-async-416-evidence.tar.gz.sha256) is pending release closeout. The archive preserves originals; its digest is kept outside its contents.

Machine-readable results, exact command exits/times, canonical mapping and all retained artifact hashes are in [backend-async-416.json](../testing/backend-async-416.json). Original local roots:

- `TestResults/async-416-real-role-first-1` and `async-416-real-user-first-1`: same-binary real query RED.
- `TestResults/async-416-real-warm-original-1`: actual compiled cache prewarm pair.
- `TestResults/async-416-minimal-original-1`: six native controls, same-native exports and uninstrumented IL/PDB.
- `TestResults/async-416-minimal-direct-original-1`: six direct-Cobertura controls.
- `TestResults/async-416-mapping-original-1`: canonical product IL/PDB mapping.
- `TestResults/async-416-feedback-red-1`: explicit comparison failure.

#416's bounded attribution is complete for these current focused controls. A confirmed upstream collector defect was not demonstrated, so there is no required upstream submission or human action. Any future proposal to accept process-topology equivalence needs its own reviewed coverage model and measurements; this diagnosis supplies neither an exclusion nor permission to relax exact comparison or the independent runtime budget.
