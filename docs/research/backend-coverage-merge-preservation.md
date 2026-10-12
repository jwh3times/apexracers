# Backend coverage preservation investigation (#408)

## Scope and current decision

The existing general `Test` job, direct Microsoft Testing Platform Cobertura collection,
product exclusions, and positive-denominator 85% line and branch gates remain authoritative.
The Driver Privacy Contract workflow, receipt, 43-row matrix and collector remain unchanged.
This investigation does not establish the #405 timing targets or adopt its proposed topology.

The repository pins `Microsoft.Testing.Extensions.CodeCoverage` 18.12.0. The rejected
[#405 preflight](backend-coverage-shard-prototype.md) used `dotnet-coverage` 18.12.0
and ReportGenerator 5.5.11. Its original reports and failures remain evidence; format
support alone does not prove preservation of source, method, line or individual branch identities.

## Primary-source findings

Microsoft's [merge documentation](https://learn.microsoft.com/en-us/dotnet/core/additional-tools/dotnet-coverage#dotnet-coverage-merge)
supports native `.coverage`, Microsoft XML and Cobertura inputs. That is a format capability,
not a guarantee that every input contains the information needed for this branch gate.

| Path | Maintainer evidence | Consequence for this gate |
| --- | --- | --- |
| Native `.coverage` to Cobertura | [#171 clarification](https://github.com/microsoft/codecoverage/issues/171#issuecomment-2820374306) says the native format does not store branch information. | Export cannot reconstruct missing individual branch observations. |
| Microsoft XML to Cobertura | [#147 clarification](https://github.com/microsoft/codecoverage/issues/147#issuecomment-2514102960) distinguishes Cobertura branches from XML/native block coverage. | Block coverage is a different model; substituting it changes the contract. |
| Cobertura reports merged after collection | [#11 explanation](https://github.com/microsoft/codecoverage/issues/11#issuecomment-1457935172) describes ambiguous partially covered switch conditions being removed. [#104 explanation](https://github.com/microsoft/codecoverage/issues/104#issuecomment-1991051175) demonstrates the same ambiguity for partially covered jumps. | Equal counters cannot identify whether different runs exercised the same or opposite branch. |
| Microsoft XML block merge | [#217 recommendation](https://github.com/microsoft/codecoverage/issues/217#issuecomment-4168665594) says XML can also lack enough block information for a complete merge and recommends native intermediates for block coverage. | That recommendation addresses blocks, not restoration of Cobertura branches. |

For example, two runs reporting one covered side of the same two-way jump may represent
either the same side twice or both sides together. Both inputs can say `50% (1/2)` and use
the same condition number. A merge cannot distinguish those executions from those fields.
Reconstructing a denominator or taking the maximum percentage would leave that ambiguity intact.

The complete [configuration reference at commit 9360f52d](https://github.com/microsoft/codecoverage/blob/9360f52ded3127f33f8558c37e12e54c1c1a0b28/docs/configuration.md)
documents report formats, symbol search and instrumentation settings. It does not document a
native/XML branch-identity retention switch. Symbol lookup helps instrument modules; it is not
documented as restoring missing execution observations during export. This is a bounded finding
about the reviewed supported paths, not a claim about unpublished internals or future releases.

The newer multi-application MTP artifact processor also needs care. The maintainer records its
implementation in [#234](https://github.com/microsoft/codecoverage/issues/234#issuecomment-5426949378),
and the [SDK validation PR](https://github.com/dotnet/sdk/pull/55940) verifies consolidation of
native coverage artifacts while retaining originals. That evidence does not demonstrate this
repository's Cobertura branch model or merge coverage across isolated hosted runners.

## Supported shared-session path and focused validation

The maintainer's [#104 workaround](https://github.com/microsoft/codecoverage/issues/104#issuecomment-1991051175)
is materially different: execute all tests inside one `dotnet-coverage collect` collection.
The collector retains additional information about which condition side was exercised before
writing the final report. This is the supported path under investigation; it avoids exporting and
then attempting to reconstruct a union from incomplete native/XML or Cobertura observations.

Microsoft documents both [command-mode collection and server sessions](https://learn.microsoft.com/en-us/dotnet/core/additional-tools/dotnet-coverage#sessions).
A collector can supervise a command and its managed subprocesses, or a collection server can
accept multiple `connect` clients and write a collective report after shutdown. The documented
server/client example does not establish cross-runner network collection. The #405 issue requires
independent containers, databases, processes, ports, artifacts and cleanup; its rejected proposal
implemented that isolation with separate hosted runners. A shared VM and collector therefore need
an explicit topology review, including shared CPU contention and the collector failure boundary.

A candidate experiment would launch two independently configured test processes under one
collector on one runner, preserving complete/disjoint whole-class inventories, GUID databases,
independent containers, dynamic ports, primary/history separation, real HTTP and physical restores,
separate files and original outcomes. It must avoid nested coverage collectors and use the same
product binaries, symbols and exclusions as a direct-MTP control. Dynamic collection of child
processes may change the product model; that possibility must be measured and rejected when
incompatible. No common container, schema, seed, host or connection pool may replace these boundaries.

The pinned [configuration reference](https://github.com/microsoft/codecoverage/blob/9360f52ded3127f33f8558c37e12e54c1c1a0b28/docs/configuration.md)
also identifies wrapper differences to verify: child-process collection defaults to enabled, while
test-assembly and missing-source defaults differ between the tool and the MTP extension. Preserve
the existing explicit module/function/attribute exclusions, retain the actual effective settings,
and require all expected product modules. Run the compiled test application without its `--coverage`
option when an outer collector owns collection; enabling both would create nested collectors rather
than the proposed single authority. This command choice follows the documented independent collector
and test-runner interfaces; no unsupported internal disabling flag is assumed.

The focused investigation makes three documented MTP defaults explicit in the outer collector's
effective settings: `IncludeTestAssembly=False` and `ExcludeAssembliesWithoutSources=MissingAll`
under `Configuration`, and `UseVerifiableInstrumentation=False` under `CodeCoverage`. These are
derived probe settings; the production `coverage.runsettings` and workflow stay unchanged. The
fourth outer-collection probe runs the same 25 representative cases as its direct-MTP control and
matches the reported product model: 4,991 valid lines, 2,637 valid branches, 17 covered lines and
two covered branches. The earlier three probes admitting 13 modules retain their original reports
and failed compatibility results. Since all three defaults changed in one trial, the result does
not establish which individual setting caused the difference.

A separate eight-case controlled MTP expression fixture checks discriminating branch behavior.
Its shared collection reports three covered branches out of five when processes exercise overlapping
but different paths, and two out of five when the path is repeated. This distinguishes a union of
observed paths from summing counters or merely treating repeated execution as new coverage. Earlier
statement-only controls produced no branch denominator and are retained as non-discriminating probes.
These focused results establish feasibility for those inputs; matching totals alone remains
insufficient to establish the full product source, line, condition and branch model. A full-product
comparison bound to one unchanged committed build is still required before closing #408.

Microsoft's [instrumentation reference](https://github.com/microsoft/codecoverage/blob/9360f52ded3127f33f8558c37e12e54c1c1a0b28/docs/instrumentation.md)
explains that static instrumentation changes DLL/PDB files and normally restores them after the
process finishes. Independently copied build bytes are consequently important if any candidate uses
static instrumentation. The documented `connect` command uses dynamic instrumentation for subprocesses.
Do not assume that copying build files or inheriting profiler environment variables alone proves the
effective instrumentation mode; retain settings and before/after file bindings.

Before adoption, the new topology needs a separate isolation and cancellation review, exact source,
line and condition-model comparison to its unchanged full-suite baseline, branch-side preservation
probes, product module completeness, and the original matched hosted timing comparisons. Sharing
one collector also creates a common failure boundary. This candidate is not the original two-runner
artifact-merge proposal, and no performance result follows from its documented availability.

## Conservative fallback

If the shared-session path cannot establish a compatible model, retain the original full-suite
direct-MTP coverage run as the mandatory coverage authority. Any isolated plain test shards would
be additional diagnostic jobs; their results cannot replace that coverage gate, and their coverage
percentages cannot be averaged or merged by guessed unions. Adding diagnostics would consume more
runner time and would not satisfy #405's <=20 minute general gate by itself.

Changing collectors, branch definitions, exclusions, thresholds, mandatory full-suite coverage,
or required status contexts would require separate review. No license purchase, production
configuration change or human credential action is selected by this investigation.
