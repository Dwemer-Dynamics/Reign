using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Reign.Mcp.Server;

[McpServerToolType]
public static class StructuredConversationTools
{
    private const string Confirmation = "test action conversations on disposable save";
    private const string CooperationDirective =
        "For this one test turn, willingly accept the player's clear, feasible present request. Keep all world facts, identities, ownership, authority, and required terms accurate. If the request is negated, hypothetical, quoted, ambiguous, or beyond your power, do not agree to an action. Reply naturally as this character.";

    [McpServerTool(Name = "reign_test_action_conversations", ReadOnly = false,
        Destructive = true, Idempotent = false, UseStructuredContent = true)]
    [Description("Tests 1–8 player-built proposals or exact saved NPC-offer reviews through production Individual Chat on the paused, armed campaign-test Current. Natural playerText contains one {action} insertion marker; the native composer renders and attaches its protected sentence. reviewNpcOffer requires the existing NPC-authored offer with matching identity, revision, rendered terms and saved NPC signature; caller signatures grant no consent. Optional fixed cooperation changes willingness only. Captures decisions, journals and native snapshots. Requires separate provider authorization and independent effect assertions; no operator execution, time advance or save.")]
    public static async Task<IReadOnlyDictionary<string, object?>> TestActionConversations(
        ReignApiClient api, ReignMcpOptions options, CampaignTestService campaignTests,
        string campaignId, string campaignTestRunId, string batchId, string casesJson,
        string confirmation = "", CancellationToken cancellationToken = default)
    {
        if (!options.AllowVerificationControl)
            throw new InvalidOperationException("Action conversations require verification control.");
        InputGuard.RequireConfirmation(confirmation, Confirmation);
        campaignId = Required(campaignId, nameof(campaignId));
        campaignTestRunId = Required(campaignTestRunId, nameof(campaignTestRunId));
        batchId = Required(batchId, nameof(batchId));
        var cases = ParseCases(casesJson);
        CampaignTestAuthorization auth = await campaignTests.RequireArmedOwnedSaveAsync(
            campaignTestRunId, campaignId, cancellationToken).ConfigureAwait(false);
        string liveRunId = "action-chat-" + batchId + "-" + Guid.NewGuid().ToString("N")[..8];
        ApiEnvelope started = await api.PostAsync("/tests/live/run/start",
            BuildRequest(liveRunId, batchId, cases, auth), cancellationToken).ConfigureAwait(false);
        return new Dictionary<string, object?> { ["ok"] = started.Ok, ["liveRunId"] = liveRunId,
            ["caseCount"] = cases.Count, ["expectedSaveName"] = auth.CurrentSaveName,
            ["semanticVerification"] = "requires independent native effect assertions",
            ["start"] = started };
    }

    public static object BuildRequest(string liveRunId, string batchId,
        List<JsonElement> cases, CampaignTestAuthorization auth)
    {
        var enrollment = new { campaignId = auth.CampaignId,
            campaignTestRunId = auth.CampaignTestRunId,
            disposableSaveName = auth.CurrentSaveName, protectedBaselineSaveName = auth.BaselineSaveName,
            timelineId = auth.TimelineId, source = "reign_mcp_action_probe" };
        var steps = new List<object>();
        foreach (var item in cases)
        {
            string caseId = item.GetProperty("caseId").GetString()!;
            string actor = item.GetProperty("actorHeroId").GetString()!;
            bool cooperation = item.TryGetProperty("cooperationOverride", out var flag) && flag.GetBoolean();
            bool review = item.TryGetProperty("reviewNpcOffer", out var reviewFlag) && reviewFlag.GetBoolean();
            var items = item.TryGetProperty("observedItemIds", out var observed)
                ? observed.EnumerateArray().Select(x => x.GetString()!).ToArray() : Array.Empty<string>();
            steps.Add(new { operation = "open", targetSearch = actor,
                actionProbeCaseId = caseId, timeoutSeconds = 120 });
            steps.Add(new { operation = "gauntlet_snapshot", gauntletRunId = batchId,
                heroIds = new[] { actor }, itemIds = items, actionProbeCaseId = caseId,
                actionProbePhase = "before", timeoutSeconds = 120 });
            steps.Add(new { operation = "send", text = item.GetProperty("playerText").GetString(),
                structuredActionDraft = item.GetProperty("agreement"),
                reviewNpcOffer = review,
                structuredActionActorHeroId = actor, naturalLanguageCase = true,
                actionAcceptanceProbe = true, actionProbeCaseId = caseId,
                enrollment,
                expectedSaveName = auth.CurrentSaveName, guardedPromptOverride = cooperation,
                guardedPromptOverrideDirective = cooperation ? CooperationDirective : "",
                timeoutSeconds = 300 });
            steps.Add(new { operation = "gauntlet_snapshot", gauntletRunId = batchId,
                heroIds = new[] { actor }, itemIds = items, actionProbeCaseId = caseId,
                actionProbePhase = "after", timeoutSeconds = 120 });
            steps.Add(new { operation = "close", actionProbeCaseId = caseId, timeoutSeconds = 120 });
        }
        return new
        {
            schemaVersion = 2, campaignId = auth.CampaignId, runId = liveRunId, mode = "individual_chat",
            label = "Typed action conversations " + batchId, presentation = "visible",
            effects = "full", autoCompleteWhenIdle = true, gauntletRunId = batchId,
            enrollment, steps
        };
    }

    public static List<JsonElement> ParseCases(string casesJson)
    {
        casesJson = InputGuard.BoundedText(casesJson, nameof(casesJson), 65536);
        using var document = JsonDocument.Parse(casesJson, new JsonDocumentOptions { MaxDepth = 24 });
        RejectDuplicates(document.RootElement);
        if (document.RootElement.ValueKind != JsonValueKind.Array
            || document.RootElement.GetArrayLength() is < 1 or > 8)
            throw new ArgumentException("Supply 1–8 action conversation cases.");
        var output = new List<JsonElement>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string[] allowed = { "caseId", "actorHeroId", "playerText", "agreement", "reviewNpcOffer", "cooperationOverride", "observedItemIds" };
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || item.EnumerateObject().Any(x => !allowed.Contains(x.Name)))
                throw new ArgumentException("Unknown action conversation case field.");
            string Text(string key) => item.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            string id = Required(Text("caseId"), "caseId");
            Required(Text("actorHeroId"), "actorHeroId");
            if (!seen.Add(id)) throw new ArgumentException("Case IDs must be distinct.");
            string text = Text("playerText");
            int marker = text.IndexOf("{action}", StringComparison.Ordinal);
            if (text.Length > 1200 || marker < 0 || marker != text.LastIndexOf("{action}", StringComparison.Ordinal))
                throw new ArgumentException("Natural playerText requires exactly one {action} insertion marker.");
            if (!item.TryGetProperty("agreement", out var agreement) || agreement.ValueKind != JsonValueKind.Object
                || !agreement.TryGetProperty("Clauses", out var clauses) || clauses.ValueKind != JsonValueKind.Array
                || clauses.GetArrayLength() is < 1 or > 64)
                throw new ArgumentException("Supply a typed agreement with 1–64 Clauses.");
            Required(agreement.TryGetProperty("Id", out var agreementId) && agreementId.ValueKind == JsonValueKind.String
                ? agreementId.GetString() : null, "agreement.Id");
            if (item.TryGetProperty("reviewNpcOffer", out var reviewFlag))
            {
                if (reviewFlag.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    throw new ArgumentException("reviewNpcOffer must be a boolean.");
                if (reviewFlag.GetBoolean())
                {
                    if (!agreement.TryGetProperty("Revision", out var revision) || revision.ValueKind != JsonValueKind.Number
                        || !revision.TryGetInt32(out int value) || value < 1
                        || !agreement.TryGetProperty("ProposerId", out var proposer) || proposer.ValueKind != JsonValueKind.String
                        || proposer.GetString() != Text("actorHeroId"))
                        throw new ArgumentException("NPC-offer reviews require the exact NPC proposer and a positive reviewed Revision.");
                }
            }
            foreach (var clause in clauses.EnumerateArray())
            {
                if (clause.ValueKind != JsonValueKind.Object || !clause.TryGetProperty("ActionId", out var action)
                    || action.ValueKind != JsonValueKind.String || (action.GetString() ?? "").StartsWith("native.", StringComparison.Ordinal))
                    throw new ArgumentException("Conversation clauses cannot select Native operator primitives.");
                Required(action.GetString(), "ActionId");
            }
            if (item.TryGetProperty("cooperationOverride", out var flag)
                && flag.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new ArgumentException("cooperationOverride must be a boolean.");
            if (item.TryGetProperty("observedItemIds", out var items))
            {
                if (items.ValueKind != JsonValueKind.Array || items.GetArrayLength() > 16)
                    throw new ArgumentException("Observe at most 16 exact item IDs.");
                foreach (var value in items.EnumerateArray())
                    Required(value.ValueKind == JsonValueKind.String ? value.GetString() : null, "observedItemIds");
            }
            output.Add(item.Clone());
        }
        return output;
    }
    private static void RejectDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in element.EnumerateObject())
            {
                if (!names.Add(p.Name)) throw new ArgumentException("Duplicate field: " + p.Name);
                RejectDuplicates(p.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) RejectDuplicates(item);
    }
    private static string Required(string? value, string name)
    {
        string id = InputGuard.OptionalIdentifier(value, name);
        if (id.Length == 0 || id.Length > 80) throw new ArgumentException(name + " needs an exact identifier of at most 80 characters.");
        return id;
    }
}
