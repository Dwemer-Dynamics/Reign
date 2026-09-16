using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;

namespace Reign.Mcp.Server;

[McpServerToolType]
public static partial class TestingTools
{
    [McpServerTool(Name = "reign_get_verification_status", ReadOnly = true, Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Returns current Verification Lab status. It does not start, cancel, or replay a run.")]
    public static Task<ApiEnvelope> GetVerificationStatus(
        ReignApiClient api,
        CancellationToken cancellationToken)
    {
        return api.GetAsync("/verification/status", cancellationToken: cancellationToken);
    }

    [McpServerTool(Name = "reign_get_verification_results", ReadOnly = true, Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Lists recent Verification Lab results or loads one run by ID. Results remain subject to Reign's distinction between offline and native game acceptance.")]
    public static Task<ApiEnvelope> GetVerificationResults(
        ReignApiClient api,
        string runId = "",
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        runId = InputGuard.OptionalIdentifier(runId, nameof(runId));
        limit = InputGuard.Range(limit, nameof(limit), 1, 200);
        return api.GetAsync("/verification/results", new Dictionary<string, string?>
        {
            ["runId"] = runId,
            ["limit"] = limit.ToString()
        }, cancellationToken);
    }

    [McpServerTool(Name = "reign_get_live_test_status", ReadOnly = true, Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Observes the current Bannerlord live-test bridge and optional run status. It cannot arm the bridge or enqueue game commands.")]
    public static async Task<IReadOnlyDictionary<string, ApiEnvelope>> GetLiveTestStatus(
        ReignApiClient api,
        string campaignId = "",
        string runId = "",
        CancellationToken cancellationToken = default)
    {
        campaignId = InputGuard.OptionalIdentifier(campaignId, nameof(campaignId));
        runId = InputGuard.OptionalIdentifier(runId, nameof(runId));
        var runtime = api.GetAsync("/tests/live/runtime", new Dictionary<string, string?>
        {
            ["campaignId"] = campaignId
        }, cancellationToken);
        var run = api.GetAsync("/tests/live/run/status", new Dictionary<string, string?>
        {
            ["campaignId"] = campaignId,
            ["runId"] = runId
        }, cancellationToken);
        await Task.WhenAll(runtime, run).ConfigureAwait(false);
        return new Dictionary<string, ApiEnvelope>
        {
            ["runtime"] = await runtime.ConfigureAwait(false),
            ["run"] = await run.ConfigureAwait(false)
        };
    }

    [McpServerTool(Name = "reign_get_live_test_report", ReadOnly = true, Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Loads the detailed report for a known live-test run without pausing, resuming, cancelling, or changing the game.")]
    public static Task<ApiEnvelope> GetLiveTestReport(
        ReignApiClient api,
        string runId,
        string campaignId = "",
        CancellationToken cancellationToken = default)
    {
        runId = InputGuard.OptionalIdentifier(runId, nameof(runId));
        if (runId.Length == 0)
        {
            throw new ArgumentException("runId is required.", nameof(runId));
        }
        campaignId = InputGuard.OptionalIdentifier(campaignId, nameof(campaignId));
        return api.GetAsync("/tests/live/run/report", new Dictionary<string, string?>
        {
            ["campaignId"] = campaignId,
            ["runId"] = runId
        }, cancellationToken);
    }

    [McpServerTool(Name = "reign_get_release_readiness", ReadOnly = true, Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Returns live readiness evidence and social-balance status for a campaign. It does not reset, prepare, record, or evaluate a run.")]
    public static async Task<IReadOnlyDictionary<string, ApiEnvelope>> GetReleaseReadiness(
        ReignApiClient api,
        string campaignId = "",
        string timelineId = "main",
        string runId = "",
        CancellationToken cancellationToken = default)
    {
        campaignId = InputGuard.OptionalIdentifier(campaignId, nameof(campaignId));
        timelineId = InputGuard.OptionalIdentifier(timelineId, nameof(timelineId));
        runId = InputGuard.OptionalIdentifier(runId, nameof(runId));
        var readiness = api.GetAsync("/tests/live/readiness", new Dictionary<string, string?>
        {
            ["campaignId"] = campaignId
        }, cancellationToken);
        var social = api.GetAsync("/tests/social-balance/status", new Dictionary<string, string?>
        {
            ["campaignId"] = campaignId,
            ["timelineId"] = timelineId.Length == 0 ? "main" : timelineId,
            ["runId"] = runId
        }, cancellationToken);
        Task<ApiEnvelope>? campaignCommand = runId.Length == 0 ? null : api.GetAsync(
            "/tests/campaign-command/readiness", new Dictionary<string, string?>
            {
                ["runId"] = runId
            }, cancellationToken);
        var pending = campaignCommand == null
            ? new[] { readiness, social }
            : new[] { readiness, social, campaignCommand };
        await Task.WhenAll(pending).ConfigureAwait(false);
        var result = new Dictionary<string, ApiEnvelope>
        {
            ["readiness"] = await readiness.ConfigureAwait(false),
            ["socialBalance"] = await social.ConfigureAwait(false)
        };
        if (campaignCommand != null)
            result["campaignCommand"] = await campaignCommand.ConfigureAwait(false);
        return result;
    }

    [McpServerTool(Name = "reign_start_verification", ReadOnly = false, Destructive = false, Idempotent = false, UseStructuredContent = true)]
    [Description("Starts one Verification Lab run in Reign's isolated verification storage. Disabled unless explicitly enabled. Requires exact confirmation and never deploys or controls the game directly.")]
    public static Task<ApiEnvelope> StartVerification(
        ReignApiClient api,
        ReignMcpOptions options,
        [AllowedValues("quick", "offline", "live-llm", "game", "all")]
        string tier = "offline",
        string suite = "",
        int seed = 1337,
        int repeat = 1,
        bool failFast = true,
        int liveCaseCap = 20,
        int gameTimeoutSeconds = 900,
        [Description("Exact text required: start Reign verification")] string confirmation = "",
        [Description("Only for codex_performance: JSON object with model, caseIds, baselineOptions, variantOptions, changedOption, totalProviderCallCap (1..1000), and confirmation='run bounded Codex performance comparison'. Omit for other suites.")] string codexPerformanceJson = "",
        CancellationToken cancellationToken = default)
    {
        if (!options.AllowVerificationControl)
        {
            throw new InvalidOperationException(
                "Verification control is disabled. Set REIGN_MCP_ALLOW_VERIFICATION_CONTROL=true in the trusted MCP configuration.");
        }
        InputGuard.RequireConfirmation(confirmation, "start Reign verification");
        tier = tier.Trim().ToLowerInvariant();
        if (tier is not ("quick" or "offline" or "live-llm" or "game" or "all"))
        {
            throw new ArgumentException("Unsupported verification tier.", nameof(tier));
        }
        suite = InputGuard.OptionalIdentifier(suite, nameof(suite));
        seed = InputGuard.Range(seed, nameof(seed), 0, int.MaxValue);
        repeat = InputGuard.Range(repeat, nameof(repeat), 1, 100);
        liveCaseCap = InputGuard.Range(liveCaseCap, nameof(liveCaseCap), 1, 100);
        gameTimeoutSeconds = InputGuard.Range(gameTimeoutSeconds, nameof(gameTimeoutSeconds), 30, 7200);
        JsonObject? codexPerformance = null;
        if (suite == "codex_performance" && tier is not ("quick" or "offline"))
        {
            if (tier != "live-llm") throw new ArgumentException("codex_performance provider comparisons require the explicit live-llm tier.");
            if (string.IsNullOrWhiteSpace(codexPerformanceJson) || codexPerformanceJson.Length > 32768)
                throw new ArgumentException("An explicit bounded codexPerformanceJson plan is required.");
            codexPerformance = JsonNode.Parse(codexPerformanceJson) as JsonObject ?? throw new ArgumentException("Codex performance plan must be a JSON object.");
            codexPerformance["validationWorkspaceRoot"] = options.WorkspaceRoot;
            if (codexPerformance["confirmation"]?.GetValue<string>() != "run bounded Codex performance comparison")
                throw new ArgumentException("Codex performance requires separate explicit provider-usage confirmation.");
            int cap = codexPerformance["totalProviderCallCap"]?.GetValue<int>() ?? 0;
            InputGuard.Range(cap, "totalProviderCallCap", 1, 1000);
            if (string.IsNullOrWhiteSpace(codexPerformance["model"]?.GetValue<string>()) || codexPerformance["caseIds"] is not JsonArray { Count: > 0 }
                || codexPerformance["baselineOptions"] is not JsonObject || codexPerformance["variantOptions"] is not JsonObject)
                throw new ArgumentException("Codex performance needs an exact model, explicit cases, and both configurations.");
        }
        return api.PostAsync("/verification/run", new
        {
            tier,
            suite,
            seed,
            repeat,
            failFast,
            liveCaseCap,
            gameTimeoutSeconds,
            codexPerformance,
            requestedBy = "Reign MCP"
        }, cancellationToken);
    }

    [McpServerTool(Name = "reign_cancel_verification", ReadOnly = false, Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Requests cancellation of the active Verification Lab run after its current check. Disabled unless explicitly enabled and requires exact confirmation.")]
    public static Task<ApiEnvelope> CancelVerification(
        ReignApiClient api,
        ReignMcpOptions options,
        [Description("Exact text required: cancel Reign verification")] string confirmation,
        CancellationToken cancellationToken = default)
    {
        if (!options.AllowVerificationControl)
        {
            throw new InvalidOperationException(
                "Verification control is disabled. Set REIGN_MCP_ALLOW_VERIFICATION_CONTROL=true in the trusted MCP configuration.");
        }
        InputGuard.RequireConfirmation(confirmation, "cancel Reign verification");
        return api.PostAsync("/verification/cancel", new { requestedBy = "Reign MCP" }, cancellationToken);
    }

    [McpServerTool(Name = "reign_build", ReadOnly = false, Destructive = false, Idempotent = false, UseStructuredContent = true)]
    [Description("Builds a catalog-discovered Reign project ID or the core, product, tooling, or all profile into a fresh workspace-confined .codex-build directory. Product excludes side tooling; all is ecosystem release validation. New managed projects are discovered automatically and unclassified projects fail closed.")]
    public static Task<BuildBatchResult> Build(
        ReignBuildService buildService,
        [Description("A project ID from reign_get_validation_plan, or core, product, tooling, or all.")]
        string component = "all",
        [AllowedValues("Debug", "Release")]
        string configuration = "Release",
        [Description("Allow NuGet restore for this build. Also requires REIGN_MCP_ALLOW_RESTORE=true.")] bool restore = false,
        CancellationToken cancellationToken = default)
    {
        return buildService.BuildAsync(component, configuration, restore, cancellationToken);
    }

    [McpServerTool(Name = "reign_get_validation_plan", ReadOnly = true, Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Discovers every managed Reign project, resolves changed paths through reign.modules.json, selects the lowest safe validation tier, and returns the exact build/test plan without executing it.")]
    public static ValidationPlan GetValidationPlan(
        ReignValidationService validation,
        [AllowedValues("changed", "core", "product", "tooling", "all")]
        string profile = "all",
        [AllowedValues("Debug", "Release")]
        string configuration = "Release",
        bool restore = false,
        [Description("Semicolon or newline separated workspace-relative changed paths. Required for the changed profile; empty, unknown, or ambiguous input blocks without building.")]
        string changedPaths = "")
    {
        return validation.GetPlan(profile, configuration, restore, changedPaths);
    }

    [McpServerTool(Name = "reign_get_validation_status", ReadOnly = true, Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Reads the workspace validation lease, owner task, requested paths, current operation and report location. Does not start, cancel or take over work. Availability is a snapshot; the OS lease remains authoritative.")]
    public static ValidationLaneStatus GetValidationStatus(ReignValidationService validation) => validation.GetStatus();

    [McpServerTool(Name = "reign_validate", ReadOnly = false, Destructive = false, Idempotent = false, UseStructuredContent = true)]
    [Description("Runs the canonical fail-closed, module-aware Reign validation plan and writes a structured report with the selected tier and reasons. It never deploys, starts a listener, or controls Bannerlord/Reign processes.")]
    public static Task<ValidationReport> Validate(
        ReignValidationService validation,
        [AllowedValues("changed", "core", "product", "tooling", "all")]
        string profile = "changed",
        [AllowedValues("Debug", "Release")]
        string configuration = "Release",
        bool restore = false,
        [Description("Semicolon or newline separated workspace-relative changed paths. Required for changed. Use product for product-wide confidence and all only for ecosystem release validation.")]
        string changedPaths = "",
        bool failFast = true,
        CancellationToken cancellationToken = default,
        [Description("Exact current Codex task UUID for validation ownership attribution. Empty leaves the owner task unknown; it never grants authority.")]
        string requestingTaskId = "")
    {
        return validation.ValidateAsync(
            profile,
            configuration,
            restore,
            changedPaths,
            failFast,
            cancellationToken,
            requestingTaskId);
    }

    [McpServerTool(Name = "reign_build_linux_server", ReadOnly = false, Destructive = false, Idempotent = false, UseStructuredContent = true)]
    [Description("Cross-publishes the self-contained Linux server under the canonical build lease and paired hygiene audit. Returns a component artifact manifest; does not deploy, start a listener, or claim full validation.")]
    public static Task<LinuxServerBuildReport> BuildLinuxServer(
        ReignValidationService validation, bool restore = false, string requestingTaskId = "",
        CancellationToken cancellationToken = default)
    {
        return validation.BuildLinuxServerAsync(restore, requestingTaskId, cancellationToken);
    }

    [McpServerTool(Name = "reign_audit_module_coverage", ReadOnly = true, Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Inventories validation-relevant files beneath every managed root and reports owned, non-code, unmapped, and ambiguous paths without building anything.")]
    public static ModuleCoverageAudit AuditModuleCoverage(
        ReignValidationService validation)
    {
        return validation.GetPlan("all", "Release", false, "").CoverageAudit;
    }

    [McpServerTool(Name = "reign_run_offline_verification", ReadOnly = false, Destructive = false, Idempotent = false, UseStructuredContent = true)]
    [Description("Runs Reign's non-listening CLI verifier from the exact successful Release artifact named by validationRunId. It writes isolated verification artifacts, refuses to overlap an active server verification run, and cannot run live-LLM or game tiers.")]
    public static Task<OfflineVerificationResult> RunOfflineVerification(
        ReignBuildService buildService,
        [AllowedValues("quick", "offline")] string tier = "offline",
        string suite = "",
        int seed = 1337,
        int repeat = 1,
        bool failFast = true,
        [Description("Exact runId returned by a successful Release reign_validate report; the verifier executes that run's immutable server artifact.")] string validationRunId = "",
        [Description("Exact text required: run isolated offline verification")] string confirmation = "",
        CancellationToken cancellationToken = default)
    {
        return buildService.RunOfflineVerificationAsync(
            tier, suite, seed, repeat, failFast, validationRunId, confirmation, cancellationToken);
    }
}
