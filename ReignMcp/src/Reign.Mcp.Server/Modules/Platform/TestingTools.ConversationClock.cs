using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Reign.Mcp.Server;

[McpServerToolType]
public static class ConversationClockTools
{
    internal const string Confirmation = "probe Reign conversation clock on disposable save";

    [McpServerTool(Name = "reign_probe_conversation_clock", ReadOnly = false,
        Destructive = true, Idempotent = false, UseStructuredContent = true)]
    [Description("Observes the native clock in a locked Individual Chat on the exact armed, paused disposable Current. Conversation time advancement is retired; pulse requests are rejected. Never opens/closes chat, calls a provider or advances time.")]
    public static async Task<IReadOnlyDictionary<string, object?>> ProbeConversationClock(
        ReignApiClient api, ReignMcpOptions options, CampaignTestService campaignTests,
        string campaignId, string campaignTestRunId, string probeId,
        string profile = "observe", string confirmation = "",
        CancellationToken cancellationToken = default)
    {
        if (!options.AllowVerificationControl)
            throw new InvalidOperationException("Conversation clock diagnostics require the verification-control gate.");
        InputGuard.RequireConfirmation(confirmation, Confirmation);
        campaignId = RequireId(campaignId, nameof(campaignId));
        campaignTestRunId = RequireId(campaignTestRunId, nameof(campaignTestRunId));
        probeId = RequireId(probeId, nameof(probeId));
        if (profile != "observe")
            throw new ArgumentException("Conversation time advancement is retired; only observe is supported.", nameof(profile));
        CampaignTestAuthorization authorization = await campaignTests.RequireArmedOwnedPausedActionSaveAsync(
            campaignTestRunId, campaignId, cancellationToken).ConfigureAwait(false);
        campaignTests.RequireRetainedRecoveryCheckpoint(authorization);
        string liveRunId = "clock-" + probeId + "-" + Guid.NewGuid().ToString("N")[..8];
        ApiEnvelope started = await api.PostAsync("/tests/live/run/start", new
        {
            schemaVersion = 2, campaignId, runId = liveRunId, mode = "individual_chat",
            label = "Conversation clock diagnostic " + probeId,
            presentation = "visible", effects = "guarded", autoCompleteWhenIdle = true,
            enrollment = new
            {
                campaignId, campaignTestRunId, timelineId = authorization.TimelineId,
                disposableSaveName = authorization.CurrentSaveName,
                protectedBaselineSaveName = authorization.BaselineSaveName,
                recoveryCheckpointVerified = true, source = "reign_mcp_conversation_clock"
            },
            steps = new[] { new { operation = "conversation_clock_probe", profile, probeId,
                expectedSaveName = authorization.CurrentSaveName, confirmation = Confirmation, timeoutSeconds = 30 } }
        }, cancellationToken).ConfigureAwait(false);
        return new Dictionary<string, object?>
        {
            ["runStarted"] = started.Ok, ["liveRunId"] = liveRunId, ["probeId"] = probeId,
            ["profile"] = profile, ["expectedSaveName"] = authorization.CurrentSaveName,
            ["timerAccountingQualified"] = false, ["inSceneAcceptance"] = false,
            ["requiresCheckpointRestore"] = profile == "pulse", ["start"] = started
        };
    }

    private static string RequireId(string value, string name)
    {
        string result = InputGuard.OptionalIdentifier(value, name);
        if (result.Length == 0) throw new ArgumentException(name + " is required.", name);
        return result;
    }
}
