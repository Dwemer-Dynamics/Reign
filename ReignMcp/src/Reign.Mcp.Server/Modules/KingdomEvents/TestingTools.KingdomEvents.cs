using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;

namespace Reign.Mcp.Server;

public static partial class TestingTools
{
    [McpServerTool(Name = "reign_get_kingdom_event_test_manifest", ReadOnly = true, Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Returns the authoritative risk-based Kingdom Events launch-acceptance manifest without starting Bannerlord commands.")]
    public static IReadOnlyDictionary<string, object?> GetKingdomEventTestManifest(
        ReignMcpOptions options)
    {
        string path = Path.Combine(options.WorkspaceRoot, "ReignBetaServer", "ReignLiveTest",
            "scenarios", "kingdom-event-acceptance-manifest.json");
        if (!File.Exists(path))
            throw new FileNotFoundException("The authoritative Kingdom Events acceptance manifest is missing.", path);
        Dictionary<string, object?> manifest = JsonSerializer.Deserialize<Dictionary<string, object?>>(
            File.ReadAllText(path), new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("The Kingdom Events acceptance manifest is empty.");
        manifest["manifestPath"] = path;
        return manifest;
    }

    [McpServerTool(Name = "reign_start_kingdom_event_test", ReadOnly = false, Destructive = true, Idempotent = false, UseStructuredContent = true)]
    [Description("Starts one prepared catastrophe/boon live-game acceptance phase. Forced events use production eligibility and effects but do not consume the normal 1% daily roll. Run only on a verified disposable baseline; war, peace, and succession are immediate world mutations.")]
    public static async Task<ApiEnvelope> StartKingdomEventTest(
        ReignApiClient api,
        ReignMcpOptions options,
        CampaignTestService campaignTests,
        string campaignId,
        string campaignTestRunId,
        [AllowedValues("preflight", "force", "observe", "advance_observe", "expire_observe", "no_target", "organic_prepare", "organic_verify", "save_prepare", "save_verify", "cleanup_marker")]
        string profile = "preflight",
        [AllowedValues("famine", "pestilence", "crime_wave", "trade_collapse", "construction_stagnation", "political_unrest", "border_crisis", "bountiful_harvest", "population_boom", "law_and_order", "trade_boom", "golden_age", "national_unity", "grand_reconciliation", "orderly_succession")]
        string archetypeId = "famine",
        [AllowedValues("first", "middle", "last")] string targetVariant = "first",
        string fixtureRunId = "kingdom_event_acceptance",
        string kingdomId = "",
        [Description("For organic_prepare only, temporarily raises the probability for exactly the next disposable-save daily tick. The client consumes the non-serialized override before production selection and organic_verify proves restoration.")]
        bool accelerateOrganicTrigger = false,
        [Description("Exact text required: start Reign kingdom event test on disposable save")] string confirmation = "",
        CancellationToken cancellationToken = default)
    {
        if (!options.AllowVerificationControl)
            throw new InvalidOperationException("Verification control is disabled. Set REIGN_MCP_ALLOW_VERIFICATION_CONTROL=true in the trusted MCP configuration.");
        InputGuard.RequireConfirmation(confirmation, "start Reign kingdom event test on disposable save");
        campaignId = RequiredIdentifier(campaignId, nameof(campaignId));
        campaignTestRunId = RequiredIdentifier(campaignTestRunId, nameof(campaignTestRunId));
        fixtureRunId = RequiredIdentifier(fixtureRunId, nameof(fixtureRunId));
        profile = profile.Trim().ToLowerInvariant();
        archetypeId = archetypeId.Trim().ToLowerInvariant();
        targetVariant = targetVariant.Trim().ToLowerInvariant();
        if (profile is not ("preflight" or "force" or "observe" or "advance_observe" or "expire_observe"
            or "no_target" or "organic_prepare" or "organic_verify" or "save_prepare" or "save_verify" or "cleanup_marker"))
            throw new ArgumentException("Unsupported kingdom-event profile.", nameof(profile));
        string[] archetypes = { "famine", "pestilence", "crime_wave", "trade_collapse", "construction_stagnation", "political_unrest", "border_crisis", "bountiful_harvest", "population_boom", "law_and_order", "trade_boom", "golden_age", "national_unity", "grand_reconciliation", "orderly_succession" };
        if (!archetypes.Contains(archetypeId, StringComparer.Ordinal))
            throw new ArgumentException("Unsupported kingdom-event archetype.", nameof(archetypeId));
        if (targetVariant is not ("first" or "middle" or "last"))
            throw new ArgumentException("Unsupported target variant.", nameof(targetVariant));
        if (accelerateOrganicTrigger && profile != "organic_prepare")
            throw new ArgumentException("Organic acceleration is accepted only by organic_prepare.", nameof(accelerateOrganicTrigger));
        kingdomId = InputGuard.BoundedText(kingdomId ?? string.Empty, nameof(kingdomId), 120);
        CampaignTestAuthorization authorization = await campaignTests.RequireArmedOwnedSaveAsync(
            campaignTestRunId, campaignId, cancellationToken).ConfigureAwait(false);

        var steps = new List<object>();
        object EventStep(string phase, bool expectExpired = false) => new
        {
            operation = "kingdom_event_test", phase, fixtureRunId, archetypeId, targetVariant,
            kingdomId, accelerateOrganicTrigger,
            expectedSaveName = authorization.CurrentSaveName,
            campaignTestRunId,
            confirmation = phase == "force" || phase == "no_target"
                ? "force Reign kingdom event on disposable save" : string.Empty,
            expectExpired, timeoutSeconds = 180
        };
        switch (profile)
        {
            case "preflight": steps.Add(EventStep("preflight")); break;
            case "force": steps.Add(EventStep("force")); break;
            case "observe": steps.Add(EventStep("observe")); break;
            case "advance_observe": steps.Add(EventStep("observe")); break;
            case "expire_observe": steps.Add(EventStep("observe", true)); break;
            case "no_target": steps.Add(EventStep("no_target")); break;
            case "organic_prepare": steps.Add(EventStep("organic_prepare")); break;
            case "organic_verify": steps.Add(EventStep("organic_verify")); break;
            case "save_prepare": steps.Add(EventStep("mark_reload")); break;
            case "save_verify": steps.Add(EventStep("verify_reload")); break;
            case "cleanup_marker": steps.Add(EventStep("cleanup_marker")); break;
        }
        return await api.PostAsync("/tests/live/run/start", new
        {
            schemaVersion = 2, campaignId, mode = "kingdom_event",
            label = "MCP kingdom event " + profile + " - " + archetypeId + " - " + targetVariant,
            presentation = "visible", effects = "full", autoCompleteWhenIdle = true, steps
        }, cancellationToken).ConfigureAwait(false);
    }
}
