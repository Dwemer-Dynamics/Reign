using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using ModelContextProtocol.Server;

namespace Reign.Mcp.Server;

public static partial class TestingTools
{
    private static readonly string[] CampaignCommandProfiles =
    {
        "preflight", "offline_contracts", "llm_matrix", "native_primitives",
        "composite_orders", "geography", "personality", "correspondence",
        "fault_recovery", "save_roundtrip", "campaign_soak", "evaluate", "cleanup"
    };

    [McpServerTool(Name = "reign_get_campaign_command_test_manifest", ReadOnly = true,
        Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Returns the authoritative Campaign Command capability registry, certification profiles, seeds, pass counts, LLM matrix, soak matrix, fatal-stop rules, fingerprints, evidence contract, and save-isolation namespace.")]
    public static Task<ApiEnvelope> GetCampaignCommandTestManifest(ReignApiClient api,
        CancellationToken cancellationToken = default) =>
        api.GetAsync("/tests/campaign-command/manifest", cancellationToken: cancellationToken);

    [McpServerTool(Name = "reign_prepare_campaign_command_test", ReadOnly = false,
        Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Creates or refreshes Campaign Command certification against an existing guarded campaign-test enrollment. The baseline remains immutable and feature mutation is authorized only on its exact run-owned Current save.")]
    public static Task<ApiEnvelope> PrepareCampaignCommandTest(ReignApiClient api,
        CampaignTestService campaignTests, string campaignId, string campaignTestRunId, string runId,
        string sourceFingerprint = "unknown", string serverBuild = "unknown",
        string clientBuild = "unknown", string bannerlordVersion = "unknown",
        string providerConfigurationFingerprint = "unknown",
        CancellationToken cancellationToken = default)
    {
        campaignId = RequiredCampaignCommandIdentifier(campaignId, nameof(campaignId));
        campaignTestRunId = RequiredCampaignCommandIdentifier(campaignTestRunId, nameof(campaignTestRunId));
        runId = RequiredCampaignCommandIdentifier(runId, nameof(runId));
        CampaignTestAuthorization authorization = campaignTests.RequireEnrollment(campaignTestRunId, campaignId);
        return api.PostAsync("/tests/campaign-command/prepare", new
        {
            campaignId, runId, campaignTestRunId,
            disposableSaveName = authorization.CurrentSaveName,
            disposableSavePrefix = authorization.SavePrefix,
            protectedBaselineSaveName = authorization.BaselineSaveName,
            timelineId = authorization.TimelineId,
            sourceFingerprint = BoundedFingerprint(sourceFingerprint),
            serverBuild = BoundedFingerprint(serverBuild), clientBuild = BoundedFingerprint(clientBuild),
            bannerlordVersion = BoundedFingerprint(bannerlordVersion),
            providerConfigurationFingerprint = BoundedFingerprint(providerConfigurationFingerprint),
            dependencyFingerprints = new Dictionary<string, object>()
        }, cancellationToken);
    }

    [McpServerTool(Name = "reign_verify_campaign_command_disposable_save", ReadOnly = false,
        Destructive = false, Idempotent = false, UseStructuredContent = true)]
    [Description("Starts the preflight only after the guarded campaign-test service proves the exact armed run-owned Current save is loaded and Save Sync is aligned.")]
    public static async Task<IReadOnlyDictionary<string, ApiEnvelope>> VerifyCampaignCommandDisposableSave(ReignApiClient api,
        ReignMcpOptions options, CampaignTestService campaignTests, string campaignId,
        string campaignTestRunId, string runId,
        [Description("Exact text required: verify Reign campaign command disposable save")]
        string confirmation = "", CancellationToken cancellationToken = default)
    {
        RequireCampaignCommandControl(options);
        InputGuard.RequireConfirmation(confirmation, "verify Reign campaign command disposable save");
        campaignId = RequiredCampaignCommandIdentifier(campaignId, nameof(campaignId));
        campaignTestRunId = RequiredCampaignCommandIdentifier(campaignTestRunId, nameof(campaignTestRunId));
        runId = RequiredCampaignCommandIdentifier(runId, nameof(runId));
        CampaignTestAuthorization authorization = await campaignTests.RequireArmedOwnedSaveAsync(
            campaignTestRunId, campaignId, cancellationToken).ConfigureAwait(false);
        string disposableSaveName = authorization.CurrentSaveName;
        string liveRunId = CampaignCommandLiveRunId(runId, "verify", 1);
        ApiEnvelope association = await api.PostAsync("/tests/campaign-command/execution",
            new { runId, liveRunId, profile = "preflight" }, cancellationToken).ConfigureAwait(false);
        ApiEnvelope liveRun = await api.PostAsync("/tests/live/run/start", new
        {
            schemaVersion = 2, campaignId, runId = liveRunId, mode = "campaign_command", seed = 1337,
            label = "MCP Campaign Command disposable-save preflight", presentation = "visible",
            effects = "guarded", autoCompleteWhenIdle = true,
            enrollment = new { campaignId, runId, campaignTestRunId, disposableSaveName,
                disposableSavePrefix = authorization.SavePrefix, timelineId = authorization.TimelineId },
            steps = new object[] { new { operation = "campaign_command_test", profile = "preflight",
                campaignTestRunId, disposableSavePrefix = authorization.SavePrefix,
                certificationRunId = runId, disposableSaveName, seed = 1337, pass = 1, timeoutSeconds = 300 } }
        }, cancellationToken).ConfigureAwait(false);
        return new Dictionary<string, ApiEnvelope>
        { ["association"] = association, ["liveRun"] = liveRun };
    }

    [McpServerTool(Name = "reign_start_campaign_command_profile", ReadOnly = false,
        Destructive = true, Idempotent = false, UseStructuredContent = true)]
    [Description("Starts one prepared Campaign Command certification profile through the visible armed bridge. Each case uses the enrolled disposable save and emits an isolated checkpoint and replay bundle.")]
    public static async Task<IReadOnlyDictionary<string, ApiEnvelope>> StartCampaignCommandProfile(ReignApiClient api,
        ReignMcpOptions options, CampaignTestService campaignTests, string campaignId,
        string campaignTestRunId, string runId,
        [AllowedValues("preflight", "offline_contracts", "llm_matrix", "native_primitives",
            "composite_orders", "geography", "personality", "correspondence",
            "fault_recovery", "save_roundtrip", "campaign_soak", "evaluate", "cleanup")]
        string profile, int seed = 1337, int pass = 1,
        [Description("Exact text required: start Reign campaign command profile on disposable save")]
        string confirmation = "", CancellationToken cancellationToken = default)
    {
        RequireCampaignCommandControl(options);
        InputGuard.RequireConfirmation(confirmation,
            "start Reign campaign command profile on disposable save");
        campaignId = RequiredCampaignCommandIdentifier(campaignId, nameof(campaignId));
        campaignTestRunId = RequiredCampaignCommandIdentifier(campaignTestRunId, nameof(campaignTestRunId));
        runId = RequiredCampaignCommandIdentifier(runId, nameof(runId));
        CampaignTestAuthorization authorization = await campaignTests.RequireArmedOwnedSaveAsync(
            campaignTestRunId, campaignId, cancellationToken).ConfigureAwait(false);
        string disposableSaveName = authorization.CurrentSaveName;
        profile = NormalizeCampaignCommandProfile(profile);
        seed = InputGuard.Range(seed, nameof(seed), 0, int.MaxValue);
        pass = InputGuard.Range(pass, nameof(pass), 1, 1);
        int timeout = profile == "campaign_soak" ? 7200 : profile == "llm_matrix" ? 7200 : 1200;
        string liveRunId = CampaignCommandLiveRunId(runId, profile, pass);
        object[] profileSteps = profile == "save_roundtrip"
            ? new object[]
            {
                new { operation = "campaign_command_test", profile, phase = "prepare", certificationRunId = runId,
                    campaignTestRunId, disposableSavePrefix = authorization.SavePrefix,
                    disposableSaveName, seed, pass, timeoutSeconds = timeout }
            }
            : profile == "campaign_soak"
            ? new object[]
            {
                new { operation = "save_checkpoint", saveName = disposableSaveName, timeoutSeconds = 300 },
                new { operation = "campaign_command_test", profile, certificationRunId = runId,
                    campaignTestRunId, disposableSavePrefix = authorization.SavePrefix,
                    disposableSaveName, seed, pass, soakDays = 30, checkpointPrepared = true,
                    timeoutSeconds = timeout }
            }
            : new object[] { new { operation = "campaign_command_test", profile, certificationRunId = runId,
                campaignTestRunId, disposableSavePrefix = authorization.SavePrefix,
                disposableSaveName, seed, pass, timeoutSeconds = timeout } };
        ApiEnvelope association = await api.PostAsync("/tests/campaign-command/execution",
            new { runId, liveRunId, profile }, cancellationToken).ConfigureAwait(false);
        ApiEnvelope liveRun = await api.PostAsync("/tests/live/run/start", new
        {
            schemaVersion = 2, campaignId, runId = liveRunId, mode = "campaign_command", seed,
            label = "MCP Campaign Command " + profile + " pass " + pass,
            presentation = "visible", effects = profile is "preflight" or "offline_contracts"
                or "llm_matrix" or "geography" or "personality" or "evaluate" ? "guarded" : "full",
            autoCompleteWhenIdle = true,
            enrollment = new { campaignId, runId, campaignTestRunId, disposableSaveName,
                disposableSavePrefix = authorization.SavePrefix, timelineId = authorization.TimelineId },
            steps = profileSteps
        }, cancellationToken).ConfigureAwait(false);
        return new Dictionary<string, ApiEnvelope>
        { ["association"] = association, ["liveRun"] = liveRun };
    }

    [McpServerTool(Name = "reign_verify_campaign_command_save_roundtrip", ReadOnly = false,
        Destructive = true, Idempotent = false, UseStructuredContent = true)]
    [Description("Verifies the prepared Campaign Command order after the guarded campaign-test service checkpointed and restarted the exact disposable Current save in a different native game instance.")]
    public static async Task<IReadOnlyDictionary<string, ApiEnvelope>> VerifyCampaignCommandSaveRoundtrip(
        ReignApiClient api, ReignMcpOptions options, CampaignTestService campaignTests,
        string campaignId, string campaignTestRunId, string runId, string restartReceipt,
        [Description("Exact text required: verify Reign campaign command save reload on disposable save")]
        string confirmation = "", CancellationToken cancellationToken = default)
    {
        RequireCampaignCommandControl(options);
        InputGuard.RequireConfirmation(confirmation,
            "verify Reign campaign command save reload on disposable save");
        campaignId = RequiredCampaignCommandIdentifier(campaignId, nameof(campaignId));
        campaignTestRunId = RequiredCampaignCommandIdentifier(campaignTestRunId, nameof(campaignTestRunId));
        runId = RequiredCampaignCommandIdentifier(runId, nameof(runId));
        restartReceipt = RequiredCampaignCommandIdentifier(restartReceipt, nameof(restartReceipt));
        campaignTests.RequireRestartReceipt(campaignTestRunId, campaignId, restartReceipt);
        CampaignTestAuthorization authorization = await campaignTests.RequireArmedOwnedSaveAsync(
            campaignTestRunId, campaignId, cancellationToken).ConfigureAwait(false);
        string liveRunId = CampaignCommandLiveRunId(runId, "verify", 1);
        ApiEnvelope association = await api.PostAsync("/tests/campaign-command/execution",
            new { runId, liveRunId, profile = "save_roundtrip" }, cancellationToken).ConfigureAwait(false);
        ApiEnvelope liveRun = await api.PostAsync("/tests/live/run/start", new
        {
            schemaVersion = 2, campaignId, runId = liveRunId, mode = "campaign_command", seed = 1337,
            label = "MCP Campaign Command save reload verification", presentation = "visible",
            effects = "full", autoCompleteWhenIdle = true,
            enrollment = new { campaignId, runId, campaignTestRunId,
                disposableSaveName = authorization.CurrentSaveName,
                disposableSavePrefix = authorization.SavePrefix, timelineId = authorization.TimelineId,
                restartReceipt },
            steps = new object[] { new { operation = "campaign_command_test",
                profile = "save_roundtrip", phase = "verify", certificationRunId = runId,
                campaignTestRunId, disposableSavePrefix = authorization.SavePrefix,
                disposableSaveName = authorization.CurrentSaveName, seed = 1337, pass = 1,
                restartVerified = true, restartReceipt, timeoutSeconds = 1200 } }
        }, cancellationToken).ConfigureAwait(false);
        return new Dictionary<string, ApiEnvelope>
        { ["association"] = association, ["liveRun"] = liveRun };
    }

    [McpServerTool(Name = "reign_start_campaign_command_certification", ReadOnly = false,
        Destructive = true, Idempotent = false, UseStructuredContent = true)]
    [Description("Starts the one-fingerprint Campaign Command certification sequence. It schedules one clean pass of each non-restart profile and one combined 30-day soak; save reload is verified separately with a guarded restart receipt.")]
    public static async Task<IReadOnlyDictionary<string, ApiEnvelope>> StartCampaignCommandCertification(ReignApiClient api,
        ReignMcpOptions options, CampaignTestService campaignTests, string campaignId,
        string campaignTestRunId, string runId,
        [Description("Exact text required: start Reign campaign command certification on disposable save")]
        string confirmation = "", CancellationToken cancellationToken = default)
    {
        RequireCampaignCommandControl(options);
        InputGuard.RequireConfirmation(confirmation,
            "start Reign campaign command certification on disposable save");
        campaignId = RequiredCampaignCommandIdentifier(campaignId, nameof(campaignId));
        campaignTestRunId = RequiredCampaignCommandIdentifier(campaignTestRunId, nameof(campaignTestRunId));
        runId = RequiredCampaignCommandIdentifier(runId, nameof(runId));
        CampaignTestAuthorization authorization = await campaignTests.RequireArmedOwnedSaveAsync(
            campaignTestRunId, campaignId, cancellationToken).ConfigureAwait(false);
        string disposableSaveName = authorization.CurrentSaveName;
        string liveRunId = CampaignCommandLiveRunId(runId, "certification", 1);
        int[] seeds = { 1337 };
        var steps = new List<object>();
        foreach (string profile in CampaignCommandProfiles.Where(x => x != "campaign_soak"
            && x != "evaluate" && x != "cleanup" && x != "save_roundtrip"))
        foreach (int seed in seeds)
        {
            int pass = Array.IndexOf(seeds, seed) + 1;
            int timeout = profile == "llm_matrix" ? 7200 : 1200;
            steps.Add(new { operation = "campaign_command_test", profile, certificationRunId = runId,
                campaignTestRunId, disposableSavePrefix = authorization.SavePrefix,
                disposableSaveName, seed, pass, timeoutSeconds = timeout });
        }
        for (int soak = 0; soak < 1; soak++)
        {
            steps.Add(new { operation = "save_checkpoint", saveName = disposableSaveName,
                timeoutSeconds = 300 });
            steps.Add(new { operation = "campaign_command_test", profile = "campaign_soak", certificationRunId = runId,
                campaignTestRunId, disposableSavePrefix = authorization.SavePrefix,
                disposableSaveName, seed = seeds[soak % seeds.Length] + soak * 17,
                pass = soak + 1, soakDays = 30, checkpointPrepared = true, timeoutSeconds = 7200 });
        }
        foreach (int seed in seeds)
            steps.Add(new { operation = "campaign_command_test", profile = "evaluate", certificationRunId = runId,
                campaignTestRunId, disposableSavePrefix = authorization.SavePrefix,
                disposableSaveName, seed, pass = Array.IndexOf(seeds, seed) + 1,
                timeoutSeconds = 1200 });
        steps.Add(new { operation = "campaign_command_test", profile = "cleanup", certificationRunId = runId,
            campaignTestRunId, disposableSavePrefix = authorization.SavePrefix,
            disposableSaveName, seed = 1337, pass = 1, timeoutSeconds = 1200 });
        ApiEnvelope association = await api.PostAsync("/tests/campaign-command/execution",
            new { runId, liveRunId, profile = "certification" }, cancellationToken).ConfigureAwait(false);
        ApiEnvelope liveRun = await api.PostAsync("/tests/live/run/start", new
        {
            schemaVersion = 2, campaignId, runId = liveRunId, mode = "campaign_command", seed = 1337,
            label = "MCP Campaign Command autonomous certification", presentation = "visible",
            effects = "full", autoCompleteWhenIdle = true,
            enrollment = new { campaignId, runId, campaignTestRunId, disposableSaveName,
                disposableSavePrefix = authorization.SavePrefix, timelineId = authorization.TimelineId }, steps
        }, cancellationToken).ConfigureAwait(false);
        return new Dictionary<string, ApiEnvelope>
        { ["association"] = association, ["liveRun"] = liveRun };
    }

    [McpServerTool(Name = "reign_get_campaign_command_test_status", ReadOnly = true,
        Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Returns durable Campaign Command section state, clean-pass counts, fingerprints, failure classes, fatal-stop state, and live bridge progress.")]
    public static async Task<IReadOnlyDictionary<string, ApiEnvelope>> GetCampaignCommandTestStatus(
        ReignApiClient api, string runId, string campaignId = "",
        CancellationToken cancellationToken = default)
    {
        runId = RequiredCampaignCommandIdentifier(runId, nameof(runId));
        campaignId = InputGuard.OptionalIdentifier(campaignId, nameof(campaignId));
        Task<ApiEnvelope> certification = api.GetAsync("/tests/campaign-command/status",
            new Dictionary<string, string?> { ["runId"] = runId }, cancellationToken);
        Task<ApiEnvelope> live = api.GetAsync("/tests/live/run/status",
            new Dictionary<string, string?> { ["campaignId"] = campaignId },
            cancellationToken);
        await Task.WhenAll(certification, live).ConfigureAwait(false);
        return new Dictionary<string, ApiEnvelope>
        {
            ["certification"] = await certification.ConfigureAwait(false),
            ["liveRun"] = await live.ConfigureAwait(false)
        };
    }

    [McpServerTool(Name = "reign_get_campaign_command_test_report", ReadOnly = true,
        Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Returns the Campaign Command human summary fields, section certificates, case ledger, evidence paths, metrics, save isolation, and replay references.")]
    public static Task<ApiEnvelope> GetCampaignCommandTestReport(ReignApiClient api,
        string runId, CancellationToken cancellationToken = default) =>
        api.GetAsync("/tests/campaign-command/report", new Dictionary<string, string?>
        { ["runId"] = RequiredCampaignCommandIdentifier(runId, nameof(runId)) }, cancellationToken);

    [McpServerTool(Name = "reign_get_campaign_command_replay_bundle", ReadOnly = true,
        Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Returns the minimal replay bundle and isolated evidence references for one Campaign Command certification section.")]
    public static Task<ApiEnvelope> GetCampaignCommandReplayBundle(ReignApiClient api,
        string runId,
        [AllowedValues("preflight", "offline_contracts", "llm_matrix", "native_primitives",
            "composite_orders", "geography", "personality", "correspondence",
            "fault_recovery", "save_roundtrip", "campaign_soak", "evaluate", "cleanup")]
        string profile, CancellationToken cancellationToken = default) =>
        api.GetAsync("/tests/campaign-command/replay", new Dictionary<string, string?>
        {
            ["runId"] = RequiredCampaignCommandIdentifier(runId, nameof(runId)),
            ["profile"] = NormalizeCampaignCommandProfile(profile)
        }, cancellationToken);

    [McpServerTool(Name = "reign_resume_campaign_command_certification", ReadOnly = false,
        Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Resumes durable Campaign Command certification state after a repair. Previously certified independent sections remain cached by fingerprint; fatal harness blocks cannot be bypassed.")]
    public static Task<ApiEnvelope> ResumeCampaignCommandCertification(ReignApiClient api,
        ReignMcpOptions options, string runId,
        [Description("Comma-separated failed/changed profile ids. Only these profiles and their declared dependents are invalidated.")]
        string changedProfiles = "",
        string sourceFingerprint = "",
        [Description("Exact text required: resume Reign campaign command certification")]
        string confirmation = "", CancellationToken cancellationToken = default)
    {
        RequireCampaignCommandControl(options);
        InputGuard.RequireConfirmation(confirmation, "resume Reign campaign command certification");
        changedProfiles = InputGuard.BoundedText(changedProfiles, nameof(changedProfiles), 500);
        return api.PostAsync("/tests/campaign-command/resume", new
        {
            runId = RequiredCampaignCommandIdentifier(runId, nameof(runId)), changedProfiles,
            sourceFingerprint = BoundedFingerprint(sourceFingerprint)
        }, cancellationToken);
    }

    [McpServerTool(Name = "reign_cancel_campaign_command_certification", ReadOnly = false,
        Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Safely cancels the durable Campaign Command certification and requests cancellation of its active live run after the current bounded case.")]
    public static Task<ApiEnvelope> CancelCampaignCommandCertification(
        ReignApiClient api, ReignMcpOptions options, string runId, string campaignId = "",
        [Description("Exact text required: cancel Reign campaign command certification")]
        string confirmation = "", CancellationToken cancellationToken = default)
    {
        RequireCampaignCommandControl(options);
        InputGuard.RequireConfirmation(confirmation, "cancel Reign campaign command certification");
        runId = RequiredCampaignCommandIdentifier(runId, nameof(runId));
        campaignId = InputGuard.OptionalIdentifier(campaignId, nameof(campaignId));
        return api.PostAsync("/tests/campaign-command/cancel", new { runId }, cancellationToken);
    }

    [McpServerTool(Name = "reign_evaluate_campaign_command_launch_readiness", ReadOnly = true,
        Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Evaluates the strict Campaign Command launch gate: one complete clean final-fingerprint pass, 100% deterministic/native/persistence/authority/safety checks, >=98% LLM accuracy, and 100% LLM safety dimensions.")]
    public static Task<ApiEnvelope> EvaluateCampaignCommandLaunchReadiness(ReignApiClient api,
        string runId, CancellationToken cancellationToken = default) =>
        api.GetAsync("/tests/campaign-command/readiness", new Dictionary<string, string?>
        { ["runId"] = RequiredCampaignCommandIdentifier(runId, nameof(runId)) }, cancellationToken);

    private static void RequireCampaignCommandControl(ReignMcpOptions options)
    {
        if (!options.AllowVerificationControl)
            throw new InvalidOperationException("Verification control is disabled. Set REIGN_MCP_ALLOW_VERIFICATION_CONTROL=true in the trusted MCP configuration.");
    }

    private static string RequiredCampaignCommandIdentifier(string value, string parameterName)
    {
        string result = InputGuard.OptionalIdentifier(value, parameterName);
        if (result.Length == 0) throw new ArgumentException(parameterName + " is required.", parameterName);
        return result;
    }

    private static string NormalizeCampaignCommandProfile(string value)
    {
        string profile = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (!CampaignCommandProfiles.Contains(profile, StringComparer.Ordinal))
            throw new ArgumentException("Unsupported Campaign Command profile.", nameof(value));
        return profile;
    }

    private static string BoundedFingerprint(string value) =>
        InputGuard.BoundedText(value ?? "unknown", nameof(value), 256);

    private static string CampaignCommandLiveRunId(string certificationRunId, string profile, int pass) =>
        RequiredCampaignCommandIdentifier(certificationRunId, nameof(certificationRunId)) + "-"
        + NormalizeCampaignCommandProfile(profile == "certification" || profile == "verify"
            ? "preflight" : profile) + "-p" + pass.ToString(System.Globalization.CultureInfo.InvariantCulture)
        + "-" + Guid.NewGuid().ToString("N")[..8];
}
