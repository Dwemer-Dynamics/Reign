# New-Game Readiness Seal Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Keep every new Reign campaign paused until authoritative, native, and gameplay-critical preparation is sealed, then guarantee that online, in-capacity saves can register matching Save Sync snapshots without inherited initialization backlog.

**Architecture:** Add a pure, testable readiness state machine and one `ReignCampaignPreparationCampaignBehavior` that owns the gate, persistent journal, stage orchestration, retry, and generation cancellation. Existing behaviors expose idempotent preparation entry points; the coordinator alone may run initialization-scoped server requests and release the gate after native relationship reconciliation, two stable queue observations, and a server watermark acknowledgement.

**Tech Stack:** C# / .NET Framework 4.7.2 Bannerlord client, C# / .NET Framework 4.7.2 Reign server, Newtonsoft.Json, xUnit through Reign MCP, TaleWorlds CampaignBehavior save data, loopback HTTP.

## Global Constraints

- Block only authoritative, native, and gameplay-critical initialization.
- Portrait image rendering, semantic embeddings, vector optimization, deduplication, compaction, and other rebuildable caches remain asynchronous.
- The local server must be healthy and Save Sync must have capacity for the post-release save guarantee.
- No timeout or bypass may release an unsealed campaign.
- Every stage and retry must be idempotent.
- Every asynchronous mutation must validate the active campaign-generation token.
- Persisted initialization data must remain safely below Bannerlord's signed 16-bit archive-entry limit.
- New games run the full pipeline; partial saves resume; established legacy saves run a compatibility audit; sealed saves use ordinary load alignment.
- Build and test only through `mcp__reign.reign_validate`; do not invoke `dotnet`, MSBuild, VSTest, or project scripts directly.
- Cross-cutting completion requires the canonical `all` validation profile, validated deployment artifacts, rollback backups, and the visible unified Reign lifecycle.

---

## File Structure

- Create `ReignBeta/src/Integration/ReignCampaignReadinessState.cs`: pure stages, journal, queue observation, sealing policy, and diagnostics model.
- Create `ReignBeta/src/Campaign/ReignCampaignPreparationCampaignBehavior.cs`: persisted coordinator and Bannerlord-facing pipeline owner.
- Create `ReignBeta/src/Integration/ReignInitializationServerClient.cs`: initialization-only server calls and seal acknowledgement.
- Create `ReignServer/InitializationReadiness.cs`: server watermark validation and self-tests.
- Create `ReignMcp/tests/Reign.Mcp.Tests/ReignCampaignReadinessStateTests.cs`: deterministic state-machine regression tests.
- Modify `ReignMcp/tests/Reign.Mcp.Tests/Reign.Mcp.Tests.csproj`: link the pure readiness source into tests.
- Modify `ReignBeta/src/Integration/ReignCampaignInitializationGate.cs`: generation-scoped initialization request permission and coordinator-only release token.
- Modify `ReignBeta/src/Campaign/ReignCharacterEditorCampaignBehavior.cs`: expose idempotent personality preparation and stop owning popup/gate release.
- Modify `ReignBeta/src/Campaign/ReignRelationshipCampaignBehavior.cs`: expose baseline, initial ambient snapshot, projection, receipt, and queue progress operations.
- Modify `ReignBeta/src/Integration/ReignRelationshipRealtimeClient.cs`: provide coordinator-driven initialization pumping and stable queue diagnostics.
- Modify `ReignBeta/src/Integration/ReignRelationshipDirectorClient.cs`: expose pending ambient-input count and explicit initial snapshot submission.
- Modify `ReignBeta/src/Integration/ReignWorldHistoryClient.cs`: expose finite-watermark and quiescence observations.
- Modify `ReignBeta/src/Campaign/ReignWorldHistoryCampaignBehavior.cs`: expose awaited timeline preparation and sequence state.
- Modify `ReignBeta/src/Campaign/ReignAICampaignBehavior.cs`, `ReignRulerReputationCampaignBehavior.cs`, `ReignRebellionCampaignBehavior.cs`, `ReignWorldDiplomacyCampaignBehavior.cs`, `ReignSocialEventsCampaignBehavior.cs`, `ReignFamilyCampaignBehavior.cs`, `ReignCourtPersonalityReputationCampaignBehavior.cs`, and `ReignBeta/src/Court/ReignCourtCampaignBehavior.cs`: expose idempotent native/session foundation methods and stop scheduling them after gate release.
- Modify `ReignBeta/src/UI/ReignNotableGenerationPopupManager.cs` and `ReignBeta/GUI/Prefabs/ReignNotableGenerationPopup.xml`: bind stage/progress/failure status while retaining the input lock.
- Modify `ReignBeta/src/SubModule.cs`: register/tick the preparation behavior before gameplay-facing systems.
- Modify `ReignBeta/src/Campaign/ReignSaveSyncCampaignBehavior.cs`: reject unsealed saves defensively and drain only the captured finite watermark after release.
- Modify `ReignBeta/src/Integration/ReignLiveTestClient.cs` and `ReignBeta/src/Campaign/ReignLiveInteractionTestHost.cs`: publish seal and queue evidence.
- Modify `ReignServer/Program.cs`: route `POST /initialization/readiness/seal`.
- Modify `ReignServer/VerificationLab.cs`: include readiness self-tests in offline verification.
- Modify `REIGN_ROADMAP.md`: add a dated completion note only after live acceptance.

---

### Task 1: Pure Readiness State Machine

**Files:**
- Create: `ReignBeta/src/Integration/ReignCampaignReadinessState.cs`
- Create: `ReignMcp/tests/Reign.Mcp.Tests/ReignCampaignReadinessStateTests.cs`
- Modify: `ReignMcp/tests/Reign.Mcp.Tests/Reign.Mcp.Tests.csproj`

**Interfaces:**
- Produces `ReignInitializationStage` with `CampaignStart`, `Personalities`, `NativeFoundations`, `Timeline`, `Identity`, `AuthoritativeSnapshots`, `Relationships`, `CriticalDrain`, `ServerAcknowledgement`, `Ready`, and `Failed`.
- Produces `ReignInitializationJournal` with `SealVersion`, `CampaignId`, `TimelineId`, `CompletedStageMask`, `SealedHistorySequence`, and `IsSealed`.
- Produces `ReignInitializationQueueObservation` with world-history, relationship-input, native-target, native-receipt, and in-flight counts plus `HistorySequence`.
- Produces `ReignCampaignReadinessState.Advance`, `Fail`, `Retry`, `ObserveQuiescence`, `AcceptServerAcknowledgement`, `CanRelease`, and `Snapshot`.

- [ ] **Step 1: Link the missing source and write failing stage-order tests**

```xml
<Compile Include="..\..\..\ReignBeta\src\Integration\ReignCampaignReadinessState.cs"
         Link="Shared\ReignCampaignReadinessState.cs" />
```

```csharp
[Fact]
public void Personality_completion_cannot_release_the_campaign()
{
    var state = ReignCampaignReadinessState.Start("campaign-a", "generation-a");
    state.Advance(ReignInitializationStage.Personalities);
    Assert.False(state.CanRelease);
    Assert.Equal(ReignInitializationStage.NativeFoundations, state.RequiredStage);
}

[Fact]
public void Release_requires_two_identical_quiescent_observations_and_server_ack()
{
    var state = ReadyThroughCriticalDrain();
    var idle = ReignInitializationQueueObservation.Idle(historySequence: 42);
    Assert.False(state.ObserveQuiescence(idle));
    Assert.True(state.ObserveQuiescence(idle));
    Assert.False(state.CanRelease);
    state.AcceptServerAcknowledgement("campaign-a", "main-a", 42, sealVersion: 1);
    Assert.True(state.CanRelease);
}
```

- [ ] **Step 2: Run the canonical changed profile and verify RED**

Run `mcp__reign.reign_validate` with profile `changed`, Release, no restore, and changed paths for the test, test project, and missing source.

Expected: compilation fails because `ReignCampaignReadinessState.cs` and the named types do not exist.

- [ ] **Step 3: Implement the minimal pure state model**

```csharp
internal sealed class ReignCampaignReadinessState
{
    internal const int CurrentSealVersion = 1;
    internal static ReignCampaignReadinessState Start(string campaignId, string generationId);
    internal void Advance(ReignInitializationStage completed);
    internal void Fail(string error);
    internal void Retry();
    internal bool ObserveQuiescence(ReignInitializationQueueObservation observation);
    internal void AcceptServerAcknowledgement(string campaignId, string timelineId, long historySequence, int sealVersion);
    internal bool CanRelease { get; }
    internal ReignInitializationStage RequiredStage { get; }
    internal JObject Snapshot();
}
```

Reset the stable-observation count whenever any count, generation, campaign, timeline, or history watermark changes.

- [ ] **Step 4: Add migration and stale-generation tests**

```csharp
[Theory]
[InlineData(0, ReignInitializationMode.CompatibilityAudit)]
[InlineData(1, ReignInitializationMode.Sealed)]
public void Journal_selects_expected_load_mode(int sealVersion, ReignInitializationMode expected)
{
    Assert.Equal(expected, ReignInitializationJournal.SelectLoadMode(
        sealVersion, personalitiesComplete: true, establishedCampaign: true));
}

[Fact]
public void Stale_generation_cannot_advance_active_state()
{
    var state = ReignCampaignReadinessState.Start("campaign-a", "generation-new");
    Assert.False(state.TryAdvance("generation-old", ReignInitializationStage.Personalities));
}
```

- [ ] **Step 5: Run changed validation and verify GREEN**

Expected: the linked xUnit tests pass, with no warnings or unmanaged projects.

- [ ] **Step 6: Commit the pure readiness model**

```powershell
git add -- ReignBeta/src/Integration/ReignCampaignReadinessState.cs ReignMcp/tests/Reign.Mcp.Tests/ReignCampaignReadinessStateTests.cs ReignMcp/tests/Reign.Mcp.Tests/Reign.Mcp.Tests.csproj
git commit -m "feat: add campaign readiness state machine"
```

---

### Task 2: Coordinator-Only Gate and Persistent Journal

**Files:**
- Create: `ReignBeta/src/Campaign/ReignCampaignPreparationCampaignBehavior.cs`
- Modify: `ReignBeta/src/Integration/ReignCampaignInitializationGate.cs`
- Modify: `ReignBeta/src/SubModule.cs`
- Modify: `ReignMcp/tests/Reign.Mcp.Tests/ReignCampaignReadinessStateTests.cs`

**Interfaces:**
- Produces `ReignCampaignInitializationGate.BeginGeneration(string generationId)`.
- Produces `ReignCampaignInitializationGate.RunInitializationRequestAsync<T>(string generationId, Func<Task<T>> action)`.
- Produces `ReignCampaignInitializationGate.TrySealAndMarkReady(string generationId, ReignInitializationReleaseToken token)`.
- Produces `ReignCampaignPreparationCampaignBehavior.Instance`, `ApplicationTick(float)`, `Snapshot()`, and save-backed journal fields.

- [ ] **Step 1: Write failing authorization tests**

```csharp
[Fact]
public void Only_matching_generation_with_release_token_can_mark_ready()
{
    var authorization = ReignInitializationAuthorization.Begin("generation-a");
    Assert.False(authorization.CanRelease("generation-b", ReignInitializationReleaseToken.Invalid));
    Assert.True(authorization.CanRelease("generation-a", ReignInitializationReleaseToken.Valid));
}
```

- [ ] **Step 2: Run changed validation and verify RED**

Expected: the test fails because generation authorization and release tokens are absent.

- [ ] **Step 3: Implement generation-scoped initialization requests**

Use `AsyncLocal<string>` inside the gate. `WaitForReadyAsync` may bypass the public gate only when the current async scope matches the active generation. Save Sync alignment remains independently enforced.

```csharp
internal static Task<T> RunInitializationRequestAsync<T>(
    string generationId,
    Func<Task<T>> action);

internal static bool TrySealAndMarkReady(
    string generationId,
    ReignInitializationReleaseToken token);
```

Remove public/internal call paths that can invoke unconditional `MarkReady`.

- [ ] **Step 4: Add the campaign behavior and persisted journal**

Register `ReignCampaignPreparationCampaignBehavior` immediately after `ReignSaveSyncCampaignBehavior` and before every gameplay-facing behavior. Its `SyncData` stores only compact primitive fields:

```csharp
dataStore.SyncData("_reignInitializationSealVersion", ref _sealVersion);
dataStore.SyncData("_reignInitializationCampaignId", ref _sealedCampaignId);
dataStore.SyncData("_reignInitializationTimelineId", ref _sealedTimelineId);
dataStore.SyncData("_reignInitializationStageMask", ref _completedStageMask);
dataStore.SyncData("_reignInitializationHistorySequence", ref _sealedHistorySequence);
```

`SubModule.OnApplicationTick` ticks the coordinator and returns early while the gate is pending.

- [ ] **Step 5: Verify GREEN with changed validation**

Expected: coordinator authorization tests pass and the client builds with zero warnings.

- [ ] **Step 6: Commit the coordinator shell**

```powershell
git add -- ReignBeta/src/Campaign/ReignCampaignPreparationCampaignBehavior.cs ReignBeta/src/Integration/ReignCampaignInitializationGate.cs ReignBeta/src/SubModule.cs ReignMcp/tests/Reign.Mcp.Tests/ReignCampaignReadinessStateTests.cs
git commit -m "feat: centralize campaign initialization ownership"
```

---

### Task 3: Personality and Native Foundation Stages

**Files:**
- Modify: `ReignBeta/src/Campaign/ReignCharacterEditorCampaignBehavior.cs`
- Modify: `ReignBeta/src/Campaign/ReignCampaignPreparationCampaignBehavior.cs`
- Modify: `ReignBeta/src/Campaign/ReignRelationshipCampaignBehavior.cs`
- Modify: `ReignBeta/src/Campaign/ReignAICampaignBehavior.cs`
- Modify: `ReignBeta/src/Campaign/ReignRulerReputationCampaignBehavior.cs`
- Modify: `ReignBeta/src/Campaign/ReignRebellionCampaignBehavior.cs`
- Modify: `ReignBeta/src/Campaign/ReignWorldDiplomacyCampaignBehavior.cs`
- Modify: `ReignBeta/src/Campaign/ReignSocialEventsCampaignBehavior.cs`
- Modify: `ReignBeta/src/Campaign/ReignFamilyCampaignBehavior.cs`
- Modify: `ReignBeta/src/Campaign/ReignCourtPersonalityReputationCampaignBehavior.cs`
- Modify: `ReignBeta/src/Court/ReignCourtCampaignBehavior.cs`
- Test: `ReignMcp/tests/Reign.Mcp.Tests/ReignCampaignReadinessStateTests.cs`

**Interfaces:**
- Produces `Task<ReignPersonalityPreparationResult> EnsureInitialPersonalitiesAsync(string generationId, Action<int,int,string> progress)`.
- Produces idempotent `PrepareInitialState()` methods on each native/session behavior.
- Coordinator calls every foundation method on the main thread, then advances exactly once.

- [ ] **Step 1: Write failing checkpoint-resume tests**

```csharp
[Fact]
public void Partial_save_with_personalities_complete_resumes_at_native_foundations()
{
    var journal = new ReignInitializationJournal
    {
        SealVersion = 0,
        CompletedStageMask = ReignInitializationStageMask.Personalities
    };
    Assert.Equal(ReignInitializationStage.NativeFoundations,
        ReignCampaignReadinessState.Resume(journal, "campaign-a", "generation-a").RequiredStage);
}
```

- [ ] **Step 2: Run changed validation and verify RED**

Expected: resume does not yet select the native-foundation stage.

- [ ] **Step 3: Refactor personality generation without changing its all-or-nothing behavior**

Move popup ownership and `MarkReady` out of `ReignCharacterEditorCampaignBehavior`. Return a result only after assignment, native application, and server confirmation all succeed. Keep `_reignNotableBackgroundInitializationComplete` for compatibility and do not regenerate when it is already true.

- [ ] **Step 4: Expose and invoke idempotent native foundations**

Each behavior's existing private session initializer becomes an `internal` idempotent entry point. Remove its `RunWhenReadyOnMainThread` registration callback so it cannot duplicate coordinator work after release.

The coordinator invokes, on the main thread:

```csharp
ReignRelationshipCampaignBehavior.Instance.PrepareInitialBaselines();
ReignCalendarService.InitializeForCurrentCampaign();
ReignRulerReputationCampaignBehavior.Instance.PrepareInitialState();
ReignRebellionCampaignBehavior.Instance.PrepareInitialState();
ReignWorldDiplomacyCampaignBehavior.Instance.PrepareInitialState();
ReignSocialEventsCampaignBehavior.Instance.PrepareInitialState();
ReignFamilyCampaignBehavior.Instance.PrepareInitialState();
ReignCourtPersonalityReputationCampaignBehavior.Instance.PrepareInitialState();
ReignCourtCampaignBehavior.Instance.PrepareInitialState();
```

Async catalog loading remains optional because built-in thresholds are authoritative fallbacks.

- [ ] **Step 5: Verify GREEN with changed validation**

Expected: partial-save resume test passes; all touched client code builds without warnings.

- [ ] **Step 6: Commit personality and foundation integration**

Stage only the files listed in this task and commit:

```powershell
git commit -m "feat: prepare native campaign foundations before release"
```

---

### Task 4: Authoritative Timeline, Identity, and Initial Snapshots

**Files:**
- Create: `ReignBeta/src/Integration/ReignInitializationServerClient.cs`
- Modify: `ReignBeta/src/Campaign/ReignCampaignPreparationCampaignBehavior.cs`
- Modify: `ReignBeta/src/Campaign/ReignWorldHistoryCampaignBehavior.cs`
- Modify: `ReignBeta/src/Integration/ReignServerClient.cs`
- Modify: `ReignBeta/src/Campaign/ReignAICampaignBehavior.cs`
- Modify: `ReignBeta/src/Campaign/ReignWorldDiplomacyCampaignBehavior.cs`
- Test: `ReignMcp/tests/Reign.Mcp.Tests/ReignCampaignReadinessStateTests.cs`

**Interfaces:**
- Produces `Task<ReignTimelinePreparationResult> EnsureTimelineReadyAsync(string generationId)`.
- Produces `Task<JObject> SynchronizeIdentityNetworkForInitializationAsync(string generationId)`.
- Produces `Task<ReignAuthoritativeFoundationResult> SubmitInitialAuthoritativeSnapshotsAsync(string generationId)`.

- [ ] **Step 1: Write failing stale-response tests**

```csharp
[Fact]
public void Authoritative_response_for_previous_generation_is_rejected()
{
    var state = ReignCampaignReadinessState.Start("campaign-a", "generation-new");
    Assert.False(state.AcceptStageResult("generation-old",
        ReignInitializationStage.AuthoritativeSnapshots, success: true));
}
```

- [ ] **Step 2: Run changed validation and verify RED**

Expected: no generation-checked stage result API exists.

- [ ] **Step 3: Make timeline preparation awaitable and retryable**

`ReignWorldHistoryCampaignBehavior` no longer fire-and-forgets `OpenTimelineAsync`. The coordinator awaits the successful response; a transport failure remains a failed stage and may not set `_timelineReady = true`.

- [ ] **Step 4: Run identity and initial snapshots inside the initialization request scope**

The coordinator uses `RunInitializationRequestAsync` for `/world-history/timeline/open`, `/identity/synchronize`, `/portrait-roster/upsert-batch`, `/events/ingest`, and the initial diplomacy/world snapshot routes. Each result is checked for `ok == true` and the active campaign/timeline before the stage advances.

- [ ] **Step 5: Verify GREEN with changed validation**

Expected: stale-result tests pass and no normal gameplay request bypasses the public gate.

- [ ] **Step 6: Commit authoritative initialization**

```powershell
git commit -m "feat: await authoritative campaign foundations"
```

---

### Task 5: Initial Relationship Reconciliation and Stable Drain

**Files:**
- Modify: `ReignBeta/src/Campaign/ReignRelationshipCampaignBehavior.cs`
- Modify: `ReignBeta/src/Integration/ReignRelationshipRealtimeClient.cs`
- Modify: `ReignBeta/src/Integration/ReignRelationshipDirectorClient.cs`
- Modify: `ReignBeta/src/Integration/ReignWorldHistoryClient.cs`
- Modify: `ReignBeta/src/Campaign/ReignCampaignPreparationCampaignBehavior.cs`
- Test: `ReignMcp/tests/Reign.Mcp.Tests/ReignCampaignReadinessStateTests.cs`

**Interfaces:**
- Produces `Task<ReignInitialRelationshipResult> StartInitialRelationshipRunAsync(string generationId)`.
- Produces `ReignNativeRelationSyncProgress ProcessInitializationProjectionFrame(string generationId)`.
- Produces `Task<bool> PumpInitializationReceiptsAsync(string generationId)`.
- Produces `ReignInitializationQueueObservation CaptureInitializationQueueObservation(long historyWatermark)`.
- Produces `Task<bool> FlushThroughWatermarkAsync(long historyWatermark, TimeSpan timeout)`.

- [ ] **Step 1: Write failing moving-target and failure tests**

```csharp
[Fact]
public void New_work_between_idle_observations_resets_stability()
{
    var state = ReadyThroughCriticalDrain();
    Assert.False(state.ObserveQuiescence(ReignInitializationQueueObservation.Idle(42)));
    Assert.False(state.ObserveQuiescence(new ReignInitializationQueueObservation
    {
        HistorySequence = 42,
        NativeReceiptCount = 1
    }));
    Assert.False(state.ObserveQuiescence(ReignInitializationQueueObservation.Idle(42)));
}

[Fact]
public void Failed_native_projection_blocks_seal()
{
    var observation = ReignInitializationQueueObservation.Idle(42);
    observation.NativeProjectionFailureCount = 1;
    Assert.False(observation.IsQuiescent);
}
```

- [ ] **Step 2: Run changed validation and verify RED**

Expected: queue observations do not yet model receipts and projection failures.

- [ ] **Step 3: Add coordinator-driven relationship initialization**

Capture exactly one initial ambient input, submit until the server durably accepts it, pull all targets, process them through existing bounded per-frame native application, upload receipts, and report the completed plan. Do not run the continuous relationship tick until release.

- [ ] **Step 4: Add finite world-history watermark draining**

Capture the sequence after initial producers finish. Flush routine buckets and drain only events with sequence less than or equal to that watermark. Events after the watermark are excluded from the current seal and cannot extend its deadline.

- [ ] **Step 5: Require two stable frame-separated observations**

The coordinator records one idle observation, yields at least one application frame, records another identical observation, and advances only when both are quiescent and failure-free.

- [ ] **Step 6: Verify GREEN with changed validation**

Expected: moving-target, projection-failure, and stable-drain tests pass.

- [ ] **Step 7: Commit reconciliation and drain behavior**

```powershell
git commit -m "feat: seal initialization at stable queue watermark"
```

---

### Task 6: Server Seal Acknowledgement and Defensive Save Contract

**Files:**
- Create: `ReignServer/InitializationReadiness.cs`
- Modify: `ReignServer/Program.cs`
- Modify: `ReignServer/VerificationLab.cs`
- Modify: `ReignBeta/src/Integration/ReignInitializationServerClient.cs`
- Modify: `ReignBeta/src/Campaign/ReignCampaignPreparationCampaignBehavior.cs`
- Modify: `ReignBeta/src/Campaign/ReignSaveSyncCampaignBehavior.cs`
- Modify: `ReignBeta/src/Integration/ReignLiveTestClient.cs`
- Modify: `ReignBeta/src/Campaign/ReignLiveInteractionTestHost.cs`

**Interfaces:**
- Adds `POST /initialization/readiness/seal`.
- Request fields: `campaignId`, `timelineId`, `generationId`, `sealVersion`, `expectedHistorySequence`, `relationshipPlanId`.
- Response fields: `ok`, `campaignId`, `timelineId`, `sealVersion`, `acknowledgedHistorySequence`, `relationshipPlanComplete`, `pendingNativeTargets`, `error`.

- [ ] **Step 1: Add a failing server self-test**

```csharp
add("initialization_seal_rejects_missing_history_watermark",
    ReadBool(InitializationReadinessApi(new Dictionary<string, object>
    {
        ["campaignId"] = campaignId,
        ["timelineId"] = timelineId,
        ["expectedHistorySequence"] = 99L,
        ["sealVersion"] = 1
    }), "ok", true) == false,
    "The server cannot acknowledge a seal beyond durable world history.");
```

- [ ] **Step 2: Run changed validation and verify RED**

Expected: server compilation fails because the readiness API does not exist.

- [ ] **Step 3: Implement server acknowledgement**

The endpoint verifies the exact campaign/timeline, durable maximum world-history sequence, no active initialization relationship plan targets, and supported seal version. It performs no snapshot and no mutation beyond an idempotent bounded acknowledgement record.

- [ ] **Step 4: Persist the client seal before releasing the gate**

Only a successful matching response creates `ReignInitializationReleaseToken.Valid`. Persist journal fields, then call `TrySealAndMarkReady`, then close the popup and enable recurring producers.

- [ ] **Step 5: Defend Save Sync against unsealed state**

`OnBeforeSave` reports `initialization_not_sealed` if a mod or external action somehow starts a save while the gate is pending. For sealed campaigns, it drains the captured finite watermark instead of an unbounded moving queue.

- [ ] **Step 6: Extend live diagnostics**

Publish stage, generation, seal version, retry count, failure, all queue counts, captured watermark, and server acknowledgement in both live runtime surfaces.

- [ ] **Step 7: Run changed validation and verify GREEN**

Expected: server readiness self-tests, linked state tests, client build, and route contracts pass.

- [ ] **Step 8: Commit the server contract and save defense**

```powershell
git commit -m "feat: acknowledge and enforce campaign readiness seals"
```

---

### Task 7: Preparation Popup Progress and Retry

**Files:**
- Modify: `ReignBeta/src/UI/ReignNotableGenerationPopupManager.cs`
- Modify: `ReignBeta/GUI/Prefabs/ReignNotableGenerationPopup.xml`
- Modify: `ReignBeta/src/Campaign/ReignCampaignPreparationCampaignBehavior.cs`

**Interfaces:**
- Produces `ReignNotableGenerationPopupManager.UpdateStatus(string title, string detail, int completed, int total, string error)`.
- View model exposes `Title`, `Detail`, `ProgressText`, `HasError`, and `ExecuteRetry`.

- [ ] **Step 1: Add source-contract assertions to the readiness tests**

Assert the popup source binds stage progress and that the only successful hide call occurs after `TrySealAndMarkReady`.

- [ ] **Step 2: Run changed validation and verify RED**

Expected: source-contract assertions fail because the current view model is empty.

- [ ] **Step 3: Implement bound stage progress**

Display the exact coordinator stage and bounded progress. On failure, keep the input lock, display the actionable error, and expose retry. Do not expose a continue/bypass command.

- [ ] **Step 4: Verify GREEN with changed validation**

Expected: UI source contracts and client build pass with zero warnings.

- [ ] **Step 5: Commit the popup integration**

```powershell
git commit -m "feat: show complete campaign preparation progress"
```

---

### Task 8: Full Verification, Deployment, and Live Acceptance

**Files:**
- Modify after successful live acceptance: `REIGN_ROADMAP.md`
- Read evidence: `.codex-build/reign-mcp/validation/<run-id>/validation-report.json`
- Backup installed artifacts under: `ReignBeta/staging/deployment-backups/<timestamp>-new-game-readiness-seal`

**Interfaces:**
- Consumes all prior tasks.
- Produces validated installed client/server artifacts and live acceptance evidence.

- [ ] **Step 1: Run the canonical `all` validation profile**

Call `mcp__reign.reign_validate` with profile `all`, Release, no restore, fail fast, and every changed path.

Expected: coverage complete, zero unmanaged projects, every build warning-free, every test and offline verification check passing.

- [ ] **Step 2: Preserve the failing live campaign and installed rollback artifacts**

Copy `BaseTwo.sav`, current client/server binaries, and relevant installed contracts into the timestamped backup directory. Record SHA-256 hashes.

- [ ] **Step 3: Close the game and visible Reign lifetime group before replacement**

Use the supported visible lifecycle. Do not start a hidden or second server.

- [ ] **Step 4: Deploy only artifacts from the successful all-profile validation report**

Verify installed hashes match the report artifacts before restart.

- [ ] **Step 5: Restart through the supported unified visible launch path**

Confirm `/health` reports the dedicated Control Center, server, and vector worker in one lifetime group.

- [ ] **Step 6: Verify partial-save recovery**

Load `BaseTwo`; confirm completed personalities are retained, missing stages resume behind the popup, the seal succeeds, and the campaign releases without replaying native baselines.

- [ ] **Step 7: Verify a fresh new campaign**

Create a disposable fresh campaign. Observe every stage and confirm:

- the popup remains open until seal acknowledgement;
- passive recurring producers do not run before release;
- save-critical queues are empty at release;
- optional portrait/vector work does not block release.

- [ ] **Step 8: Run immediate and repeated save/load cycles**

Save on the first playable frame, wait for finalization, load, then repeat at least twice while passive systems are active. Confirm every native save has a ready server point and no `Save Sync Deferred` warning occurs.

- [ ] **Step 9: Verify overwrite, deletion, and retention status**

Overwrite one test slot, delete another through Bannerlord, and confirm server point synchronization. Confirm the 15-state count remains correct.

- [ ] **Step 10: Run a fresh post-deployment `all` validation if deployment revealed any source correction**

Redeploy only if the resulting artifact hashes differ, then repeat affected live acceptance steps.

- [ ] **Step 11: Update the roadmap**

Add one dated progress note under the existing Save Sync fast-path entry with the root cause, readiness-seal behavior, validation report, deployment backup, and live save/load evidence. Do not create a duplicate item.

- [ ] **Step 12: Apply verification-before-completion**

Read the verification skill, inspect fresh health/status/log/hash evidence, and make no completion claim unless the complete evidence remains green.
