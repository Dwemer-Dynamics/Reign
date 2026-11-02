using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Reign.Mcp.Server;

[McpServerToolType]
public static class NpcLivesTestTools
{
    [McpServerTool(Name = "reign_test_npc_lives", ReadOnly = false, Destructive = true, Idempotent = false, UseStructuredContent = true)]
    [Description("Open an unsent real party action builder with composer (two distinct actual party heroId/otherHeroId and stable operationId only), snapshot one NPC, prepare an unaccepted authored story or leave invitation, decline its exact existing NPC-signed offer through production refusal, place the player at an accepted story's bound destination, or resolve its real native duel/hideout with bounded win/loss/withdraw. decline requires heroId, matterId, proposalId, its observed termsHash and a positive decimal revision; it cannot accept an offer or invent terms. encounter witnesses both actual agents and the bound native captive; controlled-win cleanup may acknowledge only that captive's owned conversation at most once, with no dialogue choice or rescue credit. place journals one movement with no destination override. composer journals before opening; identical replay only observes the retained receipt/current UI, changed terms fail and partial/uncertain attempts never reopen. It submits no action, consent, text or announcement; use the real UI to insert/cancel and independently verify conservation. announcement_transport accepts only caseId, phase, two actual party heroId/otherHeroId, operationId and control arm/observe/release. It holds only original announcement dispatch for at most 180 monotonic seconds; only the actual UI queues text and native hearers. It never sends, edits, retargets or acknowledges a payload; replay never rearms. Release or expiry resumes ordinary transport. Prove original outbox drained before time/save. Requires the exact armed paused verified campaign-test Current and retained recovery checkpoint. No provider calls, time advance or save. Verify production outcomes and exactly-once effects independently.")]
    public static async Task<IReadOnlyDictionary<string, object?>> TestNpcLives(
        ReignApiClient api, ReignMcpOptions options, CampaignTestService campaignTests,
        string campaignId, string campaignTestRunId, string batchId, string casesJson,
        string confirmation = "", CancellationToken cancellationToken = default)
    {
        if (!options.AllowVerificationControl) throw new InvalidOperationException("NPC Lives tests require verification control.");
        InputGuard.RequireConfirmation(confirmation, "test NPC lives on disposable save");
        campaignId = Id(campaignId); campaignTestRunId = Id(campaignTestRunId); batchId = Id(batchId);
        var cases = ParseCases(casesJson);
        var auth = await campaignTests.RequireArmedOwnedPausedActionSaveAsync(campaignTestRunId, campaignId, cancellationToken).ConfigureAwait(false);
        campaignTests.RequireRetainedRecoveryCheckpoint(auth);
        string liveRunId = "npc-lives-" + batchId + "-" + Guid.NewGuid().ToString("N")[..8];
        var started = await api.PostAsync("/tests/live/run/start", new {
            schemaVersion = 2, campaignId, runId = liveRunId, mode = "npc_lives", label = "NPC Lives native checks",
            presentation = "headless", effects = "full", autoCompleteWhenIdle = true,
            enrollment = new { campaignId, campaignTestRunId, disposableSaveName = auth.CurrentSaveName,
                protectedBaselineSaveName = auth.BaselineSaveName, timelineId = auth.TimelineId,
                source = "reign_mcp_npc_lives", recoveryCheckpointVerified = true },
            steps = cases.Select(c => new { operation = "npc_lives_test", testCase = c, timeoutSeconds = 120 }).ToArray()
        }, cancellationToken).ConfigureAwait(false);
        return new Dictionary<string, object?> { ["ok"] = started.Ok, ["liveRunId"] = liveRunId,
            ["caseCount"] = cases.Count, ["expectedSaveName"] = auth.CurrentSaveName,
            ["coverage"] = "operator native fixture; verify production outcome and exactly-once rewards", ["start"] = started };
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
                if (!new[] { "caseId", "phase", "storyId", "objectiveId", "encounterId", "outcome", "heroId", "otherHeroId", "operationId", "control", "templateId", "chapterId", "locationId", "invitationKind", "matterId", "proposalId", "revision", "termsHash" }.Contains(field.Name)
                    || !fields.Add(field.Name) || field.Value.ValueKind != JsonValueKind.String)
                    throw new ArgumentException("Unknown, duplicate or non-string case field.");
            string Get(string name) => item.TryGetProperty(name, out var v) ? v.GetString() ?? "" : "";
            if (!ids.Add(Id(Get("caseId")))) throw new ArgumentException("Case identities must be distinct.");
            string phase = Get("phase");
            if (!new[] { "snapshot", "prepare", "decline", "composer", "announcement_transport", "place", "observe", "start", "finish", "encounter" }.Contains(phase)) throw new ArgumentException("Choose snapshot, prepare, decline, composer, announcement_transport, place, observe, start, finish or encounter.");
            var allowed = new HashSet<string>(new[] { "caseId", "phase" }, StringComparer.Ordinal);
            if (phase is "prepare" or "snapshot" or "decline" or "composer" or "announcement_transport") { Id(Get("heroId")); allowed.Add("heroId"); }
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
