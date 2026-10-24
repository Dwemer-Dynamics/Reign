using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Reign.Mcp.Server;

[McpServerToolType]
public static class PersonalGossipTestTools
{
    public const string Confirmation = "test personal gossip on disposable save";

    [McpServerTool(Name = "reign_get_personal_gossip_test_manifest", ReadOnly = true, Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Reads the versioned pass-once personal gossip launch requirements and risk-based playstyle matrix. Does not start a test, call a provider or mutate a campaign.")]
    public static JsonElement GetManifest(ReignMcpOptions options)
    {
        string path = Path.Combine(options.WorkspaceRoot, "ReignServer", "tests", "ReignLiveTest",
            "scenarios", "personal-gossip-launch-manifest.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (document.RootElement.GetProperty("schema").GetString() != "reign-personal-gossip-launch-manifest-v1")
            throw new InvalidOperationException("Unsupported personal gossip test manifest.");
        return document.RootElement.Clone();
    }

    [McpServerTool(Name = "reign_test_personal_gossip", ReadOnly = false, Destructive = true,
        Idempotent = false, UseStructuredContent = true)]
    [Description("Runs 1-8 bounded gossip cases on the exact armed paused campaign-test Current with retained recovery. Conversation supports2-8 exact group participantHeroIds, unchanged natural whispers and read-only scheduling observations before closure. prepare.activateResidents allows1-4 inactive adult actor/family prerequisites, journaled before safe native activation/placement. No motive, consent or source facts are injected. Policy probes write no gossip/history. Conversation/tick/composition_probe require explicit allowProviderCalls. composition_probe makes one unpublished proposal from an exact terminal original malicious job; no live retry/history changes. No time, save or arbitrary SQL/property/prompt override. Completion collects evidence, never launch certification.")]
    public static async Task<IReadOnlyDictionary<string, object?>> TestPersonalGossip(
        ReignApiClient api, ReignMcpOptions options, CampaignTestService campaignTests,
        string campaignId, string campaignTestRunId, string batchId, string casesJson,
        bool allowProviderCalls = false, string confirmation = "",
        CancellationToken cancellationToken = default)
    {
        if (!options.AllowVerificationControl) throw new InvalidOperationException("Gossip tests require verification control.");
        InputGuard.RequireConfirmation(confirmation, Confirmation);
        campaignId = Id(campaignId); campaignTestRunId = Id(campaignTestRunId); batchId = Id(batchId);
        var cases = ParseCases(casesJson);
        if (!allowProviderCalls && cases.Any(c => new[] { "conversation", "tick", "composition_probe" }.Contains(c.GetProperty("phase").GetString())))
            throw new InvalidOperationException("Conversation/tick/composition_probe requires explicit provider authorization.");
        var auth = await campaignTests.RequireArmedOwnedPausedActionSaveAsync(campaignTestRunId, campaignId, cancellationToken).ConfigureAwait(false);
        campaignTests.RequireRetainedRecoveryCheckpoint(auth);
        string liveRun = "gossip-" + batchId + "-" + Guid.NewGuid().ToString("N")[..8];
        var enrollment = new { campaignId, campaignTestRunId, disposableSaveName = auth.CurrentSaveName,
            protectedBaselineSaveName = auth.BaselineSaveName, timelineId = auth.TimelineId,
            source = "reign_mcp_personal_gossip", recoveryCheckpointVerified = true,
            allowProviderCalls };
        var started = await api.PostAsync("/tests/live/run/start", new {
            schemaVersion = 2, campaignId, runId = liveRun, mode = "personal_gossip",
            label = "Personal gossip " + batchId, presentation = "visible", effects = "full",
            autoCompleteWhenIdle = true, enrollment,
            steps = cases.Select(c => new { operation = "personal_gossip_test", testCase = c,
                enrollment, timeoutSeconds = new[] { "conversation", "composition_probe" }.Contains(c.GetProperty("phase").GetString()) ? 1800 : 120 }).ToArray()
        }, cancellationToken).ConfigureAwait(false);
        return new Dictionary<string, object?> { ["ok"] = started.Ok, ["liveRunId"] = liveRun,
            ["caseCount"] = cases.Count, ["expectedSaveName"] = auth.CurrentSaveName,
            ["coverage"] = "collected observations only; individual/party cases use the shipping player close after while-open observations; independent assertions and provider-quality review required",
            ["start"] = started };
    }

    private static string Id(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128
            || value.Any(c => !char.IsLetterOrDigit(c) && c != '_' && c != '-' && c != ':'))
            throw new ArgumentException("Use a bounded exact identity.");
        return value;
    }

    private static void ExactFields(JsonElement obj, params string[] allowed)
    {
        if (obj.ValueKind != JsonValueKind.Object) throw new ArgumentException("Expected an object.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in obj.EnumerateObject())
            if (!allowed.Contains(f.Name) || !seen.Add(f.Name)) throw new ArgumentException("Unknown or duplicate case field: " + f.Name);
    }

    private static void Text(JsonElement obj, string name, int maximum, bool required = false)
    {
        if (!obj.TryGetProperty(name, out var value)) { if (required) throw new ArgumentException("Missing " + name); return; }
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString())
            || value.GetString()!.Length > maximum) throw new ArgumentException("Invalid " + name);
    }

    private static void Array(JsonElement obj, string name, int maximum, Action<JsonElement> validate)
    {
        if (!obj.TryGetProperty(name, out var values)) return;
        if (values.ValueKind != JsonValueKind.Array || values.GetArrayLength() > maximum)
            throw new ArgumentException("Invalid bounded array " + name);
        foreach (var value in values.EnumerateArray()) validate(value);
    }

    public static List<JsonElement> ParseCases(string text)
    {
        text = InputGuard.BoundedText(text, nameof(text), 65536);
        using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 8 });
        if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() is < 1 or > 8)
            throw new ArgumentException("Supply one through eight gossip cases.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<JsonElement>();
        foreach (var c in document.RootElement.EnumerateArray())
        {
            ExactFields(c, "caseId", "phase", "heroIds", "search", "actorHeroId", "sourceMarker",
                "placements", "relationships", "traitProfiles", "draws", "playerTurns", "conversationMode",
                "sourceText", "targets", "mentionedIds", "topicType", "familyMotivation", "messageId",
                "offset", "limit", "compositionMode", "participantHeroIds", "activateResidents");
            string id = Id(c.GetProperty("caseId").GetString() ?? "");
            if (!ids.Add(id)) throw new ArgumentException("Case identities must be distinct.");
            Text(c, "phase", 24, true);
            string phase = c.GetProperty("phase").GetString()!;
            if (!new[] { "catalog", "snapshot", "presence_contract", "prepare", "policy_probe", "tick", "conversation", "composition_probe" }.Contains(phase))
                throw new ArgumentException("Unknown gossip phase.");
            var phaseFields = phase switch {
                "catalog" => new[] { "search", "heroIds", "offset", "limit" },
                "snapshot" => new[] { "heroIds", "actorHeroId", "offset", "limit" },
                "prepare" => new[] { "actorHeroId", "sourceMarker", "placements", "relationships", "traitProfiles", "draws", "compositionMode", "activateResidents" },
                "policy_probe" => new[] { "actorHeroId", "heroIds", "sourceText", "mentionedIds", "targets", "topicType" },
                "conversation" => new[] { "actorHeroId", "playerTurns", "conversationMode", "participantHeroIds" },
                "composition_probe" => new[] { "actorHeroId", "messageId" },
                _ => System.Array.Empty<string>() };
            ExactFields(c, new[] { "caseId", "phase" }.Concat(phaseFields).ToArray());
            Text(c, "search", 80); Text(c, "sourceText", 4000); Text(c, "sourceMarker", 100);
            if (c.TryGetProperty("actorHeroId", out var actor)) Id(actor.GetString() ?? "");
            if (new[] { "prepare", "policy_probe", "conversation", "composition_probe" }.Contains(phase)
                && !c.TryGetProperty("actorHeroId", out _)) throw new ArgumentException("The phase needs actorHeroId.");
            if (c.TryGetProperty("messageId", out var message)) Id(message.GetString() ?? "");
            if (phase == "composition_probe" && !c.TryGetProperty("messageId", out _))
                throw new ArgumentException("A composition probe needs exact messageId.");
            foreach (string field in new[] { "offset", "limit", "familyMotivation" })
                if (c.TryGetProperty(field, out var number)
                    && (!number.TryGetInt32(out int n) || n < 0 || n > (field == "offset" ? 8192 : 100)))
                    throw new ArgumentException("Invalid " + field);
            Array(c, "heroIds", 16, v => Id(v.GetString() ?? ""));
            Array(c, "mentionedIds", 16, v => Id(v.GetString() ?? ""));
            Array(c, "participantHeroIds", 8, v => Id(v.GetString() ?? ""));
            Array(c, "activateResidents", 4, v => Id(v.GetString() ?? ""));
            Array(c, "playerTurns", 8, v => {
                if (v.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(v.GetString()) || v.GetString()!.Length > 1800)
                    throw new ArgumentException("Invalid natural player turn.");
            });
            if (phase == "conversation" && (!c.TryGetProperty("playerTurns", out var turns) || turns.GetArrayLength() == 0))
                throw new ArgumentException("Conversation needs natural player turns.");
            if (c.TryGetProperty("conversationMode", out var mode)
                && !new[] { "individual_chat", "party_chat", "social_event", "wilderness_event" }.Contains(mode.GetString()))
                throw new ArgumentException("Unsupported production conversation mode.");
            if (phase == "conversation" && c.TryGetProperty("conversationMode", out var partyMode)
                && partyMode.GetString() == "party_chat" && !c.TryGetProperty("participantHeroIds", out _))
                throw new ArgumentException("Party chat needs an explicit group of exact participants.");
            if (c.TryGetProperty("participantHeroIds", out var group))
            {
                var groupIds = group.EnumerateArray().Select(v => v.GetString()!).ToArray();
                if (groupIds.Length < 2 || groupIds.Distinct(StringComparer.Ordinal).Count() != groupIds.Length
                    || !groupIds.Contains(c.GetProperty("actorHeroId").GetString(), StringComparer.Ordinal)
                    || !c.TryGetProperty("conversationMode", out var groupMode) || groupMode.GetString() == "individual_chat")
                    throw new ArgumentException("A group needs two through eight distinct exact participants including its actor and a group conversation mode.");
            }
            if (c.TryGetProperty("activateResidents", out var activation))
            {
                var activationIds = activation.EnumerateArray().Select(v => v.GetString()!).ToArray();
                if (activationIds.Length == 0 || activationIds.Distinct(StringComparer.Ordinal).Count() != activationIds.Length
                    || !c.TryGetProperty("placements", out var destinations)
                    || activationIds.Any(id => destinations.EnumerateArray().Count(p => p.GetProperty("heroId").GetString() == id) != 1))
                    throw new ArgumentException("Every activated resident needs one explicit placement; duplicate activation identities are forbidden.");
            }
            if (c.TryGetProperty("topicType", out var kind) && !new[] { "positive", "negative", "impression" }.Contains(kind.GetString()))
                throw new ArgumentException("Invalid topic type.");
            if (c.TryGetProperty("compositionMode", out var composition)
                && !new[] { "production", "fail_once", "fail_always", "delay_once", "delay_past_watch_once" }.Contains(composition.GetString()))
                throw new ArgumentException("Unsupported bounded composition fault.");
            Array(c, "placements", 16, v => {
                ExactFields(v, "heroId", "settlementId", "party");
                Id(v.GetProperty("heroId").GetString() ?? "");
                bool town = v.TryGetProperty("settlementId", out var s), party = v.TryGetProperty("party", out var p);
                if (town == party) throw new ArgumentException("Choose a settlement or main party placement.");
                if (town) Id(s.GetString() ?? ""); else if (p.GetString() != "main") throw new ArgumentException("Only the actual main party is allowed.");
            });
            Array(c, "relationships", 64, v => {
                ExactFields(v, "observerId", "subjectId", "affinity");
                if (Id(v.GetProperty("observerId").GetString() ?? "") == Id(v.GetProperty("subjectId").GetString() ?? ""))
                    throw new ArgumentException("No self relationship.");
                if (!v.GetProperty("affinity").TryGetInt32(out int a) || a < -100 || a > 100)
                    throw new ArgumentException("Affinity must be -100 through 100.");
            });
            Array(c, "traitProfiles", 16, v => {
                ExactFields(v, "heroId", "profile"); Id(v.GetProperty("heroId").GetString() ?? "");
                if (!new[] { "excitable", "reserved", "honest_loyal", "vengeful" }.Contains(v.GetProperty("profile").GetString()))
                    throw new ArgumentException("Unknown fixed personality fixture.");
            });
            Array(c, "targets", 16, v => {
                ExactFields(v, "heroId", "hurt", "grievance", "relevant", "evidenceQuote");
                Id(v.GetProperty("heroId").GetString() ?? "");
                foreach (string f in new[] { "hurt", "grievance" })
                    if (v.TryGetProperty(f, out var n) && (!n.TryGetInt32(out int a) || !new[] { 0,25,50,75,100 }.Contains(a)))
                        throw new ArgumentException("Use defined evidence levels.");
                if (v.TryGetProperty("relevant", out var b) && b.ValueKind != JsonValueKind.True && b.ValueKind != JsonValueKind.False)
                    throw new ArgumentException("Relevance is a boolean.");
                Text(v, "evidenceQuote", 1000);
            });
            if (c.TryGetProperty("draws", out var draws))
            {
                ExactFields(draws, "share", "negative", "disclosure", "distortion", "severity",
                    "negative_recipient", "friendly_recipient", "raise", "reply_disclosure");
                foreach (var f in draws.EnumerateObject())
                {
                    if (!f.Value.TryGetDouble(out double n) || !double.IsFinite(n)
                        || (f.Name.EndsWith("_recipient", StringComparison.Ordinal)
                            ? n < 0 || n >= 1 : n < 1 || n > 100 || n != Math.Truncate(n)))
                        throw new ArgumentException("Invalid saved stage draw.");
                }
                Text(c, "sourceMarker", 100, true);
                if (phase != "prepare") throw new ArgumentException("Stage draws are prepared before a source, never edited afterward.");
            }
            if (c.TryGetProperty("draws", out _) || c.TryGetProperty("compositionMode", out _))
            {
                Text(c, "sourceMarker", 100, true);
                if (c.GetProperty("sourceMarker").GetString()!.Length < 8)
                    throw new ArgumentException("A natural marker needs at least eight characters.");
            }
            var participants = new HashSet<string>(StringComparer.Ordinal);
            if (c.TryGetProperty("actorHeroId", out var owner)) participants.Add(owner.GetString()!);
            foreach (string f in new[] { "heroIds", "mentionedIds", "participantHeroIds", "activateResidents" })
                if (c.TryGetProperty(f, out var list)) foreach (var value in list.EnumerateArray()) participants.Add(value.GetString()!);
            foreach (string f in new[] { "placements", "traitProfiles", "targets", "relationships" })
                if (c.TryGetProperty(f, out var list)) foreach (var value in list.EnumerateArray())
                    foreach (string k in f == "relationships" ? new[] { "observerId", "subjectId" } : new[] { "heroId" }) participants.Add(value.GetProperty(k).GetString()!);
            if (participants.Count > 16) throw new ArgumentException("At most sixteen distinct participants per case.");
            result.Add(c.Clone());
        }
        return result;
    }
}

