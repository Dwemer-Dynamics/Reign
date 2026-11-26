using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Server;
using static Reign.Mcp.Server.CampaignTestCheckpointOwnership;

namespace Reign.Mcp.Server;

[McpServerToolType]
public static class CampaignTestTools
{
    [McpServerTool(Name = "reign_prepare_campaign_test", ReadOnly = false,
        Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Enrolls one exact campaign, timeline, user-provided immutable baseline save, run id, and descriptive objective for guarded autonomous testing. It does not arm the bridge or mutate Bannerlord.")]
    public static IReadOnlyDictionary<string, object?> PrepareCampaignTest(
        CampaignTestService service,
        string campaignId,
        string timelineId,
        string baselineSaveName,
        string runId,
        [Description("Human-readable test objective used to create identifiable disposable save names.")]
        string testName)
    {
        return service.Prepare(campaignId, timelineId, baselineSaveName, runId, testName);
    }

    [McpServerTool(Name = "reign_start_campaign_test", ReadOnly = false,
        Destructive = true, Idempotent = false, UseStructuredContent = true)]
    [Description("Starts Bannerlord visibly on the exact enrolled baseline/current checkpoint, or attaches when Bannerlord is already running. Exact save verification occurs during arming.")]
    public static Task<IReadOnlyDictionary<string, object?>> StartCampaignTest(
        CampaignTestService service,
        string runId,
        [Description("Exact text required: start Reign campaign test on enrolled save")]
        string confirmation = "",
        [Description("Seconds allowed for native campaign load, from 60 through 900.")]
        int waitSeconds = 300,
        CancellationToken cancellationToken = default)
    {
        return service.StartAsync(runId, confirmation, waitSeconds, cancellationToken);
    }

    [McpServerTool(Name = "reign_arm_campaign_test", ReadOnly = false,
        Destructive = true, Idempotent = false, UseStructuredContent = true)]
    [Description("Arms the live bridge, verifies the actual loaded save/campaign/timeline and Save Sync, and creates the descriptive run-owned disposable Current copy before advancement.")]
    public static Task<IReadOnlyDictionary<string, object?>> ArmCampaignTest(
        CampaignTestService service,
        string runId,
        [Description("Exact text required: arm Reign campaign test on disposable save")]
        string confirmation = "",
        [Description("Bridge authorization duration in minutes, from 5 through 720.")]
        int minutes = 720,
        CancellationToken cancellationToken = default)
    {
        return service.ArmAsync(runId, confirmation, minutes, cancellationToken);
    }

    [McpServerTool(Name = "reign_get_campaign_test_status", ReadOnly = true,
        Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Returns durable enrollment, current native runtime/world status, active save identity, queue health, and Save Sync checkpoint capacity for one campaign-test run.")]
    public static Task<IReadOnlyDictionary<string, object?>> GetCampaignTestStatus(
        CampaignTestService service,
        string runId,
        CancellationToken cancellationToken = default)
    {
        return service.StatusAsync(runId, cancellationToken);
    }

    [McpServerTool(Name = "reign_advance_campaign_test", ReadOnly = false,
        Destructive = true, Idempotent = false, UseStructuredContent = true)]
    [Description("Advances an armed disposable campaign by days or to a target day, pauses at the boundary, drains all required queues, and optionally overwrites the rolling checkpoint. Interrupted controllers automatically cancel the exact native run and attempt a cancellation-independent drain without saving.")]
    public static Task<IReadOnlyDictionary<string, object?>> AdvanceCampaignTest(
        CampaignTestService service,
        string runId,
        [Description("Exact text required: advance Reign campaign test on disposable save")]
        string confirmation = "",
        [Description("Relative campaign days. Supply this or targetDay, never both.")]
        double? days = null,
        [Description("Absolute native campaign day. Supply this or days, never both.")]
        double? targetDay = null,
        [Description("none leaves the boundary unsaved; rolling overwrites the run-owned Current checkpoint.")]
        [AllowedValues("none", "rolling")]
        string checkpointMode = "none",
        [Description("Advancement timeout in seconds, from 120 through 21600.")]
        int timeoutSeconds = 7200,
        [Description("Queue catch-up timeout in seconds, from 60 through 3600.")]
        int catchUpTimeoutSeconds = 900,
        [Description("Preserve automatic government news for player observation. Default false; requires checkpointMode none and manual reading/acknowledgment before advancement can continue.")]
        bool observeGovernmentNews = false,
        CancellationToken cancellationToken = default)
    {
        return service.AdvanceAsync(runId, confirmation, days, targetDay,
            checkpointMode, timeoutSeconds, catchUpTimeoutSeconds, cancellationToken, observeGovernmentNews);
    }

    [McpServerTool(Name = "reign_stop_campaign_test", ReadOnly = false,
        Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Preempts an active passive-world advance, pauses an armed campaign test, and requires the quiescence gate to drain before returning.")]
    public static Task<IReadOnlyDictionary<string, object?>> StopCampaignTest(
        CampaignTestService service,
        string runId,
        [Description("Exact text required: stop and drain Reign campaign test")]
        string confirmation = "",
        [Description("Queue catch-up timeout in seconds, from 60 through 3600.")]
        int catchUpTimeoutSeconds = 900,
        CancellationToken cancellationToken = default)
    {
        return service.StopAsync(runId, confirmation, catchUpTimeoutSeconds, cancellationToken);
    }

    [McpServerTool(Name = "reign_checkpoint_campaign_test", ReadOnly = false,
        Destructive = true, Idempotent = false, UseStructuredContent = true)]
    [Description("Drains queues and saves either the rolling Current checkpoint or a capacity-checked milestone inside the exact enrolled test namespace.")]
    public static Task<IReadOnlyDictionary<string, object?>> CheckpointCampaignTest(
        CampaignTestService service,
        string runId,
        [Description("Exact text required: save Reign campaign-test checkpoint")]
        string confirmation = "",
        [Description("Optional alphanumeric milestone label. Empty overwrites Current.")]
        string milestoneLabel = "",
        [Description("Save completion timeout in seconds, from 180 through 900.")]
        int saveTimeoutSeconds = 420,
        [Description("Queue catch-up timeout in seconds, from 60 through 3600.")]
        int catchUpTimeoutSeconds = 900,
        CancellationToken cancellationToken = default)
    {
        return service.CheckpointAsync(runId, confirmation, milestoneLabel,
            saveTimeoutSeconds, catchUpTimeoutSeconds, cancellationToken);
    }

    [McpServerTool(Name = "reign_restart_campaign_test", ReadOnly = false,
        Destructive = true, Idempotent = false, UseStructuredContent = true)]
    [Description("Restarts Bannerlord visibly onto the exact checkpointed run-owned Current save and returns a durable reload receipt. It refuses an uncheckpointed or unarmed campaign test.")]
    public static Task<IReadOnlyDictionary<string, object?>> RestartCampaignTest(
        CampaignTestService service,
        string runId,
        [Description("Exact text required: restart Reign campaign test on checkpointed disposable save")]
        string confirmation = "",
        int waitSeconds = 600,
        CancellationToken cancellationToken = default) =>
        service.RestartAsync(runId, confirmation, waitSeconds, cancellationToken);

    [McpServerTool(Name = "reign_restore_campaign_test_checkpoint", ReadOnly = false,
        Destructive = true, Idempotent = false, UseStructuredContent = true)]
    [Description("Discards unsaved transient fixture state and visibly reloads the exact existing run-owned Current checkpoint. It never saves the current native state and refuses a missing, foreign, baseline, or unarmed checkpoint.")]
    public static Task<IReadOnlyDictionary<string, object?>> RestoreCampaignTestCheckpoint(
        CampaignTestService service,
        string runId,
        [Description("Exact text required: restore Reign campaign-test checkpoint without saving current state")]
        string confirmation = "",
        int waitSeconds = 600,
        CancellationToken cancellationToken = default) =>
        service.RestoreCheckpointAsync(runId, confirmation, waitSeconds,
            cancellationToken);

    [McpServerTool(Name = "reign_get_campaign_test_report", ReadOnly = true,
        Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Returns the durable campaign-test enrollment, checkpoint ownership, advancement/recovery ledger, catalog fingerprint, and current status evidence.")]
    public static Task<IReadOnlyDictionary<string, object?>> GetCampaignTestReport(
        CampaignTestService service,
        string runId,
        CancellationToken cancellationToken = default)
    {
        return service.ReportAsync(runId, cancellationToken);
    }

    [McpServerTool(Name = "reign_observe_held_notable_startup", ReadOnly = false,
        Destructive = false, Idempotent = false, UseStructuredContent = true)]
    [Description("Starts one read-only native trait observation for a held startup reservation on the exact owned and rolled-back Current. It creates only test evidence and cannot settle the reservation.")]
    public static async Task<ApiEnvelope> ObserveHeldNotableStartup(
        CampaignTestService service, ReignApiClient api, ReignMcpOptions options,
        string campaignId, string runId, string heroId,
        [Description("Exact text required: observe Reign held notable on disposable save")]
        string confirmation = "", CancellationToken cancellationToken = default)
    {
        if (!options.AllowVerificationControl)
            throw new InvalidOperationException("Verification control is disabled.");
        InputGuard.RequireConfirmation(confirmation, "observe Reign held notable on disposable save");
        heroId = InputGuard.OptionalIdentifier(heroId, nameof(heroId));
        if (heroId.Length == 0)
            throw new ArgumentException("An exact notable identifier is required.", nameof(heroId));
        var (enrollment, checkpoint, gameInstance, clientProcess) =
            await service.RequireNotableStartupObservationCheckpointAsync(runId, campaignId, cancellationToken).ConfigureAwait(false);
        const string mode = "notable_startup_observation";
        return await api.PostAsync("/tests/live/run/start", new
        {
            campaignId = enrollment.CampaignId, gameInstanceId = gameInstance,
            mode, presentation = "headless", effects = "observed", autoCompleteWhenIdle = true,
            enrollment = new
            {
                source = "reign_mcp_notable_startup_observation",
                campaignId = enrollment.CampaignId,
                campaignTestRunId = enrollment.CampaignTestRunId,
                timelineId = enrollment.TimelineId,
                disposableSaveName = enrollment.CurrentSaveName,
                protectedBaselineSaveName = enrollment.BaselineSaveName,
                savePointId = checkpoint.SavePointId,
                checkpointSha256 = checkpoint.Sha256.ToLowerInvariant(),
                heroId,
                gameInstanceId = gameInstance,
                clientProcessInstanceId = clientProcess,
                origin = options.ServerBaseUri.GetLeftPart(UriPartial.Authority)
            },
            steps = new[] { new { operation = mode } }
        }, cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "reign_cleanup_campaign_test", ReadOnly = false,
        Destructive = true, Idempotent = true, UseStructuredContent = true)]
    [Description("Deletes only exact checkpoints owned by an enrolled run. It refuses the baseline, unrelated saves, and the currently loaded save.")]
    public static Task<IReadOnlyDictionary<string, object?>> CleanupCampaignTest(
        CampaignTestService service,
        string runId,
        [Description("Exact text required: delete Reign campaign-test disposable saves")]
        string confirmation = "",
        CancellationToken cancellationToken = default)
    {
        return service.CleanupAsync(runId, confirmation, cancellationToken);
    }
}

public sealed class CampaignTestService
{
    private const int SaveSyncLimit = 15;
    private static readonly object StateGate = new();
    private static readonly JsonSerializerOptions StateJson = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly ReignMcpOptions _options;
    private readonly ReignProcessRunner _runner;
    private readonly ReignApiClient _api;
    private readonly TestingCatalogService _catalog;

    public CampaignTestService(ReignMcpOptions options, ReignProcessRunner runner,
        ReignApiClient api, TestingCatalogService catalog)
    {
        _options = options;
        _runner = runner;
        _api = api;
        _catalog = catalog;
    }

    public CampaignTestAuthorization RequireEnrollment(string runId, string expectedCampaignId)
    {
        CampaignTestState state = ReadState(runId);
        expectedCampaignId = RequiredId(expectedCampaignId, nameof(expectedCampaignId));
        if (!string.Equals(state.CampaignId, expectedCampaignId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The feature test campaign does not match the guarded campaign-test enrollment.");
        return CampaignTestAuthorization.From(state);
    }

    public async Task<CampaignTestAuthorization> RequireArmedOwnedSaveAsync(string runId,
        string expectedCampaignId, CancellationToken cancellationToken)
    {
        CampaignTestState state = RequireArmed(runId);
        expectedCampaignId = RequiredId(expectedCampaignId, nameof(expectedCampaignId));
        if (!string.Equals(state.CampaignId, expectedCampaignId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The feature test campaign does not match the guarded campaign-test enrollment.");
        ControllerCall snapshot = await VerifyActiveOwnedSaveAsync(state, cancellationToken).ConfigureAwait(false);
        if (!SameName(snapshot.FindString("activeSaveName"), state.CurrentSaveName))
            throw new InvalidOperationException("Feature tests require the exact proven Current checkpoint to be loaded.");
        state = RequireArmed(runId);
        ValidateSnapshot(state, snapshot, allowBaseline: false);
        return CampaignTestAuthorization.From(state);
    }

    // Paused action fixtures may need a field encounter. Autonomous advancement
    // continues to use RequireArmedOwnedSaveAsync and its safe-settlement gate.
    public async Task<CampaignTestAuthorization> RequireArmedOwnedPausedActionSaveAsync(string runId,
        string expectedCampaignId, CancellationToken cancellationToken)
        => (await RequireArmedOwnedPausedActionCheckpointAsync(runId, expectedCampaignId, false, cancellationToken)
            .ConfigureAwait(false)).Authorization;

    internal async Task<(CampaignTestAuthorization Authorization, CampaignCheckpointReceipt Checkpoint, JsonElement? LoadOwner)>
        RequireArmedOwnedPausedActionCheckpointAsync(string runId, string expectedCampaignId, bool bindCheckpoint,
            CancellationToken cancellationToken, bool postArmCurrentDiagnostic = false)
    {
        CampaignTestState state = RequireArmed(runId);
        expectedCampaignId = RequiredId(expectedCampaignId, nameof(expectedCampaignId));
        if (!string.Equals(state.CampaignId, expectedCampaignId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The feature test campaign does not match the guarded campaign-test enrollment.");
        ControllerCall snapshot = await SnapshotAsync(state, cancellationToken, postArmCurrentDiagnostic).ConfigureAwait(false);
        ValidatePausedActionSnapshot(state, snapshot);
        CampaignCheckpointReceipt checkpoint = await VerifyReloadableCurrentAsync(state, cancellationToken)
            .ConfigureAwait(false);
        state = RequireArmed(runId);
        ValidatePausedActionSnapshot(state, snapshot);
        if (bindCheckpoint && Find(state, state.CurrentSaveName) != checkpoint)
            throw new InvalidOperationException("The enrolled Current checkpoint changed during verification.");
        JsonElement? loadOwner = postArmCurrentDiagnostic
            ? ReadPausedActionDiagnosticOwner(state, checkpoint, snapshot)
            : null;
        return (CampaignTestAuthorization.From(state), checkpoint, loadOwner);
    }

    internal async Task<(CampaignTestAuthorization Enrollment, CampaignCheckpointReceipt Checkpoint, string GameInstance, string ClientProcess)>
        RequireNotableStartupObservationCheckpointAsync(string runId, string campaignId, CancellationToken cancellationToken)
    {
        CampaignTestState state = RequireArmed(runId);
        if (state.CampaignId != RequiredId(campaignId, nameof(campaignId)))
            throw new InvalidOperationException("The observation campaign differs from the armed enrollment.");
        await RequireUnifiedHealthAsync(cancellationToken).ConfigureAwait(false);
        CampaignCheckpointReceipt checkpoint = await VerifyReloadableCurrentAsync(state, cancellationToken).ConfigureAwait(false);
        ApiEnvelope observed = await _api.GetAsync("/tests/live/runtime",
            new Dictionary<string, string?> { ["campaignId"] = state.CampaignId }, cancellationToken).ConfigureAwait(false);
        if (!observed.Ok || observed.Data is not { ValueKind: JsonValueKind.Object } envelope || !IsTrue(envelope, "gameOnline")
            || !envelope.TryGetProperty("runtime", out JsonElement runtime) || runtime.ValueKind != JsonValueKind.Object
            || Text(runtime, "campaignId") != state.CampaignId || Text(runtime, "activeSaveName") != state.CurrentSaveName
            || !runtime.TryGetProperty("saveSync", out JsonElement sync) || sync.ValueKind != JsonValueKind.Object
            || Text(sync, "timelineId") != state.TimelineId || !IsTrue(sync, "ready")
            || !sync.TryGetProperty("alignmentPending", out JsonElement pending) || pending.ValueKind != JsonValueKind.False
            || !runtime.TryGetProperty("executionIdentity", out JsonElement execution) || execution.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("The native heartbeat does not identify the aligned, run-owned Current.");

        string instance = Text(runtime, "gameInstanceId");
        string client = Text(execution, "clientProcessInstanceId");
        CampaignTestState retained = RequireArmed(runId);
        if (instance.Length == 0 || !Guid.TryParseExact(client, "N", out _)
            || CampaignTestAuthorization.From(state) != CampaignTestAuthorization.From(retained)
            || Find(retained, retained.CurrentSaveName) != checkpoint)
            throw new InvalidOperationException("The exact native observation owner changed during checkpoint verification.");
        // Rollback identity comes from the server's registered receipt. The issued command
        // then cross-checks that identity against the native coordinator on the game thread.
        return (CampaignTestAuthorization.From(retained), checkpoint, instance, client);
    }

    internal static void ValidatePausedActionSnapshot(CampaignTestState state, ControllerCall snapshot)
    {
        ValidateSnapshot(state, snapshot, allowBaseline: false, requireSafeSettlement: false);
        if (!string.Equals(snapshot.FindString("activeSaveName"), state.CurrentSaveName, StringComparison.Ordinal))
            throw new InvalidOperationException("Paused action tests require the exact enrolled Current save.");
        if (!string.Equals(snapshot.FindString("timeMode"), "Stop", StringComparison.Ordinal))
            throw new InvalidOperationException("Paused action tests require stopped native campaign time.");
    }

    public void RequireRetainedRecoveryCheckpoint(CampaignTestAuthorization authorization)
    {
        lock (StateGate)
            RequireRetainedRecoveryCheckpoint(ReadState(authorization.CampaignTestRunId), authorization);
    }

    internal static void RequireRetainedRecoveryCheckpoint(CampaignTestState state,
        CampaignTestAuthorization authorization)
    {
        if (!state.Armed || CampaignTestAuthorization.From(state) != authorization
            || string.IsNullOrWhiteSpace(state.SavePrefix)
            || state.CurrentSaveName != state.SavePrefix + "_Current"
            || string.Equals(state.CurrentSaveName, state.BaselineSaveName, StringComparison.OrdinalIgnoreCase)
            || Find(state, state.CurrentSaveName) is null || state.PendingCheckpoint is not null)
            throw new InvalidOperationException("Siege setup requires the exact armed run-owned Current recovery checkpoint.");
        bool saved = state.Events.Any(e =>
        {
            if (e.Operation != "checkpoint" || e.Evidence is null) return false;
            JsonElement evidence = JsonSerializer.SerializeToElement(e.Evidence);
            return evidence.ValueKind == JsonValueKind.Object
                && evidence.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True
                && evidence.TryGetProperty("activeSaveName", out var name) && name.ValueKind == JsonValueKind.String
                && string.Equals(name.GetString(), state.CurrentSaveName, StringComparison.OrdinalIgnoreCase);
        });
        if (!saved)
            throw new InvalidOperationException("Save a drained Current checkpoint before preparing a siege fixture.");
    }

    public IReadOnlyDictionary<string, object?> Prepare(string campaignId, string timelineId,
        string baselineSaveName, string runId, string testName)
    {
        campaignId = RequiredId(campaignId, nameof(campaignId));
        timelineId = RequiredId(timelineId, nameof(timelineId), 512);
        runId = RequiredId(runId, nameof(runId));
        baselineSaveName = RequiredSaveName(baselineSaveName, nameof(baselineSaveName));
        testName = InputGuard.BoundedText(testName, nameof(testName), 80);
        string slug = TaskSlug(testName);
        if (slug.Length == 0) throw new ArgumentException("testName must contain letters or digits.", nameof(testName));
        string suffix = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(runId)))
            .ToLowerInvariant()[..8];
        string prefix = $"ReignTest_{slug}_{suffix}";
        string current = prefix + "_Current";
        RequireDistinctBaseline(baselineSaveName, current);
        if (current.Length > 64) throw new InvalidOperationException("The generated test save name exceeds Bannerlord's 64-character limit.");

        lock (StateGate)
        {
            CampaignTestState? existing = TryReadState(runId);
            if (existing is not null)
            {
                if (!string.Equals(existing.CampaignId, campaignId, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(existing.TimelineId, timelineId, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(existing.BaselineSaveName, baselineSaveName, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(existing.TestName, testName, StringComparison.Ordinal))
                    throw new InvalidOperationException("The run id is already enrolled with different immutable campaign-test inputs.");
                RequireDistinctBaseline(existing.BaselineSaveName, existing.CurrentSaveName);
                return StateEnvelope(existing, "already_prepared");
            }
            RequireVacant(current, NativeHash(current), registered: false);
            var state = new CampaignTestState
            {
                Schema = "reign-campaign-test-state-v1",
                RunId = runId,
                CampaignId = campaignId,
                TimelineId = timelineId,
                BaselineSaveName = baselineSaveName,
                TestName = testName,
                SavePrefix = prefix,
                CurrentSaveName = current,
                Status = "prepared",
                PreparedUtc = DateTime.UtcNow.ToString("O"),
                UpdatedUtc = DateTime.UtcNow.ToString("O")
            };
            AddEvent(state, "prepared", $"Enrolled immutable baseline {baselineSaveName}.", null);
            WriteState(state);
            return StateEnvelope(state, "prepared");
        }
    }

    public async Task<IReadOnlyDictionary<string, object?>> StartAsync(string runId,
        string confirmation, int waitSeconds, CancellationToken cancellationToken)
    {
        RequireControl(confirmation, "start Reign campaign test on enrolled save");
        waitSeconds = InputGuard.Range(waitSeconds, nameof(waitSeconds), 60, 900);
        using var operation = AcquireSaveOperation(runId);
        CampaignTestState state = ReadState(runId);
        RequireResolved(state);
        RequireDistinctBaseline(state.BaselineSaveName, state.CurrentSaveName);
        CampaignCheckpointReceipt? current = Find(state, state.CurrentSaveName);
        if (state.Armed || current is not null)
            current = await VerifyReloadableCurrentAsync(state, cancellationToken).ConfigureAwait(false);
        await RequireUnifiedHealthAsync(cancellationToken).ConfigureAwait(false);
        ControllerCall status = await RunControllerAsync(new[] { "game", "status" }, 60,
            cancellationToken).ConfigureAwait(false);
        string lifecycleStatus = status.FindString("status");
        string expectedSave = current?.Attempt.SaveName ?? state.BaselineSaveName;
        ControllerCall result;
        if (status.FindBoolean("running"))
        {
            if (!string.Equals(lifecycleStatus, "campaign_ready", StringComparison.Ordinal)
                || !string.Equals(status.FindString("activeSaveName"), expectedSave,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "Bannerlord is already running without the exact enrolled campaign-test save. Stop it without saving, then start this campaign-test run again.");
            result = status;
        }
        else
        {
            var args = new List<string> { "game", "start", "--save", expectedSave,
                "--wait", waitSeconds.ToString(), "--save-sync-grace", "420" };
            if (current is not null) args.AddRange(["--owned-save-sha256", current.Sha256]);
            result = await RunControllerAsync(args, waitSeconds + 450, cancellationToken).ConfigureAwait(false);
        }
        if (result.Ok)
        {
            // The native world_snapshot operation needs an armed bridge. Start
            // precedes arm, so verify the fresh read-only heartbeat here; arm
            // will independently require the full native snapshot before it
            // creates or advances any disposable save.
            await VerifyStartedRuntimeAsync(state, expectedSave,
                cancellationToken).ConfigureAwait(false);
        }
        lock (StateGate)
        {
            state = ReadState(runId);
            AddEvent(state, "start", result.Ok ? "Bannerlord start/attach completed." : "Bannerlord start/attach failed.", result.Summary());
            state.Status = result.Ok ? "started" : "start_failed";
            WriteState(state);
        }
        return OperationEnvelope(state, "start", result);
    }

    private async Task VerifyStartedRuntimeAsync(CampaignTestState state,
        string expectedSave, CancellationToken cancellationToken)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(120);
        while (true)
        {
            ApiEnvelope observed = await _api.GetAsync("/tests/live/runtime",
                new Dictionary<string, string?> { ["campaignId"] = state.CampaignId },
                cancellationToken).ConfigureAwait(false);
            if (observed.Ok && observed.Data.HasValue)
            {
                JsonElement envelope = observed.Data.Value;
                if (envelope.TryGetProperty("gameOnline", out JsonElement online)
                    && online.ValueKind == JsonValueKind.True
                    && envelope.TryGetProperty("runtime", out JsonElement runtime)
                    && runtime.ValueKind == JsonValueKind.Object)
                {
                    string campaignId = runtime.TryGetProperty("campaignId", out JsonElement campaign)
                        ? campaign.GetString() ?? string.Empty : string.Empty;
                    string saveName = runtime.TryGetProperty("activeSaveName", out JsonElement save)
                        ? save.GetString() ?? string.Empty : string.Empty;
                    if (!string.Equals(campaignId, state.CampaignId,
                            StringComparison.OrdinalIgnoreCase)
                        || !string.Equals(saveName, expectedSave,
                            StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException(
                            "Bannerlord loaded a campaign or native save outside this exact enrollment.");
                    if (runtime.TryGetProperty("saveSync", out JsonElement saveSync)
                        && saveSync.ValueKind == JsonValueKind.Object
                        && saveSync.TryGetProperty("timelineId", out JsonElement timeline)
                        && string.Equals(timeline.GetString(), state.TimelineId,
                            StringComparison.OrdinalIgnoreCase)
                        && saveSync.TryGetProperty("ready", out JsonElement ready)
                        && ready.ValueKind == JsonValueKind.True
                        && saveSync.TryGetProperty("alignmentPending", out JsonElement pending)
                        && pending.ValueKind == JsonValueKind.False
                        && runtime.TryGetProperty("location", out JsonElement location)
                        && location.ValueKind == JsonValueKind.Object
                        && location.TryGetProperty("settlementId", out JsonElement settlement)
                        && !string.IsNullOrWhiteSpace(settlement.GetString())
                        && ((location.TryGetProperty("inTown", out JsonElement inTown)
                                && inTown.ValueKind == JsonValueKind.True)
                            || (settlement.GetString() ?? string.Empty).StartsWith(
                                "castle_", StringComparison.OrdinalIgnoreCase))
                        && location.TryGetProperty("onOpenMap", out JsonElement onMap)
                        && onMap.ValueKind == JsonValueKind.False)
                        return;
                }
            }
            if (DateTime.UtcNow >= deadline)
                throw new InvalidOperationException(
                    "Bannerlord did not publish a fresh, aligned heartbeat for the enrolled save and safe settlement.");
            await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyDictionary<string, object?>> ArmAsync(string runId,
        string confirmation, int minutes, CancellationToken cancellationToken)
    {
        RequireControl(confirmation, "arm Reign campaign test on disposable save");
        minutes = InputGuard.Range(minutes, nameof(minutes), 5, 720);
        using var operation = AcquireSaveOperation(runId);
        CampaignTestState state = ReadState(runId);
        RequireResolved(state);
        RequireDistinctBaseline(state.BaselineSaveName, state.CurrentSaveName);
        bool hasCurrent = Find(state, state.CurrentSaveName) is not null;
        if (!hasCurrent)
        {
            if (state.Armed || state.Checkpoints.Count > 0)
                throw new InvalidOperationException("This enrollment has no provable Current checkpoint. Preserve its saves and use a fresh enrollment.");
            RequireVacant(state.CurrentSaveName, NativeHash(state.CurrentSaveName), registered: false);
        }
        await RequireUnifiedHealthAsync(cancellationToken).ConfigureAwait(false);
        if (hasCurrent)
            await VerifyReloadableCurrentAsync(state, cancellationToken).ConfigureAwait(false);
        await VerifyStartedRuntimeAsync(state, hasCurrent ? state.CurrentSaveName : state.BaselineSaveName,
            cancellationToken).ConfigureAwait(false);
        ControllerCall armed = await RunControllerAsync(new[] { "arm", "--minutes", minutes.ToString() },
            60, cancellationToken).ConfigureAwait(false);
        if (!armed.Ok) return OperationEnvelope(state, "arm", armed);
        ControllerCall snapshot = await SnapshotAsync(state, cancellationToken).ConfigureAwait(false);
        ValidateSnapshot(state, snapshot, allowBaseline: true);
        string active = snapshot.FindString("activeSaveName");
        if (string.Equals(active, state.BaselineSaveName, StringComparison.OrdinalIgnoreCase))
        {
            if (state.Armed || state.Checkpoints.Count > 0 || state.CheckpointReceipts.Count > 0)
                throw new InvalidOperationException("This enrollment has checkpoint history. Load its proven Current or use a fresh enrollment.");
            CampaignCheckpointAttempt attempt = await BeginCheckpointAsync(state, snapshot,
                state.CurrentSaveName, cancellationToken).ConfigureAwait(false);
            ControllerCall initial = await RunControllerAsync(new[] { "world-test", "checkpoint",
                "--campaign", state.CampaignId, "--timeline", state.TimelineId,
                "--save", state.CurrentSaveName, "--save-timeout", "420",
                "--catch-up-timeout", "900", "--initial-baseline", "--checkpoint-ownership",
                JsonSerializer.Serialize(attempt, StateJson) }, 1380, cancellationToken).ConfigureAwait(false);
            state = await FinishCheckpointAsync(state, attempt, initial, cancellationToken).ConfigureAwait(false);
            if (!initial.Ok) return OperationEnvelope(state, "initial_checkpoint", initial);
            snapshot = await SnapshotAsync(state, cancellationToken).ConfigureAwait(false);
        }
        ValidateSnapshot(state, snapshot, allowBaseline: false);
        if (!SameName(snapshot.FindString("activeSaveName"), state.CurrentSaveName))
            throw new InvalidOperationException("Arming requires the exact proven Current to be loaded.");
        await VerifyReloadableCurrentAsync(state, cancellationToken).ConfigureAwait(false);
        lock (StateGate)
        {
            state = ReadState(runId);
            state.Armed = true;
            state.Status = "armed";
            AddEvent(state, "armed", "Verified campaign test and disposable save isolation.", snapshot.Summary());
            WriteState(state);
        }
        return OperationEnvelope(state, "armed", snapshot, new Dictionary<string, object?>
        {
            ["bridge"] = armed.AsObject()
        });
    }

    public async Task<IReadOnlyDictionary<string, object?>> StatusAsync(string runId,
        CancellationToken cancellationToken)
    {
        CampaignTestState state = ReadState(runId);
        ControllerCall status = await RunControllerAsync(new[] { "world-test", "status",
            "--campaign", state.CampaignId, "--timeline", state.TimelineId }, 90,
            cancellationToken).ConfigureAwait(false);
        IReadOnlyDictionary<string, object?> capacity = await SaveCapacityAsync(state, state.CurrentSaveName, cancellationToken)
            .ConfigureAwait(false);
        return OperationEnvelope(state, "status", status, new Dictionary<string, object?>
        {
            ["checkpointCapacity"] = capacity
        });
    }

    public async Task<IReadOnlyDictionary<string, object?>> AdvanceAsync(string runId,
        string confirmation, double? days, double? targetDay, string checkpointMode,
        int timeoutSeconds, int catchUpTimeoutSeconds, CancellationToken cancellationToken,
        bool observeGovernmentNews = false)
    {
        RequireControl(confirmation, "advance Reign campaign test on disposable save");
        if (days.HasValue == targetDay.HasValue)
            throw new ArgumentException("Supply exactly one of days or targetDay.");
        if (days is < 0d or > 6300d) throw new ArgumentOutOfRangeException(nameof(days));
        if (targetDay is < 0d) throw new ArgumentOutOfRangeException(nameof(targetDay));
        checkpointMode = (checkpointMode ?? string.Empty).Trim().ToLowerInvariant();
        if (checkpointMode is not "none" and not "rolling")
            throw new ArgumentException("checkpointMode must be none or rolling.", nameof(checkpointMode));
        if (observeGovernmentNews && checkpointMode == "rolling")
            throw new ArgumentException("Government-news observation requires checkpointMode none (--no-save).");
        timeoutSeconds = InputGuard.Range(timeoutSeconds, nameof(timeoutSeconds), 120, 21600);
        catchUpTimeoutSeconds = InputGuard.Range(catchUpTimeoutSeconds, nameof(catchUpTimeoutSeconds), 60, 3600);
        using var operation = AcquireSaveOperation(runId);
        CampaignTestState state = RequireArmed(runId);
        ControllerCall snapshot = await VerifyActiveOwnedSaveAsync(state, cancellationToken).ConfigureAwait(false);
        CampaignCheckpointAttempt? attempt = null;
        if (checkpointMode == "rolling")
            attempt = await BeginCheckpointAsync(state, snapshot, state.CurrentSaveName, cancellationToken).ConfigureAwait(false);
        var args = new List<string> { "world-test", "advance", "--campaign", state.CampaignId,
            "--timeline", state.TimelineId, "--timeout", timeoutSeconds.ToString(),
            "--catch-up-timeout", catchUpTimeoutSeconds.ToString() };
        if (days.HasValue) { args.Add("--days"); args.Add(days.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)); }
        else { args.Add("--target-day"); args.Add(targetDay!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)); }
        if (attempt is not null)
        {
            args.AddRange(["--save", state.CurrentSaveName, "--checkpoint-ownership",
                JsonSerializer.Serialize(attempt, StateJson)]);
        }
        else args.Add("--no-save");
        args.AddRange(["--campaign-test-source", SerializeCheckpointSource(state, snapshot)]);
        if (observeGovernmentNews) args.Add("--observe-government-news");
        ControllerCall result;
        IReadOnlyDictionary<string, object?>? safetyRecovery = null;
        try
        {
            result = await RunControllerAsync(args,
                timeoutSeconds + catchUpTimeoutSeconds + 540,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            safetyRecovery = await RecoverInterruptedAdvanceAsync(state,
                catchUpTimeoutSeconds, observeGovernmentNews).ConfigureAwait(false);
            lock (StateGate)
            {
                state = ReadState(runId);
                state.Status = state.PendingCheckpoint is not null ? "checkpoint_uncertain"
                    : Convert.ToBoolean(safetyRecovery["ok"] ?? false) ? "advance_cancelled_safe"
                    : "advance_cancelled_recovery_failed";
                AddEvent(state, "advance",
                    "Campaign advancement controller was cancelled; guarded safety recovery was attempted.",
                    safetyRecovery);
                WriteState(state);
            }
            throw;
        }
        if (!result.Ok)
        {
            safetyRecovery = await RecoverInterruptedAdvanceAsync(state,
                catchUpTimeoutSeconds, observeGovernmentNews).ConfigureAwait(false);
        }
        if (attempt is not null)
            state = await FinishCheckpointAsync(state, attempt, result, cancellationToken).ConfigureAwait(false);
        lock (StateGate)
        {
            state = ReadState(runId);
            bool recoveryOk = safetyRecovery is not null
                && Convert.ToBoolean(safetyRecovery["ok"] ?? false);
            if (state.PendingCheckpoint is null)
                state.Status = result.Ok
                    ? "paused_caught_up"
                    : recoveryOk ? "advance_failed_safe" : "advance_failed_recovery_failed";
            AddEvent(state, "advance",
                result.Ok
                    ? "Campaign advancement reached a drained boundary."
                    : recoveryOk
                        ? "Campaign advancement failed; native time was cancelled and queues were drained safely."
                        : "Campaign advancement failed and its safety recovery did not prove a drained boundary.",
                safetyRecovery is null
                    ? result.Summary()
                    : new Dictionary<string, object?>
                    {
                        ["controller"] = result.Summary(),
                        ["safetyRecovery"] = safetyRecovery
                    });
            WriteState(state);
        }
        return OperationEnvelope(state, "advance", result,
            safetyRecovery is null
                ? null
                : new Dictionary<string, object?> { ["safetyRecovery"] = safetyRecovery });
    }

    public async Task<IReadOnlyDictionary<string, object?>> StopAsync(string runId,
        string confirmation, int catchUpTimeoutSeconds, CancellationToken cancellationToken)
    {
        RequireControl(confirmation, "stop and drain Reign campaign test");
        catchUpTimeoutSeconds = InputGuard.Range(catchUpTimeoutSeconds,
            nameof(catchUpTimeoutSeconds), 60, 3600);
        CampaignTestState state = RequireArmed(runId, allowPendingCheckpoint: true);
        IReadOnlyDictionary<string, object?> activeAdvancePreemption =
            await CancelActivePassiveWorldRunAsync(state).ConfigureAwait(false);
        if (!Convert.ToBoolean(activeAdvancePreemption["ok"] ?? false))
            throw new InvalidOperationException(
                "The active passive-world run could not be cancelled and paused before drain.");
        ControllerCall snapshot = await VerifyActiveOwnedSaveAsync(state, cancellationToken).ConfigureAwait(false);
        ControllerCall result = await RunControllerAsync(new[] { "world-test", "drain",
            "--campaign", state.CampaignId, "--timeline", state.TimelineId,
            "--catch-up-timeout", catchUpTimeoutSeconds.ToString(),
            "--campaign-test-source", SerializeCheckpointSource(state, snapshot) },
            catchUpTimeoutSeconds + 180, cancellationToken).ConfigureAwait(false);
        lock (StateGate)
        {
            state = ReadState(runId);
            if (state.PendingCheckpoint is null)
                state.Status = result.Ok ? "paused_caught_up" : "drain_failed";
            AddEvent(state, "stop", result.Ok ? "Campaign paused and queues drained." : "Campaign drain failed.", result.Summary());
            WriteState(state);
        }
        return OperationEnvelope(state, "stop", result,
            new Dictionary<string, object?>
            {
                ["activeAdvancePreemption"] = activeAdvancePreemption
            });
    }

    public Task<IReadOnlyDictionary<string, object?>> CheckpointAsync(string runId,
        string confirmation, string milestoneLabel, int saveTimeoutSeconds,
        int catchUpTimeoutSeconds, CancellationToken cancellationToken) =>
        CheckpointCoreAsync(runId, confirmation, milestoneLabel, saveTimeoutSeconds,
            catchUpTimeoutSeconds, cancellationToken);

    internal Task<IReadOnlyDictionary<string, object?>> CheckpointPartyAgencyPhaseAsync(string runId,
        string campaignId, string confirmation, PartyAgencyCheckpointStage stage, int saveTimeoutSeconds,
        int catchUpTimeoutSeconds, CancellationToken ct) =>
        CheckpointCoreAsync(runId, confirmation, "", saveTimeoutSeconds, catchUpTimeoutSeconds, ct, stage, campaignId);

    private async Task<IReadOnlyDictionary<string, object?>> CheckpointCoreAsync(string runId,
        string confirmation, string milestoneLabel, int saveTimeoutSeconds, int catchUpTimeoutSeconds,
        CancellationToken cancellationToken, PartyAgencyCheckpointStage? partyAgencyStage = null,
        string? expectedCampaignId = null)
    {
        RequireControl(confirmation, "save Reign campaign-test checkpoint");
        saveTimeoutSeconds = InputGuard.Range(saveTimeoutSeconds, nameof(saveTimeoutSeconds), 180, 900);
        catchUpTimeoutSeconds = InputGuard.Range(catchUpTimeoutSeconds,
            nameof(catchUpTimeoutSeconds), 60, 3600);
        using var operation = AcquireSaveOperation(runId);
        CampaignTestState state = RequireArmed(runId);
        if (partyAgencyStage is not null && state.CampaignId != expectedCampaignId)
            throw new InvalidOperationException("The Party Agency stage does not belong to the enrolled campaign.");
        ControllerCall snapshot = await VerifyActiveOwnedSaveAsync(state, cancellationToken).ConfigureAwait(false);
        string saveName = state.CurrentSaveName;
        if (!string.IsNullOrWhiteSpace(milestoneLabel))
        {
            string label = new string(milestoneLabel.Where(char.IsAsciiLetterOrDigit).Take(16).ToArray());
            if (label.Length == 0) throw new ArgumentException("milestoneLabel must contain ASCII letters or digits.", nameof(milestoneLabel));
            saveName = state.SavePrefix + "_" + label;
            if (SameName(saveName, state.CurrentSaveName)) saveName = state.CurrentSaveName;
            if (saveName.Length > 64) throw new ArgumentException("The milestone save name exceeds 64 characters.", nameof(milestoneLabel));
        }
        CampaignCheckpointAttempt attempt = await BeginCheckpointAsync(state, snapshot, saveName,
            cancellationToken, partyAgencyStage).ConfigureAwait(false);
        ControllerCall result = await RunControllerAsync(new[] { "world-test", "checkpoint",
            "--campaign", state.CampaignId, "--timeline", state.TimelineId,
            "--save", saveName, "--save-timeout", saveTimeoutSeconds.ToString(),
            "--catch-up-timeout", catchUpTimeoutSeconds.ToString(), "--checkpoint-ownership",
            JsonSerializer.Serialize(attempt, StateJson) },
            saveTimeoutSeconds + catchUpTimeoutSeconds + 180, cancellationToken).ConfigureAwait(false);
        state = await FinishCheckpointAsync(state, attempt, result, cancellationToken).ConfigureAwait(false);
        lock (StateGate)
        {
            state = ReadState(runId);
            if (state.PendingCheckpoint is null)
                state.Status = result.Ok && SameName(saveName, state.CurrentSaveName) ? "checkpointed"
                    : result.Ok ? "milestone_checkpointed" : "checkpoint_failed";
            AddEvent(state, "checkpoint", result.Ok ? $"Saved {saveName}." : $"Checkpoint {saveName} failed.", result.Summary());
            WriteState(state);
        }
        return OperationEnvelope(state, "checkpoint", result, new Dictionary<string, object?>
        {
            ["saveName"] = saveName
        });
    }

    public async Task<IReadOnlyDictionary<string, object?>> RestartAsync(string runId,
        string confirmation, int waitSeconds, CancellationToken cancellationToken)
    {
        RequireControl(confirmation,
            "restart Reign campaign test on checkpointed disposable save");
        waitSeconds = InputGuard.Range(waitSeconds, nameof(waitSeconds), 60, 900);
        using var operation = AcquireSaveOperation(runId);
        CampaignTestState state = RequireArmed(runId);
        ControllerCall active = await VerifyActiveOwnedSaveAsync(state, cancellationToken).ConfigureAwait(false);
        if (!SameName(active.FindString("activeSaveName"), state.CurrentSaveName))
            throw new InvalidOperationException("Restart requires the exact Current checkpoint to be loaded.");
        CampaignCheckpointReceipt current = await VerifyReloadableCurrentAsync(state, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(state.Status, "checkpointed", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "The disposable Current save must be checkpointed and drained immediately before restart.");
        ControllerCall result = await RunControllerAsync(new[] { "game", "restart",
            "--save", state.CurrentSaveName, "--no-save", "--wait", waitSeconds.ToString(),
            "--save-sync-grace", "420", "--owned-save-sha256", current.Sha256,
            "--campaign", state.CampaignId, "--campaign-test-source", SerializeCheckpointSource(state, active) },
            waitSeconds + 600, cancellationToken)
            .ConfigureAwait(false);
        if (!result.Ok) return OperationEnvelope(state, "restart", result);
        ControllerCall snapshot = await SnapshotAsync(state, cancellationToken, postArmCurrentDiagnostic: true)
            .ConfigureAwait(false);
        VerifyReloadedCurrent(state, snapshot, active);
        string receipt = "campaign_test_restart_" + Guid.NewGuid().ToString("N");
        var evidence = new Dictionary<string, object?>
        {
            ["restartReceipt"] = receipt,
            ["saveName"] = state.CurrentSaveName,
            ["savePointId"] = current.SavePointId,
            ["restartEvidenceSchema"] = "reign-campaign-checkpoint-restart-v1",
            ["checkpointAttemptId"] = current.Attempt.Id,
            ["checkpointSha256"] = current.Sha256,
            ["checkpointGameInstanceId"] = active.FindString("checkpointGameInstanceId"),
            ["reloadedGameInstanceId"] = snapshot.FindString("checkpointGameInstanceId"),
            ["controller"] = result.Summary(),
            ["reloadedSnapshot"] = snapshot.Summary()
        };
        var currentObservation = ReadPostArmCurrentObservation(state, current, result, snapshot,
            active.FindString("checkpointGameInstanceId"), snapshot.FindString("checkpointGameInstanceId"));
        if (currentObservation is not null) evidence["postArmCurrentObservation"] = currentObservation;
        lock (StateGate)
        {
            state = ReadState(runId);
            state.Status = "restarted_checkpoint_loaded";
            AddEvent(state, "restart", "Restarted Bannerlord on the exact checkpointed Current save.", evidence);
            WriteState(state);
        }
        return OperationEnvelope(state, "restart", result, evidence);
    }

    public async Task<IReadOnlyDictionary<string, object?>> RestoreCheckpointAsync(
        string runId, string confirmation, int waitSeconds,
        CancellationToken cancellationToken)
    {
        RequireControl(confirmation,
            "restore Reign campaign-test checkpoint without saving current state");
        waitSeconds = InputGuard.Range(waitSeconds, nameof(waitSeconds), 60, 900);
        using var operation = AcquireSaveOperation(runId);
        CampaignTestState state = RequireArmed(runId);
        ControllerCall activeSnapshot = await SnapshotAsync(state, cancellationToken)
            .ConfigureAwait(false);
        ValidateSnapshot(state, activeSnapshot, allowBaseline: false,
            requireSafeSettlement: false);
        VerifyOwnedBytes(state, activeSnapshot.FindString("activeSaveName"));
        CampaignCheckpointReceipt current = await VerifyReloadableCurrentAsync(state, cancellationToken).ConfigureAwait(false);
        ControllerCall result = await RunControllerAsync(new[] { "game", "restart",
            "--save", state.CurrentSaveName, "--no-save", "--wait", waitSeconds.ToString(),
            "--save-sync-grace", "420", "--owned-save-sha256", current.Sha256,
            "--campaign", state.CampaignId, "--campaign-test-source", SerializeCheckpointSource(state, activeSnapshot) },
            waitSeconds + 600, cancellationToken)
            .ConfigureAwait(false);
        if (!result.Ok) return OperationEnvelope(state, "restore_checkpoint", result);
        ControllerCall snapshot = await SnapshotAsync(state, cancellationToken).ConfigureAwait(false);
        VerifyReloadedCurrent(state, snapshot, activeSnapshot);
        string receipt = "campaign_test_restore_" + Guid.NewGuid().ToString("N");
        var evidence = new Dictionary<string, object?>
        {
            ["restoreReceipt"] = receipt,
            ["saveName"] = state.CurrentSaveName,
            ["discardedUnsavedState"] = true,
            ["controller"] = result.Summary(),
            ["reloadedSnapshot"] = snapshot.Summary()
        };
        lock (StateGate)
        {
            state = ReadState(runId);
            state.Status = "restored_checkpoint_loaded";
            AddEvent(state, "restore_checkpoint",
                "Discarded unsaved fixture state and restored the exact run-owned Current checkpoint.",
                evidence);
            WriteState(state);
        }
        return OperationEnvelope(state, "restore_checkpoint", result, evidence);
    }

    public CampaignTestAuthorization RequireRestartReceipt(string runId,
        string expectedCampaignId, string restartReceipt)
    {
        var (state, _, _) = ReadRestartReceipts(runId, expectedCampaignId, restartReceipt);
        return CampaignTestAuthorization.From(state);
    }

    internal (CampaignCheckpointReceipt Checkpoint, string ReloadedGameInstanceId)
        RequirePartyAgencyRestartProof(string runId, string expectedCampaignId, string restartReceipt)
    {
        var (state, checkpoint, receipts) = ReadRestartReceipts(runId, expectedCampaignId, restartReceipt);
        if (receipts.Count != 1)
            throw new InvalidOperationException("The request did not identify one unambiguous recorded restart.");
        if (state.CampaignId != expectedCampaignId || checkpoint.Attempt.PartyAgencyStage is not { } stage
            || stage.GameInstanceId != checkpoint.Attempt.GameInstanceId
            || !state.PartyAgencyCheckpointStageReceiptIds.Contains(stage.ReceiptId))
            throw new InvalidOperationException("The reloaded Current checkpoint is missing its durable Party Agency stage binding.");
        return (checkpoint, ReadRestartedGameInstance(checkpoint, receipts[0]));
    }

    private (CampaignTestState State, CampaignCheckpointReceipt Checkpoint, List<JsonElement> Receipts)
        ReadRestartReceipts(string runId, string expectedCampaignId, string restartReceipt)
    {
        CampaignTestState state = RequireArmed(runId);
        var checkpoint = VerifyOwnedBytes(state, state.CurrentSaveName);
        expectedCampaignId = RequiredId(expectedCampaignId, nameof(expectedCampaignId));
        restartReceipt = RequiredId(restartReceipt, nameof(restartReceipt));
        if (!string.Equals(state.CampaignId, expectedCampaignId,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "The reload receipt does not belong to the requested campaign test.");
        var receipts = new List<JsonElement>();
        foreach (CampaignTestEvent item in state.Events)
        {
            if (item.Operation != "restart" || item.Evidence is null) continue;
            JsonElement evidence = JsonSerializer.SerializeToElement(item.Evidence, StateJson);
            if (Text(evidence, "restartReceipt") == restartReceipt
                && Text(evidence, "savePointId") == checkpoint.SavePointId)
                receipts.Add(evidence);
        }
        if (receipts.Count == 0) throw new InvalidOperationException(
            "The supplied reload receipt was not issued by this guarded campaign test.");
        return (state, checkpoint, receipts);
    }

    public async Task<IReadOnlyDictionary<string, object?>> ReportAsync(string runId,
        CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<string, object?> status = await StatusAsync(runId, cancellationToken)
            .ConfigureAwait(false);
        CampaignTestState state = ReadState(runId);
        return new Dictionary<string, object?>
        {
            ["ok"] = true,
            ["schema"] = "reign-campaign-test-report-v1",
            ["state"] = state,
            ["status"] = status,
            ["testingCatalog"] = _catalog.GetCatalog("campaign")
        };
    }

    public async Task<IReadOnlyDictionary<string, object?>> CleanupAsync(string runId,
        string confirmation, CancellationToken cancellationToken)
    {
        RequireControl(confirmation, "delete Reign campaign-test disposable saves");
        using var operation = AcquireSaveOperation(runId);
        CampaignTestState state = ReadState(runId);
        RequireResolved(state);
        RequireDistinctBaseline(state.BaselineSaveName, state.CurrentSaveName);
        ControllerCall snapshot = await SnapshotAsync(state, cancellationToken).ConfigureAwait(false);
        if (!snapshot.Ok || !SameName(snapshot.FindString("campaignId"), state.CampaignId)
            || !SameName(snapshot.FindString("timelineId"), state.TimelineId))
            throw new InvalidOperationException("Cleanup requires a verified native snapshot of the enrolled campaign and timeline.");
        string active = snapshot.FindString("activeSaveName");
        if (string.IsNullOrWhiteSpace(active) || state.Checkpoints.Any(save => SameName(save, active)))
            throw new InvalidOperationException("Cleanup requires a known loaded save outside the run's checkpoints.");
        CampaignCheckpointReceipt[] receipts = state.Checkpoints.Select(save => VerifyOwnedBytes(state, save)).ToArray();
        var deleted = new List<object?>();
        foreach (CampaignCheckpointReceipt receipt in receipts)
        {
            string save = receipt.Attempt.SaveName;
            ControllerCall result = await RunControllerAsync(new[] { "world-test", "delete-checkpoint",
                "--campaign", state.CampaignId, "--timeline", state.TimelineId,
                "--save", save, "--prefix", state.SavePrefix, "--checkpoint-ownership",
                JsonSerializer.Serialize(new
                {
                    baselineSaveName = state.BaselineSaveName,
                    campaignId = state.CampaignId,
                    timelineId = state.TimelineId,
                    gameInstanceId = snapshot.FindString("checkpointGameInstanceId"),
                    savePointId = receipt.SavePointId, sha256 = receipt.Sha256
                }, StateJson) }, 180, cancellationToken).ConfigureAwait(false);
            deleted.Add(new Dictionary<string, object?> { ["saveName"] = save, ["result"] = result.AsObject() });
            if (!result.Ok) return OperationEnvelope(state, "cleanup", result,
                new Dictionary<string, object?> { ["deleted"] = deleted });
            lock (StateGate)
            {
                state = ReadState(runId);
                state.Checkpoints.RemoveAll(name => SameName(name, save));
                state.CheckpointReceipts.RemoveAll(prior => SameName(prior.Attempt.SaveName, save));
                state.Armed = false;
                state.Status = "cleanup_in_progress";
                AddEvent(state, "cleanup", $"Deleted verified checkpoint {save}.", result.Summary());
                WriteState(state);
            }
        }
        lock (StateGate)
        {
            state = ReadState(runId);
            state.Armed = false;
            state.Status = "cleaned";
            AddEvent(state, "cleanup", "Deleted all exact run-owned saves; baseline preserved.", null);
            WriteState(state);
        }
        return new Dictionary<string, object?>
        {
            ["ok"] = true,
            ["operation"] = "cleanup",
            ["baselinePreserved"] = state.BaselineSaveName,
            ["deleted"] = deleted,
            ["state"] = state
        };
    }

    private async Task<ControllerCall> VerifyActiveOwnedSaveAsync(CampaignTestState state,
        CancellationToken cancellationToken)
    {
        ControllerCall snapshot = await SnapshotAsync(state, cancellationToken).ConfigureAwait(false);
        ValidateSnapshot(state, snapshot, allowBaseline: false);
        var receipt = VerifyOwnedBytes(state, snapshot.FindString("activeSaveName"));
        await RequireRegisteredPointAsync(state, receipt, cancellationToken).ConfigureAwait(false);
        return snapshot;
    }

    private static CampaignCheckpointReceipt VerifyOwnedBytes(CampaignTestState state, string name)
    {
        var receipt = Find(state, name) ?? throw new InvalidOperationException(
            $"No durable campaign-test ownership receipt identifies {name}.");
        VerifyBytes(receipt, NativeHash(name));
        return receipt;
    }

    private static void VerifyReloadedCurrent(CampaignTestState state, ControllerCall snapshot,
        ControllerCall previous)
    {
        ValidateSnapshot(state, snapshot, allowBaseline: false);
        string previousInstance = previous.FindString("checkpointGameInstanceId");
        string currentInstance = snapshot.FindString("checkpointGameInstanceId");
        if (string.IsNullOrWhiteSpace(previousInstance) || string.IsNullOrWhiteSpace(currentInstance)
            || previousInstance == currentInstance)
            throw new InvalidOperationException("The checkpoint reload did not report a new native game instance.");
        if (!SameName(snapshot.FindString("activeSaveName"), state.CurrentSaveName))
            throw new InvalidOperationException("Bannerlord did not reload the exact run-owned Current checkpoint.");
        VerifyOwnedBytes(state, state.CurrentSaveName);
    }

    private async Task<CampaignCheckpointReceipt> VerifyReloadableCurrentAsync(CampaignTestState state,
        CancellationToken cancellationToken)
    {
        var receipt = VerifyOwnedBytes(state, state.CurrentSaveName);
        await RequireRegisteredPointAsync(state, receipt, cancellationToken).ConfigureAwait(false);
        return receipt;
    }

    private static string SerializeCheckpointSource(CampaignTestState state, ControllerCall snapshot) =>
        JsonSerializer.Serialize(new
        {
            campaignId = state.CampaignId, timelineId = state.TimelineId,
            sourceSaveName = snapshot.FindString("activeSaveName"),
            sourcePointId = snapshot.FindString("checkpointSavePointId"),
            gameInstanceId = snapshot.FindString("checkpointGameInstanceId")
        }, StateJson);

    private Task<ControllerCall> SnapshotAsync(CampaignTestState state,
        CancellationToken cancellationToken, bool postArmCurrentDiagnostic = false)
    {
        string[] args = ["world-test", "snapshot", "--campaign", state.CampaignId, "--timeline", state.TimelineId];
        if (postArmCurrentDiagnostic) args = [.. args, "--post-arm-current-diagnostic"];
        return RunControllerAsync(args, 180, cancellationToken);
    }

    internal static void ValidateSnapshot(CampaignTestState state, ControllerCall snapshot,
        bool allowBaseline, bool requireSafeSettlement = true)
    {
        if (!snapshot.Ok) throw new InvalidOperationException("The native campaign snapshot failed.");
        string campaign = snapshot.FindString("campaignId");
        string timeline = snapshot.FindString("timelineId");
        string active = snapshot.FindString("activeSaveName");
        if (!string.Equals(campaign, state.CampaignId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The loaded campaign does not match the enrolled campaign.");
        if (!string.Equals(timeline, state.TimelineId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The loaded timeline does not match the enrolled timeline.");
        RequireDistinctBaseline(state.BaselineSaveName, state.CurrentSaveName);
        if (!(allowBaseline && SameName(active, state.BaselineSaveName)))
        {
            var receipt = Find(state, active);
            if (receipt is null || receipt.SavePointId != snapshot.FindString("checkpointSavePointId"))
                throw new InvalidOperationException("The actual loaded Bannerlord save lacks matching native and run-owned checkpoint evidence.");
        }
        if (!snapshot.FindBoolean("saveSyncReady") || snapshot.FindBoolean("saveSyncAlignmentPending", fallback: true))
            throw new InvalidOperationException("Save Sync is not finalized and aligned for the loaded campaign.");
        if (requireSafeSettlement && !snapshot.FindBoolean("safeSettlement"))
            throw new InvalidOperationException("The player must be safely inside a town or castle before autonomous campaign testing.");
    }

    private async Task<CampaignCheckpointAttempt> BeginCheckpointAsync(CampaignTestState state,
        ControllerCall source, string destination, CancellationToken cancellationToken,
        PartyAgencyCheckpointStage? partyAgencyStage = null)
    {
        RequireDistinctBaseline(state.BaselineSaveName, destination);
        string instanceId = source.FindString("checkpointGameInstanceId");
        string sourceName = source.FindString("activeSaveName");
        if (string.IsNullOrWhiteSpace(instanceId))
            throw new InvalidOperationException("The native client did not report the checkpoint game instance.");
        if (NativeHash(sourceName) is null)
            throw new InvalidOperationException("The native source save is missing.");

        var receipt = Find(state, destination);
        var (point, count, _) = await ReadCheckpointSlotAsync(state.CampaignId, destination, cancellationToken)
            .ConfigureAwait(false);
        bool retained = point.HasValue && receipt is not null && MatchesPoint(point.Value, receipt);
        if (point.HasValue && !retained)
            RequireVacant(destination, nativeHash: null, registered: true);

        string? destinationHash = NativeHash(destination);
        if (receipt is null)
        {
            RequireVacant(destination, destinationHash, registered: false);
            if (state.Checkpoints.Any(name => SameName(name, destination)))
                throw new InvalidOperationException("A legacy checkpoint name in this run has no provable creation receipt.");
            if (count >= SaveSyncLimit)
                throw new InvalidOperationException("Save Sync already retains 15 unique states. A new checkpoint requires available capacity.");
        }
        else
        {
            VerifyBytes(receipt, destinationHash);
            if (!retained)
                throw new InvalidOperationException("The destination's run-owned Save Sync point is no longer available.");
        }

        var attempt = new CampaignCheckpointAttempt(Guid.NewGuid().ToString("N"), state.RunId,
            state.CampaignId, state.TimelineId, state.BaselineSaveName, destination, sourceName,
            source.FindString("checkpointSavePointId"), instanceId, receipt?.SavePointId ?? "",
            receipt?.Sha256 ?? "", DateTime.UtcNow.ToString("O"))
        {
            PartyAgencyStage = partyAgencyStage
        };
        lock (StateGate)
        {
            var latest = ReadState(state.RunId);
            RequireResolved(latest);
            ReservePartyAgencyStageReceipt(latest, attempt);
            latest.PendingCheckpoint = attempt;
            latest.Status = "checkpoint_pending";
            AddEvent(latest, "checkpoint_intent", "Persisted checkpoint intent before issuing native SaveAs.", attempt);
            WriteState(latest);
        }
        return attempt;
    }

    private async Task<CampaignTestState> FinishCheckpointAsync(CampaignTestState state,
        CampaignCheckpointAttempt attempt, ControllerCall result, CancellationToken cancellationToken)
    {
        CampaignCheckpointReceipt? receipt = null;
        bool notSubmitted = !result.Ok && result.Data is { ValueKind: JsonValueKind.Object } root
            && Text(root, "checkpointNotSubmitted") == attempt.Id;
        if (notSubmitted && attempt.PartyAgencyStage is not null)
        {
            // The exact no-submit witness alone cannot release a consumed stage.
            // Recheck the previous registered point and native bytes before reopening it.
            CampaignCheckpointReceipt? prior = Find(state, attempt.SaveName);
            notSubmitted = prior is not null && prior.SavePointId == attempt.PreviousDestinationPointId
                && string.Equals(prior.Sha256, attempt.PreviousDestinationSha256, StringComparison.OrdinalIgnoreCase)
                && await StillMatchesPriorCheckpointAsync(state, prior, cancellationToken).ConfigureAwait(false);
        }
        if (result.Ok)
        {
            try
            {
                receipt = ReadCompletion(attempt, result);
                VerifyBytes(receipt, NativeHash(attempt.SaveName));
                var (point, ledgerCampaign) = await RequireRegisteredPointAsync(state, receipt, cancellationToken)
                    .ConfigureAwait(false);
                var observed = ObserveRegisteredPoint(receipt, result, point, ledgerCampaign);
                if (observed is not null)
                    receipt = receipt with { RegisteredPointEvidence = observed };
            }
            catch (InvalidOperationException error)
            {
                lock (StateGate)
                {
                    state = ReadState(state.RunId);
                    state.Status = "checkpoint_uncertain";
                    AddEvent(state, "checkpoint_uncertain", error.Message, result.Summary());
                    WriteState(state);
                }
                throw;
            }
        }

        lock (StateGate)
        {
            state = ReadState(state.RunId);
            if (state.PendingCheckpoint != attempt)
                throw new InvalidOperationException("The durable pending checkpoint changed while the native operation was running.");
            if (receipt is not null)
            {
                state.CheckpointReceipts.RemoveAll(item => SameName(item.Attempt.SaveName, attempt.SaveName));
                state.CheckpointReceipts.Add(receipt);
                if (!state.Checkpoints.Any(name => SameName(name, attempt.SaveName)))
                    state.Checkpoints.Add(attempt.SaveName);
                AddEvent(state, "checkpoint_receipt", "Verified native bytes and the finalized Save Sync point.", receipt);
            }
            else
            {
                AddEvent(state, notSubmitted ? "checkpoint_not_submitted" : "checkpoint_uncertain",
                    notSubmitted ? "The controller refused before submitting SaveAs."
                        : "Checkpoint completion could not be established. The intent remains unresolved.",
                    result.Summary());
            }
            if (notSubmitted && attempt.PartyAgencyStage is { } stage)
                state.PartyAgencyCheckpointStageReceiptIds.Remove(stage.ReceiptId);
            if (receipt is not null || notSubmitted)
                state.PendingCheckpoint = null;
            state.Status = receipt is not null ? "checkpoint_verified"
                : notSubmitted ? "checkpoint_not_submitted" : "checkpoint_uncertain";
            WriteState(state);
            return state;
        }
    }

    private async Task<bool> StillMatchesPriorCheckpointAsync(CampaignTestState state,
        CampaignCheckpointReceipt prior, CancellationToken cancellationToken)
    {
        var (point, _, _) = await ReadCheckpointSlotAsync(state.CampaignId, prior.Attempt.SaveName, cancellationToken)
            .ConfigureAwait(false);
        return point.HasValue && MatchesPoint(point.Value, prior)
            && string.Equals(NativeHash(prior.Attempt.SaveName), prior.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<(JsonElement Point, string CampaignId)> RequireRegisteredPointAsync(CampaignTestState state,
        CampaignCheckpointReceipt receipt, CancellationToken cancellationToken)
    {
        var (point, _, campaign) = await ReadCheckpointSlotAsync(state.CampaignId, receipt.Attempt.SaveName, cancellationToken)
            .ConfigureAwait(false);
        if (!point.HasValue || !MatchesPoint(point.Value, receipt))
            throw new InvalidOperationException("Save Sync did not confirm the exact available finalized checkpoint point.");
        return (point.Value, campaign);
    }

    private async Task<(JsonElement? Point, int Count, string CampaignId)> ReadCheckpointSlotAsync(
        string campaignId, string destination, CancellationToken cancellationToken)
    {
        ApiEnvelope envelope = await _api.GetAsync("/api/save-sync/status",
            new Dictionary<string, string?> { ["campaignId"] = campaignId, ["checkpointSlot"] = destination },
            cancellationToken).ConfigureAwait(false);
        var (point, count) = ParseCheckpointSlot(envelope, campaignId, destination);
        return (point, count, Text(envelope.Data.GetValueOrDefault(), "campaignId"));
    }

    internal static (JsonElement? Point, int Count) ParseCheckpointSlot(
        ApiEnvelope envelope, string campaignId, string destination)
    {
        if (!envelope.Ok || envelope.Data is not { ValueKind: JsonValueKind.Object } root
            || !IsTrue(root, "enabled") || !SameName(Text(root, "campaignId"), campaignId)
            || !SameName(Text(root, "checkpointSlot"), destination)
            || !root.TryGetProperty("foreignRegistration", out var foreign)
            || foreign.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidOperationException("Save Sync could not prove checkpoint destination occupancy.");

        int count = ExactCount(root, "registrationUniqueStateCount");
        int registrations = ExactCount(root, "registrationCount");
        if (foreign.GetBoolean() || registrations > 1)
            RequireVacant(destination, nativeHash: null, registered: true);
        bool hasPoint = root.TryGetProperty("point", out var point);
        if (registrations == 0 && !hasPoint)
            return (null, count);
        if (registrations != 1 || !hasPoint || point.ValueKind != JsonValueKind.Object
            || !SameName(NativeSlot(Text(point, "nativeSaveName")), destination))
            throw new InvalidOperationException("Save Sync did not return the registration for the exact checkpoint slot.");
        return (point, count);
    }

    private static int ExactCount(JsonElement item, string key)
    {
        if (!item.TryGetProperty(key, out var property) || property.ValueKind != JsonValueKind.Number
            || !property.TryGetInt32(out int value) || value < 0)
            throw new InvalidOperationException($"Save Sync omitted a valid {key} count.");
        return value;
    }

    private static string NativeSlot(string name) => name.Trim().EndsWith(".sav", StringComparison.OrdinalIgnoreCase)
        ? name.Trim()[..^4] : name.Trim();

    private static bool MatchesPoint(JsonElement point, CampaignCheckpointReceipt receipt) =>
        Text(point, "savePointId") == receipt.SavePointId
        && SameName(Text(point, "timelineId"), receipt.Attempt.TimelineId)
        && SameName(NativeSlot(Text(point, "nativeSaveName")), receipt.Attempt.SaveName)
        && Text(point, "status") == "ready" && IsTrue(point, "snapshotAvailable")
        && !string.IsNullOrWhiteSpace(Text(point, "saveFinalizedUtc"));

    private async Task<IReadOnlyDictionary<string, object?>> SaveCapacityAsync(
        CampaignTestState state, string requested, CancellationToken cancellationToken)
    {
        ApiEnvelope envelope = await _api.GetAsync("/api/save-sync/status",
            new Dictionary<string, string?> { ["campaignId"] = state.CampaignId },
            cancellationToken).ConfigureAwait(false);
        int count = envelope.Data.HasValue ? FindInt(envelope.Data.Value, "uniqueStateCount", 0) : 0;
        bool retained = envelope.Data.HasValue && FindStrings(envelope.Data.Value, "nativeSaveName")
            .Any(value => string.Equals(value, requested, StringComparison.OrdinalIgnoreCase));
        return new Dictionary<string, object?>
        {
            ["ok"] = envelope.Ok,
            ["uniqueStateCount"] = count,
            ["uniqueStateLimit"] = SaveSyncLimit,
            ["remainingUniqueStates"] = Math.Max(0, SaveSyncLimit - count),
            ["saveAlreadyRetained"] = retained,
            ["storageWarning"] = count >= 10
        };
    }

    private async Task RequireUnifiedHealthAsync(CancellationToken cancellationToken)
    {
        ApiEnvelope health = await _api.GetAsync("/health", cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (!health.Ok || !health.Data.HasValue)
            throw new InvalidOperationException("The managed Reign server must be healthy before a campaign test starts.");

        JsonElement server = health.Data.Value;
        if (FindBoolean(server, "unifiedControlCenter")
            && FindBoolean(server, "allProcessesCloseTogether"))
            return;

        string installationPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".reign", "installation.json");
        if (!File.Exists(installationPath))
            throw new InvalidOperationException("The managed ReignServer WSL installation record is missing.");

        using JsonDocument installation = JsonDocument.Parse(File.ReadAllText(installationPath));
        JsonElement record = installation.RootElement;
        string distro = record.TryGetProperty("wslDistro", out JsonElement distroValue)
            ? distroValue.GetString() ?? string.Empty : string.Empty;
        string mode = record.TryGetProperty("serverMode", out JsonElement modeValue)
            ? modeValue.GetString() ?? string.Empty : string.Empty;
        string managedBy = server.TryGetProperty("managedBy", out JsonElement managerValue)
            ? managerValue.GetString() ?? string.Empty : string.Empty;
        bool schemaReady = server.TryGetProperty("databaseSchemaUpToDate", out JsonElement schemaValue)
            && schemaValue.ValueKind == JsonValueKind.True;
        if (!string.Equals(mode, "dwemerdistro-wsl", StringComparison.Ordinal)
            || !string.Equals(distro, "ReignServer", StringComparison.Ordinal)
            || !string.Equals(managedBy, distro, StringComparison.Ordinal)
            || !schemaReady)
            throw new InvalidOperationException(
                "The healthy server is not the installed managed ReignServer WSL runtime with a current database schema.");
    }

    private async Task<IReadOnlyDictionary<string, object?>> RecoverInterruptedAdvanceAsync(
        CampaignTestState state, int catchUpTimeoutSeconds, bool observeGovernmentNews = false)
    {
        IReadOnlyDictionary<string, object?> cancellation =
            await CancelActivePassiveWorldRunAsync(state).ConfigureAwait(false);
        if (!Convert.ToBoolean(cancellation["ok"] ?? false))
        {
            return new Dictionary<string, object?>
            {
                ["ok"] = false,
                ["cancellation"] = cancellation,
                ["drainAttempted"] = false
            };
        }

        ControllerCall snapshot;
        try
        {
            snapshot = await VerifyActiveOwnedSaveAsync(state, CancellationToken.None).ConfigureAwait(false);
        }
        catch (InvalidOperationException error)
        {
            return new Dictionary<string, object?>
            {
                ["ok"] = false, ["cancellation"] = cancellation, ["drainAttempted"] = false,
                ["error"] = error.Message
            };
        }
        var drainArgs = new List<string> { "world-test", "drain",
            "--campaign", state.CampaignId, "--timeline", state.TimelineId,
            "--catch-up-timeout", catchUpTimeoutSeconds.ToString(),
            "--campaign-test-source", SerializeCheckpointSource(state, snapshot) };
        if (observeGovernmentNews) drainArgs.Add("--observe-government-news");
        ControllerCall drain = await RunControllerAsync(drainArgs,
            catchUpTimeoutSeconds + 180, CancellationToken.None).ConfigureAwait(false);
        return new Dictionary<string, object?>
        {
            ["ok"] = drain.Ok,
            ["cancellation"] = cancellation,
            ["drainAttempted"] = true,
            ["drain"] = drain.Summary()
        };
    }

    private async Task<IReadOnlyDictionary<string, object?>> CancelActivePassiveWorldRunAsync(
        CampaignTestState state)
    {
        ApiEnvelope runtime = await _api.GetAsync("/tests/live/runtime",
            new Dictionary<string, string?> { ["campaignId"] = state.CampaignId },
            CancellationToken.None).ConfigureAwait(false);
        if (!runtime.Ok || !runtime.Data.HasValue)
        {
            return new Dictionary<string, object?>
            {
                ["ok"] = false,
                ["active"] = false,
                ["error"] = runtime.Error ?? "Live-test runtime status was unavailable."
            };
        }

        bool busy = FindBoolean(runtime.Data.Value, "busy");
        string liveRunId = FindStrings(runtime.Data.Value, "activeRunId")
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
        string activeMode = FindStrings(runtime.Data.Value, "activeMode")
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
        if (!busy)
        {
            return new Dictionary<string, object?>
            {
                ["ok"] = true,
                ["active"] = false,
                ["cancelAttempted"] = false
            };
        }
        if (!runtime.Data.Value.TryGetProperty("runtime", out var native)
            || !SameName(Text(native, "campaignId"), state.CampaignId)
            || Find(state, Text(native, "activeSaveName")) is not { } activeReceipt
            || !SameName(activeReceipt.Sha256, NativeHash(activeReceipt.Attempt.SaveName))
            || !native.TryGetProperty("saveSync", out var sync)
            || !SameName(Text(sync, "timelineId"), state.TimelineId))
        {
            return new Dictionary<string, object?>
            {
                ["ok"] = false, ["active"] = true, ["cancelAttempted"] = false,
                ["error"] = "The busy native runtime could not be matched to a proven checkpoint in this run."
            };
        }
        if (string.IsNullOrWhiteSpace(liveRunId))
        {
            return new Dictionary<string, object?>
            {
                ["ok"] = false,
                ["active"] = true,
                ["cancelAttempted"] = false,
                ["error"] = "The native bridge is busy but did not expose its active run id."
            };
        }
        if (!string.Equals(activeMode, "passive_world", StringComparison.OrdinalIgnoreCase))
        {
            return new Dictionary<string, object?>
            {
                ["ok"] = false,
                ["active"] = true,
                ["cancelAttempted"] = false,
                ["runId"] = liveRunId,
                ["activeMode"] = activeMode,
                ["error"] = "The native bridge is busy with a non-passive-world run; campaign-test recovery will not cancel unrelated feature work."
            };
        }

        ApiEnvelope cancelled = await _api.PostAsync("/tests/live/run/cancel",
            new { campaignId = state.CampaignId, runId = liveRunId },
            CancellationToken.None).ConfigureAwait(false);
        if (!cancelled.Ok)
        {
            return new Dictionary<string, object?>
            {
                ["ok"] = false,
                ["active"] = true,
                ["cancelAttempted"] = true,
                ["runId"] = liveRunId,
                ["error"] = cancelled.Error ?? "The active live-test run rejected cancellation."
            };
        }

        DateTime deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(500, CancellationToken.None).ConfigureAwait(false);
            ApiEnvelope observed = await _api.GetAsync("/tests/live/runtime",
                new Dictionary<string, string?> { ["campaignId"] = state.CampaignId },
                CancellationToken.None).ConfigureAwait(false);
            if (observed.Ok && observed.Data.HasValue
                && !FindBoolean(observed.Data.Value, "busy"))
            {
                return new Dictionary<string, object?>
                {
                    ["ok"] = true,
                    ["active"] = true,
                    ["cancelAttempted"] = true,
                    ["runId"] = liveRunId,
                    ["quiescedAfterCancel"] = true
                };
            }
        }
        return new Dictionary<string, object?>
        {
            ["ok"] = false,
            ["active"] = true,
            ["cancelAttempted"] = true,
            ["runId"] = liveRunId,
            ["quiescedAfterCancel"] = false,
            ["error"] = "The active live-test run did not become idle within 60 seconds of cancellation."
        };
    }

    private async Task<ControllerCall> RunControllerAsync(IEnumerable<string> arguments,
        int timeoutSeconds, CancellationToken cancellationToken)
    {
        // The MCP and live-test controller are built into sibling directories
        // of one immutable validation run. The retired Windows server package
        // no longer exists in the ReignServer WSL installation.
        string validationRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", ".."));
        ReignMcpOptions.EnsureWithin(_options.BuildRoot, validationRoot,
            "validated live-test controller");
        string reportPath = Path.Combine(validationRoot, "validation-report.json");
        if (!File.Exists(reportPath))
            throw new FileNotFoundException(
                "The MCP controller requires its own successful validation report.", reportPath);
        using JsonDocument report = JsonDocument.Parse(File.ReadAllText(reportPath));
        JsonElement root = report.RootElement;
        bool reportPassed = root.TryGetProperty("Ok", out JsonElement ok)
            && ok.ValueKind == JsonValueKind.True;
        bool controllerBuilt = root.TryGetProperty("Results", out JsonElement results)
            && results.ValueKind == JsonValueKind.Array
            && results.EnumerateArray().Any(result =>
                result.TryGetProperty("Project", out JsonElement project)
                && project.TryGetProperty("Id", out JsonElement id)
                && string.Equals(id.GetString(), "live-test", StringComparison.Ordinal)
                && result.TryGetProperty("Operation", out JsonElement operation)
                && string.Equals(operation.GetString(), "build", StringComparison.Ordinal)
                && result.TryGetProperty("Process", out JsonElement process)
                && process.TryGetProperty("Ok", out JsonElement buildOk)
                && buildOk.ValueKind == JsonValueKind.True);
        if (!reportPassed || !controllerBuilt)
            throw new InvalidOperationException(
                "The MCP validation run did not successfully build its live-test controller.");
        string executable = Path.Combine(validationRoot, "live-test", "out", "ReignLiveTest.exe");
        if (!File.Exists(executable))
            throw new FileNotFoundException(
                "The live-test controller from the MCP validation run is missing.", executable);
        var full = arguments.Concat(new[] { "--server", _options.ServerBaseUri.ToString().TrimEnd('/'), "--json" }).ToArray();
        ProcessResult process = await _runner.RunAsync(executable, full,
            Path.GetDirectoryName(executable)!, TimeSpan.FromSeconds(timeoutSeconds),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return ControllerCall.Create(process);
    }

    private void RequireControl(string actualConfirmation, string expectedConfirmation)
    {
        if (!_options.AllowVerificationControl)
            throw new InvalidOperationException("Verification control is disabled. Set REIGN_MCP_ALLOW_VERIFICATION_CONTROL=true in the trusted MCP configuration.");
        InputGuard.RequireConfirmation(actualConfirmation, expectedConfirmation);
    }

    private CampaignTestState RequireArmed(string runId, bool allowPendingCheckpoint = false)
    {
        CampaignTestState state = ReadState(runId);
        if (!allowPendingCheckpoint) RequireResolved(state);
        RequireDistinctBaseline(state.BaselineSaveName, state.CurrentSaveName);
        if (!state.Armed || Find(state, state.CurrentSaveName) is null)
            throw new InvalidOperationException("The campaign-test run is not armed with a proven Current checkpoint.");
        return state;
    }

    private FileStream AcquireSaveOperation(string runId)
    {
        string path = StatePath(RequiredId(runId, nameof(runId))) + ".lock";
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException error)
        {
            throw new InvalidOperationException("A guarded campaign-test operation already owns this run, or its lock is unavailable.", error);
        }
    }

    private CampaignTestState ReadState(string runId)
    {
        runId = RequiredId(runId, nameof(runId));
        lock (StateGate)
            return TryReadState(runId) ?? throw new InvalidOperationException("Unknown campaign-test run id.");
    }

    private CampaignTestState? TryReadState(string runId)
    {
        string path = StatePath(runId);
        return File.Exists(path)
            ? JsonSerializer.Deserialize<CampaignTestState>(File.ReadAllText(path), StateJson)
            : null;
    }

    private void WriteState(CampaignTestState state)
    {
        state.UpdatedUtc = DateTime.UtcNow.ToString("O");
        string path = StatePath(state.RunId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".tmp";
        using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(output, state, StateJson);
            output.Flush(flushToDisk: true);
        }
        File.Move(temporary, path, true);
    }

    private string StatePath(string runId)
    {
        string root = Path.Combine(_options.WorkspaceRoot, ".codex-live-artifacts", "campaign-tests");
        string path = Path.Combine(root, runId + ".json");
        ReignMcpOptions.EnsureWithin(root, path, nameof(runId));
        return path;
    }

    private static IReadOnlyDictionary<string, object?> StateEnvelope(CampaignTestState state,
        string status)
    {
        return new Dictionary<string, object?> { ["ok"] = true, ["status"] = status, ["state"] = state };
    }

    private static IReadOnlyDictionary<string, object?> OperationEnvelope(CampaignTestState state,
        string operation, ControllerCall call, IReadOnlyDictionary<string, object?>? extra = null)
    {
        var result = new Dictionary<string, object?>
        {
            ["ok"] = call.Ok,
            ["operation"] = operation,
            ["state"] = state,
            ["controller"] = call.AsObject()
        };
        foreach (var pair in extra ?? new Dictionary<string, object?>()) result[pair.Key] = pair.Value;
        return result;
    }

    private static void AddEvent(CampaignTestState state, string operation, string message,
        object? evidence)
    {
        state.Events.Add(new CampaignTestEvent
        {
            Utc = DateTime.UtcNow.ToString("O"),
            Operation = operation,
            Message = message,
            Evidence = evidence
        });
        if (state.Events.Count > 100) state.Events.RemoveRange(0, state.Events.Count - 100);
    }

    private static string RequiredId(string value, string name, int maximumLength = 100)
    {
        value = InputGuard.BoundedText(value, name, maximumLength);
        if (value.Length == 0 || value.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
            throw new ArgumentException($"{name} must contain only ASCII letters, digits, hyphen, or underscore.", name);
        return value;
    }

    private static string RequiredSaveName(string value, string name)
    {
        value = InputGuard.BoundedText(value, name, 64);
        if (value.Length == 0 || value.Any(character => !char.IsLetterOrDigit(character) && character is not '_' and not '-' and not ' '))
            throw new ArgumentException($"{name} contains unsupported Bannerlord save-name characters.", name);
        return value;
    }

    private static string TaskSlug(string value)
    {
        return new string((value ?? string.Empty).Where(char.IsAsciiLetterOrDigit).Take(24).ToArray());
    }

    private static bool FindBoolean(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (property.NameEquals(name) && property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    return property.Value.GetBoolean();
                if (FindBoolean(property.Value, name)) return true;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (JsonElement item in element.EnumerateArray()) if (FindBoolean(item, name)) return true;
        return false;
    }

    private static int FindInt(JsonElement element, string name, int fallback)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (property.NameEquals(name) && property.Value.TryGetInt32(out int value)) return value;
                int nested = FindInt(property.Value, name, int.MinValue);
                if (nested != int.MinValue) return nested;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (JsonElement item in element.EnumerateArray())
            {
                int nested = FindInt(item, name, int.MinValue);
                if (nested != int.MinValue) return nested;
            }
        return fallback;
    }

    private static IEnumerable<string> FindStrings(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object)
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (property.NameEquals(name) && property.Value.ValueKind == JsonValueKind.String)
                    yield return property.Value.GetString() ?? string.Empty;
                foreach (string nested in FindStrings(property.Value, name)) yield return nested;
            }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (JsonElement item in element.EnumerateArray())
                foreach (string nested in FindStrings(item, name)) yield return nested;
    }
}

public sealed record CampaignTestState
{
    public string Schema { get; set; } = "reign-campaign-test-state-v1";
    public string RunId { get; set; } = "";
    public string CampaignId { get; set; } = "";
    public string TimelineId { get; set; } = "";
    public string BaselineSaveName { get; set; } = "";
    public string TestName { get; set; } = "";
    public string SavePrefix { get; set; } = "";
    public string CurrentSaveName { get; set; } = "";
    public string Status { get; set; } = "";
    public bool Armed { get; set; }
    public string PreparedUtc { get; set; } = "";
    public string UpdatedUtc { get; set; } = "";
    public List<string> Checkpoints { get; set; } = [];
    public List<CampaignCheckpointReceipt> CheckpointReceipts { get; set; } = [];
    public HashSet<string> PartyAgencyCheckpointStageReceiptIds { get; set; } = [];
    public CampaignCheckpointAttempt? PendingCheckpoint { get; set; }
    public List<CampaignTestEvent> Events { get; set; } = [];
}

public sealed record CampaignTestAuthorization(string CampaignTestRunId, string CampaignId,
    string TimelineId, string BaselineSaveName, string SavePrefix, string CurrentSaveName)
{
    public static CampaignTestAuthorization From(CampaignTestState state) => new(
        state.RunId, state.CampaignId, state.TimelineId, state.BaselineSaveName,
        state.SavePrefix, state.CurrentSaveName);
}

public sealed record CampaignTestEvent
{
    public string Utc { get; set; } = "";
    public string Operation { get; set; } = "";
    public string Message { get; set; } = "";
    public object? Evidence { get; set; }
}

internal sealed class ControllerCall
{
    public required ProcessResult Process { get; init; }
    public JsonElement? Data { get; init; }
    public bool Ok => Process.Ok && Data.HasValue && FindBooleanValue(Data.Value, "ok", false);

    public static ControllerCall Create(ProcessResult process)
    {
        JsonElement? data = null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(process.StandardOutput);
            data = document.RootElement.Clone();
        }
        catch (JsonException) { }
        return new ControllerCall { Process = process, Data = data };
    }

    public string FindString(string name)
    {
        return Data.HasValue ? FindStringValue(Data.Value, name) : string.Empty;
    }

    public bool FindBoolean(string name, bool fallback = false)
    {
        return Data.HasValue ? FindBooleanValue(Data.Value, name, fallback) : fallback;
    }

    public object AsObject()
    {
        return new Dictionary<string, object?>
        {
            ["ok"] = Ok,
            ["process"] = Process,
            ["data"] = Data.HasValue ? JsonSerializer.Deserialize<object>(Data.Value.GetRawText()) : null
        };
    }

    public object Summary()
    {
        return new Dictionary<string, object?>
        {
            ["ok"] = Ok,
            ["durationMs"] = Process.DurationMs,
            ["timedOut"] = Process.TimedOut,
            ["status"] = FindString("status"),
            ["worldDay"] = FindString("worldDay"),
            ["activeSaveName"] = FindString("activeSaveName"),
            ["error"] = FindString("error")
        };
    }

    private static string FindStringValue(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object)
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (property.NameEquals(name))
                {
                    if (property.Value.ValueKind == JsonValueKind.String) return property.Value.GetString() ?? string.Empty;
                    if (property.Value.ValueKind == JsonValueKind.Number) return property.Value.GetRawText();
                }
                string nested = FindStringValue(property.Value, name);
                if (nested.Length > 0) return nested;
            }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (JsonElement item in element.EnumerateArray())
            {
                string nested = FindStringValue(item, name);
                if (nested.Length > 0) return nested;
            }
        return string.Empty;
    }

    private static bool FindBooleanValue(JsonElement element, string name, bool fallback)
    {
        if (element.ValueKind == JsonValueKind.Object)
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (property.NameEquals(name) && property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    return property.Value.GetBoolean();
                bool nested = FindBooleanValue(property.Value, name, fallback);
                if (nested != fallback) return nested;
            }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (JsonElement item in element.EnumerateArray())
            {
                bool nested = FindBooleanValue(item, name, fallback);
                if (nested != fallback) return nested;
            }
        return fallback;
    }
}
