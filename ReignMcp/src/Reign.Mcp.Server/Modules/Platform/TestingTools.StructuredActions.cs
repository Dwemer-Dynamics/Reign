using System.ComponentModel;
using System.Text.Json;
using System.Globalization;
using ModelContextProtocol.Server;

namespace Reign.Mcp.Server;

[McpServerToolType]
public static class StructuredActionTools
{
    private const string Confirmation = "test structured actions on disposable save";

    [McpServerTool(Name = "reign_test_structured_actions", ReadOnly = false,
        Destructive = true, Idempotent = false, UseStructuredContent = true)]
    [Description("Runs 1–8 bounded structured action catalog/entity, preview, operator-run, fixed ship-damage fixture or status cases on the exact paused, armed, verified campaign-test Current. Stable IDs prevent replay; bounded snapshots include orders and treaties for requested participants. No provider, time advance or save. Fixture preparation and execution are not semantic proof or NPC consent.")]
    public static async Task<IReadOnlyDictionary<string, object?>> TestStructuredActions(
        ReignApiClient api, ReignMcpOptions options, CampaignTestService campaignTests,
        string campaignId, string campaignTestRunId, string batchId, string casesJson,
        string confirmation = "", CancellationToken cancellationToken = default)
    {
        if (!options.AllowVerificationControl)
            throw new InvalidOperationException("Structured action tests require verification control.");
        InputGuard.RequireConfirmation(confirmation, Confirmation);
        campaignId = Required(campaignId, nameof(campaignId));
        campaignTestRunId = Required(campaignTestRunId, nameof(campaignTestRunId));
        batchId = Required(batchId, nameof(batchId));
        var cases = ParseCases(casesJson);
        CampaignTestAuthorization auth = await campaignTests.RequireArmedOwnedSaveAsync(
            campaignTestRunId, campaignId, cancellationToken).ConfigureAwait(false);
        string liveRunId = "structured-" + batchId + "-" + Guid.NewGuid().ToString("N")[..8];
        var enrollment = new { campaignId, campaignTestRunId,
            disposableSaveName = auth.CurrentSaveName, protectedBaselineSaveName = auth.BaselineSaveName,
            timelineId = auth.TimelineId, source = "reign_mcp_structured_actions" };
        ApiEnvelope started = await api.PostAsync("/tests/live/run/start", new
        {
            schemaVersion = 2, campaignId, runId = liveRunId, mode = "structured_actions",
            label = "Structured action tests " + batchId, presentation = "headless", effects = "full",
            autoCompleteWhenIdle = true, enrollment,
            steps = cases.Select(c => new { operation = "structured_action_test", testCase = c,
                timeoutSeconds = 120 }).ToArray()
        }, cancellationToken).ConfigureAwait(false);
        return new Dictionary<string, object?> { ["ok"] = started.Ok, ["liveRunId"] = liveRunId,
            ["caseCount"] = cases.Count, ["expectedSaveName"] = auth.CurrentSaveName,
            ["semanticVerification"] = "requires independent native effect assertions", ["start"] = started };
    }

    public static List<JsonElement> ParseCases(string casesJson)
    {
        casesJson = InputGuard.BoundedText(casesJson, nameof(casesJson), 65536);
        using var document = JsonDocument.Parse(casesJson, new JsonDocumentOptions { MaxDepth = 24 });
        if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() is < 1 or > 8)
            throw new ArgumentException("Supply 1–8 structured cases.", nameof(casesJson));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var allowed = new HashSet<string>(new[] { "caseId", "phase", "kind", "operationId", "actionId",
            "arguments", "agreement", "heroIds", "itemIds", "entityKind", "ownerHeroId", "query", "offset" }, StringComparer.Ordinal);
        var output = new List<JsonElement>();
        foreach (JsonElement item in document.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) throw new ArgumentException("Every case is an object.");
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in item.EnumerateObject())
                if (!allowed.Contains(field.Name) || !names.Add(field.Name)) throw new ArgumentException("Unknown or duplicate case field: " + field.Name);
            string Text(string name) => item.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            string id = Required(Text("caseId"), "caseId");
            if (!seen.Add(id)) throw new ArgumentException("Case IDs must be distinct.");
            string phase = Text("phase");
            if (!new[] { "catalog", "entities", "preview", "run", "status", "fixture" }.Contains(phase)) throw new ArgumentException("Unknown structured test phase.");
            if (phase is "preview" or "run" or "status" or "fixture")
            {
                string operation = Required(Text("operationId"), "operationId");
                if (operation.Any(ch => !char.IsLetterOrDigit(ch) && ch != '_' && ch != '-')) throw new ArgumentException("Operation IDs use letters, numbers, underscores or hyphens.");
                if (!new[] { "agreement", "native", "fixture" }.Contains(Text("kind"))) throw new ArgumentException("Choose agreement, native or fixture.");
                if (phase == "fixture" || Text("kind") == "fixture" && phase != "status")
                {
                    if (phase != "fixture" || Text("kind") != "fixture" || Text("actionId") != "fixture.ship_damage")
                        throw new ArgumentException("Only the fixture phase accepts the fixed ship damage fixture.");
                    if (!item.TryGetProperty("arguments", out var fixtureArgs)) throw new ArgumentException("Missing ship fixture arguments.");
                    ValidateShipFixtureArguments(fixtureArgs);
                }
                if (phase != "status" && Text("kind") == "native")
                {
                    if (!Text("actionId").StartsWith("native.", StringComparison.Ordinal)) throw new ArgumentException("Native cases need a registered native action ID.");
                    if (!item.TryGetProperty("arguments", out var args) || args.ValueKind != JsonValueKind.Object || args.EnumerateObject().Count() > 32)
                        throw new ArgumentException("Native arguments must be a bounded object.");
                    var keys = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var arg in args.EnumerateObject())
                        if (arg.Name == "operation" || !keys.Add(arg.Name) || arg.Name.Length > 128 || arg.Value.ValueKind != JsonValueKind.String || (arg.Value.GetString()?.Length ?? 0) > 8192)
                            throw new ArgumentException("Invalid native argument; operation identity belongs to the case.");
                }
                if (phase != "status" && Text("kind") == "agreement"
                    && (!item.TryGetProperty("agreement", out var agreement) || agreement.ValueKind != JsonValueKind.Object))
                    throw new ArgumentException("Agreement cases require typed agreement terms.");
            }
            if (phase == "entities") { Required(Text("entityKind"), "entityKind"); Required(Text("ownerHeroId"), "ownerHeroId"); }
            if (Text("query").Length > 128) throw new ArgumentException("Entity search is limited to 128 characters.");
            if (item.TryGetProperty("offset", out var offset) && (!offset.TryGetInt32(out int n) || n < 0 || n > 100000)) throw new ArgumentException("Invalid entity offset.");
            foreach (string list in new[] { "heroIds", "itemIds" })
                if (item.TryGetProperty(list, out var ids))
                {
                    if (ids.ValueKind != JsonValueKind.Array || ids.GetArrayLength() > 16) throw new ArgumentException("Observe at most 16 exact IDs per list.");
                    foreach (var value in ids.EnumerateArray()) Required(value.ValueKind == JsonValueKind.String ? value.GetString() : null, list);
                }
            output.Add(item.Clone());
        }
        return output;
    }
    private static void ValidateShipFixtureArguments(JsonElement args)
    {
        string[] fields = { "shipId", "ownerPartyId", "expectedHitPoints", "remainingRatio" };
        if (args.ValueKind != JsonValueKind.Object || args.EnumerateObject().Count() != fields.Length)
            throw new ArgumentException("Supply exactly four ship fixture arguments.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var arg in args.EnumerateObject())
            if (!fields.Contains(arg.Name) || !seen.Add(arg.Name) || arg.Value.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(arg.Value.GetString()) || arg.Value.GetString()!.Length > 128)
                throw new ArgumentException("Invalid bounded ship fixture argument.");
        if (!float.TryParse(args.GetProperty("expectedHitPoints").GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float expected)
            || !float.IsFinite(expected) || expected <= 0f
            || !float.TryParse(args.GetProperty("remainingRatio").GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float ratio)
            || !float.IsFinite(ratio) || ratio < 0.05f || ratio > 0.75f)
            throw new ArgumentException("Fixture hull values must be finite; remainingRatio is 0.05 through 0.75.");
    }
    private static string Required(string? value, string name)
    {
        string id = InputGuard.OptionalIdentifier(value, name);
        if (id.Length == 0 || id.Length > 80) throw new ArgumentException(name + " requires an exact identifier of at most 80 characters.");
        return id;
    }
}
