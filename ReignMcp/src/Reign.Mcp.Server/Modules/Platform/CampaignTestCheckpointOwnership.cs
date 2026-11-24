using System.Security.Cryptography;
using System.Text.Json;

namespace Reign.Mcp.Server;

public sealed record CampaignCheckpointAttempt(string Id, string RunId, string CampaignId,
    string TimelineId, string BaselineSaveName, string SaveName, string SourceSaveName,
    string SourcePointId, string GameInstanceId, string PreviousDestinationPointId,
    string PreviousDestinationSha256, string StartedUtc);

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
