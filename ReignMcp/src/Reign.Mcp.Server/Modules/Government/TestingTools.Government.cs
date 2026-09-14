using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Reign.Mcp.Server;

public static partial class TestingTools
{
    [McpServerTool(Name = "reign_get_government_test_manifest", ReadOnly = true,
        Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Returns the authoritative Government feature-harness manifest without starting Bannerlord commands.")]
    public static IReadOnlyDictionary<string, object?> GetGovernmentTestManifest(ReignMcpOptions options)
    {
        string path = Path.Combine(options.WorkspaceRoot, "ReignBetaServer", "ReignLiveTest",
            "scenarios", "government-system-manifest.json");
        if (!File.Exists(path))
            throw new FileNotFoundException("The authoritative Government feature-harness manifest is missing.", path);
        Dictionary<string, object?> manifest = JsonSerializer.Deserialize<Dictionary<string, object?>>(
            File.ReadAllText(path), new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("The Government feature-harness manifest is empty.");
        manifest["manifestPath"] = path;
        return manifest;
    }

    [McpServerTool(Name = "reign_start_government_test", ReadOnly = false,
        Destructive = true, Idempotent = false, UseStructuredContent = true)]
    [Description("Starts one Government feature-harness profile through the armed visible live-test bridge. Every profile is restricted to the exact enrolled disposable campaign-test Current save; native time advancement, save creation, and baseline deletion remain owned by the campaign-test controller.")]
    public static async Task<ApiEnvelope> StartGovernmentTest(
        ReignApiClient api,
        ReignMcpOptions options,
        CampaignTestService campaignTests,
        string campaignId,
        string campaignTestRunId,
        [AllowedValues("preflight", "contracts", "snapshot", "hearing_compose", "authority", "resolutions", "voting", "pressure", "save_prepare", "save_verify", "case_prepare", "case_observe", "case_execute", "soak_prepare", "soak_verify", "cleanup")]
        string profile = "preflight",
        string fixtureRunId = "government_system_acceptance",
        [Description("Required for save_verify: the 64-character feature fingerprint returned by save_prepare before the external checkpoint/restart boundary.")]
        string expectedFeatureFingerprint = "",
        [Description("Required for save_verify: the native game-instance ID returned by save_prepare.")]
        string previousGameInstanceId = "",
        [Description("Exact text required: start Reign government test on disposable save")]
        string confirmation = "",
        [Description("Required for hearing_compose: exact already-open hearing business ID. No case is created or selected.")]
        string businessId = "",
        [Description("Required for hearing_compose: 1-1200 characters of natural player language. Sets only unsent text; native Speak submits it.")]
        string playerMessage = "",
        CancellationToken cancellationToken = default)
    {
        if (!options.AllowVerificationControl)
            throw new InvalidOperationException("Verification control is disabled. Set REIGN_MCP_ALLOW_VERIFICATION_CONTROL=true in the trusted MCP configuration.");
        InputGuard.RequireConfirmation(confirmation, "start Reign government test on disposable save");
        campaignId = RequiredIdentifier(campaignId, nameof(campaignId));
        campaignTestRunId = RequiredIdentifier(campaignTestRunId, nameof(campaignTestRunId));
        fixtureRunId = RequiredIdentifier(fixtureRunId, nameof(fixtureRunId));
        profile = (profile ?? string.Empty).Trim().ToLowerInvariant();
        if (profile is not ("preflight" or "contracts" or "snapshot" or "hearing_compose" or "authority"
            or "resolutions" or "voting" or "pressure" or "save_prepare" or "save_verify"
            or "case_prepare" or "case_observe" or "case_execute"
            or "soak_prepare" or "soak_verify" or "cleanup"))
            throw new ArgumentException("Unsupported Government feature-harness profile.", nameof(profile));
        expectedFeatureFingerprint = InputGuard.BoundedText(expectedFeatureFingerprint ?? string.Empty,
            nameof(expectedFeatureFingerprint), 64);
        previousGameInstanceId = InputGuard.BoundedText(previousGameInstanceId ?? string.Empty,
            nameof(previousGameInstanceId), 120);
        if (profile == "save_verify"
            && (expectedFeatureFingerprint.Length != 64 || previousGameInstanceId.Length == 0))
            throw new ArgumentException("save_verify requires the exact save_prepare feature fingerprint and previous game-instance ID.");
        if (profile == "hearing_compose")
        {
            businessId = RequiredIdentifier(businessId, nameof(businessId));
            playerMessage = InputGuard.BoundedText(playerMessage ?? string.Empty, nameof(playerMessage), 1200);
            if (string.IsNullOrWhiteSpace(playerMessage))
                throw new ArgumentException("hearing_compose requires natural player text.", nameof(playerMessage));
        }
        else if (!string.IsNullOrEmpty(businessId) || !string.IsNullOrEmpty(playerMessage))
            throw new ArgumentException("Hearing text is only accepted by hearing_compose.");

        CampaignTestAuthorization authorization = await campaignTests.RequireArmedOwnedSaveAsync(
            campaignTestRunId, campaignId, cancellationToken).ConfigureAwait(false);
        object[] steps =
        {
            new
            {
                operation = "government_test", profile, fixtureRunId,
                expectedSaveName = authorization.CurrentSaveName,
                campaignTestRunId, expectedFeatureFingerprint, previousGameInstanceId, businessId, playerMessage,
                timeoutSeconds = 180
            }
        };
        return await api.PostAsync("/tests/live/run/start", new
        {
            schemaVersion = 2, campaignId, mode = "government",
            label = "MCP Government feature harness - " + profile,
            presentation = "visible", effects = "full", autoCompleteWhenIdle = true, steps
        }, cancellationToken).ConfigureAwait(false);
    }
}
