# Passive World Balance Observatory Design

Date: 2026-07-27  
Status: Approved design  
Initial acceptance horizon: One Reign year (126 campaign days)

## Purpose

Extend World Test into a scalable, read-only observatory for Reign's passive
systems. It must support maximum-speed campaigns without competing with the
systems it measures, establish a baseline before imposing balance targets, and
eventually compare three independent campaigns at one-, three-, and five-year
checkpoints.

The first release is deliberately staged. One fresh shakedown campaign runs for
one Reign year before additional campaigns or longer runs are attempted.

## Goals

- Verify that passive systems continue processing correctly at maximum game
  speed and catch up after temporary lag.
- Measure long-term distributions, rates, funnels, concentration, and
  saturation rather than relying on isolated event totals.
- Distinguish simulation lag from observability lag.
- Explain anomalies with bounded diagnostic evidence without retaining every
  roll or state transition.
- Preserve campaign and Save Sync timeline isolation.
- Establish a three-campaign baseline before defining balance warnings.
- Keep test instrumentation removable from a release build without removing
  the core passive-system processing architecture.

## Non-goals

- Changing rumor eligibility, exposure, promotion, correction, storage, or UI
  behavior.
- Changing relationship compatibility or progression rules.
- Driving, retrying, injecting, resetting, or otherwise mutating campaign state
  from World Test.
- Treating globally known world events as rumors.
- Retaining every relationship roll, intermediate affinity, native receipt, or
  polling callback.
- Full vanilla economy, party AI, or tournament analytics.

## Systems in Scope

1. Passive NPC relationships and native relationship projection.
2. Organic romance, affairs, family lifecycle, and romantic Reign marriages.
3. Arranged marriages outside ruler diplomacy.
4. Diplomatic marriage packages and other ruler-driven diplomacy.
5. The Social Reputation rumor pipeline:
   public-event eligibility, exposure, temporary rumor tags, streaks,
   promotion, durable reputation, corrections, expiration, and consequences.
6. Relationship-triggered rebellions and civil-war resolution.
7. Supporting action, ingestion, retention, compaction, Save Sync, and native
   synchronization pipelines.
8. Selected vanilla world totals used only to validate Reign consequences.

Player-only reputation, court choices, player-triggered encounters, and native
arena/tournament systems do not contribute to passive-system success totals.

## Design Principles

### Authoritative ledgers remain authoritative

World Test reads the existing relationship, family, diplomacy, rumor,
rebellion, action, and history stores. It does not copy raw events into a
parallel event store.

### Observe changes where they are already calculated

The relationship worker already reads an old pair state and writes a new one.
The same transaction emits a compact counter delta. World Test must not replay
daily relationship work or rescan the complete relationship table to discover
what changed.

### Store populations and funnels, not individual routine events

Routine processing produces integer counters, distributions, and duration
histograms. Detailed evidence is kept only for consequential outcomes, errors,
contradictions, bounded samples, and statistical outliers.

### Observability cannot block gameplay

Rollup and report generation run independently of passive simulation.
Instrumentation lag is visible but cannot fail a relationship, rumor,
diplomacy, rebellion, or family action.

### Every rate has a denominator

Counts such as marriages, promotions, or rebellions are always presented with
the eligible opportunities or evaluations from which they arose.

## Evidence Layers

### 1. Current authoritative state

Use one current authoritative row or ledger entry for each:

- Directional relationship pair and lifecycle state.
- Rumor occurrence, subject tag, exposure streak, promotion result, and durable
  reputation.
- Diplomatic event and action.
- Rebellion, membership, and outcome.
- Family lifecycle state.
- Coalesced native synchronization target.

### 2. Transactional chunk counters

High-volume workers emit idempotent summaries keyed by:

`campaignId + timelineId + campaignDay + subsystem + chunkKey`

For relationships, a chunk summary includes:

- Eligible and evaluated pair counts.
- Positive and negative directional outcomes.
- Signed affinity movement totals.
- Band entries and exits.
- Tag entries and exits.
- Lifecycle evaluations and outcomes.
- Native targets created or replaced.
- Processing duration and evaluated units.

The summary is committed atomically with the authoritative state changes.
Failure rolls back both. Retrying the same chunk replaces or ignores the same
idempotency key and cannot double-count it.

Lower-volume systems may update equivalent compact counters in their normal
authoritative transaction.

### 3. Compact daily rollup

One rollup is stored for each campaign, timeline, and completed campaign day.
It combines current distributions, transactional counters, subsystem health,
performance, and storage diagnostics.

Rebuilding a day replaces that rollup. It never adds duplicate cumulative
totals.

### 4. Bounded diagnostic evidence

Detailed evidence is limited to:

- Consequential events already retained by the authoritative system.
- Terminal failures and server/native contradictions.
- Stalled or overdue work.
- Invalid invariants, such as a related romantic pair or player participation
  in an automatic rebellion roll.
- A configurable representative sample per subsystem and day.
- Outliers such as extreme per-NPC relationship or reputation concentration.

Routine diagnostic samples use a rolling retention window. Their aggregate
counts remain after the detailed payload expires.

### 5. Cached checkpoint reports

Checkpoint reports are generated asynchronously from daily rollups and current
authoritative state. UI refreshes and exports read cached reports rather than
recomputing years of campaign data.

## Incremental Exact Distributions

Relationship band and tag populations must be exact while remaining cheap.

- When an edge changes band, decrement the old band and increment the new band.
- Maintain per-character membership counts for every band and tag.
- A character enters a unique-NPC total when membership changes from zero to
  one and exits when it changes from one to zero.
- Maintain pair counts separately from directional-edge counts.
- Apply tag changes using the same zero-to-one membership method.
- Record daily entries and exits only as counters.

This avoids a full relationship-table scan at daily completion.

## Time and Lag Model

World Test displays these clocks separately:

- Latest game-observed campaign day.
- Latest ingested input day for each producer.
- Latest completed subsystem day.
- Partial-day cursor where applicable.
- Latest completed World Test rollup day.
- Latest native-confirmed projection day or oldest target age.

The dashboard must never infer that relationship simulation is broken merely
because World Test rollups are behind. Conversely, a current rollup must not
hide a simulation backlog.

Pausing the game may allow workers to catch up, but correctness does not depend
on pausing.

## Metrics

### Relationships

- Latest ingested/completed day, queued days, queued pair-days, throughput, and
  estimated catch-up time.
- Eligible characters, co-presence groups, eligible pairs, evaluated pairs,
  and newly introduced pairs.
- Unique NPCs, directional edges, and pairs in every affinity band and tag.
- Daily aggregate band/tag entries and exits.
- Mean, median, percentiles, minimum, and maximum directional affinity.
- Positive/negative roll rate and average signed movement.
- Symmetric and strongly asymmetric pair counts.
- Noble-noble, noble-notable, and notable-notable cohorts.
- Time-to-band and time-to-tag histograms from first eligible exposure.
- Per-NPC close-friend, enemy, lover, devoted, bonded, and nemesis
  concentration.
- Native target count, application rate, oldest age, lag, failures, retries,
  and contradictions.

### Romance and family

Organic romance, arranged marriage, and diplomatic marriage are reported as
three distinct funnels.

- Eligible evaluations and exclusions.
- Interest/compatibility successes.
- Courtships, lovers, active affairs, discovery, betrayal, estrangement, and
  separation.
- Romantic Reign marriages.
- Arranged marriages outside ruler diplomacy.
- Diplomatic marriage packages.
- Accepted, refused, failed, invalid, and abandoned attempts.
- Time from eligibility to relationship and marriage.
- Conceptions, failed pregnancies, births, concealed parentage, revelations,
  and linked rumors.
- Related-character or other invalid-romance violations.

### Rumors and durable reputation

These metrics instrument the existing Social Reputation behavior without
changing it.

- Eligible public-event rumor hooks.
- Exposure opportunities, attempts, passed rolls, failed rolls, and suppressed
  duplicates.
- Occurrences by archetype and provenance.
- Temporary subject tags by subject and role.
- Exposure streak and shared-tag streak distributions.
- Promotion attempts and outcomes.
- Temporary-rumor-to-durable-reputation conversion rate.
- Expiration, correction, disproval, verification, and counterevidence.
- Time from event to exposure, promotion, correction, or expiration.
- NPCs with zero rumors, active rumors, expired rumors, or durable
  reputations.
- Per-NPC, archetype, kingdom, culture, and source distributions.
- Reputation concentration and runaway accumulation.
- Relationship, family, political, and social consequences linked to their
  source occurrence.

Globally known world-history events are displayed separately and never counted
as rumor occurrences solely because they are public.

### Diplomacy

- Due and completed evaluations, legitimate no-action results, missed cadence,
  and evaluation latency.
- Attempts by intent family and command.
- Accepted, refused, countered, invalid, queued, completed, failed, overdue,
  and abandoned outcomes.
- Actor and target totals by kingdom and ruler.
- Wars, peace, treaties, alliances, tribute, guarantees, prisoner exchanges,
  transfers, and diplomatic marriage packages.
- Repetition between the same participants and concentration by actor/target.
- Time from evaluation to announcement and native application.

### Rebellions

- Weekly eligible kingdoms and leaders.
- Persisted rolls, successes, failures, and exclusion reasons.
- Observed success rate, sample size, and confidence interval.
- Outbreaks, participating clans, side selection, duration, resolution, and
  cleanup.
- Repeated outbreaks and concentration by kingdom.
- Player inclusion violations.
- Native rebel-kingdom, war-state, clan-transfer, ruler, reunification, origin
  restoration, and cooldown confirmation.

### Pipeline, retention, and storage

- Pending, overdue, failed, and orphaned actions by subsystem, including
  Relationship Director actions.
- Producer cadence, last success, and last worker error.
- Client outbox and server ingestion backlog.
- Relationship and native projection lag.
- Campaign/timeline continuity and Save Sync alignment.
- Database and WAL bytes and growth per campaign day.
- Raw events by retention class, oldest retained raw day, prune rate,
  compaction backlog, and compacted totals.
- Rollup latency and World Test API response latency.
- CPU and memory observations where available.

### Native context

Report vanilla kingdoms, clans, living nobles, wars, marriages, pregnancies,
births, deaths, ruling-clan changes, and settlement ownership changes only as
contextual validation of Reign outcomes.

## Health Versus Balance

### Immediate technical health

Technical invariants can produce warnings or errors from the first day:

- Missing or invalid permanent MBTI assignment.
- Missed or duplicate eligible pair-day processing.
- Increasing relationship backlog or lag of at least one campaign day.
- Failed or contradicted native application.
- Diplomacy cadence failure.
- Missing weekly rebellion roll set after seven eligible campaign days.
- Terminal action failure.
- Save/timeline reversal without a branch/reset.
- Unbounded storage/WAL growth or stalled compaction.
- Instrumentation lag of at least one completed campaign day.
- Broken aggregate-to-source reconciliation.

### Baseline-first balance

During the first three campaigns, relationship distributions, rumor
prevalence, marriage rates, diplomatic pacing, and rebellion frequency remain
informational. Sample size and confidence are displayed.

The observed baseline is not automatically declared desirable. After the
three-campaign dataset is complete, explicit design review converts selected
measurements into documented target ranges and warnings.

## Staged Test Methodology

### Stage 1: One-year shakedown

Run one fresh campaign at maximum campaign speed to day 126.

Review gates:

- Day 1: initialization, permanent MBTI import, and producer registration.
- Day 7: first weekly rebellion cycle and backlog behavior.
- Day 31.5: first Reign season and early distributions.
- Day 63: midpoint accumulation, storage, and performance.
- Day 126: complete one-year report.

Stop early for timeline corruption, skipped/duplicated processing, runaway
storage, unrecoverable backlog, broken rollup accounting, repeated terminal
failures, or invalid passive outcomes.

At a review gate, campaign time may be paused while completed-system and
rollup days reach the gate. Failure to catch up is recorded rather than hidden.

### Stage 2: One-year replication

After the shakedown passes, run two additional fresh campaigns to day 126.
Compare all three campaigns individually and together.

### Stage 3: Three-year extension

Proceed to day 378 only after the one-year baseline is technically sound.
Review each campaign and the combined dataset.

### Stage 4: Five-year extension

Proceed to day 630 only after the three-year results pass technical review.

## Reproducibility Manifest

Each test run records:

- Reign mod and server build identifiers.
- Campaign and timeline IDs.
- Feature-toggle and configuration hashes.
- Compatibility matrix and personality-template versions.
- Rumor archetype/schema version.
- Diplomacy model/provider configuration without credentials.
- Worker, batch, retention, and compaction settings.
- Campaign start day and world seed where available.

## Checkpoint Reports

Each report contains:

- Current distributions.
- Cumulative totals and eligible denominators.
- Per-season and per-year normalized rates.
- Changes since the prior gate.
- Processing, synchronization, and rollup health.
- Bounded anomaly evidence and authoritative source links.
- Database, WAL, memory, CPU, and latency measurements.
- JSON export and selected CSV tables.

The comparison report shows individual campaigns, combinable totals,
per-campaign minimum, maximum, mean, median, sample size, confidence intervals,
and outliers.

## World Test UI

### Fixed header

- Campaign/timeline selector.
- Build/configuration manifest.
- Game, ingested, completed-system, and rollup days.
- Current stage and next review gate.
- Overall technical health.
- Catch-up estimates.
- Refresh, JSON export, and current-table CSV export.

### Cards

Provide separate cards for:

1. Relationships.
2. Romance and Family.
3. Rumors and Reputation.
4. Diplomacy.
5. Rebellions.
6. Pipeline and Storage.
7. Native World Context.

Each card shows health, totals, normalized rates, change since the prior gate,
anomalies, and a drill-down table.

### Rumor inspector

Provide a bounded, scrollable inspector with:

- Archetype and provenance.
- Subject and relevant participants.
- Exposure attempt and result.
- Temporary tags and streaks.
- Promotion or durable-reputation result.
- Correction, expiration, or consequence.
- Campaign day and source-ledger reference.

World events appear in a separate context table.

### Checkpoint and baseline views

The shakedown view compares days 1, 7, 31.5, 63, and 126. Later stages add days
378 and 630. After replication, the baseline view presents all three campaigns
side by side and combined where valid.

Technical failures and balance observations use separate visual states.

### Strictly observational

World Test contains no force-run, retry, injection, reset, deletion, or
simulation-speed controls.

## Performance Budgets

- Less than 5% relationship-worker overhead from instrumentation.
- No per-pair diagnostic files.
- No routine full relationship-table scan at daily completion or UI refresh.
- Daily rollup completes within five real-time seconds after its underlying
  subsystem completes the day.
- World Test overview responds within 500 milliseconds on a five-year
  campaign.
- Storage growth is proportional to campaign days and consequential events,
  not eligible pair-days.
- No reduction of the adaptive relationship worker's intended 10,000-plus
  pair-day capacity.
- Instrumentation lag of one completed campaign day raises a warning.

## Recovery and Reconciliation

- Durable chunk summaries resume from the last committed cursor after restart.
- Duplicate ingestion and retries are idempotent.
- Daily rollups can be rebuilt from compact summaries without individual roll
  history.
- Periodic reconciliation compares sampled authoritative records with current
  counters and checkpoint totals.
- A failed observability worker records its error and retries independently.
- Save branches never read observations from another timeline.

## Acceptance Criteria

The initial implementation is ready for the one-year shakedown when:

- All in-scope producers register and report distinct clocks.
- Relationship instrumentation is atomic, idempotent, and incrementally
  maintains exact band/tag populations.
- Rumor instrumentation covers the exposure-to-reputation funnel without
  changing rumor behavior.
- Daily rollups and checkpoint reports can be rebuilt without duplication.
- UI reads rollups/materialized counters rather than high-volume raw state.
- Bounded diagnostic retention is enforced.
- Timeline isolation and source reconciliation tests pass.
- Performance budgets pass representative maximum-speed fixtures.
- The Reign MCP `changed` or required broader validation profile completes
  successfully and its authoritative report covers all changed projects.

The testing methodology itself succeeds when the shakedown campaign reaches
day 126 with complete checkpoint reports, bounded storage, recoverable or zero
processing lag, correct aggregate reconciliation, and no unresolved critical
technical failure.

