# Passive World Balance Observatory Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a scalable, read-only World Test observatory that measures Reign's passive relationships, romance/family, Social Reputation rumors, diplomacy, rebellions, pipeline health, and native context through a staged one-year-first campaign methodology.

**Architecture:** Passive systems update compact, idempotent counters in the same transaction as their authoritative state changes. A separate rollup worker merges those counters into one cached record per campaign timeline/day and cached checkpoint reports; the UI reads only materialized counters, daily rollups, and bounded diagnostic evidence. Rumor behavior and all other passive-system rules remain unchanged.

**Tech Stack:** C#/.NET Framework 4.7.2, Microsoft.Data.Sqlite, Newtonsoft.Json/JObject client payloads, the existing single-file Control Center HTML/CSS/JavaScript, and the canonical Reign MCP validation engine.

## Global Constraints

- The local workspace is authoritative; do not pull from or push to the outdated online repository.
- Preserve unrelated local and uncommitted work.
- Before runtime edits, call `mcp__reign.reign_get_validation_plan` with `profile="all"`, `configuration="Release"`, and `restore=false`.
- Never invoke `dotnet build`, `dotnet test`, MSBuild, VSTest, or project verification executables directly.
- After each task, use `mcp__reign.reign_validate` with `profile="changed"` and exact changed paths.
- At the final code gate, use `mcp__reign.reign_validate` with `profile="all"`, `configuration="Release"`, and `restore=false`.
- World Test remains read-only and contains no force-run, retry, injection, reset, deletion, or speed controls.
- Do not change rumor eligibility, exposure, promotion, correction, storage semantics, or presentation outside World Test.
- Globally known world events are not rumors.
- Do not store individual relationship rolls, intermediate affinities, native receipts, polling callbacks, or per-pair diagnostic files.
- Instrumentation must add less than 5% relationship-worker time, perform no routine full pair-table scan, finish a daily rollup within five real-time seconds of subsystem completion, and keep overview latency below 500 ms on a five-year fixture.
- Storage growth must follow campaign days and consequential events, not eligible pair-days.
- Do not launch a hidden/detached Reign server or an ordinary browser Control Center.

---

## File Structure

### New server files

- `ReignServer/WorldTestTelemetry.cs` — schema, idempotent counter writes, exact relationship category membership, bounded evidence, and test fixtures.
- `ReignServer/WorldTestRollupWorker.cs` — independent queue/worker, daily rollup finalization, checkpoint caching, reconciliation, and worker status.
- `ReignServer/WorldTestReports.cs` — checkpoint and cross-campaign comparison response builders.

### Existing server files

- `ReignServer/MbtiRelationships.cs` — emit relationship chunk deltas inside existing 500-pair transactions.
- `ReignServer/ContinuousRelationshipWorker.cs` — enqueue completed days and expose simulation-versus-rollup clocks.
- `ReignServer/SocialReputation.cs` — emit rumor funnel counters without changing decisions.
- `ReignServer/ExpandedSocialReputation.cs` — count corrections/counterevidence.
- `ReignServer/RelationshipLifecycle.cs` — classify organic romance/family funnel outcomes.
- `ReignServer/RelationshipDirector.cs` — classify arranged-marriage funnel outcomes.
- `ReignServer/WorldDiplomacyDirector.cs` — classify cadence, intent, proposal, and outcome counters.
- `ReignServer/WorldTest.cs` — replace synchronous full aggregation with materialized reads and extend details.
- `ReignServer/Program.cs` — routes and Control Center presentation.
- `ReignServer/VerificationLab.cs` — source-contract coverage for the new architecture.
- `ReignServer/ReignServer.csproj` — include new sources in copied verification contracts.

### Existing client files

- `ReignBeta/src/Integration/ReignWorldTestClient.cs` — reproducibility manifest, native context, producer clocks, and compact rebellion evidence.

## Shared Interfaces

Create these exact server-side interfaces in `WorldTestTelemetry.cs`:

```csharp
private static void EnsureWorldTestTelemetrySchema(SqliteConnection connection);

private static void RecordWorldTestCounter(
    SqliteConnection connection,
    string campaignId,
    string timelineId,
    int dayKey,
    string subsystem,
    string chunkKey,
    Dictionary<string, object> counters);

private static void RecordWorldTestEvidence(
    SqliteConnection connection,
    string campaignId,
    string timelineId,
    int dayKey,
    string subsystem,
    string evidenceKey,
    string severity,
    Dictionary<string, object> payload);

private static void ApplyWorldTestRelationshipPairDelta(
    SqliteConnection connection,
    string campaignId,
    string timelineId,
    int dayKey,
    Dictionary<string, object> before,
    Dictionary<string, object> after,
    Dictionary<string, object> chunkCounters);

private static void EnqueueWorldTestRollup(
    SqliteConnection connection,
    string campaignId,
    string timelineId,
    int dayKey,
    string reason);
```

`RecordWorldTestCounter` must be idempotent by the full primary key
`campaign_id,timeline_id,day_key,subsystem,chunk_key`. It stores counters only,
never a raw relationship or rumor event.

---

### Task 1: Durable telemetry foundation

**Files:**
- Create: `ReignServer/WorldTestTelemetry.cs`
- Modify: `ReignServer/ReignServer.csproj`
- Modify: `ReignServer/VerificationLab.cs`

**Interfaces:**
- Produces: all shared interfaces listed above.
- Produces tables: `world_test_manifests`, `world_test_chunk_counters`,
  `world_test_current_metrics`, `world_test_relationship_memberships`,
  `world_test_relationship_pair_memberships`,
  `world_test_diagnostic_evidence`, `world_test_rollup_queue`,
  `world_test_checkpoint_reports`.

- [ ] **Step 1: Add failing telemetry self-tests**

Add `RunWorldTestTelemetrySelfTests()` with fixtures asserting:

```csharp
add("counter_chunk_idempotent",
    CounterValue(connection, campaign, "main", 1, "relationships", "evaluatedPairs") == 500,
    "Replaying one chunk key does not double-count its 500 evaluations.");

add("timeline_counter_isolation",
    CounterValue(connection, campaign, "branch", 1, "relationships", "evaluatedPairs") == 37,
    "A branch retains counters independent of main.");

add("bounded_evidence_replaces_same_key",
    EvidenceCount(connection, campaign, "main", "relationships", "pair-a-b") == 1,
    "Repeated anomaly evidence replaces the same bounded record.");
```

Call the new test list from `RunWorldTestSelfTests()`.

- [ ] **Step 2: Validate the expected failure through Reign MCP**

Call `mcp__reign.reign_validate` with:

```json
{
  "profile": "changed",
  "configuration": "Release",
  "restore": false,
  "changedPaths": "ReignServer/WorldTestTelemetry.cs;ReignServer/WorldTest.cs;ReignServer/VerificationLab.cs;ReignServer/ReignServer.csproj"
}
```

Expected: the new telemetry contract tests fail because the schema and helpers
do not exist yet.

- [ ] **Step 3: Implement the schema and idempotent writes**

Use compact tables with these keys:

```sql
PRIMARY KEY(campaign_id,timeline_id,day_key,subsystem,chunk_key)
PRIMARY KEY(campaign_id,timeline_id,metric_scope,metric_key)
PRIMARY KEY(campaign_id,timeline_id,category_type,category_key,hero_id)
PRIMARY KEY(campaign_id,timeline_id,category_type,category_key,pair_key)
PRIMARY KEY(campaign_id,timeline_id,subsystem,evidence_key)
PRIMARY KEY(campaign_id,timeline_id,day_key)
PRIMARY KEY(campaign_id,timeline_id,checkpoint_day)
```

`RecordWorldTestCounter` uses `INSERT ... ON CONFLICT ... DO UPDATE SET
counters_json=$counters`; it must not add a replayed payload to an existing
payload. Counter summation occurs only when the rollup worker reads distinct
chunk rows.

`RecordWorldTestEvidence` stores at most 100 ordinary sampled records per
subsystem/day, while `warning` and `error` evidence replaces the same
`evidence_key` without that sampling cap.

- [ ] **Step 4: Add exact membership primitives**

Implement category membership changes with an `edge_count` column. Increment
when an edge enters a band/tag, decrement when it exits, delete at zero, and
update `world_test_current_metrics` only on zero-to-one or one-to-zero
transitions. Apply the same zero-to-one rule to the pair-membership table so a
pair whose two directional edges share a band or tag is counted once.
Keep `uniqueNpcCount`, `directionalEdgeCount`, and `pairCount` separate.

- [ ] **Step 5: Validate the foundation**

Run the same MCP `changed` validation. Expected: telemetry idempotency,
timeline-isolation, and bounded-evidence checks pass.

- [ ] **Step 6: Commit the task**

Stage only the four task files and commit:

```text
feat: add bounded world test telemetry foundation
```

---

### Task 2: Incremental relationship instrumentation

**Files:**
- Modify: `ReignServer/MbtiRelationships.cs:312-760`
- Modify: `ReignServer/ContinuousRelationshipWorker.cs:148-228`
- Modify: `ReignServer/WorldTestTelemetry.cs`
- Modify: `ReignServer/WorldTest.cs:403-603`

**Interfaces:**
- Consumes: `ApplyWorldTestRelationshipPairDelta`,
  `RecordWorldTestCounter`, and `EnqueueWorldTestRollup`.
- Produces: exact relationship distributions and per-day relationship funnel
  counters without a routine `SELECT * FROM relationship_pair_chemistry`.

- [ ] **Step 1: Add failing relationship telemetry fixtures**

Extend `RunMbtiRelationshipPersistenceSelfTests()` to process a fixture where
one edge moves `neutral -> acquaintance`, one tag changes, and a replay repeats
the same day. Assert:

```csharp
add("world_test_relationship_delta_exact",
    ReadMetric("band:neutral:directionalEdgeCount") == 1
    && ReadMetric("band:acquaintance:directionalEdgeCount") == 1
    && ReadMetric("band:acquaintance:pairCount") == 1,
    "Band populations are adjusted from pair deltas.");

add("world_test_relationship_replay_idempotent",
    ReadDayCounter("evaluatedPairs") == expectedPairs,
    "Replaying a completed relationship day does not duplicate counters.");

add("world_test_relationship_no_full_scan",
    ReadInt(result, "telemetryPairScans", -1) == 0,
    "Routine relationship telemetry performs no full pair-table scan.");
```

- [ ] **Step 2: Run MCP changed validation and confirm failure**

Use changed paths for `MbtiRelationships.cs`,
`ContinuousRelationshipWorker.cs`, `WorldTestTelemetry.cs`, and
`WorldTest.cs`. Expected: the three new checks fail.

- [ ] **Step 3: Accumulate pair deltas in each existing 500-pair transaction**

Before each pair upsert, use the already loaded `existing` row as `before`.
Construct `after` from the exact effective affinities, directional tags,
shared tag, hero IDs, and projected native target being written. Call
`ApplyWorldTestRelationshipPairDelta` before the same `COMMIT`.

Maintain an in-memory `Dictionary<string, object> chunkCounters` containing:

```text
eligiblePairs, evaluatedPairs, rolledDirections, positiveRolls,
negativeRolls, signedAffinityMovement, absoluteAffinityMovement,
nativeTargetsCreated, nativeTargetsReplaced, lifecycleEvaluations,
loversStarted, loversEnded, affairsStarted, affairsEnded,
romanticMarriagesQueued, divorcesQueued, conceptionsQueued
```

Persist it with chunk keys `000000-<lastPairKey>`,
`000500-<lastPairKey>`, and the final partial chunk. Use only characters safe
for SQLite text keys.

- [ ] **Step 4: Track cohort and duration histograms**

Increment cohort counters for `noble_noble`, `noble_notable`, and
`notable_notable`. Bucket time-to-band and time-to-tag as
`0-7`, `8-31`, `32-63`, `64-126`, and `127+` days based on the pair's
`first_day`.

- [ ] **Step 5: Enqueue the day only after authoritative completion**

After `mbti_relationship_last_processed_day:<timeline>` and
`relationship_daily_inputs.status='processed'` are durable, call
`EnqueueWorldTestRollup(..., "relationships_completed")`. Expose
`latestIngestedDay`, `latestCompletedDay`, `partialDayCursor`,
`evaluationsPerSecond`, and `queuedDays` independently of rollup status.

- [ ] **Step 6: Replace routine relationship aggregation reads**

Change `BuildWorldTestRelationships` to read
`world_test_current_metrics`, current lifecycle/action totals, and daily
rollups. Keep a full source reconciliation scan only behind the checkpoint
builder and self-test fixture; never call it from overview refresh.

- [ ] **Step 7: Validate performance and exactness**

Run MCP changed validation. The existing 1,900-pair scale fixture and the new
delta/replay checks must pass. Record elapsed telemetry overhead in test data
and require it to remain below 5%.

- [ ] **Step 8: Commit the task**

```text
feat: aggregate relationship telemetry in worker chunks
```

---

### Task 3: Social Reputation rumor funnel instrumentation

**Files:**
- Modify: `ReignServer/SocialReputation.cs:330-620,670-910`
- Modify: `ReignServer/ExpandedSocialReputation.cs:280-430`
- Modify: `ReignServer/WorldTestTelemetry.cs`
- Modify: `ReignServer/WorldTest.cs:262-317,647-692`

**Interfaces:**
- Consumes: `RecordWorldTestCounter` and `RecordWorldTestEvidence`.
- Produces: exact eligible/exposure/promotion/correction funnel metrics keyed
  by campaign/timeline/day.

- [ ] **Step 1: Add failing rumor funnel contract tests**

Extend `RunRumorSubsystemSelfTests()` with deterministic calls for failed
exposure, successful exposure, duplicate replay, promotion, expiration, and
correction. Assert:

```csharp
add("world_test_rumor_funnel_counts",
    eligible == 5 && attempts == 4 && exposed == 2
    && failedExposure == 2 && promoted == 1,
    "World Test counts the complete rumor funnel with denominators.");

add("world_test_rumor_duplicate_suppressed",
    duplicateSuppressed == 1 && occurrences == 2,
    "A duplicate source is counted as suppressed and does not create an occurrence.");

add("world_test_public_event_not_rumor",
    worldEvents == 1 && rumorOccurrences == 0,
    "A globally known public event remains world history rather than a rumor.");
```

- [ ] **Step 2: Run MCP changed validation and confirm failure**

Use the four task paths. Expected: new rumor telemetry checks fail while
existing rumor behavior checks remain green.

- [ ] **Step 3: Instrument every return path without altering decisions**

Record compact counters for:

```text
eligibleHooks, disabledArchetypes, ineligibleParticipants,
exposureAttempts, exposurePassed, exposureFailed,
duplicateSourcesSuppressed, occurrencesCreated, subjectTagsCreated,
streakAdvanced, promotionAttempts, promotionPassed, promotionFailed,
durableReputationsCreated, expired, corrected, disproven, verified,
counterevidenceApplied
```

Use a deterministic `chunkKey` derived from the existing exposure seed or
source event ID. Store no prompt, conversation, or raw event payload.
Successful occurrence/promotion writes and their counters share the existing
transaction. Failed exposure counters use an idempotent compact chunk row.

- [ ] **Step 4: Add distributions and duration buckets**

Populate archetype, provenance, subject role, kingdom, and culture counters
from already available payload fields. Bucket event-to-exposure,
exposure-to-promotion, and occurrence lifetime using
`same_day`, `1-7`, `8-31`, `32-45`, and `46+`.

- [ ] **Step 5: Upgrade rumor overview and bounded details**

Make `BuildWorldTestRumors` read funnel counters plus authoritative current
occurrence/tag/reputation counts. Extend `/world-test/details` with
`subsystem=rumor funnel` and bounded evidence. Remove legacy
`chainCount`, `deliveryRuns`, `attempts`, and `acquisitions` fields that refer
to the deleted rumor network.

- [ ] **Step 6: Validate behavior parity**

Run MCP changed validation. Require all existing rumor contract tests to pass
unchanged, plus the new funnel, duplicate, and world-event-separation tests.

- [ ] **Step 7: Commit the task**

```text
feat: observe social reputation rumor funnel
```

---

### Task 4: Romance, diplomacy, rebellion, manifest, and pipeline counters

**Files:**
- Modify: `ReignServer/RelationshipLifecycle.cs`
- Modify: `ReignServer/RelationshipDirector.cs`
- Modify: `ReignServer/WorldDiplomacyDirector.cs`
- Modify: `ReignServer/RebellionDirector.cs`
- Modify: `ReignServer/WorldTest.cs`
- Modify: `ReignBeta/src/Integration/ReignWorldTestClient.cs`

**Interfaces:**
- Consumes: telemetry counter/evidence interfaces.
- Produces: three distinct marriage funnels, diplomacy cadence/outcomes,
  rebellion denominators/outcomes, reproducibility manifest, producer clocks,
  and native context.

- [ ] **Step 1: Add failing subsystem classification tests**

Add checks to the existing lifecycle, relationship director, diplomacy, and
rebellion self-test lists:

```csharp
add("world_test_marriage_routes_separate",
    romantic == 1 && arranged == 1 && diplomatic == 1,
    "Organic, arranged, and diplomatic marriages have separate funnels.");

add("world_test_no_action_is_healthy_evaluation",
    evaluations == 1 && noAction == 1 && failures == 0,
    "A legitimate diplomacy no-action result satisfies cadence.");

add("world_test_rebellion_denominators",
    weeklyEligible == 2 && excludedCooldown == 1 && triggered == 1,
    "Rebellion rates retain eligible and exclusion denominators.");
```

- [ ] **Step 2: Confirm failure with MCP changed validation**

Validate the six task paths. Expected: the new classifications are absent.

- [ ] **Step 3: Emit romance and family funnel counters**

Classify lifecycle results into organic romance and family counters. Classify
`marriage_evaluations.route` and `marriage_leader_rolls` into arranged
marriage. Count accepted, refused, failed, invalid-native, and abandoned
attempts without merging them with diplomatic marriage packages.

- [ ] **Step 4: Emit diplomacy counters**

At ruler evaluation completion, count `due`, `completed`, `no_action`,
`initiative_attempted`, and `initiative_succeeded`; then count intent family,
command, response outcome, queued/applied/failed action, actor kingdom, target
kingdom, announcement latency, and native-application latency.

- [ ] **Step 5: Complete rebellion counters**

Use persisted weekly roll records for eligible, threshold exclusion, war
restriction, cooldown exclusion, success, and player-inclusion violation.
Count outbreak kingdom, rebel/loyalist membership, resolution, duration,
native confirmation, and cleanup.

- [ ] **Step 6: Extend the compact native heartbeat**

Add a `manifest` object containing assembly versions, feature/configuration
hashes, timeline, relationship compatibility version, rumor catalog/schema
revision, worker settings, and campaign seed when available. Add producer
clocks and native totals for births, deaths, ruling-clan changes, and
settlement transfers. Add current process CPU/memory observations where they
are safely available. Do not include API credentials or full NPC profiles.

- [ ] **Step 7: Validate all passive producer contracts**

Run MCP changed validation. Require the new route/cadence/denominator checks
and existing diplomacy, rebellion, relationship, and World Test heartbeat
checks to pass.

- [ ] **Step 8: Commit the task**

```text
feat: instrument passive world outcome funnels
```

---

### Task 5: Asynchronous rollups, checkpoints, and comparisons

**Files:**
- Create: `ReignServer/WorldTestRollupWorker.cs`
- Create: `ReignServer/WorldTestReports.cs`
- Modify: `ReignServer/WorldTest.cs:49-151,340-401,812-947`
- Modify: `ReignServer/Program.cs:1319-1337`
- Modify: `ReignServer/ReignServer.csproj`

**Interfaces:**
- Consumes: telemetry tables and authoritative source ledgers.
- Produces:

```csharp
private static Dictionary<string, object> WorldTestCheckpointsApi(
    Dictionary<string, string> query);

private static Dictionary<string, object> WorldTestComparisonApi(
    Dictionary<string, string> query);

private static Dictionary<string, object> WorldTestRollupWorkerStatus();
```

- [ ] **Step 1: Add failing rollup and checkpoint tests**

Create `RunWorldTestRollupSelfTests()` and include it from
`RunWorldTestSelfTests()`. Cover duplicate queueing, restart recovery,
timeline branching, half-season checkpoint selection, and three-campaign
comparison.

Assert day gates exactly:

```csharp
double[] gates = { 1d, 7d, 31.5d, 63d, 126d, 378d, 630d };
```

- [ ] **Step 2: Confirm failure through MCP changed validation**

Validate the five task paths. Expected: worker/report symbols and routes are
missing.

- [ ] **Step 3: Implement the independent rollup worker**

Use a background thread signaled by `EnqueueWorldTestRollup`. Acquire
`CampaignDataGate` read access, select the oldest pending
campaign/timeline/day, merge distinct chunk summaries, read current
materialized counts, and replace `world_test_daily_rollups`.

Set queue status to `completed` only after the rollup commit. On failure, store
`last_error`, increment `attempt_count`, and retry independently. Never throw
the error into the producer request. A later producer update for an already
completed day increments its queue revision and returns it to `pending`, so the
same rollup is replaced rather than duplicated.

- [ ] **Step 4: Remove synchronous overview construction from heartbeat**

`WorldTestHeartbeatApi` must store the heartbeat and enqueue the day, then
return immediately. It must not call `BuildWorldTestOverview` before
acknowledging. Add separate game, ingested, completed-system, rollup, and
native-confirmed clocks to overview.

- [ ] **Step 5: Build cached checkpoint reports**

Generate a report when the latest completed rollup first reaches each gate.
For day 31.5, select the first completed rollup with `world_day >= 31.5`.
Cache absolute totals, denominators, normalized rates, prior-gate changes,
health, evidence links, storage, and timing.

- [ ] **Step 6: Implement technical health classification**

Keep technical health separate from balance observations. Emit immediate
warning/error states for missing MBTI assignments, skipped or duplicated
pair-days, relationship or instrumentation lag of at least one completed day,
native failures/contradictions, missed diplomacy cadence, missing rebellion
rolls after seven eligible days, terminal actions, timeline regression,
stalled compaction, runaway storage growth, and source reconciliation
mismatch. Leave pacing and population distributions informational during the
baseline runs.

- [ ] **Step 7: Build three-campaign comparison**

Accept exactly three selected `campaignId|timelineId` values. Return each
campaign, combinable totals, minimum, maximum, mean, median, sample size, and
Wilson confidence intervals for low-frequency binomial rates. Reject mixed
schema/configuration manifests with `comparable=false` and an explicit reason.

- [ ] **Step 8: Add routes**

Register:

```text
GET /world-test/checkpoints
GET /world-test/comparison
```

Both routes are read-only and paginated where they return evidence rows.

- [ ] **Step 9: Validate recovery and latency fixtures**

Run MCP changed validation. The fixture must show duplicate queue idempotency,
branch isolation, successful restart recovery, cached overview reads, and
rollup completion under five seconds.

- [ ] **Step 10: Commit the task**

```text
feat: add asynchronous world test checkpoint reports
```

---

### Task 6: Control Center observatory UI

**Files:**
- Modify: `ReignServer/Program.cs:23280-23300,23982-24020,25285-25535`
- Modify: `ReignServer/VerificationLab.cs:647-658`

**Interfaces:**
- Consumes: `/world-test/overview`, `/world-test/details`,
  `/world-test/checkpoints`, and `/world-test/comparison`.
- Produces: staged shakedown, subsystem-card, rumor-inspector, checkpoint, and
  baseline-comparison views.

- [ ] **Step 1: Add failing UI contract checks**

Extend Verification Lab source contracts to require these stable DOM IDs:

```text
worldTestStage
worldTestClockStrip
worldTestCheckpointTable
worldTestRelationshipCard
worldTestRomanceFamilyCard
worldTestRumorCard
worldTestDiplomacyCard
worldTestRebellionCard
worldTestPipelineCard
worldTestNativeContextCard
worldTestBaselineComparison
```

Also assert that no World Test button text contains `force`, `retry`, `reset`,
`delete`, `inject`, or `speed`.

- [ ] **Step 2: Confirm contract failure through MCP changed validation**

Validate `Program.cs` and `VerificationLab.cs`. Expected: new IDs are absent.

- [ ] **Step 3: Render the fixed header and staged gates**

Show manifest, current stage, next gate, and distinct game/ingested/completed/
rollup/native clocks. Use day 126 as the initial acceptance horizon; hide
three- and five-year comparison states until their reports exist.

- [ ] **Step 4: Render seven subsystem cards**

Keep technical health visually separate from informational balance. Show each
rate with its denominator and change since the previous gate.

- [ ] **Step 5: Upgrade the rumor inspector**

Display archetype, provenance, subject/role, exposure result, streak,
promotion, correction/expiration/consequence, campaign day, and source-ledger
reference. Put public world events in the Native Context card instead.

- [ ] **Step 6: Add checkpoint and comparison views**

Render shakedown columns for day 1, 7, 31.5, 63, and 126. Render three selected
campaigns side-by-side only when comparison data is available. Preserve JSON
and current-table CSV export.

- [ ] **Step 7: Validate UI and JavaScript contracts**

Run MCP changed validation. Require a clean JavaScript syntax contract,
responsive layout contract, all stable DOM IDs, read-only-control check, and
existing rumor inspector contract to pass.

- [ ] **Step 8: Commit the task**

```text
feat: present staged passive world observatory
```

---

### Task 7: Reconciliation, performance, and release validation

**Files:**
- Modify: `ReignServer/WorldTestTelemetry.cs`
- Modify: `ReignServer/WorldTestRollupWorker.cs`
- Modify: `ReignServer/WorldTest.cs`
- Modify: `ReignServer/VerificationLab.cs`
- Modify: `ReignServer/ReignServer.csproj`
- Modify: `docs/superpowers/specs/2026-07-27-passive-world-balance-observatory-design.md`

**Interfaces:**
- Consumes: every prior task.
- Produces: source reconciliation, performance evidence, and the final
one-year shakedown readiness gate.

- [ ] **Step 1: Add failing reconciliation and scale fixtures**

Create fixtures for empty, partial, healthy, failed, branched, and five-year
campaigns. Compare materialized relationship counts against an explicit source
scan performed only inside the fixture/checkpoint reconciler.

Use at least:

```text
2,500 NPCs
42,000 eligible pairs/day
630 daily rollups
three campaign manifests
1,000 active rumor tags
```

- [ ] **Step 2: Confirm failure through MCP changed validation**

Expected failures: no reconciliation result, no five-year latency result, or
performance budget not yet enforced.

- [ ] **Step 3: Implement checkpoint-only reconciliation**

At checkpoint generation, compare exact current metrics with authoritative
source ledgers. Store only mismatches and a compact reconciliation summary.
Return `error` for count disagreement and include the source ledger and
checkpoint day.

- [ ] **Step 4: Implement bounded cleanup**

After a completed rollup:

- Delete merged chunk summaries older than seven completed days.
- Retain warning/error evidence until inspected and outside its configured
  window.
- Cap ordinary samples at 100 per subsystem/day.
- Run `PRAGMA wal_checkpoint(PASSIVE)` only after completed rollup or existing
  compaction work.
- Never delete consequential authoritative events.

- [ ] **Step 5: Enforce performance assertions**

The fixture must assert:

```text
relationship telemetry overhead < 5%
daily rollup elapsed < 5,000 ms
cached overview elapsed < 500 ms
no full relationship scan during routine overview/rollup
storage rows scale with days plus bounded evidence
```

- [ ] **Step 6: Run the canonical full validation**

Call:

```json
{
  "profile": "all",
  "configuration": "Release",
  "restore": false,
  "failFast": false
}
```

Expected: `coverageComplete=true`, no unmanaged projects, and every build/test
project in the returned validation report succeeds.

- [ ] **Step 7: Review the authoritative report**

Open the latest structured report under
`.codex-build/reign-mcp/validation`. Confirm it covers client, server,
live-test controller, verification runner, MCP, tooling, and both discovered
test projects. Do not claim success from console text alone.

- [ ] **Step 8: Commit the final verified implementation**

```text
test: verify passive world observatory at scale
```

- [ ] **Step 9: Deploy only through the supported lifecycle**

Ask the user to close the visible Reign Control Center/server if they are open.
Replace installed files only while the lifetime group is stopped. Restart, if
requested, only through `Start ReignBeta Server.cmd` or the installed visible
shortcut. Never start a second listener or hidden process.

- [ ] **Step 10: Execute the Stage 1 acceptance campaign**

Use one fresh campaign and review at days 1, 7, 31.5, 63, and 126. Stop early
for skipped/duplicate processing, unrecoverable lag, timeline corruption,
runaway storage, repeated terminal failures, invalid passive outcomes, or
aggregate reconciliation errors. Only after day 126 passes should two more
one-year campaigns be scheduled.
