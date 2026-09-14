# Reign 500-Call Final Conversation Gauntlet Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Convert the existing exhaustive Final Conversation Gauntlet into a permanent release gate that preserves explicit coverage of every approved requirement while refusing to dispatch more than 500 physical external-provider requests.

**Architecture:** Keep the generated 5,000-plus-case catalog as the authoritative traceability inventory, but classify most rows as deterministic or represented evidence rather than one-provider-call-per-row. The server will build a deterministic risk-weighted live covering set, persist an atomic provider-call ledger at the central provider middleware boundary, and produce complete evidence and a blinded review pack. `ReignLiveTest.exe` will execute the selected cases, `LNG-001`, `LNG-002`, and the twenty final scenes through production controllers while respecting save and lifecycle constraints.

**Tech Stack:** C# / .NET Framework 4.7.2, Bannerlord campaign APIs, `Microsoft.Data.Sqlite`, `JavaScriptSerializer`, the existing loopback Live Interaction Bridge, Save Sync, provider middleware, Verification Lab, and Reign MCP validation.

## Global Constraints

- `ConvTest` is the mutable baseline for native acceptance. Never mutate or delete `BaseOne`, `BaseTwo`, or `BaseThree`.
- Reign supports at most fifteen unique active Save Sync states. Use at most two rotating derivative saves plus `ConvTest`; delete only gauntlet-created derivatives after their snapshots are reconciled.
- Preserve unrelated dirty-worktree changes.
- Do not create or move a `.csproj`.
- Run all build and test validation through `mcp__reign.reign_validate`; never invoke `dotnet`, MSBuild, VSTest, or verification executables directly.
- Never start a second Reign server. Use the visible server/Control Center/vector-worker lifetime group.
- Count every physical external provider dispatch, including transient retries, format repair, summaries, relationship-history generation, action routing, and memory work.
- Refuse physical provider request 501 before network dispatch. Budget exhaustion makes the run incomplete and can never be reported as passing.
- Stage A consumes at most 40 calls and only fills missing compatible readiness evidence.
- Stage B budgets are: representative live covering set 120, twenty final scenes 60, `LNG-001` 110, `LNG-002` 44, and shared auxiliary/retry reserve 126. The absolute total remains 500.
- Each final scene executes once. A failed scene is recorded and execution continues; there is no automatic repair or scene rerun.
- Provider ambiguity is reconciled by correlation ID. A logical provider operation gets at most three physical attempts, each of which consumes budget.
- Final acceptance requires zero critical failures, 100% structural/hard assertions, at least 95% qualitative success, and a human-review pack of 10–12 already-generated scenes.

---

### Task 1: Add the physical provider-call budget ledger

**Files:**
- Create: `ReignBetaServer/FinalConversationGauntletProviderBudget.cs`
- Create: `ReignBetaServer/FinalConversationGauntletProviderBudgetSelfTests.cs`
- Modify: `ReignBetaServer/FinalConversationGauntletStore.cs`
- Modify: `ReignBetaServer/ProviderMiddleware.cs`
- Modify: `ReignBetaServer/VerificationLab.cs`

**Interfaces:**
- Consumes: provider `correlationId`, `requestType`, and `model` from `ProviderPostJson`.
- Produces: `FinalConversationGauntletProviderBudget.TryReservePhysicalDispatch(...)`, `CompletePhysicalDispatch(...)`, and durable `final_gauntlet_provider_calls` rows.

- [ ] **Step 1: Write failing budget tests**

Add `RunFinalConversationGauntletProviderBudgetSelfTests()` with literal assertions for:

```csharp
reserve calls 1..500 => allowed with ordinal 1..500
reserve call 501 => denied before delegate/network invocation
same correlation with attempt 1 and 2 => two physical calls
idempotency-cache hit => zero new physical calls
unrelated production correlation => not charged
restart with 499 persisted rows => one allowed, next denied
two concurrent reservations at remaining=1 => exactly one allowed
```

Register the suite as `pipeline.final_gauntlet_provider_budget`.

- [ ] **Step 2: Run MCP changed validation and verify RED**

Run `mcp__reign.reign_validate` with profile `changed`, Release configuration, and the five paths above. The new suite must fail because the provider budget API/schema is absent.

- [ ] **Step 3: Add the ledger schema and atomic reservation**

Add:

```sql
CREATE TABLE IF NOT EXISTS final_gauntlet_provider_calls(
    run_id TEXT NOT NULL,
    provider_call_id TEXT NOT NULL,
    case_instance_id TEXT NOT NULL,
    correlation_id TEXT NOT NULL,
    request_type TEXT NOT NULL,
    model TEXT NOT NULL,
    physical_attempt INTEGER NOT NULL,
    ordinal INTEGER NOT NULL,
    status TEXT NOT NULL,
    started_ts INTEGER NOT NULL,
    completed_ts INTEGER NOT NULL DEFAULT 0,
    error TEXT NOT NULL DEFAULT '',
    PRIMARY KEY(run_id,provider_call_id),
    UNIQUE(run_id,ordinal));
```

Implement reservation in a SQLite immediate transaction. Count existing rows, reject when count is 500, and insert ordinal `count + 1`. Derive `provider_call_id` from run, case, correlation, request type, and physical attempt.

- [ ] **Step 4: Gate the actual network boundary**

In `ProviderPostJson`, preserve idempotency-cache lookup first. Immediately before `PostJsonToLlmWithTransientRetry`, resolve gauntlet scope from the registered correlation ledger and reserve each physical attempt. Move the transient loop callback below the gate so every call to `PostJsonToLlm` consumes one ordinal. Throw `FinalGauntletProviderBudgetExceededException` before `PostJsonToLlm` when no ordinal remains.

- [ ] **Step 5: Verify GREEN and commit**

Run MCP changed validation. Confirm the concurrency test permits exactly one final reservation and the denied delegate was never invoked. Commit only Task 1 files with `feat: enforce final gauntlet provider budget`.

### Task 2: Classify exhaustive coverage and build the 120-call live covering set

**Files:**
- Create: `ReignBetaServer/FinalConversationGauntletCoverage.cs`
- Create: `ReignBetaServer/FinalConversationGauntletCoverageSelfTests.cs`
- Modify: `ReignBetaServer/FinalConversationGauntletModels.cs`
- Modify: `ReignBetaServer/FinalConversationGauntletCatalog.cs`
- Modify: `ReignBetaServer/FinalConversationGauntletCatalog.Core.cs`
- Modify: `ReignBetaServer/FinalConversationGauntletCatalog.Actions.cs`
- Modify: `ReignBetaServer/VerificationLab.cs`

**Interfaces:**
- Produces: `FinalGauntletCoverageKind` (`Deterministic`, `RepresentedLive`, `DedicatedLive`, `LongHorizon`, `FinalScene`) and `BuildLiveCoveringSet(catalog, seed, maximumProviderCalls)`.
- Guarantees: every catalog requirement maps to at least one evidence-producing case and selected estimated cost is no more than 120.

- [ ] **Step 1: Write failing coverage tests**

Use the real discovered catalog and assert:

```csharp
all requirement IDs have a nonempty coverage kind
all enabled actions/resolvers/traits/cultures/modes/occupations/rumors remain catalogued
all ACTION rows have deterministic conformance evidence
all five-by-five Honor/Boldness cells for both sexes remain represented
all pairwise axis pairs remain represented
live covering set estimated provider cost <= 120
selected set contains 40 single-speaker dense fixtures
selected set contains 10 three-speaker dense group fixtures
same seed produces the same ordered selection and mapping
removing the sole representative for any requirement fails coverage validation
```

- [ ] **Step 2: Verify RED**

Run MCP changed validation and confirm failure because coverage kinds and selection do not exist.

- [ ] **Step 3: Extend descriptors and implement deterministic set cover**

Add to `FinalGauntletCaseDescriptor`:

```csharp
public string CoverageKind;
public int EstimatedProviderCalls;
public string[] RepresentedRequirementIds;
public int RiskWeight;
```

Implement stable greedy selection ordered by:

1. uncovered zero-tolerance requirements;
2. risk weight descending;
3. number of uncovered requirements per estimated call descending;
4. `CaseId` ordinal.

Seed only ties among identical ranking tuples. Dense single-speaker fixtures cost 2 calls (reply plus closure summary). Dense group fixtures cost 4 calls (three sequential replies plus closure summary).

- [ ] **Step 4: Keep matrices exhaustive but provider-free**

Mark parser/schema/resolver/validator/idempotency/statistical-boundary/action-applicability rows deterministic when their production component can be invoked without an LLM. Map live semantic action checks by action family rather than every action-by-requirement product. Preserve an explicit evidence map from every omitted live row to its representative case and deterministic prerequisites.

- [ ] **Step 5: Verify GREEN and commit**

Run MCP changed validation and inspect the emitted selection manifest. Commit with `feat: select bounded live gauntlet coverage`.

### Task 3: Select only missing Stage A evidence and enforce its 40-call partition

**Files:**
- Create: `ReignBetaServer/FinalConversationGauntletReadinessSelection.cs`
- Create: `ReignBetaServer/FinalConversationGauntletReadinessSelectionSelfTests.cs`
- Modify: `ReignBetaServer/FinalConversationGauntletApi.cs`
- Modify: `ReignBetaServer/FinalConversationGauntletScheduler.cs`
- Modify: `ReignBetaServer/ConversationReadiness.cs`
- Modify: `ReignBetaServer/VerificationLab.cs`

**Interfaces:**
- Consumes: current build/settings/prompt/campaign compatibility fingerprints and readiness denominators.
- Produces: `SelectMissingReadinessCases(..., maximumProviderCalls: 40)`.

- [ ] **Step 1: Write failing Stage A selection tests**

Cover:

```csharp
fully compatible high evidence => zero Stage A cases
one missing category => only cases mapped to that category
incompatible build fingerprint => evidence is not silently reused
selection cost > 40 => start rejected with explicit unmet categories
Stage B promotion rejected until all selected Stage A cases terminate successfully
unused Stage A allowance does not enlarge Stage B partitions
```

- [ ] **Step 2: Verify RED**

Run MCP changed validation and observe the old hard-coded `.Take(100)` selection failing the new assertions.

- [ ] **Step 3: Replace hard-coded selection**

Remove the current `Take(100)` Stage A manifest. Store selected evidence IDs, compatibility reasons, per-case estimated calls, and the 40-call partition in the run record. Do not regenerate categories already high on compatible evidence.

- [ ] **Step 4: Verify GREEN and commit**

Run MCP changed validation. Commit with `feat: bound final gauntlet qualification gaps`.

### Task 4: Implement exact Stage B partitions and one-pass final scenes

**Files:**
- Create: `ReignBetaServer/FinalConversationGauntletManifest.cs`
- Create: `ReignBetaServer/FinalConversationGauntletManifestSelfTests.cs`
- Modify: `ReignBetaServer/FinalConversationGauntletScheduler.cs`
- Modify: `ReignBetaServer/FinalConversationGauntletApi.cs`
- Modify: `ReignBetaServer/ReignLiveTest/FinalGauntletRunner.cs`
- Modify: `ReignBetaServer/VerificationLab.cs`

**Interfaces:**
- Produces: frozen Stage B manifest with partitions `representative=120`, `finalScenes=60`, `lng001=110`, `lng002=44`, `reserve=126`.

- [ ] **Step 1: Write failing manifest tests**

Assert:

```csharp
all twenty GAUNTLET-001..020 cases appear exactly once
every final scene has MaxExecutions=1 and ContinueAfterFailure=true
representative cases estimated total <= 120
LNG-001 estimated total == 110
LNG-002 estimated total == 44
partition totals plus reserve == 460 when Stage A is zero
absolute run total remains 500 including Stage A maximum
failed final scene is terminal and next scene becomes leaseable
no failed Stage B case is automatically rescheduled
```

- [ ] **Step 2: Verify RED**

Run MCP changed validation and confirm the current promotion of the entire provider-backed catalog violates the bounded manifest.

- [ ] **Step 3: Freeze the partitioned manifest**

During promotion, execute every deterministic row locally and schedule only the selected live covering set, long-horizon cases, and final scenes. Persist `partition`, `estimated_provider_calls`, `max_executions`, and the complete represented-requirement list per case.

- [ ] **Step 4: Make final scenes non-fail-fast and single execution**

The CLI must record provider exhaustion or assertion failure as terminal for that scene and lease the next case. Logical recovery attempts remain inside the same execution and consume the shared reserve.

- [ ] **Step 5: Verify GREEN and commit**

Run MCP changed validation. Commit with `feat: freeze 500-call gauntlet manifest`.

### Task 5: Make long-horizon execution match the approved call arithmetic

**Files:**
- Modify: `ReignBetaServer/ReignLiveTest/FinalGauntletLongHorizon.cs`
- Modify: `ReignBetaServer/FinalConversationGauntletLongHorizonEvaluation.cs`
- Create: `ReignBetaServer/FinalConversationGauntletLongHorizonBudgetSelfTests.cs`
- Modify: `ReignBetaServer/VerificationLab.cs`

**Interfaces:**
- `LNG-001`: 100 dialogue replies in ten closed scenes plus ten summary calls = 110.
- `LNG-002`: twenty sequential group replies, four group summaries, ten private probes, and ten private summaries = 44.

- [ ] **Step 1: Write failing sequence tests**

Build manifests without invoking a provider and assert literal step counts, scene boundaries, stable correlations, full twenty-NPC roster coverage, ten private knowledge probes, and no unbudgeted provider-backed closure.

- [ ] **Step 2: Verify RED**

Run MCP changed validation and confirm current sequence arithmetic differs from the approved manifests.

- [ ] **Step 3: Implement `LNG-001`**

Use one focal adult NPC, ten scenes of ten exchanges, and production closure after every scene. Insert recall probes for same scene, beyond the recent-30 window, post-summary, post-consolidation, post-reload, and post-restart. Use two rotating gauntlet derivative saves; confirm Save Sync before each reload.

- [ ] **Step 4: Implement `LNG-002`**

Select twenty distinct adults spanning roles, cultures, traits, and relationships. Run four groups of five sequential speakers, close each group once, then privately probe ten members. Maintain expected knowledge states `said`, `heard`, `witnessed`, `inferred`, `rumor`, `public`, and `unknown`.

- [ ] **Step 5: Verify GREEN and commit**

Run MCP changed validation. Commit with `feat: bound gauntlet long horizon sequences`.

### Task 6: Report the exact call ledger, coverage, and blinded review pack

**Files:**
- Modify: `ReignBetaServer/FinalConversationGauntletReports.cs`
- Modify: `ReignBetaServer/FinalConversationGauntletReviewPack.cs`
- Modify: `ReignBetaServer/FinalConversationGauntletReportSelfTests.cs`
- Modify: `ReignBetaServer/Program.cs`

**Interfaces:**
- Produces: report fields `providerBudget`, `coverageMap`, `partitionRollup`, `unexecutedRequirements`, `completionEligibility`, and a 10–12 item blinded pack.

- [ ] **Step 1: Write failing report tests**

Require:

```csharp
physical calls grouped by partition, request type, case, model, status
reserved/used/remaining totals and denied call evidence
every original requirement linked to deterministic or live evidence
budget exhaustion => completionEligible=false
critical failure => completionEligible=false
qualitative success below 95% => completionEligible=false
10..12 review items with answer keys stored separately
review items contain no traits, expected outcomes, scores, or internal mechanics
review pack causes no provider dispatch
```

- [ ] **Step 2: Verify RED**

Run MCP changed validation and confirm new report fields are absent.

- [ ] **Step 3: Extend JSON, NDJSON, CLI, and Control Center output**

Display actual physical calls, not scenario estimates. Make incompleteness explicit when a required live case never ran. Add progress text such as `physical provider calls 183/500` and partition use.

- [ ] **Step 4: Verify GREEN and commit**

Run MCP changed validation. Commit with `feat: report bounded gauntlet evidence`.

### Task 7: Harden pause, save rotation, resume, and budget recovery

**Files:**
- Modify: `ReignBetaServer/ReignLiveTest/FinalGauntletLifecycle.cs`
- Modify: `ReignBetaServer/ReignLiveTest/FinalGauntletProviderRecovery.cs`
- Modify: `ReignBetaServer/ReignLiveTest/FinalGauntletRunner.cs`
- Create: `ReignBetaServer/FinalConversationGauntletLifecycleSelfTests.cs`
- Modify: `ReignBetaServer/VerificationLab.cs`

**Interfaces:**
- Produces: safe `pause` checkpoint and restart-safe resume using the same run, case, correlation ledger, and provider ordinal.

- [ ] **Step 1: Write failing lifecycle tests**

Assert:

```csharp
pause stops new leases but lets accepted command reconcile
session closes before save
Save Sync finalization precedes reload/stop
only ConvTest_Gauntlet_A and ConvTest_Gauntlet_B are created
protected save names are rejected for overwrite/delete
15-state limit triggers rotation of gauntlet-created saves only
restart resumes ordinal N+1
ambiguous provider result is reconciled before retry
retry consumes another physical ordinal without duplicating a player turn
```

- [ ] **Step 2: Verify RED**

Run MCP changed validation.

- [ ] **Step 3: Implement safe pause and rotation**

Add CLI `gauntlet pause --run <id>`. Persist `pause_requested`, stop leasing, reconcile the active command, close production session, export partial report, save to the next rotating derivative, and wait for Save Sync `finalized`. Never delete or overwrite an unrecognized save.

- [ ] **Step 4: Verify GREEN and commit**

Run MCP changed validation. Commit with `feat: make final gauntlet safely resumable`.

### Task 8: Cross-cutting validation, deployment, and ConvTest acceptance

**Files:**
- Modify only files required by failures found in Tasks 1–7.
- Update: `ReignBetaServer/docs/FinalConversationGauntlet.md`
- Update: `REIGN_ROADMAP.md` only if an existing gauntlet entry requires a progress note.

**Interfaces:**
- Produces: installed, resumable 500-call harness and the first ConvTest run/report.

- [ ] **Step 1: Run the full Reign validation plan**

Call `mcp__reign.reign_get_validation_plan` with profile `all`, Release configuration, and all changed paths. Then call `mcp__reign.reign_validate` with that exact profile. Resolve every failure without bypassing tests.

- [ ] **Step 2: Deploy through the supported visible lifecycle**

Pause any active gauntlet work at a reconciled Save Sync point. Stop the visible Reign lifetime group and Bannerlord only if installed client/server binaries must be replaced. Deploy through the established Reign deployment path, then restart the visible server and BLSE campaign only when needed.

- [ ] **Step 3: Perform preflight against `ConvTest`**

Require:

```text
campaignId matches the current runtime
save name is ConvTest or a recognized gauntlet derivative
Save Sync ready and alignmentPending=false
character initialization ready
Danustica is the current settlement
no active live-test run
provider budget ledger is empty
BaseOne/BaseTwo/BaseThree are not mutation targets
```

- [ ] **Step 4: Start Stage A and continue inline**

Run the missing-evidence selector. If Stage A is empty, promote without provider calls. Otherwise run no more than 40 physical calls, record all failures, and promote only if every required gap passes.

- [ ] **Step 5: Run Stage B until pause or completion**

Execute deterministic catalog checks, the representative live set, both long-horizon sequences, and all twenty final scenes. Continue after case failures. On a user pause request, invoke the safe pause path and return a partial report plus exact resume identifiers.

- [ ] **Step 6: Verify installed evidence**

Inspect the report for:

```text
physicalProviderCalls <= 500
call501Denied if exhaustion was attempted
all original requirements mapped
all twenty final scenes terminal exactly once
LNG-001 and LNG-002 exact sequence evidence
zero critical defects for a passing result
100% hard assertions
>=95% qualitative assertions
10..12 blinded review items
no protected save mutation
```

- [ ] **Step 7: Commit documentation and report actual status**

Commit task-owned source/tests/docs only. Report the validation run ID, deployed build, gauntlet run ID, physical call usage, failures, partial/full report path, review-pack path, and resume checkpoint. Never call the run passing until the human review is complete.
