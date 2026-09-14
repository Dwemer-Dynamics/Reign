using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Server;

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
        CancellationToken cancellationToken = default)
    {
        return service.AdvanceAsync(runId, confirmation, days, targetDay,
            checkpointMode, timeoutSeconds, catchUpTimeoutSeconds, cancellationToken);
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
        await VerifyActiveOwnedSaveAsync(state, cancellationToken).ConfigureAwait(false);
        return CampaignTestAuthorization.From(state);
    }

    public IReadOnlyDictionary<string, object?> Prepare(string campaignId, string timelineId,
        string baselineSaveName, string runId, string testName)
    {
        campaignId = RequiredId(campaignId, nameof(campaignId));
        timelineId = RequiredId(timelineId, nameof(timelineId));
        runId = RequiredId(runId, nameof(runId));
        baselineSaveName = RequiredSaveName(baselineSaveName, nameof(baselineSaveName));
        testName = InputGuard.BoundedText(testName, nameof(testName), 80);
        string slug = TaskSlug(testName);
        if (slug.Length == 0) throw new ArgumentException("testName must contain letters or digits.", nameof(testName));
        string suffix = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(runId)))
            .ToLowerInvariant()[..8];
        string prefix = $"ReignTest_{slug}_{suffix}";
        string current = prefix + "_Current";
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
                return StateEnvelope(existing, "already_prepared");
            }
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
        CampaignTestState state = ReadState(runId);
        await RequireUnifiedHealthAsync(cancellationToken).ConfigureAwait(false);
        ControllerCall status = await RunControllerAsync(new[] { "game", "status" }, 60,
            cancellationToken).ConfigureAwait(false);
        string lifecycleStatus = status.FindString("status");
        string expectedSave = state.Armed ? state.CurrentSaveName : state.BaselineSaveName;
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
            result = await RunControllerAsync(new[] { "game", "start", "--save", expectedSave,
                "--wait", waitSeconds.ToString(), "--save-sync-grace", "420" },
                waitSeconds + 450, cancellationToken).ConfigureAwait(false);
        }
        if (result.Ok)
        {
            ControllerCall snapshot = await SnapshotAsync(state, cancellationToken).ConfigureAwait(false);
            ValidateSnapshot(state, snapshot, allowBaseline: !state.Armed);
            if (!string.Equals(snapshot.FindString("activeSaveName"), expectedSave,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "Bannerlord did not load the exact save selected by the campaign-test enrollment.");
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

    public async Task<IReadOnlyDictionary<string, object?>> ArmAsync(string runId,
        string confirmation, int minutes, CancellationToken cancellationToken)
    {
        RequireControl(confirmation, "arm Reign campaign test on disposable save");
        minutes = InputGuard.Range(minutes, nameof(minutes), 5, 720);
        CampaignTestState state = ReadState(runId);
        await RequireUnifiedHealthAsync(cancellationToken).ConfigureAwait(false);
        ControllerCall armed = await RunControllerAsync(new[] { "arm", "--minutes", minutes.ToString() },
            60, cancellationToken).ConfigureAwait(false);
        if (!armed.Ok) return OperationEnvelope(state, "arm", armed);
        ControllerCall snapshot = await SnapshotAsync(state, cancellationToken).ConfigureAwait(false);
        ValidateSnapshot(state, snapshot, allowBaseline: true);
        string active = snapshot.FindString("activeSaveName");
        if (string.Equals(active, state.BaselineSaveName, StringComparison.OrdinalIgnoreCase))
        {
            await RequireSaveCapacityAsync(state, state.CurrentSaveName, cancellationToken).ConfigureAwait(false);
            ControllerCall initial = await RunControllerAsync(new[] { "world-test", "checkpoint",
                "--campaign", state.CampaignId, "--timeline", state.TimelineId,
                "--save", state.CurrentSaveName, "--save-timeout", "420",
                "--catch-up-timeout", "900", "--initial-baseline" }, 1380,
                cancellationToken).ConfigureAwait(false);
            if (!initial.Ok) return OperationEnvelope(state, "initial_checkpoint", initial);
            snapshot = await SnapshotAsync(state, cancellationToken).ConfigureAwait(false);
            ValidateSnapshot(state, snapshot, allowBaseline: false);
        }
        lock (StateGate)
        {
            state = ReadState(runId);
            state.Armed = true;
            state.Status = "armed";
            if (!state.Checkpoints.Contains(state.CurrentSaveName, StringComparer.OrdinalIgnoreCase))
                state.Checkpoints.Add(state.CurrentSaveName);
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
        int timeoutSeconds, int catchUpTimeoutSeconds, CancellationToken cancellationToken)
    {
        RequireControl(confirmation, "advance Reign campaign test on disposable save");
        if (days.HasValue == targetDay.HasValue)
            throw new ArgumentException("Supply exactly one of days or targetDay.");
        if (days is < 0d or > 6300d) throw new ArgumentOutOfRangeException(nameof(days));
        if (targetDay is < 0d) throw new ArgumentOutOfRangeException(nameof(targetDay));
        checkpointMode = (checkpointMode ?? string.Empty).Trim().ToLowerInvariant();
        if (checkpointMode is not "none" and not "rolling")
            throw new ArgumentException("checkpointMode must be none or rolling.", nameof(checkpointMode));
        timeoutSeconds = InputGuard.Range(timeoutSeconds, nameof(timeoutSeconds), 120, 21600);
        catchUpTimeoutSeconds = InputGuard.Range(catchUpTimeoutSeconds, nameof(catchUpTimeoutSeconds), 60, 3600);
        CampaignTestState state = RequireArmed(runId);
        await VerifyActiveOwnedSaveAsync(state, cancellationToken).ConfigureAwait(false);
        if (checkpointMode == "rolling")
            await RequireSaveCapacityAsync(state, state.CurrentSaveName, cancellationToken).ConfigureAwait(false);
        var args = new List<string> { "world-test", "advance", "--campaign", state.CampaignId,
            "--timeline", state.TimelineId, "--timeout", timeoutSeconds.ToString(),
            "--catch-up-timeout", catchUpTimeoutSeconds.ToString() };
        if (days.HasValue) { args.Add("--days"); args.Add(days.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)); }
        else { args.Add("--target-day"); args.Add(targetDay!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)); }
        if (checkpointMode == "rolling") { args.Add("--save"); args.Add(state.CurrentSaveName); }
        else args.Add("--no-save");
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
                catchUpTimeoutSeconds).ConfigureAwait(false);
            lock (StateGate)
            {
                state = ReadState(runId);
                state.Status = Convert.ToBoolean(safetyRecovery["ok"] ?? false)
                    ? "advance_cancelled_safe"
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
                catchUpTimeoutSeconds).ConfigureAwait(false);
        }
        lock (StateGate)
        {
            state = ReadState(runId);
            bool recoveryOk = safetyRecovery is not null
                && Convert.ToBoolean(safetyRecovery["ok"] ?? false);
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
        CampaignTestState state = RequireArmed(runId);
        IReadOnlyDictionary<string, object?> activeAdvancePreemption =
            await CancelActivePassiveWorldRunAsync(state).ConfigureAwait(false);
        if (!Convert.ToBoolean(activeAdvancePreemption["ok"] ?? false))
            throw new InvalidOperationException(
                "The active passive-world run could not be cancelled and paused before drain.");
        await VerifyActiveOwnedSaveAsync(state, cancellationToken).ConfigureAwait(false);
        ControllerCall result = await RunControllerAsync(new[] { "world-test", "drain",
            "--campaign", state.CampaignId, "--timeline", state.TimelineId,
            "--catch-up-timeout", catchUpTimeoutSeconds.ToString() },
            catchUpTimeoutSeconds + 180, cancellationToken).ConfigureAwait(false);
        lock (StateGate)
        {
            state = ReadState(runId);
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

    public async Task<IReadOnlyDictionary<string, object?>> CheckpointAsync(string runId,
        string confirmation, string milestoneLabel, int saveTimeoutSeconds,
        int catchUpTimeoutSeconds, CancellationToken cancellationToken)
    {
        RequireControl(confirmation, "save Reign campaign-test checkpoint");
        saveTimeoutSeconds = InputGuard.Range(saveTimeoutSeconds, nameof(saveTimeoutSeconds), 180, 900);
        catchUpTimeoutSeconds = InputGuard.Range(catchUpTimeoutSeconds,
            nameof(catchUpTimeoutSeconds), 60, 3600);
        CampaignTestState state = RequireArmed(runId);
        await VerifyActiveOwnedSaveAsync(state, cancellationToken).ConfigureAwait(false);
        string saveName = state.CurrentSaveName;
        if (!string.IsNullOrWhiteSpace(milestoneLabel))
        {
            string label = new string(milestoneLabel.Where(char.IsAsciiLetterOrDigit).Take(16).ToArray());
            if (label.Length == 0) throw new ArgumentException("milestoneLabel must contain ASCII letters or digits.", nameof(milestoneLabel));
            saveName = state.SavePrefix + "_" + label;
            if (saveName.Length > 64) throw new ArgumentException("The milestone save name exceeds 64 characters.", nameof(milestoneLabel));
        }
        await RequireSaveCapacityAsync(state, saveName, cancellationToken).ConfigureAwait(false);
        ControllerCall result = await RunControllerAsync(new[] { "world-test", "checkpoint",
            "--campaign", state.CampaignId, "--timeline", state.TimelineId,
            "--save", saveName, "--save-timeout", saveTimeoutSeconds.ToString(),
            "--catch-up-timeout", catchUpTimeoutSeconds.ToString() },
            saveTimeoutSeconds + catchUpTimeoutSeconds + 180, cancellationToken).ConfigureAwait(false);
        lock (StateGate)
        {
            state = ReadState(runId);
            if (result.Ok && !state.Checkpoints.Contains(saveName, StringComparer.OrdinalIgnoreCase))
                state.Checkpoints.Add(saveName);
            state.Status = result.Ok ? "checkpointed" : "checkpoint_failed";
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
        CampaignTestState state = RequireArmed(runId);
        await VerifyActiveOwnedSaveAsync(state, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(state.Status, "checkpointed", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "The disposable Current save must be checkpointed and drained immediately before restart.");
        ControllerCall result = await RunControllerAsync(new[] { "game", "restart",
            "--save", state.CurrentSaveName, "--no-save", "--wait", waitSeconds.ToString(),
            "--save-sync-grace", "420" }, waitSeconds + 600, cancellationToken)
            .ConfigureAwait(false);
        if (!result.Ok) return OperationEnvelope(state, "restart", result);
        ControllerCall snapshot = await SnapshotAsync(state, cancellationToken).ConfigureAwait(false);
        ValidateSnapshot(state, snapshot, allowBaseline: false);
        string receipt = "campaign_test_restart_" + Guid.NewGuid().ToString("N");
        var evidence = new Dictionary<string, object?>
        {
            ["restartReceipt"] = receipt,
            ["saveName"] = state.CurrentSaveName,
            ["controller"] = result.Summary(),
            ["reloadedSnapshot"] = snapshot.Summary()
        };
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
        CampaignTestState state = RequireArmed(runId);
        ControllerCall activeSnapshot = await SnapshotAsync(state, cancellationToken)
            .ConfigureAwait(false);
        ValidateSnapshot(state, activeSnapshot, allowBaseline: false,
            requireSafeSettlement: false);
        if (string.Equals(state.CurrentSaveName, state.BaselineSaveName,
                StringComparison.OrdinalIgnoreCase)
            || !state.Checkpoints.Contains(state.CurrentSaveName,
                StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "The exact run-owned Current checkpoint must already exist and cannot be the enrolled baseline.");
        ControllerCall result = await RunControllerAsync(new[] { "game", "restart",
            "--save", state.CurrentSaveName, "--no-save", "--wait", waitSeconds.ToString(),
            "--save-sync-grace", "420" }, waitSeconds + 600, cancellationToken)
            .ConfigureAwait(false);
        if (!result.Ok) return OperationEnvelope(state, "restore_checkpoint", result);
        ControllerCall snapshot = await SnapshotAsync(state, cancellationToken).ConfigureAwait(false);
        ValidateSnapshot(state, snapshot, allowBaseline: false);
        if (!snapshot.FindBoolean("safeSettlement"))
            throw new InvalidOperationException(
                "The restored campaign-test checkpoint did not return to a safe settlement boundary.");
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
        CampaignTestState state = ReadState(runId);
        expectedCampaignId = RequiredId(expectedCampaignId, nameof(expectedCampaignId));
        restartReceipt = RequiredId(restartReceipt, nameof(restartReceipt));
        if (!string.Equals(state.CampaignId, expectedCampaignId,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "The reload receipt does not belong to the requested campaign test.");
        bool found = state.Events.Any(item => string.Equals(item.Operation, "restart",
                StringComparison.OrdinalIgnoreCase)
            && JsonSerializer.Serialize(item.Evidence, StateJson)
                .Contains(restartReceipt, StringComparison.Ordinal));
        if (!found) throw new InvalidOperationException(
            "The supplied reload receipt was not issued by this guarded campaign test.");
        return CampaignTestAuthorization.From(state);
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
        CampaignTestState state = ReadState(runId);
        ControllerCall snapshot = await SnapshotAsync(state, cancellationToken).ConfigureAwait(false);
        string active = snapshot.FindString("activeSaveName");
        if (state.Checkpoints.Contains(active, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("Cleanup refused because a run-owned checkpoint is currently loaded. Load the protected baseline or another save first.");
        var deleted = new List<object?>();
        foreach (string save in state.Checkpoints.Distinct(StringComparer.OrdinalIgnoreCase).ToArray())
        {
            if (string.Equals(save, state.BaselineSaveName, StringComparison.OrdinalIgnoreCase)
                || !save.StartsWith(state.SavePrefix + "_", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Cleanup state contains a save outside the exact run-owned namespace.");
            ControllerCall result = await RunControllerAsync(new[] { "world-test", "delete-checkpoint",
                "--campaign", state.CampaignId, "--timeline", state.TimelineId,
                "--save", save, "--prefix", state.SavePrefix }, 180, cancellationToken)
                .ConfigureAwait(false);
            deleted.Add(new Dictionary<string, object?> { ["saveName"] = save, ["result"] = result.AsObject() });
            if (!result.Ok) return OperationEnvelope(state, "cleanup", result,
                new Dictionary<string, object?> { ["deleted"] = deleted });
        }
        lock (StateGate)
        {
            state = ReadState(runId);
            state.Status = "cleaned";
            state.Checkpoints.Clear();
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

    private async Task VerifyActiveOwnedSaveAsync(CampaignTestState state,
        CancellationToken cancellationToken)
    {
        ControllerCall snapshot = await SnapshotAsync(state, cancellationToken).ConfigureAwait(false);
        ValidateSnapshot(state, snapshot, allowBaseline: false);
    }

    private Task<ControllerCall> SnapshotAsync(CampaignTestState state,
        CancellationToken cancellationToken)
    {
        return RunControllerAsync(new[] { "world-test", "snapshot", "--campaign", state.CampaignId,
            "--timeline", state.TimelineId }, 180, cancellationToken);
    }

    private static void ValidateSnapshot(CampaignTestState state, ControllerCall snapshot,
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
        bool owned = state.Checkpoints.Contains(active, StringComparer.OrdinalIgnoreCase)
            || string.Equals(active, state.CurrentSaveName, StringComparison.OrdinalIgnoreCase);
        if (!owned && !(allowBaseline && string.Equals(active, state.BaselineSaveName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("The actual loaded Bannerlord save is neither the enrolled baseline nor an exact run-owned checkpoint.");
        if (!snapshot.FindBoolean("saveSyncReady") || snapshot.FindBoolean("saveSyncAlignmentPending"))
            throw new InvalidOperationException("Save Sync is not finalized and aligned for the loaded campaign.");
        if (requireSafeSettlement && !snapshot.FindBoolean("safeSettlement"))
            throw new InvalidOperationException("The player must be safely inside a town or castle before autonomous campaign testing.");
    }

    private async Task RequireSaveCapacityAsync(CampaignTestState state, string saveName,
        CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<string, object?> capacity = await SaveCapacityAsync(state, saveName, cancellationToken)
            .ConfigureAwait(false);
        if (!Convert.ToBoolean(capacity["ok"] ?? false))
            throw new InvalidOperationException("Save Sync capacity is unavailable. Refusing to create a checkpoint until the server can prove the unique-state budget.");
        int count = Convert.ToInt32(capacity["uniqueStateCount"] ?? 0);
        bool alreadyRetained = Convert.ToBoolean(capacity["saveAlreadyRetained"] ?? false);
        if (!alreadyRetained && count >= SaveSyncLimit)
            throw new InvalidOperationException("Save Sync already retains 15 unique states. Delete an exact run-owned milestone or another user-approved native save before creating a new checkpoint.");
    }

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
        if (!health.Ok || !health.Data.HasValue
            || !FindBoolean(health.Data.Value, "unifiedControlCenter")
            || !FindBoolean(health.Data.Value, "allProcessesCloseTogether"))
            throw new InvalidOperationException("The supported visible unified Reign server lifetime group must be healthy.");
    }

    private async Task<IReadOnlyDictionary<string, object?>> RecoverInterruptedAdvanceAsync(
        CampaignTestState state, int catchUpTimeoutSeconds)
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

        ControllerCall drain = await RunControllerAsync(new[] { "world-test", "drain",
            "--campaign", state.CampaignId, "--timeline", state.TimelineId,
            "--catch-up-timeout", catchUpTimeoutSeconds.ToString() },
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
        string executable = Path.Combine(_options.WorkspaceRoot, "ReignBeta", "server", "app",
            "ReignLiveTest.exe");
        if (!File.Exists(executable))
            throw new FileNotFoundException("The packaged ReignLiveTest controller is missing. Deploy a validated Reign build first.", executable);
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

    private CampaignTestState RequireArmed(string runId)
    {
        CampaignTestState state = ReadState(runId);
        if (!state.Armed) throw new InvalidOperationException("The campaign-test run is not armed.");
        return state;
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
        File.WriteAllText(temporary, JsonSerializer.Serialize(state, StateJson), new UTF8Encoding(false));
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

    private static string RequiredId(string value, string name)
    {
        value = InputGuard.BoundedText(value, name, 100);
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

    public bool FindBoolean(string name)
    {
        return Data.HasValue && FindBooleanValue(Data.Value, name, false);
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
