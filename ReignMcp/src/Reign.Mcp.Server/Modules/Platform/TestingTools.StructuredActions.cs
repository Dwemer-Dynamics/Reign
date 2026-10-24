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
    [Description("Runs 1–8 bounded structured action catalog/entity, preview, operator-run, fixed ship-damage, siege-setup, player-settlement-encounter, ruler, government-player-flow or accepted-guide-player-step fixture, or status cases on the exact paused, armed, verified campaign-test Current. Stable IDs prevent replay; bounded native observations include combat, orders and treaties. Siege, player encounter, ruler, government and guide fixtures require a retained recovery checkpoint. Government fixtures visit the actual safe friendly capital or open the real unsent builder in an already-open ordinary member chat. Guide steps only catch the on-foot player up to the exact live mutually accepted ordinary-chat guide, at most twenty metres; no coordinates, NPC movement, destination change or completion. No provider, time advance or save. Fixture preparation and execution are not semantic proof or NPC consent.")]
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
        CampaignTestAuthorization auth = await campaignTests.RequireArmedOwnedPausedActionSaveAsync(
            campaignTestRunId, campaignId, cancellationToken).ConfigureAwait(false);
        bool recoveryFixture = cases.Any(RequiresRecoveryCheckpoint);
        if (recoveryFixture)
            campaignTests.RequireRetainedRecoveryCheckpoint(auth);
        string liveRunId = "structured-" + batchId + "-" + Guid.NewGuid().ToString("N")[..8];
        var enrollment = new { campaignId, campaignTestRunId,
            disposableSaveName = auth.CurrentSaveName, protectedBaselineSaveName = auth.BaselineSaveName,
            timelineId = auth.TimelineId, source = "reign_mcp_structured_actions",
            recoveryCheckpointVerified = recoveryFixture };
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
            "arguments", "agreement", "heroIds", "itemIds", "entityKind", "ownerHeroId", "counterpartHeroId", "query", "offset" }, StringComparer.Ordinal);
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
                    if (phase != "fixture" || Text("kind") != "fixture")
                        throw new ArgumentException("Fixed fixtures require the fixture phase and kind.");
                    if (!item.TryGetProperty("arguments", out var fixtureArgs)) throw new ArgumentException("Missing fixture arguments.");
                    if (Text("actionId") == "fixture.ship_damage") ValidateShipFixtureArguments(fixtureArgs);
                    else if (Text("actionId") == "fixture.siege_setup") ValidateSiegeFixtureArguments(fixtureArgs);
                    else if (Text("actionId") == "fixture.player_settlement_encounter") ValidatePlayerEncounterFixtureArguments(fixtureArgs);
                    else if (Text("actionId") == "fixture.ruler_setup") ValidateRulerFixtureArguments(fixtureArgs);
                    else if (Text("actionId") == "fixture.follow_accepted_guide_step") ValidateAcceptedGuideStepArguments(fixtureArgs);
                    else if (Text("actionId") is "fixture.government_capital_visit" or "fixture.government_action_composer")
                        ValidateGovernmentFixtureArguments(Text("actionId"), fixtureArgs);
                    else throw new ArgumentException("Choose a registered fixed structured fixture.");
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
            bool pregnancyDiscovery = phase == "entities" && Text("entityKind") == "pregnancy";
            bool hasCounterpart = item.TryGetProperty("counterpartHeroId", out var counterpartValue);
            if (pregnancyDiscovery)
            {
                if (!hasCounterpart || counterpartValue.ValueKind != JsonValueKind.String)
                    throw new ArgumentException("Pregnancy discovery requires a string counterpartHeroId.");
                string rawCounterpart = counterpartValue.GetString() ?? "";
                string counterpart = InputGuard.OptionalIdentifier(rawCounterpart, "counterpartHeroId");
                if (counterpart.Length is < 1 or > 128 || counterpart != rawCounterpart)
                    throw new ArgumentException("counterpartHeroId requires an exact identifier of at most 128 characters without outer whitespace.");
            }
            else if (hasCounterpart)
                throw new ArgumentException("counterpartHeroId is supported only for pregnancy entity discovery.");
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
    public static bool RequiresRecoveryCheckpoint(JsonElement testCase) =>
        testCase.TryGetProperty("phase", out var phase) && phase.ValueKind == JsonValueKind.String && phase.GetString() == "fixture"
        && testCase.TryGetProperty("actionId", out var id) && id.ValueKind == JsonValueKind.String
        && id.GetString() is "fixture.siege_setup" or "fixture.player_settlement_encounter" or "fixture.ruler_setup"
            or "fixture.government_capital_visit" or "fixture.government_action_composer" or "fixture.follow_accepted_guide_step";

    private static void ValidateAcceptedGuideStepArguments(JsonElement args)
    {
        string[] fields = ["agreementId", "clauseId"];
        if (args.ValueKind != JsonValueKind.Object || args.EnumerateObject().Count() != fields.Length)
            throw new ArgumentException("Supply exactly the accepted guide agreementId and clauseId.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var arg in args.EnumerateObject())
        {
            if (!fields.Contains(arg.Name) || !seen.Add(arg.Name) || arg.Value.ValueKind != JsonValueKind.String)
                throw new ArgumentException("Invalid accepted guide identifier.");
            string value = arg.Value.GetString()!;
            if (value.Length is < 1 or > 80 || value.Any(ch => !char.IsAsciiLetterOrDigit(ch) && ch != '_' && ch != '-'))
                throw new ArgumentException("Accepted guide identifiers require one through eighty ASCII letters, digits, underscores or hyphens.");
        }
    }

    private static void ValidateGovernmentFixtureArguments(string actionId, JsonElement args)
    {
        string[] fields = actionId == "fixture.government_capital_visit"
            ? ["expectedKingdomId", "expectedCapitalSettlementId", "expectedOwnerClanId"]
            : ["expectedHeroId", "businessId"];
        if (args.ValueKind != JsonValueKind.Object || args.EnumerateObject().Count() != fields.Length)
            throw new ArgumentException("Supply exactly the government fixture identifiers.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var arg in args.EnumerateObject())
        {
            if (!fields.Contains(arg.Name) || !seen.Add(arg.Name) || arg.Value.ValueKind != JsonValueKind.String)
                throw new ArgumentException("Invalid government fixture identifier.");
            Required(arg.Value.GetString(), arg.Name);
        }
    }

    private static void ValidateRulerFixtureArguments(JsonElement args)
    {
        string[] fields = { "profile", "heroId", "settlementId", "otherHeroId", "amount" };
        if (args.ValueKind != JsonValueKind.Object || args.EnumerateObject().Count() != fields.Length)
            throw new ArgumentException("Supply exactly five ruler fixture fields.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in args.EnumerateObject())
            if (!fields.Contains(p.Name) || !seen.Add(p.Name) || p.Value.ValueKind != JsonValueKind.String || (p.Value.GetString()?.Length ?? 0) > 128)
                throw new ArgumentException("Invalid ruler fixture field.");
        if (!new[] { "ruler", "garrison", "army", "party", "snapshot", "captivity", "ownership", "loyalty", "wound", "capture_roll", "judgment_draw", "prison_guard", "ransom_offer", "barter_release", "barter_transfer", "mortality", "news" }.Contains(args.GetProperty("profile").GetString())
            || string.IsNullOrWhiteSpace(args.GetProperty("heroId").GetString()) || string.IsNullOrWhiteSpace(args.GetProperty("settlementId").GetString())
            || !int.TryParse(args.GetProperty("amount").GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int amount) || amount < 0 || amount > 4000)
            throw new ArgumentException("Invalid ruler fixture profile, identity or amount.");
        if (args.GetProperty("profile").GetString() == "snapshot" && amount != 0)
            throw new ArgumentException("Snapshot requires amount zero.");
        string profile = args.GetProperty("profile").GetString()!;
        if (profile == "news" && (amount > 7 || string.IsNullOrWhiteSpace(args.GetProperty("otherHeroId").GetString())
            || args.GetProperty("heroId").GetString() == args.GetProperty("otherHeroId").GetString()))
            throw new ArgumentException("News samples require distinct heroes and amount zero through seven.");
        if (profile == "judgment_draw" && (amount >= 4000 || string.IsNullOrWhiteSpace(args.GetProperty("otherHeroId").GetString())
            || args.GetProperty("heroId").GetString() == args.GetProperty("otherHeroId").GetString()
            || args.GetProperty("otherHeroId").GetString() == "main_hero"))
            throw new ArgumentException("Judgment draws require distinct heroes, an AI deciding ruler and amount zero through3999.");
        if (profile == "mortality" && (amount > 1 || args.GetProperty("heroId").GetString() != "main_hero"
            || args.GetProperty("otherHeroId").GetString() != ""))
            throw new ArgumentException("Mortality probes require the main hero, empty other hero and amount zero or one.");
        if (profile is "prison_guard" or "ransom_offer" or "barter_release" or "barter_transfer")
        {
            string hero = args.GetProperty("heroId").GetString()!, other = args.GetProperty("otherHeroId").GetString()!;
            if (amount < 1 || (profile == "prison_guard" && amount != 1) || string.IsNullOrWhiteSpace(other) || hero == other)
                throw new ArgumentException("Choose distinct heroes, guard amount one or payment 1–4000.");
        }
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
    private static void ValidateSiegeFixtureArguments(JsonElement args)
    {
        string[] fields = { "attackerPartyId", "settlementId", "expectedOwnerClanId", "expectedAttackerFactionId" };
        if (args.ValueKind != JsonValueKind.Object || args.EnumerateObject().Count() != fields.Length)
            throw new ArgumentException("Supply exactly four siege fixture identifiers.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var arg in args.EnumerateObject())
        {
            if (!fields.Contains(arg.Name) || !seen.Add(arg.Name) || arg.Value.ValueKind != JsonValueKind.String)
                throw new ArgumentException("Invalid siege fixture argument.");
            Required(arg.Value.GetString(), arg.Name);
        }
        if (args.GetProperty("attackerPartyId").GetString() == "player_party")
            throw new ArgumentException("Siege setup never moves the player party.");
    }
    private static void ValidatePlayerEncounterFixtureArguments(JsonElement args)
    {
        string[] fields = { "settlementId", "expectedOwnerClanId", "expectedBesiegerPartyId" };
        if (args.ValueKind != JsonValueKind.Object || args.EnumerateObject().Count() != fields.Length)
            throw new ArgumentException("Supply exactly three player encounter fixture identifiers.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var arg in args.EnumerateObject())
        {
            if (!fields.Contains(arg.Name) || !seen.Add(arg.Name) || arg.Value.ValueKind != JsonValueKind.String)
                throw new ArgumentException("Invalid player encounter fixture argument.");
            Required(arg.Value.GetString(), arg.Name);
        }
        if (args.GetProperty("expectedBesiegerPartyId").GetString() == "player_party")
            throw new ArgumentException("Player encounter setup requires a hostile NPC besieger.");
    }
    private static string Required(string? value, string name)
    {
        string id = InputGuard.OptionalIdentifier(value, name);
        if (id.Length == 0 || id.Length > 80) throw new ArgumentException(name + " requires an exact identifier of at most 80 characters.");
        return id;
    }
}
