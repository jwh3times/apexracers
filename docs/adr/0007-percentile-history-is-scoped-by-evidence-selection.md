# Percentile history is scoped by evidence selection

Status: **accepted by the maintainer on 2026-10-14; implementation pending**.

The maintainer accepted all five policies following the [#352 investigation](../design/percentile-history-ownership.md).
Stored ranks can otherwise describe a different evidence selection from the displayed Personal Best,
and independent writers can disagree about history ownership. We choose separate current histories
by canonical Lap Evidence selection rather than freezing original readings or overwriting a single
history whenever the selection changes. This costs additional bounded readings and coordinated
storage/copy identities, but keeps each history coherent without making toggles destroy another
selection's readings.

The accepted policies are:

1. **Selection-scoped history.** Each User/current Driver/provenance scope, Car, Series, Race Week
   and canonical evidence selection has one current, version-checked reading. Refresh replaces that
   Week's contribution; it does not append another contribution. Practice-inclusive rank 90 and
   Race-only rank 50 may coexist in their respective histories.
2. **Canonical, versioned selection meaning.** Preserve current request equivalences: uploads off
   ignores upload-type arguments; uploads on with null/empty types is unrestricted; restricted types
   form an order-independent set with existing Unknown inclusion. Unrestricted is distinct from an
   explicit list of today's types. Newly supported types enter unrestricted through a reviewed
   selection-policy version change; affected readings are invalidated/recomputed, never silently
   reinterpreted. This approves no unknown upstream taxonomy or new wire mapping.
3. **Expected Percentile through the requested Week.** Average eligible Weeks in the same Series
   and selection whose StartDate is at or before the requested Week's StartDate, including the
   requested Week if eligible. Each distinct Week identity contributes once, including across
   Seasons. Earlier rank 50 plus requested rank 30 yields 40; a later-Week rank 90 is excluded.
4. **Missing evidence contributes nothing.** A Week without an eligible Personal Best is omitted
   from percentile history and Expected Percentile rather than retaining a stale reading. With only
   earlier rank 50 eligible, the result is 50; with no eligible Weeks it is absent, not zero. A Field
   below five likewise contributes no headline percentile, although diagnostic arithmetic may exist.
5. **Honest transition for unclassified rows.** Exclude old selection-less readings from new-policy
   results. Recompute lazily or in bounded authorized batches only when current scope, consent and
   retained sources permit it; otherwise leave history absent. Coordinate old-writer fencing with
   cutover, explicitly classify fresh seed output, and preserve original source/copy retention clocks.
   Do not reacquire evidence or promote quarantined data merely to populate history.

The shared module owns eligibility, owner resolution, Field Size, replacement/removal, source
versions and coherent aggregation. Recommendation Rank ordering, presentation and world-record
overlays stay outside. Exact Track identity and Race Week boundaries remain unchanged.

Acceptance of these product policies grants no Live access. Existing proof/consent, Demo provenance,
source preparation, copy lifecycle, withdrawal, deletion, restore and protected-dispatch obligations
remain mandatory. The typed private-upload producer, synthetic Seeder and ordinary history writers
must participate in coordinated implementation. This ADR records the decision, not shipped behavior.
