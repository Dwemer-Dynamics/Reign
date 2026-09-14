using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;

namespace Reign.Mcp.Server;

public static partial class TestingTools
{
    [McpServerTool(Name = "reign_get_spymaster_test_manifest", ReadOnly = true, Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Returns the durable Spymaster test profiles, isolation guarantees, coverage domains, and lifecycle requirements without starting a run.")]
    public static IReadOnlyDictionary<string, object> GetSpymasterTestManifest()
    {
        return new Dictionary<string, object>
        {
            ["schemaVersion"] = 2,
            ["deterministicProfiles"] = new[] { "appointment", "smoke", "feature", "save_prepare", "save_verify", "cleanup" },
            ["organicProfiles"] = new[] { "preflight", "prepare", "intelligence", "people_scopes", "sabotage", "social_fabrication", "social_mitigation", "self_mitigation", "agent_watch", "counterintelligence", "save_prepare", "save_verify", "evaluate", "cleanup_marker" },
            ["controlledProfiles"] = new[] { "controlled_social_success", "controlled_rumor_mitigation", "controlled_self_mitigation", "controlled_agent_action", "controlled_counterintelligence", "controlled_assassination_success", "controlled_capture_breakout_success" },
            ["irreversibleOrganicProfiles"] = new[] { "controlled_assassination_success", "controlled_capture_breakout_success" },
            ["mode"] = "spymaster",
            ["requires"] = new[] { "visible Reign server", "armed live-test bridge", "fresh loaded ruler campaign", "aligned Save Sync" },
            ["isolation"] = new[] { "guarded native effects", "irreversible actions recorded as intents", "feature state and gold restored in finally", "reload fixtures namespaced and explicitly cleaned" },
            ["organicGuarantees"] = new[] { "no forced mission rolls", "no direct agent insertion", "production five-day recruitment ticks", "production campaign time", "real Spymaster UI submission", "native save/restart/load boundary" },
            ["controlledGuarantees"] = new[] { "exact captured production mission ids only", "non-serialized one-shot outcome rolls", "ordinary quote formulas preserved", "production UI submission and mission resolver", "no normal-campaign or unrelated-mission effect" },
            ["authoritativeAgentThresholdFixture"] = "reign_set_spymaster_agent_activation_fixture",
            ["safety"] = new[] { "run only on a named disposable campaign save", "keep the pre-test recovery checkpoint", "destructive attempt requires its own exact confirmation", "restore the recovery checkpoint and delete only exact test saves after evidence capture" },
            ["coverage"] = new[] { "appointment", "roguery", "prices", "capacity", "timing", "payment", "invalidation", "land reports", "subterfuge", "people", "assassination", "breakout", "rumor and reputation", "natural enemy-agent recruitment", "natural enemy-agent operations", "counterintelligence exposure", "memory and dialogue payload", "UI", "serialization", "save/load", "isolation" }
        };
    }

    [McpServerTool(Name = "reign_start_spymaster_test", ReadOnly = false, Destructive = false, Idempotent = false, UseStructuredContent = true)]
    [Description("Starts a deterministic Spymaster run through the already armed exactly-once live-game bridge. The smoke/feature profiles restore campaign state and gold; save profiles intentionally create or verify a namespaced reload fixture. Disabled unless verification control is enabled.")]
    public static Task<ApiEnvelope> StartSpymasterTest(
        ReignApiClient api,
        ReignMcpOptions options,
        string campaignId,
        [AllowedValues("appointment", "smoke", "feature", "save_prepare", "save_verify", "cleanup")] string profile = "feature",
        int seed = 147147,
        string fixtureRunId = "spymaster_native_roundtrip",
        [Description("Exact text required: start Reign Spymaster test")] string confirmation = "",
        CancellationToken cancellationToken = default)
    {
        if (!options.AllowVerificationControl)
            throw new InvalidOperationException("Verification control is disabled. Set REIGN_MCP_ALLOW_VERIFICATION_CONTROL=true in the trusted MCP configuration.");
        InputGuard.RequireConfirmation(confirmation, "start Reign Spymaster test");
        campaignId = InputGuard.OptionalIdentifier(campaignId, nameof(campaignId));
        if (campaignId.Length == 0) throw new ArgumentException("campaignId is required.", nameof(campaignId));
        profile = profile.Trim().ToLowerInvariant();
        if (profile is not ("appointment" or "smoke" or "feature" or "save_prepare" or "save_verify" or "cleanup"))
            throw new ArgumentException("Unsupported Spymaster profile.", nameof(profile));
        seed = InputGuard.Range(seed, nameof(seed), 0, int.MaxValue);
        fixtureRunId = InputGuard.OptionalIdentifier(fixtureRunId, nameof(fixtureRunId));
        string runtimeProfile = profile switch
        {
            "save_prepare" => "prepare_reload",
            "save_verify" => "verify_reload",
            _ => profile
        };
        var steps = new List<object>();
        if (profile is "appointment" or "smoke" or "feature")
        {
            steps.Add(new { operation = "ui_open", targetSearch = "spymaster", timeoutSeconds = 60 });
            if (profile == "appointment")
                steps.Add(new { operation = "ui_action", targetSearch = "spymaster", text = "appoint", timeoutSeconds = 120 });
            steps.Add(new { operation = "ui_snapshot", targetSearch = "spymaster", timeoutSeconds = 60 });
        }
        steps.Add(new { operation = "spymaster_test", profile = profile == "appointment" ? "smoke" : runtimeProfile, fixtureRunId, seed, timeoutSeconds = 300 });
        if (profile is "appointment" or "smoke" or "feature") steps.Add(new { operation = "ui_close", timeoutSeconds = 30 });
        return api.PostAsync("/tests/live/run/start", new
        {
            schemaVersion = 2, campaignId, mode = "spymaster", seed,
            label = "MCP Spymaster " + profile, presentation = "visible", effects = "guarded",
            autoCompleteWhenIdle = true, steps
        }, cancellationToken);
    }

    [McpServerTool(Name = "reign_start_spymaster_organic_test", ReadOnly = false, Destructive = true, Idempotent = false, UseStructuredContent = true)]
    [Description("Starts one prepared Spymaster acceptance scenario through the armed live-game bridge. Organic profiles use production random rolls. Controlled profiles bind one-shot rolls to exact captured mission ids for rare-branch proof. Run only on a named disposable save. Disabled unless verification control is enabled.")]
    public static Task<ApiEnvelope> StartSpymasterOrganicTest(
        ReignApiClient api,
        ReignMcpOptions options,
        string campaignId,
        [AllowedValues("preflight", "prepare", "intelligence", "people_scopes", "sabotage", "social_fabrication", "controlled_social_success", "controlled_rumor_mitigation", "controlled_self_mitigation", "controlled_agent_action", "controlled_counterintelligence", "social_mitigation", "self_mitigation", "agent_watch", "counterintelligence", "save_prepare", "save_verify", "evaluate", "cleanup_marker")]
        string profile = "preflight",
        string fixtureRunId = "spymaster_organic_launch",
        [Description("Exact text required: start Reign organic Spymaster test on disposable save")] string confirmation = "",
        CancellationToken cancellationToken = default)
    {
        if (!options.AllowVerificationControl)
            throw new InvalidOperationException("Verification control is disabled. Set REIGN_MCP_ALLOW_VERIFICATION_CONTROL=true in the trusted MCP configuration.");
        InputGuard.RequireConfirmation(confirmation, "start Reign organic Spymaster test on disposable save");
        campaignId = RequiredIdentifier(campaignId, nameof(campaignId));
        fixtureRunId = RequiredIdentifier(fixtureRunId, nameof(fixtureRunId));
        profile = profile.Trim().ToLowerInvariant();
        var scenarios = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["preflight"] = "spymaster-organic-preflight.json",
            ["prepare"] = "spymaster-organic-prepare.json",
            ["intelligence"] = "spymaster-organic-intelligence.json",
            ["people_scopes"] = "spymaster-organic-people-scopes.json",
            ["sabotage"] = "spymaster-organic-sabotage.json",
            ["social_fabrication"] = "spymaster-organic-social-fabrication.json",
            ["controlled_social_success"] = "spymaster-controlled-social-success.json",
            ["controlled_rumor_mitigation"] = "spymaster-controlled-rumor-mitigation.json",
            ["controlled_self_mitigation"] = "spymaster-controlled-self-mitigation.json",
            ["controlled_agent_action"] = "spymaster-controlled-agent-action.json",
            ["controlled_counterintelligence"] = "spymaster-controlled-counterintelligence.json",
            ["social_mitigation"] = "spymaster-organic-social-mitigation.json",
            ["self_mitigation"] = "spymaster-organic-self-mitigation.json",
            ["agent_watch"] = "spymaster-organic-agent-watch.json",
            ["counterintelligence"] = "spymaster-organic-counterintelligence.json",
            ["save_prepare"] = "spymaster-organic-save-prepare.json",
            ["save_verify"] = "spymaster-organic-save-verify.json",
            ["evaluate"] = "spymaster-organic-evaluate.json",
            ["cleanup_marker"] = "spymaster-organic-cleanup-marker.json"
        };
        if (!scenarios.TryGetValue(profile, out var scenarioFile))
            throw new ArgumentException("Unsupported organic Spymaster profile.", nameof(profile));
        return StartOrganicScenario(api, options, scenarioFile, campaignId, fixtureRunId, cancellationToken);
    }

    [McpServerTool(Name = "reign_start_spymaster_organic_destructive_test", ReadOnly = false, Destructive = true, Idempotent = false, UseStructuredContent = true)]
    [Description("Starts one restore-isolated controlled destructive branch. Exact mission-bound one-shots prove successful assassination or capture plus successful breakout through production native effects. Run only on a verified disposable save.")]
    public static Task<ApiEnvelope> StartSpymasterOrganicDestructiveTest(
        ReignApiClient api,
        ReignMcpOptions options,
        string campaignId,
        [AllowedValues("controlled_assassination_success", "controlled_capture_breakout_success")]
        string profile = "controlled_assassination_success",
        string fixtureRunId = "spymaster_organic_launch",
        [Description("Exact text required: run irreversible Reign Spymaster test on disposable save")] string confirmation = "",
        CancellationToken cancellationToken = default)
    {
        if (!options.AllowVerificationControl)
            throw new InvalidOperationException("Verification control is disabled. Set REIGN_MCP_ALLOW_VERIFICATION_CONTROL=true in the trusted MCP configuration.");
        InputGuard.RequireConfirmation(confirmation, "run irreversible Reign Spymaster test on disposable save");
        campaignId = RequiredIdentifier(campaignId, nameof(campaignId));
        fixtureRunId = RequiredIdentifier(fixtureRunId, nameof(fixtureRunId));
        profile = profile.Trim().ToLowerInvariant();
        string scenarioFile = profile switch
        {
            "controlled_assassination_success" => "spymaster-controlled-assassination-success.json",
            "controlled_capture_breakout_success" => "spymaster-controlled-capture-breakout-success.json",
            _ => throw new ArgumentException("Unsupported destructive Spymaster profile.", nameof(profile))
        };
        return StartOrganicScenario(api, options, scenarioFile, campaignId, fixtureRunId, cancellationToken);
    }

    [McpServerTool(Name = "reign_set_spymaster_agent_activation_fixture", ReadOnly = false, Destructive = true, Idempotent = false, UseStructuredContent = true)]
    [Description("Sets both authoritative directional Reign affinities and Bannerlord's native relation for a player/foreign-ruler pair in an explicitly enrolled disposable save. This prevents background relationship projection from undoing the foreign-agent activation threshold during an organic acceptance window.")]
    public static async Task<ApiEnvelope> SetSpymasterAgentActivationFixture(
        ReignApiClient api,
        ReignMcpOptions options,
        CampaignTestService campaignTests,
        string campaignId,
        string campaignTestRunId,
        string mainHeroId,
        string foreignRulerHeroId,
        int relationValue = -40,
        string fixtureRunId = "spymaster_agent_activation_fixture",
        [Description("Exact text required: set Reign Spymaster agent threshold on disposable save")] string confirmation = "",
        CancellationToken cancellationToken = default)
    {
        if (!options.AllowVerificationControl)
            throw new InvalidOperationException("Verification control is disabled. Set REIGN_MCP_ALLOW_VERIFICATION_CONTROL=true in the trusted MCP configuration.");
        InputGuard.RequireConfirmation(confirmation,
            "set Reign Spymaster agent threshold on disposable save");
        campaignId = RequiredIdentifier(campaignId, nameof(campaignId));
        campaignTestRunId = RequiredIdentifier(campaignTestRunId, nameof(campaignTestRunId));
        mainHeroId = RequiredIdentifier(mainHeroId, nameof(mainHeroId));
        foreignRulerHeroId = RequiredIdentifier(foreignRulerHeroId,
            nameof(foreignRulerHeroId));
        fixtureRunId = RequiredIdentifier(fixtureRunId, nameof(fixtureRunId));
        relationValue = InputGuard.Range(relationValue, nameof(relationValue), -100, -29);
        CampaignTestAuthorization authorization = await campaignTests.RequireArmedOwnedSaveAsync(
            campaignTestRunId, campaignId, cancellationToken).ConfigureAwait(false);

        string enrollmentRunId = fixtureRunId + "_enrollment";
        ApiEnvelope prepared = await api.PostAsync("/tests/social-balance/prepare", new
        {
            confirmation = "prepare", campaignId, timelineId = authorization.TimelineId,
            runId = enrollmentRunId, mainHeroId, campaignTestRunId,
            savePrefix = authorization.SavePrefix,
            disposableSaveName = authorization.CurrentSaveName,
            protectedBaselineSaveName = authorization.BaselineSaveName
        }, cancellationToken).ConfigureAwait(false);
        if (!prepared.Ok || prepared.Data is null
            || !prepared.Data.Value.TryGetProperty("enrollment", out var enrollment))
            return prepared;

        object EffectiveAffinity(string observerId, string subjectId) => new
        {
            operation = "social_set_underlying_affinity", observerId, subjectId,
            value = relationValue, valueMode = "underlying", timeoutSeconds = 120
        };
        var steps = new object[]
        {
            new
            {
                operation = "social_set_relation", observerId = foreignRulerHeroId,
                subjectId = mainHeroId, value = relationValue, timeoutSeconds = 120
            },
            EffectiveAffinity(foreignRulerHeroId, mainHeroId),
            EffectiveAffinity(mainHeroId, foreignRulerHeroId),
            new
            {
                operation = "social_snapshot", heroIds = new[] { mainHeroId, foreignRulerHeroId },
                periodKey = "spymaster_agent_threshold", timeoutSeconds = 120
            }
        };
        return await api.PostAsync("/tests/live/run/start", new
        {
            schemaVersion = 2, campaignId, mode = "social_balance",
            label = "MCP Spymaster authoritative agent activation fixture",
            presentation = "visible", effects = "full", autoCompleteWhenIdle = true,
            enrollment, steps
        }, cancellationToken).ConfigureAwait(false);
    }

    private static Task<ApiEnvelope> StartOrganicScenario(
        ReignApiClient api,
        ReignMcpOptions options,
        string scenarioFile,
        string campaignId,
        string fixtureRunId,
        CancellationToken cancellationToken)
    {
        string scenarioPath = Path.GetFullPath(Path.Combine(options.WorkspaceRoot,
            "ReignBetaServer", "ReignLiveTest", "scenarios", scenarioFile));
        ReignMcpOptions.EnsureWithin(options.WorkspaceRoot, scenarioPath, nameof(scenarioFile));
        JsonObject scenario = JsonNode.Parse(File.ReadAllText(scenarioPath))?.AsObject()
            ?? throw new InvalidDataException("The organic Spymaster scenario is not a JSON object.");
        scenario["campaignId"] = campaignId;
        scenario["autoCompleteWhenIdle"] = true;
        if (scenario["steps"] is JsonArray steps)
        {
            foreach (JsonNode? node in steps)
            {
                if (node is JsonObject step && step["fixtureRunId"] is not null)
                    step["fixtureRunId"] = fixtureRunId;
            }
        }
        return api.PostAsync("/tests/live/run/start", scenario, cancellationToken);
    }

    private static string RequiredIdentifier(string value, string parameterName)
    {
        string normalized = InputGuard.OptionalIdentifier(value, parameterName);
        if (normalized.Length == 0) throw new ArgumentException(parameterName + " is required.", parameterName);
        return normalized;
    }
}
