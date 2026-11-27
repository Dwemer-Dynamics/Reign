using System.ComponentModel;
using System.Text.Json;
using System.Globalization;
using ModelContextProtocol.Server;

namespace Reign.Mcp.Server;

[McpServerToolType]
public static class StructuredActionTools
{
    private const string Confirmation = "test structured actions on disposable save";
    private const string RelationshipPullFixture = "fixture.relationship_native_pull";
    private const string ArrestMurderEvidenceWitness = "fixture.arrest_murder_evidence_witness";
    private const string ArrestLateEvidenceFixture = "fixture.arrest_late_evidence";
    private const string NotableStartupReservationFixture = "fixture.notable_startup_reservation";

    [McpServerTool(Name = "reign_test_structured_actions", ReadOnly = false,
        Destructive = true, Idempotent = false, UseStructuredContent = true)]
    [Description("Runs 1–8 bounded structured action catalog/entity, preview, operator-run, fixed ship-damage, siege-setup, player-settlement-encounter, ruler, government-player-flow, arranged-wedding, accepted-guide-player-step, Spymaster disruption observation, managed-birth observation, public arrest evidence witness or relationship native pull fixture, or status cases on the exact paused, armed, verified campaign-test Current. Stable IDs prevent replay; bounded native observations include combat, orders and treaties. Siege, player encounter, ruler, government and guide fixtures require a retained recovery checkpoint. Wedding fixtures observe native state or stage one exact eligible pair through the ordinary offer evaluator. Government fixtures visit the actual safe friendly capital or open the real unsent builder in an already-open ordinary member chat. Guide steps only catch the on-foot player up to the exact live mutually accepted ordinary-chat guide, at most twenty metres; no coordinates, NPC movement, destination change or completion. Spymaster disruption observation requires the retained checkpoint and reads one exact action, assignment and settlement. Disruption delivery accepts one arm or observe case on that exact reloaded Current with a matching clean provider guard, and skips at most one newly issued typed response action for the observed active assignment. Managed birth observation requires a retained recovery checkpoint and reads one existing conception for an exact mother. The arrest evidence witness requires the exact retained Current checkpoint and observes previously disclosed murder evidence for one accused and a wrong-charge negative without preparing an arrest. The late-evidence arrest fixture captures one exact run-owned accused and prepares, advances one transition, or observes the saved arrest on the same verified Current. The fixed relationship native pull fixture accepts one exact-revision arm, exact-pair arm, observe or release case on the same verified Current and can hold one genuine response across a same-process load. It requires a retained recovery checkpoint. The Notable startup fixture accepts one town ID and requires the reloaded recovery point and matching provider-free guard. It creates one merchant with an unconfirmed startup reservation. No provider, time advance or save. Fixture preparation and execution are not semantic proof or NPC consent.")]
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
        bool arrestWitness = cases.Count == 1 && IsArrestMurderEvidenceWitness(cases[0]);
        bool arrestLateEvidence = cases.Count == 1 && IsArrestLateEvidenceFixture(cases[0]);
        bool notableFixture = cases.Count == 1 && IsNotableStartupReservationFixture(cases[0]);
        bool spymasterDelivery = cases.Count == 1 && IsSpymasterDisruptionDelivery(cases[0]);
        bool bindCheckpoint = arrestWitness || arrestLateEvidence || notableFixture || spymasterDelivery
            || (cases.Count == 1 && IsRelationshipPullFixture(cases[0]));
        var (auth, checkpoint, loadOwner) = await campaignTests.RequireArmedOwnedPausedActionCheckpointAsync(
            campaignTestRunId, campaignId, bindCheckpoint, cancellationToken,
            postArmCurrentDiagnostic: arrestWitness || arrestLateEvidence || notableFixture)
            .ConfigureAwait(false);
        bool recoveryFixture = cases.Any(RequiresRecoveryCheckpoint);
        if (recoveryFixture)
            campaignTests.RequireRetainedRecoveryCheckpoint(auth);
        if (spymasterDelivery)
        {
            var provider = await api.GetAsync("/api/provider/status", cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!provider.Ok || provider.Data is not JsonElement data || !NpcLivesTestTools.MatchesCleanNativeProviderGuard(
                data, campaignTestRunId, campaignId, auth.TimelineId, auth.CurrentSaveName, auth.BaselineSaveName))
                throw new InvalidOperationException("Disruption delivery requires this campaign test's clean provider guard.");
        }
        string liveRunId = "structured-" + batchId + "-" + Guid.NewGuid().ToString("N")[..8];
        var enrollment = new Dictionary<string, object>
        {
            ["campaignId"] = campaignId, ["campaignTestRunId"] = campaignTestRunId,
            ["disposableSaveName"] = auth.CurrentSaveName, ["protectedBaselineSaveName"] = auth.BaselineSaveName,
            ["timelineId"] = auth.TimelineId, ["source"] = "reign_mcp_structured_actions",
            ["recoveryCheckpointVerified"] = recoveryFixture
        };
        if (bindCheckpoint)
        {
            enrollment["checkpointSavePointId"] = checkpoint.SavePointId;
            enrollment["checkpointSha256"] = checkpoint.Sha256.ToUpperInvariant();
        }
        if (loadOwner is { } nativeOwner)
            enrollment["checkpointLoadOwner"] = nativeOwner;
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
            if (IsRelationshipPullFixture(item) && (phase != "fixture" || Text("kind") != "fixture"))
                throw new ArgumentException("The relationship pull control requires the fixed fixture phase and kind.");
            if (IsSpymasterDisruptionDelivery(item) && (phase != "fixture" || Text("kind") != "fixture"))
                throw new ArgumentException("Disruption delivery requires the fixed fixture phase and kind.");
            if (IsArrestMurderEvidenceWitness(item) && (phase != "fixture" || Text("kind") != "fixture"))
                throw new ArgumentException("The arrest evidence witness requires the fixed fixture phase and kind.");
            if (IsArrestLateEvidenceFixture(item) && (phase != "fixture" || Text("kind") != "fixture"))
                throw new ArgumentException("The late arrest evidence requires the fixed fixture phase and kind.");
            if (IsNotableStartupReservationFixture(item) && (phase != "fixture" || Text("kind") != "fixture"))
                throw new ArgumentException("The Notable reservation requires the fixed fixture phase and kind.");
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
                    else if (Text("actionId") == "fixture.arranged_wedding") ValidateWeddingFixtureArguments(fixtureArgs);
                    else if (Text("actionId") == "fixture.spymaster_disruption_observe") ValidateSpymasterDisruptionObservationArguments(fixtureArgs);
                    else if (IsSpymasterDisruptionDelivery(item))
                        ValidateSpymasterDisruptionDelivery(fixtureArgs, document.RootElement.GetArrayLength(), item.EnumerateObject().Count());
                    else if (Text("actionId") == "fixture.managed_birth_observe") ValidateManagedBirthObservationArguments(fixtureArgs);
                    else if (Text("actionId") == "fixture.follow_accepted_guide_step") ValidateAcceptedGuideStepArguments(fixtureArgs);
                    else if (IsArrestMurderEvidenceWitness(item))
                        ValidateArrestMurderEvidenceWitness(item, fixtureArgs, document.RootElement.GetArrayLength());
                    else if (IsArrestLateEvidenceFixture(item))
                        ValidateArrestLateEvidenceFixture(item, fixtureArgs, document.RootElement.GetArrayLength());
                    else if (IsRelationshipPullFixture(item)) ValidateRelationshipPullFixture(item, fixtureArgs, operation, document.RootElement.GetArrayLength());
                    else if (IsNotableStartupReservationFixture(item))
                        ValidateNotableStartupReservationFixture(fixtureArgs, document.RootElement.GetArrayLength());
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
            or "fixture.government_capital_visit" or "fixture.government_action_composer" or "fixture.follow_accepted_guide_step"
            or "fixture.arranged_wedding" or "fixture.spymaster_disruption_observe" or "fixture.spymaster_disruption_delivery"
            or "fixture.managed_birth_observe"
            or RelationshipPullFixture or ArrestMurderEvidenceWitness or ArrestLateEvidenceFixture or NotableStartupReservationFixture;

    private static bool IsNotableStartupReservationFixture(JsonElement testCase) =>
        testCase.TryGetProperty("actionId", out var action) && action.ValueKind == JsonValueKind.String
        && action.GetString() == NotableStartupReservationFixture;

    private static void ValidateNotableStartupReservationFixture(JsonElement args, int caseCount)
    {
        if (caseCount != 1 || args.ValueKind != JsonValueKind.Object || args.EnumerateObject().Count() != 1
            || !args.TryGetProperty("settlementId", out var town) || town.ValueKind != JsonValueKind.String)
            throw new ArgumentException("Supply one Notable fixture case and its exact town ID.");
        string id = town.GetString() ?? "";
        if (id.Length is < 1 or > 128 || id.Any(ch => !char.IsAsciiLetterOrDigit(ch) && ch is not ('_' or '-' or '.')))
            throw new ArgumentException("The fixture town must be an exact bounded identifier.");
    }

    private static bool IsSpymasterDisruptionDelivery(JsonElement item) =>
        item.TryGetProperty("actionId", out var actionId) && actionId.ValueKind == JsonValueKind.String
        && actionId.GetString() == "fixture.spymaster_disruption_delivery";

    private static void ValidateSpymasterDisruptionDelivery(JsonElement args, int caseCount, int fieldCount)
    {
        string[] fields = ["control", "agentHeroId", "assignmentId", "settlementId"];
        if (caseCount != 1 || fieldCount != 6 || args.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Supply one bounded disruption delivery fixture case.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in args.EnumerateObject())
        {
            string value = field.Value.ValueKind == JsonValueKind.String ? field.Value.GetString() ?? "" : "";
            if (!fields.Contains(field.Name) || !seen.Add(field.Name) || value.Length is < 1 or > 128
                || value.Any(ch => !char.IsAsciiLetterOrDigit(ch) && ch is not ('_' or '-' or '.')))
                throw new ArgumentException("Disruption delivery requires arm or observe and three bounded exact selectors.");
        }
        if (seen.Count != fields.Length || args.GetProperty("control").GetString() is not ("arm" or "observe"))
            throw new ArgumentException("Disruption delivery requires arm or observe and three bounded exact selectors.");
    }

    private static void ValidateSpymasterDisruptionObservationArguments(JsonElement args)
    {
        string[] fields = ["agentHeroId", "assignmentId", "actionId", "settlementId", "effectType"];
        if (args.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Disruption observation requires its exact selectors.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in args.EnumerateObject())
        {
            string id = field.Value.ValueKind == JsonValueKind.String ? field.Value.GetString() ?? "" : "";
            if (!fields.Contains(field.Name) || !seen.Add(field.Name) || id.Length == 0
                || id.Length > (field.Name == "actionId" ? 256 : 128)
                || id.Any(ch => !char.IsAsciiLetterOrDigit(ch) && ch is not ('_' or '-' or '.')))
                throw new ArgumentException("Disruption observation requires five exact bounded selectors.");
        }
        if (seen.Count != fields.Length || args.GetProperty("effectType").GetString() is not ("food" or "construction" or "security" or "loyalty"))
            throw new ArgumentException("Use the exact agent, assignment, action, settlement, and production effect type.");
    }

    private static void ValidateManagedBirthObservationArguments(JsonElement args)
    {
        if (args.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Managed birth observation requires exact identifiers.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in args.EnumerateObject())
        {
            string id = field.Value.ValueKind == JsonValueKind.String ? field.Value.GetString() ?? "" : "";
            if (field.Name is not ("motherId" or "conceptionId") || !seen.Add(field.Name)
                || id.Length is < 1 or > 128
                || id.Any(ch => !char.IsAsciiLetterOrDigit(ch) && ch is not ('_' or '-')))
                throw new ArgumentException("Use an exact motherId and optional exact conceptionId.");
        }
        if (!seen.Contains("motherId"))
            throw new ArgumentException("Managed birth observation requires an exact motherId.");
    }

    private static bool IsArrestMurderEvidenceWitness(JsonElement testCase) =>
        testCase.TryGetProperty("actionId", out var action) && action.ValueKind == JsonValueKind.String
        && action.GetString() == ArrestMurderEvidenceWitness;

    private static bool IsArrestLateEvidenceFixture(JsonElement testCase) =>
        testCase.TryGetProperty("actionId", out var action) && action.ValueKind == JsonValueKind.String
        && action.GetString() == ArrestLateEvidenceFixture;

    private static void ValidateArrestLateEvidenceFixture(JsonElement testCase, JsonElement args, int batchSize)
    {
        if (batchSize != 1 || testCase.EnumerateObject().Count() != 6
            || args.ValueKind != JsonValueKind.Object || args.EnumerateObject().Count() != 2
            || !args.TryGetProperty("stage", out var stage) || stage.ValueKind != JsonValueKind.String
            || stage.GetString() is not ("prepare" or "advance" or "observe")
            || !args.TryGetProperty("accusedHeroId", out var accused) || accused.ValueKind != JsonValueKind.String
            || accused.GetString() is not { Length: >= 1 and <= 128 } id
            || !id.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '_' or '-'))
            throw new ArgumentException("Supply one late-evidence fixture with an exact accused ID and a supported stage.");
    }

    private static void ValidateArrestMurderEvidenceWitness(JsonElement testCase, JsonElement args, int batchSize)
    {
        if (batchSize != 1 || testCase.EnumerateObject().Count() != 6
            || args.ValueKind != JsonValueKind.Object || args.EnumerateObject().Count() != 2
            || !ReadId("accusedHeroId") || !ReadId("matterId"))
            throw new ArgumentException("Supply one arrest witness with exact accusedHeroId and public murder matterId.");

        bool ReadId(string name) => args.TryGetProperty(name, out var token) && token.ValueKind == JsonValueKind.String
            && token.GetString() is { Length: >= 1 and <= 128 } id
            && id.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '_' or '-');
    }

    private static bool IsRelationshipPullFixture(JsonElement testCase) =>
        testCase.TryGetProperty("actionId", out var action) && action.ValueKind == JsonValueKind.String
        && action.GetString() == RelationshipPullFixture;

    private static void ValidateRelationshipPullFixture(JsonElement testCase, JsonElement args, string operation, int batchSize)
    {
        if (batchSize != 1 || testCase.EnumerateObject().Count() != 6 || operation.Length > 80
            || !operation.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '_' or '-')
            || args.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("A relationship pull fixture requires one exact case and a bounded ASCII operation ID.");
        var fields = args.EnumerateObject().ToArray();
        if (fields.Select(field => field.Name).Distinct(StringComparer.Ordinal).Count() != fields.Length
            || !args.TryGetProperty("control", out var control) || control.ValueKind != JsonValueKind.String)
            throw new ArgumentException("Supply an exact relationship pull fixture control.");
        if (control.GetString() is "observe" or "release" && fields.Length == 1)
            return;
        if (args.TryGetProperty("pairKey", out var key) && key.ValueKind == JsonValueKind.String
            && key.GetString() is { Length: >= 3 and <= 257 } pair && pair == pair.Trim()
            && !pair.Any(char.IsControl) && pair.Count(ch => ch == '|') == 1
            && pair[0] != '|' && pair[^1] != '|')
        {
            if (control.GetString() == "arm_pair" && fields.Length == 2)
                return;
            if (control.GetString() == "arm" && fields.Length == 3
                && args.TryGetProperty("revision", out var revision) && revision.ValueKind == JsonValueKind.Number
                && revision.TryGetInt32(out int value) && value > 0)
                return;
        }
        throw new ArgumentException("Relationship pull arm requires an exact pair key and positive revision. Arm pair takes only an exact pair. Observe and release take only control.");
    }

    private static void ValidateWeddingFixtureArguments(JsonElement args)
    {
        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty("profile", out var value) || value.ValueKind != JsonValueKind.String)
            throw new ArgumentException("The wedding fixture requires a profile.");
        string profile = value.GetString()!;
        string[] fields = profile switch
        {
            "discover" or "observe" => ["profile"],
            "stage" => ["profile", "playerMatchId", "prospectId", "leaderId"],
            _ => throw new ArgumentException("Choose discover, stage, or observe.")
        };
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in args.EnumerateObject())
            if (!fields.Contains(field.Name) || !seen.Add(field.Name) || field.Value.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(field.Value.GetString()) || field.Value.GetString()!.Length > 128)
                throw new ArgumentException("The wedding fixture accepts only its exact profile and hero identifiers.");
        if (seen.Count != fields.Length)
            throw new ArgumentException("The wedding fixture is missing an exact hero identifier.");
    }

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
