using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Reign.Mcp.Server;

public sealed record CampaignCheckpointAttempt(string Id, string RunId, string CampaignId,
    string TimelineId, string BaselineSaveName, string SaveName, string SourceSaveName,
    string SourcePointId, string GameInstanceId, string PreviousDestinationPointId,
    string PreviousDestinationSha256, string StartedUtc)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PartyAgencyCheckpointStage? PartyAgencyStage { get; init; }
}

public sealed record PartyAgencyCheckpointStage(string CertificationRunId, string CaseId,
    string Variant, string LiveRunId, string CommandId, string ReceiptId, string GameInstanceId,
    string ExecutionLeaseId, int RetryGeneration, PartyAgencyCheckpointAgreement Agreement);

public sealed record PartyAgencyCheckpointAgreement(string AgreementId, int AgreementRevision,
    string HeroId, string OriginalClanId, string OriginalKingdomId, string SourcePartyId, string Phase);

public sealed record CampaignCheckpointReceipt(CampaignCheckpointAttempt Attempt, string SavePointId,
    string Sha256, string CompletedUtc);

internal static class CampaignTestCheckpointOwnership
{
    internal static bool SameName(string? left, string? right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    internal static void RequireDistinctBaseline(string baseline, string destination)
    {
        if (SameName(baseline, destination))
            throw new InvalidOperationException("A campaign-test checkpoint cannot use the protected baseline name.");
    }

    internal static void RequireResolved(CampaignTestState state)
    {
        if (state.PendingCheckpoint is { } pending)
            throw new InvalidOperationException(
                $"Checkpoint attempt {pending.Id} for {pending.SaveName} has no conclusive receipt. " +
                "Preserve both saves and inspect the campaign-test report. This enrollment cannot authorize another mutation.");
    }

    internal static CampaignCheckpointReceipt? Find(CampaignTestState state, string name)
    {
        if (string.IsNullOrWhiteSpace(state.SavePrefix)
            || !name.StartsWith(state.SavePrefix + "_", StringComparison.OrdinalIgnoreCase)
            || SameName(name, state.BaselineSaveName)
            || !state.Checkpoints.Any(checkpoint => SameName(checkpoint, name)))
            return null;

        return state.CheckpointReceipts.SingleOrDefault(receipt =>
            receipt.Attempt.RunId == state.RunId && receipt.Attempt.CampaignId == state.CampaignId
            && receipt.Attempt.TimelineId == state.TimelineId
            && SameName(receipt.Attempt.BaselineSaveName, state.BaselineSaveName)
            && SameName(receipt.Attempt.SaveName, name)
            && Guid.TryParseExact(receipt.Attempt.Id, "N", out _)
            && Guid.TryParseExact(receipt.SavePointId, "N", out _)
            && receipt.Sha256.Length == 64 && receipt.Sha256.All(char.IsAsciiHexDigit));
    }

    internal static void ReservePartyAgencyStageReceipt(CampaignTestState state, CampaignCheckpointAttempt attempt)
    {
        if (attempt.PartyAgencyStage is not { } stage) return;
        if (!SameName(attempt.SaveName, state.CurrentSaveName)
            || !SameName(attempt.SourceSaveName, state.CurrentSaveName)
            || string.IsNullOrWhiteSpace(stage.GameInstanceId)
            || stage.GameInstanceId != attempt.GameInstanceId)
            throw new InvalidOperationException("The Party Agency stage did not run in the native instance of the loaded run-owned Current checkpoint.");
        if (string.IsNullOrWhiteSpace(stage.ReceiptId) || !state.PartyAgencyCheckpointStageReceiptIds.Add(stage.ReceiptId))
            throw new InvalidOperationException("The Party Agency stage receipt is missing or already bound to a checkpoint attempt.");
    }

    internal static void RequireVacant(string name, string? nativeHash, bool registered)
    {
        if (nativeHash is not null || registered)
            throw new InvalidOperationException(
                $"The checkpoint destination {name} already exists without a current run-owned receipt. Preserve it and choose a different run id.");
    }

    internal static CampaignCheckpointReceipt ReadCompletion(CampaignCheckpointAttempt attempt, ControllerCall call)
    {
        if (!call.Ok || call.Data is not { ValueKind: JsonValueKind.Object } result || !IsTrue(result, "ok")
            || !IsTrue(result, "postSaveRuntimeQuiesced")
            || !result.TryGetProperty("save", out JsonElement save) || save.ValueKind != JsonValueKind.Object
            || !IsTrue(save, "ok") || !SameName(Text(save, "saveName"), attempt.SaveName)
            || !save.TryGetProperty("nativeSave", out var native) || native.ValueKind != JsonValueKind.Object
            || !IsTrue(native, "succeeded") || !SameName(Text(native, "saveName"), attempt.SaveName)
            || !save.TryGetProperty("saveFinalization", out var final) || final.ValueKind != JsonValueKind.Object
            || !IsTrue(final, "succeeded") || !SameName(Text(final, "completedSaveName"), attempt.SaveName)
            || !save.TryGetProperty("checkpointOwnership", out JsonElement ownership)
            || ownership.ValueKind != JsonValueKind.Object
            || Text(ownership, "id") != attempt.Id || Text(ownership, "runId") != attempt.RunId
            || Text(ownership, "gameInstanceId") != attempt.GameInstanceId
            || !SameName(Text(ownership, "saveName"), attempt.SaveName))
            throw new InvalidOperationException("The controller did not return a successful native receipt for this exact checkpoint attempt.");

        if (attempt.PartyAgencyStage is { } stage
            && (!ownership.TryGetProperty("partyAgencyStage", out var nativeStage)
                || !JsonElement.DeepEquals(nativeStage, JsonSerializer.SerializeToElement(stage, JsonSerializerOptions.Web))))
            throw new InvalidOperationException("The native checkpoint receipt did not echo the exact Party Agency stage and agreement.");

        string pointId = Text(ownership, "savePointId");
        string sha256 = Text(ownership, "sha256");
        if (!Guid.TryParseExact(pointId, "N", out _) || pointId == attempt.SourcePointId
            || pointId == attempt.PreviousDestinationPointId
            || sha256.Length != 64 || !sha256.All(char.IsAsciiHexDigit))
            throw new InvalidOperationException("The checkpoint receipt did not identify newly committed native bytes and a new Save Sync point.");

        return new CampaignCheckpointReceipt(attempt, pointId, sha256, DateTime.UtcNow.ToString("O"));
    }

    internal static string ReadRestartedGameInstance(CampaignCheckpointReceipt checkpoint, JsonElement evidence)
    {
        string before = Text(evidence, "checkpointGameInstanceId");
        string after = Text(evidence, "reloadedGameInstanceId");
        if (Text(evidence, "restartEvidenceSchema") != "reign-campaign-checkpoint-restart-v1"
            || !SameName(Text(evidence, "saveName"), checkpoint.Attempt.SaveName)
            || Text(evidence, "savePointId") != checkpoint.SavePointId
            || Text(evidence, "checkpointAttemptId") != checkpoint.Attempt.Id
            || !string.Equals(Text(evidence, "checkpointSha256"), checkpoint.Sha256, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(before) || before != checkpoint.Attempt.GameInstanceId
            || string.IsNullOrWhiteSpace(after) || after == before)
            throw new InvalidOperationException("The recorded restart does not establish a different native load of this exact checkpoint.");
        return after;
    }

    // Diagnostic observation only. Campaign mutations must never accept it as authorization.
    internal static object? ReadPostArmCurrentObservation(CampaignTestState state, CampaignCheckpointReceipt checkpoint,
        ControllerCall restart, ControllerCall snapshot, string beforeInstance, string afterInstance)
    {
        if (!state.Armed || state.PendingCheckpoint is not null || Find(state, state.CurrentSaveName) != checkpoint
            || state.CurrentSaveName != state.SavePrefix + "_Current" || SameName(state.CurrentSaveName, state.BaselineSaveName)
            || beforeInstance.Length == 0 || afterInstance.Length == 0 || beforeInstance == afterInstance
            || beforeInstance != checkpoint.Attempt.GameInstanceId || !snapshot.Ok
            || snapshot.Data is not { ValueKind: JsonValueKind.Object } run || !IsTrue(run, "found")
            || Text(run, "campaignId") != state.CampaignId || Text(run, "gameInstanceId") != afterInstance
            || Text(run, "mode") != "passive_world" || Text(run, "status") != "completed"
            || !run.TryGetProperty("commands", out var commands) || commands.ValueKind != JsonValueKind.Array
            || commands.GetArrayLength() != 1 || commands[0].ValueKind != JsonValueKind.Object)
            return null;

        JsonElement command = commands[0];
        string runId = Text(run, "runId"), commandId = Text(command, "commandId");
        if (runId.Length == 0 || commandId.Length == 0 || Text(command, "runId") != runId
            || Text(command, "campaignId") != state.CampaignId || Text(command, "gameInstanceId") != afterInstance
            || Text(command, "operation") != "world_snapshot" || Text(command, "status") != "completed"
            || Text(command, "snapshotMode") != "post_arm_current_diagnostic"
            || !command.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object
            || Text(result, "campaignId") != state.CampaignId || Text(result, "timelineId") != state.TimelineId
            || Text(result, "activeSaveName") != state.CurrentSaveName || Text(result, "checkpointSavePointId") != checkpoint.SavePointId
            || Text(result, "checkpointGameInstanceId") != afterInstance || Text(result, "timeMode") != "Stop"
            || Text(result, "snapshotMode") != "post_arm_current_diagnostic"
            || !IsTrue(result, "saveSyncReady") || !result.TryGetProperty("saveSyncAlignmentPending", out var aligning)
            || aligning.ValueKind != JsonValueKind.False
            || !result.TryGetProperty("verifiedCurrentLoadObservation", out var current) || current.ValueKind != JsonValueKind.Object)
            return null;

        string origin = Text(current, "origin"), loadResult = Text(current, "loadResult");
        string loadSessionDigest = Text(current, "loadSessionSha256"), restoreDigest = Text(current, "restoreGenerationSha256");
        string processInstance = Text(current, "clientProcessInstanceId");
        if (!current.TryGetProperty("nativeProcessId", out var process) || process.ValueKind != JsonValueKind.Number
            || !process.TryGetInt32(out int nativeProcessId) || nativeProcessId < 1
            || !Guid.TryParseExact(processInstance, "N", out _)
            || !MatchesReadyRestartProcess(state, restart, afterInstance, nativeProcessId, processInstance)
            || Text(current, "schema") != "reign-native-verified-current-observation-v1"
            || Text(current, "campaignId") != state.CampaignId || Text(current, "timelineId") != state.TimelineId
            || Text(current, "saveName") != state.CurrentSaveName || Text(current, "savePointId") != checkpoint.SavePointId
            || Text(current, "gameInstanceId") != afterInstance || loadResult is not ("no_op" or "rolled_back")
            || !Uri.TryCreate(origin, UriKind.Absolute, out var uri) || !uri.IsLoopback || uri.Scheme != Uri.UriSchemeHttp
            || origin != uri.GetLeftPart(UriPartial.Authority)
            || loadSessionDigest.Length != 64 || !loadSessionDigest.All(char.IsAsciiHexDigit)
            || restoreDigest.Length != 64 || !restoreDigest.All(char.IsAsciiHexDigit)
            || !current.TryGetProperty("coordinatorGeneration", out var generation) || generation.ValueKind != JsonValueKind.Number
            || !generation.TryGetInt32(out int coordinatorGeneration) || coordinatorGeneration < 1)
            return null;

        return new
        {
            schema = "reign-campaign-post-arm-current-observation-v1", diagnosticOnly = true,
            enrollmentRunId = state.RunId, campaignId = state.CampaignId, timelineId = state.TimelineId,
            protectedBaselineSaveName = state.BaselineSaveName, saveName = state.CurrentSaveName,
            checkpointAttemptId = checkpoint.Attempt.Id, checkpointSha256 = checkpoint.Sha256,
            savePointId = checkpoint.SavePointId, checkpointGameInstanceId = beforeInstance, reloadedGameInstanceId = afterInstance,
            liveRunId = runId, liveCommandId = commandId, nativeProcessId,
            origin, loadResult, coordinatorGeneration, loadSessionSha256 = loadSessionDigest, restoreGenerationSha256 = restoreDigest
        };
    }

    private static bool MatchesReadyRestartProcess(CampaignTestState state, ControllerCall restart, string gameInstanceId,
        int nativeProcessId, string processInstance)
    {
        if (!restart.Ok || restart.Data is not { ValueKind: JsonValueKind.Object } response
            || Text(response, "status") != "restarted" || !IsTrue(response, "running")
            || Text(response, "saveName") != state.CurrentSaveName
            || !response.TryGetProperty("processIds", out var processes) || processes.ValueKind != JsonValueKind.Array
            || processes.GetArrayLength() is < 1 or > 16
            || !response.TryGetProperty("runtime", out var runtime) || runtime.ValueKind != JsonValueKind.Object
            || !IsTrue(runtime, "ok") || !IsTrue(runtime, "gameOnline")
            || !runtime.TryGetProperty("runtime", out var heartbeat) || heartbeat.ValueKind != JsonValueKind.Object
            || Text(heartbeat, "campaignId") != state.CampaignId || Text(heartbeat, "gameInstanceId") != gameInstanceId
            || Text(heartbeat, "activeSaveName") != state.CurrentSaveName
            || !heartbeat.TryGetProperty("saveSync", out var sync) || sync.ValueKind != JsonValueKind.Object
            || !IsTrue(sync, "ready") || !sync.TryGetProperty("alignmentPending", out var pending)
            || pending.ValueKind != JsonValueKind.False || Text(sync, "timelineId") != state.TimelineId
            || !heartbeat.TryGetProperty("executionIdentity", out var identity) || identity.ValueKind != JsonValueKind.Object
            || Text(identity, "clientProcessInstanceId") != processInstance)
            return false;

        var seen = new HashSet<int>();
        foreach (var candidate in processes.EnumerateArray())
            if (candidate.ValueKind != JsonValueKind.Number || !candidate.TryGetInt32(out int pid) || pid < 1 || !seen.Add(pid))
                return false;
        return seen.Contains(nativeProcessId);
    }

    internal static string? NativeHash(string name, string? saveDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 64
            || name.Any(character => !char.IsLetterOrDigit(character) && character is not '_' and not '-' and not ' '))
            throw new InvalidOperationException("A valid native save name is required to check checkpoint bytes.");

        string directory = saveDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Mount and Blade II Bannerlord", "Game Saves");
        string path = Path.Combine(directory, name + ".sav");
        try
        {
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("A linked native save cannot prove campaign-test ownership.");
            return Convert.ToHexString(SHA256.HashData(input));
        }
        catch (IOException ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    internal static void VerifyBytes(CampaignCheckpointReceipt receipt, string? actualHash)
    {
        if (!string.Equals(receipt.Sha256, actualHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"The native bytes for {receipt.Attempt.SaveName} are missing or differ from the run-owned checkpoint receipt.");
    }

    internal static bool IsTrue(JsonElement item, string key) =>
        item.ValueKind == JsonValueKind.Object
        && item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.True;

    internal static string Text(JsonElement item, string property) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
}
