# Driver publication safety: coarsening and release composition

Reviewed 2026-10-02 for #361. Public-safe engineering research against primary sources.
Everything labelled **proposed** below is a candidate for evaluation, not a product decision,
implemented behavior, anonymity proof, or finding about provider permission.

The [accepted architecture](../design/driver-disclosure-ownership.md) subsequently selects a fixed
whole-cohort hierarchy, group-range-only initial aggregates, and atomic combined-release admission.
The candidate discussion below is historical research; the decision brief owns selected policy
and the remaining implementation/admission evidence obligations.

## Evidence and limits

Sweeney defines k-anonymity over the **joint** quasi-identifier values of released records,
not separate minimum counts for each column. The paper also demonstrates matching through
row order, complementary releases, and temporal changes. Correctly identifying the
quasi-identifiers is an assumption of the model, not something the minimum count establishes.
[Sweeney, 2002, sections 3-4](https://dataprivacylab.org/dataprivacy/projects/kanonymity/kanonymity.pdf).

The original l-diversity paper demonstrates that k-anonymous groups can still disclose
attributes through homogeneity or background knowledge. Five interchangeable records need
not mean five plausible identities after an observer applies additional knowledge.
[Machanavajjhala et al., 2006, section 1.1](https://www.cs.cornell.edu/people/dkifer/papers/ldiversity.pdf).

NIST describes linkage, composition, and repeated-release risks; its example shows that even
count ranges can disclose an arriving individual's category when a range changes. It recommends
release-risk assessment and re-identification testing rather than treating aggregation as a
formal guarantee. [NIST SP 800-188, sections 4.3.8-4.3.12 and 4.6](https://nvlpubs.nist.gov/nistpubs/SpecialPublications/NIST.SP.800-188.pdf).

Dinur and Nissim show reconstruction from sufficiently accurate subset-sum answers in their
statistical-database model. This is evidence against assuming many accurate aggregates are
automatically safe, not a numerical noise prescription for racing data.
[Dinur and Nissim, PODS 2003](https://systems.cs.columbia.edu/private-systems-class/papers/DinurNissim2003Revealing.pdf).

The Census Bureau describes complementary suppression: additional cells must be withheld
when published cells would otherwise recover a primary suppressed value. Suppressing a
small cell alone leaves totals available for subtraction.
[Census Bureau, cell suppression](https://www.census.gov/topics/business-economy/disclosure/about.html).

## Proposed threat model

Treat externally available named race results, standings, screenshots, and an observer's own
race knowledge as possible auxiliary information. Exact Race references, timestamps, finish
positions, ordered lap sequences, and rating changes can supply joins even without a Customer ID.
This is a racing-specific inference from the linkage research, not verification of a particular
external dataset's accessibility. Public availability also does not establish republication rights.

Removing names and upstream identifiers from internal evidence caches would not resolve this
response risk. Opaque references are appropriate only for eligible identified Drivers; hidden
records must not receive persistent Driver handles, hash aliases, source-derived cursors, or
stable row ordering that permits longitudinal joins.

## Proposed bounded production candidate

The accepted starting policy is 0.5-second lap bands; 250-point rating bands from 1000 inclusive
to 2500 exclusive, and 500-point bands outside that interval; and joint support of at least five
**distinct hidden Drivers**, excluding the owner and currently disclosed Drivers. These are
project inputs, not source-backed anonymity thresholds. The prototype's coarsening ladder is
not prescribed as production policy.

1. **Resolve the complete cohort.** Canonicalize an allowed query, load its full filtered cohort
   before pagination, and separate synthetic provenance. Resolve owner/disclosed eligibility
   against the authoritative authorization state. Count one contribution per Driver for the
   specified metric; multiple laps or races must not manufacture support.
2. **Construct the release vector.** Use lower-inclusive, upper-exclusive intervals with fixed
   origins: lap lower bound `floor(seconds / 0.5) * 0.5`; rating intervals aligned to 1000 below
   the central range and 2500 above it. Keep absent/sentinel measurements separate. Include
   every retained attribute, query context, ordering signal, and reference in the linkage review.
   Exact finishes or race identity cannot be ignored just because only lap/rating were banded.
3. **Count joint support.** Group hidden Drivers by the entire proposed public vector. A lap
   band with five Drivers and a rating band with five different Drivers do not satisfy a joint
   cell with one Driver. Owner and disclosed observations cannot pad a hidden cell.
4. **Try a finite hierarchy.** Configure a reviewed sequence of parent partitions: merge adjacent
   lap/rating intervals, remove additional attributes, then broaden context where the metric
   remains meaningful. Generalize whole sibling partitions together, never silently retaining
   finer siblings beside a parent containing them. Recompute support at each level; terminate
   at the configured root. If support or linkage review still fails, suppress the output.
5. **Choose a non-enumerating representation.** Prefer one summary per approved cell. Repeating
   the same band once per hidden Driver still publishes its exact cardinality. Do not emit hidden
   Driver cursors, exact cell counts, leftover totals, source positions, or pagination metadata
   that reconstructs them. Count ranges also require review; no safe range width is selected here.
6. **Evaluate the combined release.** Check parent/child totals, sibling filters, disclosed exact
   rows, owner variants, prior snapshots, and other endpoints together. Apply complementary
   suppression where arithmetic reveals withheld cells. Recheck eligibility before response;
   an earlier release version must never authorize a withdrawn identity.
7. **Fail closed and paginate last.** Unknown provenance, unreviewed filters, unavailable policy
   state, unresolved linkage, or exhausted hierarchy produce a generic unavailable representation.
   Do not expose the failing support count or hierarchy depth. Paginate only the approved
   representation; pagination must not recalculate privacy decisions.

## Proposed release options and remaining blockers

**Option A: bounded hierarchy plus a fixed query catalog and risk gate.** Publish only reviewed
cohort/metric combinations with versioned partitions and an internal release manifest covering
overlaps and changes. Refuse arbitrary Customer ID predicates and ad hoc intersections over hidden
data. Test the complete catalog against external linkage and prior releases before admission.
This limits the attack surface but neither a finite catalog nor complementary suppression proves
anonymity. Refusals and transitions can themselves become observations.

**Option B: aggregate-only hidden contribution.** Omit hidden rows entirely and retain only approved
cohort statistics alongside eligible Driver disclosures. This removes row linkage and repeated-row
counts, but exact extrema, totals and deterministic summaries remain subject to differencing.
For example, known total and known published contributions can recover a hidden remainder; two
cohorts differing by one known participant can reveal that participant's contribution. Repeating
an unchanged deterministic answer adds no new equation, but overlapping queries and new snapshots do.

Both options need a specified protected unit, auxiliary-information model, permissible release
catalog, hierarchy/root, count representation, temporal release policy, and adversarial validation.
A rating or lap crossing a stable band boundary, a withdrawal, or late ingestion can change the
output and link membership across versions. Snapshot freezing alone cannot undo earlier disclosures.
An exact Race classification whose positions match a named external classification should fail
the hidden-row gate unless an assessed representation removes that link. If no useful safe output
is established, withhold that context. A formal privacy mechanism and its parameters would require
a separate decision; this note selects neither an epsilon nor a differential-privacy mechanism.
