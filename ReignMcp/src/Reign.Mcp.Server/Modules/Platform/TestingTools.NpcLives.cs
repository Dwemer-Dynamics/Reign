using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Reign.Mcp.Server;

[McpServerToolType]
public static class NpcLivesTestTools
{
    private const string CheckpointEvaluation = "checkpoint_evaluation";

    [McpServerTool(Name = "reign_test_npc_lives", ReadOnly = false, Destructive = true, Idempotent = false, UseStructuredContent = true)]
    [Description("Open an unsent real party action builder with composer (two distinct actual party heroId/otherHeroId and stable operationId only), snapshot one NPC, prepare an unaccepted authored story or leave invitation, decline its exact existing NPC-signed offer through production refusal, place the player at an accepted story's bound destination, or resolve its real native duel/hideout with bounded win/loss/withdraw. decline requires heroId, matterId, proposalId, its observed termsHash and a positive decimal revision; it cannot accept an offer or invent terms. encounter witnesses both actual agents and the bound native captive; controlled-win cleanup may acknowledge only that captive's owned conversation at most once, with no dialogue choice or rescue credit. place journals one movement with no destination override. composer journals before opening; identical replay only observes the retained receipt/current UI, changed terms fail and partial/uncertain attempts never reopen. It submits no action, consent, text or announcement; use the real UI to insert/cancel and independently verify conservation. announcement_transport accepts only caseId, phase, two actual party heroId/otherHeroId, operationId and control arm/observe/release. It holds only original announcement dispatch for at most 180 monotonic seconds; only the actual UI queues text and native hearers. It never sends, edits, retargets or acknowledges a payload; replay never rearms. Release or expiry resumes ordinary transport. Prove original outbox drained before time/save. Requires the exact armed paused verified campaign-test Current and retained recovery checkpoint. The meeting_release_snapshot phase reads one existing exact meetingId and revision before normal fixture refresh, with the current native point and v3 readiness counters. The checkpoint_evaluation phase accepts one arm or observe case bound to the exact reloaded Current and its clean provider guard. Once armed, the next matching rolling checkpoint can start one fixed production evaluation with zero companions at the final native save guard. No provider calls, time advance or save command. Verify production outcomes and exactly-once effects independently.")]
    public static async Task<IReadOnlyDictionary<string, object?>> TestNpcLives(
        ReignApiClient api, ReignMcpOptions options, CampaignTestService campaignTests,
        string campaignId, string campaignTestRunId, string batchId, string casesJson,
        string confirmation = "", CancellationToken cancellationToken = default)
    {
        if (!options.AllowVerificationControl) throw new InvalidOperationException("NPC Lives tests require verification control.");
        InputGuard.RequireConfirmation(confirmation, "test NPC lives on disposable save");
        campaignId = Id(campaignId); campaignTestRunId = Id(campaignTestRunId); batchId = Id(batchId);
        var cases = ParseCases(casesJson);
        bool checkpointEvaluation = cases.Count == 1 && cases[0].GetProperty("phase").GetString() == CheckpointEvaluation;
        var (auth, checkpoint, _) = await campaignTests.RequireArmedOwnedPausedActionCheckpointAsync(
            campaignTestRunId, campaignId, checkpointEvaluation, cancellationToken).ConfigureAwait(false);
        campaignTests.RequireRetainedRecoveryCheckpoint(auth);
        var enrollment = new Dictionary<string, object>
        {
            ["campaignId"] = campaignId, ["campaignTestRunId"] = campaignTestRunId,
            ["disposableSaveName"] = auth.CurrentSaveName, ["protectedBaselineSaveName"] = auth.BaselineSaveName,
            ["timelineId"] = auth.TimelineId, ["source"] = "reign_mcp_npc_lives", ["recoveryCheckpointVerified"] = true
        };
        if (checkpointEvaluation)
        {
            var provider = await api.GetAsync("/api/provider/status", cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!provider.Ok || provider.Data is not JsonElement data || !MatchesCheckpointEvaluationGuard(
                data, campaignTestRunId, campaignId, auth.TimelineId, auth.CurrentSaveName, auth.BaselineSaveName))
                throw new InvalidOperationException("Checkpoint evaluation requires this campaign test's clean provider guard.");
            enrollment["checkpointSavePointId"] = checkpoint.SavePointId;
            enrollment["checkpointSha256"] = checkpoint.Sha256.ToUpperInvariant();
        }
        string liveRunId = "npc-lives-" + batchId + "-" + Guid.NewGuid().ToString("N")[..8];
        var started = await api.PostAsync("/tests/live/run/start", new {
            schemaVersion = 2, campaignId, runId = liveRunId, mode = "npc_lives", label = "NPC Lives native checks",
            presentation = "headless", effects = "full", autoCompleteWhenIdle = true,
            enrollment,
            steps = cases.Select(c => new { operation = "npc_lives_test", testCase = c, timeoutSeconds = 120 }).ToArray()
        }, cancellationToken).ConfigureAwait(false);
        return new Dictionary<string, object?> { ["ok"] = started.Ok, ["liveRunId"] = liveRunId,
            ["caseCount"] = cases.Count, ["expectedSaveName"] = auth.CurrentSaveName,
            ["coverage"] = "operator native fixture; verify production outcome and exactly-once rewards", ["start"] = started };
    }

    internal static bool MatchesCheckpointEvaluationGuard(JsonElement provider, string run, string campaign,
        string timeline, string current, string baseline)
    {
        static bool ExactText(JsonElement item, string name, string value) =>
            item.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.String && field.GetString() == value;

        return provider.ValueKind == JsonValueKind.Object
            && provider.TryGetProperty("nativeProviderTestGuard", out var guard) && guard.ValueKind == JsonValueKind.Object
            && ExactText(guard, "schema", "reign-native-provider-free-guard-v1")
            && guard.TryGetProperty("armed", out var armed) && armed.ValueKind == JsonValueKind.True
            && guard.TryGetProperty("cleanProviderFreeRun", out var clean) && clean.ValueKind == JsonValueKind.True
            && guard.TryGetProperty("owner", out var owner) && owner.ValueKind == JsonValueKind.Object
            && ExactText(owner, "runId", run) && ExactText(owner, "campaignId", campaign)
            && ExactText(owner, "timelineId", timeline) && ExactText(owner, "currentSaveName", current)
            && ExactText(owner, "baselineSaveName", baseline);
    }
    private static string Id(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128 || value.Any(c => !char.IsLetterOrDigit(c) && c != '_' && c != '-' && c != ':'))
            throw new ArgumentException("Use a bounded exact identifier.");
        return value;
    }
    public static List<JsonElement> ParseCases(string text)
    {
        text = InputGuard.BoundedText(text, nameof(text), 16384);
        using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 4 });
        if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() is < 1 or > 8)
            throw new ArgumentException("Supply one through eight NPC Lives cases.");
        var ids = new HashSet<string>(StringComparer.Ordinal); var result = new List<JsonElement>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) throw new ArgumentException("Each case is an object.");
            var fields = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in item.EnumerateObject())
                if (!new[] { "caseId", "phase", "storyId", "objectiveId", "encounterId", "outcome", "heroId", "otherHeroId", "operationId", "control", "templateId", "chapterId", "locationId", "invitationKind", "matterId", "proposalId", "revision", "termsHash", "meetingId" }.Contains(field.Name)
                    || !fields.Add(field.Name) || field.Value.ValueKind != JsonValueKind.String)
                    throw new ArgumentException("Unknown, duplicate or non-string case field.");
            string Get(string name) => item.TryGetProperty(name, out var v) ? v.GetString() ?? "" : "";
            if (!ids.Add(Id(Get("caseId")))) throw new ArgumentException("Case identities must be distinct.");
            string phase = Get("phase");
            if (!new[] { "snapshot", "prepare", "decline", "composer", "announcement_transport", "place", "observe", "start", "finish", "encounter", "meeting_release_snapshot", CheckpointEvaluation }.Contains(phase)) throw new ArgumentException("Unknown NPC Lives test phase.");
            var allowed = new HashSet<string>(new[] { "caseId", "phase" }, StringComparer.Ordinal);
            if (phase is "prepare" or "snapshot" or "decline" or "composer" or "announcement_transport") { Id(Get("heroId")); allowed.Add("heroId"); }
            else if (phase == CheckpointEvaluation)
            {
                string operation = Get("operationId");
                if (document.RootElement.GetArrayLength() != 1 || operation.Length is < 1 or > 80
                    || operation.Any(c => !char.IsLetterOrDigit(c) && c != '_' && c != '-')
                    || Get("control") is not ("arm" or "observe"))
                    throw new ArgumentException("Choose one checkpoint evaluation arm or observe case with an exact operation ID.");
                allowed.UnionWith(new[] { "operationId", "control" });
            }
            else if (phase == "meeting_release_snapshot")
            {
                Id(Get("meetingId"));
                string revision = Get("revision");
                if (!int.TryParse(revision, System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture, out int parsedRevision) || parsedRevision < 1
                    || revision != parsedRevision.ToString(System.Globalization.CultureInfo.InvariantCulture))
                    throw new ArgumentException("Meeting release observation requires an exact positive decimal revision.");
                allowed.UnionWith(new[] { "meetingId", "revision" });
            }
            else { Id(Get("storyId")); allowed.Add("storyId"); }
            if (phase is "composer" or "announcement_transport")
            {
                Id(Get("otherHeroId")); Id(Get("operationId"));
                if (Get("heroId") == Get("otherHeroId")) throw new ArgumentException("Choose two distinct actual party NPCs.");
                allowed.UnionWith(new[] { "otherHeroId", "operationId" });
            }
            if (phase == "announcement_transport")
            {
                if (!new[] { "arm", "observe", "release" }.Contains(Get("control")))
                    throw new ArgumentException("Choose arm, observe or release for the announcement delivery control.");
                allowed.Add("control");
            }
            if (phase == "decline")
            {
                Id(Get("matterId")); Id(Get("proposalId"));
                string revision = Get("revision");
                if (revision.Length == 0 || revision.Any(c => c < '0' || c > '9')
                    || !int.TryParse(revision, System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture, out int parsedRevision) || parsedRevision < 1
                    || revision != parsedRevision.ToString(System.Globalization.CultureInfo.InvariantCulture))
                    throw new ArgumentException("Decline requires a positive decimal revision.");
                string termsHash = Get("termsHash");
                if (termsHash.Length != 64 || termsHash.Any(c => !(c >= '0' && c <= '9' || c >= 'a' && c <= 'f')))
                    throw new ArgumentException("Decline requires the observed lowercase SHA-256 terms hash.");
                allowed.UnionWith(new[] { "matterId", "proposalId", "revision", "termsHash" });
            }
            if (phase == "prepare") { Id(Get("templateId")); Id(Get("chapterId")); allowed.UnionWith(new[] { "templateId", "chapterId" });
                if (fields.Contains("invitationKind")) { if (!new[] { "story", "leave" }.Contains(Get("invitationKind")))
                    throw new ArgumentException("Choose story or leave invitation setup."); allowed.Add("invitationKind"); }
                if (fields.Contains("locationId")) { Id(Get("locationId")); allowed.Add("locationId"); } }
            if (phase is "start" or "encounter") allowed.Add("objectiveId");
            if (phase == "encounter") allowed.Add("outcome");
            if (phase == "finish") allowed.UnionWith(new[] { "encounterId", "outcome" });
            if (fields.Any(f => !allowed.Contains(f))) throw new ArgumentException("Fields must match the selected phase.");
            if (phase is "start" or "encounter") Id(Get("objectiveId"));
            if (phase == "encounter" && !new[] { "won", "lost", "withdrawn" }.Contains(Get("outcome")))
                throw new ArgumentException("Choose won, lost or withdrawn.");
            if (phase == "finish")
            {
                Id(Get("encounterId"));
                if (!new[] { "won", "lost", "withdrawn" }.Contains(Get("outcome"))) throw new ArgumentException("Choose won, lost or withdrawn.");
            }
            result.Add(item.Clone());
        }
        return result;
    }
}
