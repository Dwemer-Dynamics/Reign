using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using ModelContextProtocol.Server;

namespace Reign.Mcp.Server;

public static partial class TestingTools
{
    private static readonly string[] SocialReputationProfiles =
    {
        "preflight", "signal_contract", "player_flirt", "player_affair", "unchaste",
        "npc_favoring_presence", "player_favoring_dialogue", "favoring_projection",
        "favoring_jealousy_charm", "favoring_rebellion", "player_parity", "save_prepare",
        "save_verify", "longitudinal_90_day", "cleanup_marker"
    };

    [McpServerTool(Name = "reign_get_social_reputation_test_manifest", ReadOnly = true, Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Returns the authoritative Social Reputation harness profiles, cases, dependency fingerprints, prerequisites, evidence contracts, and isolation rules. It does not prepare or start a run.")]
    public static Task<ApiEnvelope> GetSocialReputationTestManifest(
        ReignApiClient api, CancellationToken cancellationToken = default) =>
        api.GetAsync("/tests/social-balance/manifest", cancellationToken: cancellationToken);

    [McpServerTool(Name = "reign_prepare_social_reputation_test", ReadOnly = false, Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Enrolls one exact campaign-test Current save, campaign/timeline/main-hero, and Social Reputation run. Preparation does not arm the bridge, start Bannerlord, or mutate the campaign.")]
    public static Task<ApiEnvelope> PrepareSocialReputationTest(
        ReignApiClient api, CampaignTestService campaignTests, string campaignId,
        string campaignTestRunId, string mainHeroId, string runId,
        CancellationToken cancellationToken = default)
    {
        campaignId = RequiredSocialIdentifier(campaignId, nameof(campaignId));
        campaignTestRunId = RequiredSocialIdentifier(campaignTestRunId, nameof(campaignTestRunId));
        mainHeroId = RequiredSocialIdentifier(mainHeroId, nameof(mainHeroId));
        runId = RequiredSocialIdentifier(runId, nameof(runId));
        CampaignTestAuthorization authorization = campaignTests.RequireEnrollment(campaignTestRunId, campaignId);
        return api.PostAsync("/tests/social-balance/prepare", new
        {
            confirmation = "prepare", campaignId, timelineId = authorization.TimelineId,
            mainHeroId, runId, campaignTestRunId, savePrefix = authorization.SavePrefix,
            disposableSaveName = authorization.CurrentSaveName,
            protectedBaselineSaveName = authorization.BaselineSaveName
        }, cancellationToken);
    }

    [McpServerTool(Name = "reign_start_social_reputation_test", ReadOnly = false, Destructive = true, Idempotent = false, UseStructuredContent = true)]
    [Description("Starts one prepared Social Reputation profile through the armed visible live-test bridge. Native mutations are restricted to the exact enrolled disposable save namespace. favoring_projection produces two native parameterized favorites for server-verified collapsed and observer-specific projection proof; favoring_jealousy_charm reuses the compatible fixture for exact saved observer math; player_parity invokes the native player Unchaste producer only after compatible player Flirt, Affair, and NPC gender-matrix passes. player_affair may opt into the single-turn prompt override for deterministic capability proof and, after recording a natural exposure miss, an exact-signal one-shot exposure override; neither override is persisted.")]
    public static async Task<ApiEnvelope> StartSocialReputationTest(
        ReignApiClient api, ReignMcpOptions options, CampaignTestService campaignTests,
        string campaignId, string campaignTestRunId, string mainHeroId, string runId,
        [AllowedValues("preflight", "signal_contract", "player_flirt", "player_affair", "unchaste", "npc_favoring_presence", "player_favoring_dialogue", "favoring_projection", "favoring_jealousy_charm", "favoring_rebellion", "player_parity", "save_prepare", "save_verify", "longitudinal_90_day", "cleanup_marker")]
        string profile,
        int seed = 731911,
        [Description("Only for player_affair capability proof. Sends natural player text plus a separate one-request directive that the server accepts only for the exact armed, executing, enrolled disposable-save command; it does not change global prompts and cannot be used by other profiles.")]
        bool promptOverrideAssisted = false,
        [Description("Only for player_affair rare-branch proof after a natural production exposure miss. Forces exposure for that run's exact validated intimacy signal without changing the production chance or stored natural roll.")]
        bool forceRareAffairExposureAfterNaturalMiss = false,
        [Description("Exact text required: start Reign social reputation test on disposable save")]
        string confirmation = "", CancellationToken cancellationToken = default)
    {
        if (!options.AllowVerificationControl)
            throw new InvalidOperationException("Verification control is disabled. Set REIGN_MCP_ALLOW_VERIFICATION_CONTROL=true in the trusted MCP configuration.");
        InputGuard.RequireConfirmation(confirmation, "start Reign social reputation test on disposable save");
        campaignId = RequiredSocialIdentifier(campaignId, nameof(campaignId));
        campaignTestRunId = RequiredSocialIdentifier(campaignTestRunId, nameof(campaignTestRunId));
        mainHeroId = RequiredSocialIdentifier(mainHeroId, nameof(mainHeroId));
        runId = RequiredSocialIdentifier(runId, nameof(runId));
        profile = (profile ?? string.Empty).Trim().ToLowerInvariant();
        if (!SocialReputationProfiles.Contains(profile, StringComparer.Ordinal))
            throw new ArgumentException("Unsupported Social Reputation profile.", nameof(profile));
        if (promptOverrideAssisted && profile != "player_affair")
            throw new ArgumentException("promptOverrideAssisted is supported only for player_affair.", nameof(promptOverrideAssisted));
        if (forceRareAffairExposureAfterNaturalMiss && profile != "player_affair")
            throw new ArgumentException("forceRareAffairExposureAfterNaturalMiss is supported only for player_affair.", nameof(forceRareAffairExposureAfterNaturalMiss));
        seed = InputGuard.Range(seed, nameof(seed), 0, int.MaxValue);
        CampaignTestAuthorization authorization = await campaignTests.RequireArmedOwnedSaveAsync(
            campaignTestRunId, campaignId, cancellationToken).ConfigureAwait(false);
        var steps = new List<object>
        {
            new { operation = "social_snapshot", periodKey = "before_" + profile, timeoutSeconds = 120 },
            new { operation = "social_reputation_profile", profile, runId, seed, promptOverrideAssisted, forceRareAffairExposureAfterNaturalMiss, timeoutSeconds = profile is "longitudinal_90_day" or "player_favoring_dialogue" ? 3600 : 900 },
            new { operation = "social_snapshot", periodKey = "after_" + profile, timeoutSeconds = 120 }
        };
        return await api.PostAsync("/tests/live/run/start", new
        {
            schemaVersion = 2, campaignId, mode = "social_balance", seed,
            label = "MCP Social Reputation " + profile, presentation = "visible", effects = "full",
            autoCompleteWhenIdle = true,
            enrollment = new { campaignId, timelineId = authorization.TimelineId, mainHeroId, runId,
                campaignTestRunId, savePrefix = authorization.SavePrefix,
                disposableSaveName = authorization.CurrentSaveName,
                protectedBaselineSaveName = authorization.BaselineSaveName }, steps
        }, cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "reign_get_social_reputation_test_status", ReadOnly = true, Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Returns pending, running, failed, blocked, cached-pass, and dependency-changed Social Reputation cases for an enrolled run.")]
    public static Task<ApiEnvelope> GetSocialReputationTestStatus(ReignApiClient api,
        string campaignId = "", string timelineId = "main", string runId = "",
        CancellationToken cancellationToken = default) =>
        api.GetAsync("/tests/social-balance/status", new Dictionary<string, string?>
        {
            ["campaignId"] = InputGuard.OptionalIdentifier(campaignId, nameof(campaignId)),
            ["timelineId"] = InputGuard.OptionalIdentifier(timelineId, nameof(timelineId)),
            ["runId"] = InputGuard.OptionalIdentifier(runId, nameof(runId))
        }, cancellationToken);

    [McpServerTool(Name = "reign_get_social_reputation_test_report", ReadOnly = true, Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Returns authoritative Social Reputation assertions, fixtures, rolls, lifecycle records, projections, correlations, rollback evidence, and timings. Use caseId and includeSnapshots=false for focused evidence without full campaign snapshots.")]
    public static Task<ApiEnvelope> GetSocialReputationTestReport(ReignApiClient api,
        string campaignId = "", string timelineId = "main", string runId = "",
        string caseId = "", bool includeSnapshots = true,
        CancellationToken cancellationToken = default) =>
        api.GetAsync("/tests/social-balance/export", new Dictionary<string, string?>
        {
            ["campaignId"] = InputGuard.OptionalIdentifier(campaignId, nameof(campaignId)),
            ["timelineId"] = InputGuard.OptionalIdentifier(timelineId, nameof(timelineId)),
            ["runId"] = InputGuard.OptionalIdentifier(runId, nameof(runId)),
            ["caseId"] = InputGuard.OptionalIdentifier(caseId, nameof(caseId)),
            ["includeSnapshots"] = includeSnapshots ? "true" : "false"
        }, cancellationToken);

    [McpServerTool(Name = "reign_get_social_reputation_release_readiness", ReadOnly = true, Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Produces a read-only Social Reputation release verdict from authoritative case state. Missing, stale, blocked, failed, or dependency-changed requirements remain explicit blockers.")]
    public static Task<ApiEnvelope> GetSocialReputationReleaseReadiness(ReignApiClient api,
        string campaignId = "", string timelineId = "main", string runId = "",
        CancellationToken cancellationToken = default) =>
        api.GetAsync("/tests/social-balance/status", new Dictionary<string, string?>
        {
            ["campaignId"] = InputGuard.OptionalIdentifier(campaignId, nameof(campaignId)),
            ["timelineId"] = InputGuard.OptionalIdentifier(timelineId, nameof(timelineId)),
            ["runId"] = InputGuard.OptionalIdentifier(runId, nameof(runId)),
            ["releaseVerdict"] = "true"
        }, cancellationToken);

    private static string RequiredSocialIdentifier(string value, string parameterName)
    {
        string normalized = InputGuard.OptionalIdentifier(value, parameterName);
        if (normalized.Length == 0) throw new ArgumentException(parameterName + " is required.", parameterName);
        return normalized;
    }
}
