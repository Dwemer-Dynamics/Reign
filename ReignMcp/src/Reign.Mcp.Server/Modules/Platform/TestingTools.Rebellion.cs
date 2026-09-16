using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Reign.Mcp.Server;

public static partial class TestingTools
{
    private static readonly object RebellionStateGate = new();
    private static readonly JsonSerializerOptions RebellionJson =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly string[] RebellionProfiles = { "smoke", "feature", "pledges",
        "reporting", "summons", "verdicts", "declaration", "save_prepare", "save_verify",
        "resolution", "native_setup", "language_observe", "travel_prepare", "travel_verify", "mixed_transfer",
        "foreign_reintegration", "cleanup" };

    [McpServerTool(Name = "reign_get_rebellion_test_manifest", ReadOnly = true,
        Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Returns the compact manifest-owned Rebellion language, deterministic, native, persistence, transfer, reintegration, and cleanup certification contract.")]
    public static IReadOnlyDictionary<string, object?> GetRebellionTestManifest(
        ReignMcpOptions options)
    {
        string path = RebellionManifestPath(options);
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
        return new Dictionary<string, object?>
        {
            ["ok"] = true,
            ["schema"] = "reign-rebellion-certification-manifest-v1",
            ["schemaVersion"] = 1,
            ["manifestPath"] = path,
            ["cliEntryPoint"] = "ReignLiveTest.exe rebellion --profile <profile>",
            ["profiles"] = RebellionProfiles,
            ["manifest"] = JsonSerializer.Deserialize<object>(document.RootElement.GetRawText())
        };
    }

    [McpServerTool(Name = "reign_run_rebellion_contract_tests", ReadOnly = true,
        Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Runs the server-authoritative compact Rebellion language and deterministic state-machine contract without starting Bannerlord or mutating a campaign.")]
    public static Task<ApiEnvelope> RunRebellionContractTests(ReignApiClient api,
        CancellationToken cancellationToken = default) =>
        api.GetAsync("/rebellions/tests", null, cancellationToken);

    [McpServerTool(Name = "reign_prepare_rebellion_certification", ReadOnly = false,
        Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Binds Rebellion certification to an existing guarded campaign-test enrollment and immutable source, runtime, provider, catalog, and Bannerlord fingerprints without mutating Bannerlord.")]
    public static IReadOnlyDictionary<string, object?> PrepareRebellionCertification(
        ReignMcpOptions options, CampaignTestService campaignTests,
        string campaignId, string campaignTestRunId, string runId,
        string sourceFingerprint, string clientBuild, string serverBuild,
        string providerConfigurationFingerprint, string catalogFingerprint,
        string bannerlordVersion = "v1.4.8")
    {
        campaignId = RequiredIdentifier(campaignId, nameof(campaignId));
        campaignTestRunId = RequiredIdentifier(campaignTestRunId, nameof(campaignTestRunId));
        runId = RequiredIdentifier(runId, nameof(runId));
        CampaignTestAuthorization authorization = campaignTests.RequireEnrollment(
            campaignTestRunId, campaignId);
        JsonElement manifest = LoadRebellionManifest(options);
        var requested = new RebellionCertificationState
        {
            RunId = runId, CampaignId = campaignId, CampaignTestRunId = campaignTestRunId,
            TimelineId = authorization.TimelineId,
            ProtectedBaselineSaveName = authorization.BaselineSaveName,
            DisposableSaveName = authorization.CurrentSaveName,
            DisposableSavePrefix = authorization.SavePrefix,
            SourceFingerprint = RequiredRebellionFingerprint(sourceFingerprint, nameof(sourceFingerprint)),
            ClientBuild = RequiredRebellionFingerprint(clientBuild, nameof(clientBuild)),
            ServerBuild = RequiredRebellionFingerprint(serverBuild, nameof(serverBuild)),
            ProviderConfigurationFingerprint = RequiredRebellionFingerprint(
                providerConfigurationFingerprint, nameof(providerConfigurationFingerprint)),
            CatalogFingerprint = RequiredRebellionFingerprint(catalogFingerprint, nameof(catalogFingerprint)),
            BannerlordVersion = RequiredRebellionFingerprint(bannerlordVersion, nameof(bannerlordVersion)),
            RequiredInstances = RebellionInstances(manifest).ToList(),
            PreparedUtc = DateTime.UtcNow.ToString("O"), UpdatedUtc = DateTime.UtcNow.ToString("O")
        };
        lock (RebellionStateGate)
        {
            RebellionCertificationState? existing = TryReadRebellionState(options, runId);
            if (existing is not null)
            {
                if (!RebellionImmutableInputsMatch(existing, requested))
                    throw new InvalidOperationException(
                        "The Rebellion run id is already prepared with different immutable enrollment or fingerprint inputs.");
                return RebellionStateEnvelope(existing, "already_prepared", manifest);
            }
            WriteRebellionState(options, requested);
        }
        return RebellionStateEnvelope(requested, "prepared", manifest);
    }

    [McpServerTool(Name = "reign_run_rebellion_certification_contract", ReadOnly = false,
        Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Runs and records only the fingerprint-bound Rebellion server language-contract cases without starting Bannerlord or mutating a campaign. Organic channel cases and client state-machine cases require their own live reports.")]
    public static async Task<IReadOnlyDictionary<string, object?>> RunRebellionCertificationContract(
        ReignApiClient api, ReignMcpOptions options, string runId,
        CancellationToken cancellationToken = default)
    {
        RebellionCertificationState state = ReadRebellionState(options,
            RequiredIdentifier(runId, nameof(runId)));
        ApiEnvelope report = await RunRebellionContractTests(api, cancellationToken)
            .ConfigureAwait(false);
        bool passed = report.Ok && report.Data.HasValue
            && FindRebellionBool(report.Data.Value, "ok")
            && FindRebellionInt(report.Data.Value, "languageCaseCount") == 11;
        if (passed)
        {
            JsonElement manifest = LoadRebellionManifest(options);
            foreach (JsonElement item in manifest.GetProperty("languageCases").EnumerateArray())
                if (item.GetProperty("profile").GetString() == "language_contract"
                    && item.TryGetProperty("passOnce", out JsonElement flag)
                    && flag.ValueKind == JsonValueKind.True)
                    state.ContractPasses.Add(item.GetProperty("id").GetString() ?? string.Empty);
            state.ContractPasses = state.ContractPasses.Where(value => value.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            state.ContractReportUtc = DateTime.UtcNow.ToString("O");
            lock (RebellionStateGate) WriteRebellionState(options, state);
        }
        return new Dictionary<string, object?>
        {
            ["ok"] = passed, ["schema"] = "reign-rebellion-certification-contract-v1",
            ["recordedPasses"] = passed ? state.ContractPasses : [],
            ["report"] = report, ["state"] = state
        };
    }

    [McpServerTool(Name = "reign_carry_forward_rebellion_passes", ReadOnly = false,
        Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Carries only identical-fingerprint Rebellion language and deterministic pass-once evidence. Native, save/load, transfer, reintegration, and cleanup evidence is never carried.")]
    public static IReadOnlyDictionary<string, object?> CarryForwardRebellionPasses(
        ReignMcpOptions options, string sourceRunId, string destinationRunId,
        [Description("Exact text required: carry forward compatible Reign Rebellion passes")]
        string confirmation = "")
    {
        InputGuard.RequireConfirmation(confirmation,
            "carry forward compatible Reign Rebellion passes");
        RebellionCertificationState source = ReadRebellionState(options,
            RequiredIdentifier(sourceRunId, nameof(sourceRunId)));
        RebellionCertificationState destination = ReadRebellionState(options,
            RequiredIdentifier(destinationRunId, nameof(destinationRunId)));
        if (!RebellionFingerprintsMatch(source, destination))
            throw new InvalidOperationException(
                "Rebellion pass-once evidence cannot cross enrollment or fingerprint changes.");
        JsonElement manifest = LoadRebellionManifest(options);
        var carryable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string group in new[] { "languageCases", "deterministicCases" })
            foreach (JsonElement item in manifest.GetProperty(group).EnumerateArray())
                if (item.TryGetProperty("passOnce", out JsonElement flag)
                    && flag.ValueKind == JsonValueKind.True)
                    carryable.Add(item.GetProperty("id").GetString() ?? string.Empty);
        destination.ContractPasses = destination.ContractPasses.Concat(source.ContractPasses)
            .Where(carryable.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        destination.Executions = destination.Executions.Concat(source.Executions
                .Where(execution => carryable.Contains(execution.CaseId)))
            .GroupBy(execution => RebellionExecutionInstance(execution),
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(execution => execution.StartedUtc,
                StringComparer.Ordinal).First()).ToList();
        destination.CarriedFromRunId = source.RunId;
        lock (RebellionStateGate) WriteRebellionState(options, destination);
        return new Dictionary<string, object?>
        {
            ["ok"] = true, ["schema"] = "reign-rebellion-carried-evidence-v1",
            ["carried"] = destination.ContractPasses, ["state"] = destination
        };
    }

    [McpServerTool(Name = "reign_start_rebellion_certification_case", ReadOnly = false,
        Destructive = true, Idempotent = false, UseStructuredContent = true)]
    [Description("Starts one manifest-owned Rebellion organic-language, native, save/load, transfer, reintegration, or cleanup case through the visible live bridge and exact guarded Current save.")]
    public static async Task<IReadOnlyDictionary<string, object?>> StartRebellionCertificationCase(
        ReignApiClient api, ReignMcpOptions options, CampaignTestService campaignTests,
        string campaignId, string campaignTestRunId, string runId, string caseId,
        [AllowedValues("run", "prepare", "verify")] string stage = "run",
        string targetSearch = "",
        [Description("Exact text required: start Reign rebellion certification case on disposable save")]
        string confirmation = "", CancellationToken cancellationToken = default)
    {
        if (!options.AllowVerificationControl)
            throw new InvalidOperationException("Verification control is disabled.");
        InputGuard.RequireConfirmation(confirmation,
            "start Reign rebellion certification case on disposable save");
        campaignId = RequiredIdentifier(campaignId, nameof(campaignId));
        campaignTestRunId = RequiredIdentifier(campaignTestRunId, nameof(campaignTestRunId));
        runId = RequiredIdentifier(runId, nameof(runId));
        caseId = RequiredIdentifier(caseId, nameof(caseId)).ToUpperInvariant();
        stage = (stage ?? string.Empty).Trim().ToLowerInvariant();
        if (stage is not ("run" or "prepare" or "verify"))
            throw new ArgumentException("Unsupported Rebellion certification stage.", nameof(stage));
        RebellionCertificationState state = ReadRebellionState(options, runId);
        if (!string.Equals(state.CampaignId, campaignId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(state.CampaignTestRunId, campaignTestRunId,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "The requested Rebellion case does not match its prepared campaign-test enrollment.");
        CampaignTestAuthorization authorization = await campaignTests.RequireArmedOwnedSaveAsync(
            campaignTestRunId, campaignId, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(state.DisposableSaveName, authorization.CurrentSaveName,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "The prepared Rebellion disposable save no longer matches campaign-test enrollment.");

        JsonElement testCase = FindRebellionCase(LoadRebellionManifest(options), caseId);
        string manifestProfile = testCase.GetProperty("profile").GetString() ?? string.Empty;
        bool staged = manifestProfile.Equals("travel_save_roundtrip", StringComparison.Ordinal);
        string profile = manifestProfile;
        if (staged)
        {
            if (stage == "run")
                throw new ArgumentException("Travel save-roundtrip requires prepare and verify stages.", nameof(stage));
            profile = stage == "prepare" ? "travel_prepare" : "travel_verify";
        }
        else if (stage != "run")
            throw new ArgumentException("Only travel save-roundtrip supports staged execution.", nameof(stage));

        bool language = manifestProfile == "language" || staged && stage == "prepare";
        var steps = new List<object>();
        if (staged && stage == "prepare")
        {
            targetSearch = InputGuard.BoundedText(targetSearch, nameof(targetSearch), 200);
            if (targetSearch.Length == 0)
                throw new ArgumentException("The native travel preparation requires a real target hero.", nameof(targetSearch));
            steps.Add(new { operation = "rebellion_test", profile = "native_setup",
                fixtureRunId = runId, disposableSaveName = authorization.CurrentSaveName,
                caseId, targetHeroId = targetSearch, timeoutSeconds = 300 });
            steps.Add(new { operation = "open", mode = "individual_chat",
                targetSearches = new[] { targetSearch }, presentation = "visible",
                effects = "full", timeoutSeconds = 600 });
            steps.Add(new { operation = "send", mode = "individual_chat",
                text = "I intend to rebel against our ruler. Before you give me a final answer, tell me what concerns would govern whether your clan supports me.",
                sceneIndex = 0, turnIndex = 0, naturalLanguageCase = true,
                timeoutSeconds = 1200 });
            steps.Add(new { operation = "send", mode = "individual_chat",
                text = "I hear your concern. I have no secure route, no committed allies I can prove to you, and I offer your clan no land, title, gold, or protection. I will continue preparing the rebellion regardless. Give me your final answer now: will your clan support my planned rebellion against our ruler, or refuse me?",
                sceneIndex = 0, turnIndex = 1, naturalLanguageCase = true,
                timeoutSeconds = 1200 });
            steps.Add(new { operation = "close", mode = "individual_chat", timeoutSeconds = 300 });
            steps.Add(new { operation = "rebellion_test", profile = "travel_prepare",
                fixtureRunId = runId, disposableSaveName = authorization.CurrentSaveName,
                caseId, expectedDecision = "report", expectedChannel = "individual_chat",
                timeoutSeconds = 300 });
        }
        else if (language)
        {
            targetSearch = InputGuard.BoundedText(targetSearch, nameof(targetSearch), 200);
            if (targetSearch.Length == 0)
                throw new ArgumentException("An organic Rebellion language case requires a real target hero.", nameof(targetSearch));
            string mode = testCase.GetProperty("channel").GetString() ?? "individual_chat";
            steps.Add(new { operation = "rebellion_test", profile = "native_setup",
                fixtureRunId = runId, disposableSaveName = authorization.CurrentSaveName,
                caseId, targetHeroId = targetSearch, timeoutSeconds = 300 });
            JsonElement[] turns = testCase.GetProperty("turns").EnumerateArray().ToArray();
            if (mode == "correspondence")
            {
                for (int turnIndex = 0; turnIndex < turns.Length; turnIndex++)
                {
                    steps.Add(new { operation = "open", mode,
                        targetSearches = new[] { targetSearch }, presentation = "visible",
                        effects = "full", timeoutSeconds = 600 });
                    steps.Add(new { operation = "send", mode,
                        text = turns[turnIndex].GetString() ?? string.Empty,
                        sceneIndex = 0, turnIndex, naturalLanguageCase = true,
                        timeoutSeconds = 1200 });
                    steps.Add(new { operation = "close", mode, timeoutSeconds = 300 });
                    steps.Add(new { operation = "world_advance", mode = "passive_world",
                        days = 6, timeoutSeconds = 7200 });
                    steps.Add(new { operation = "wait_for_correspondence",
                        stableMilliseconds = 25000, timeoutSeconds = 600 });
                }
            }
            else
            {
                steps.Add(new { operation = "open", mode,
                    targetSearches = new[] { targetSearch }, presentation = "visible",
                    effects = "full", timeoutSeconds = 600 });
                for (int turnIndex = 0; turnIndex < turns.Length; turnIndex++)
                    steps.Add(new { operation = "send", mode,
                        text = turns[turnIndex].GetString() ?? string.Empty,
                        sceneIndex = 0, turnIndex, naturalLanguageCase = true,
                        timeoutSeconds = 1200 });
                steps.Add(new { operation = "close", mode, timeoutSeconds = 300 });
            }
            steps.Add(new { operation = "rebellion_test", profile = "language_observe",
                fixtureRunId = runId, disposableSaveName = authorization.CurrentSaveName,
                caseId, expectedDecision = testCase.GetProperty("expectedDecision").GetString() ?? "",
                expectedChannel = mode, timeoutSeconds = 300 });
        }
        else
        {
            if (!RebellionProfiles.Contains(profile, StringComparer.Ordinal))
                throw new InvalidOperationException("The manifest maps this case to an unsupported profile.");
            steps.Add(new { operation = "rebellion_test", profile, fixtureRunId = runId,
                disposableSaveName = authorization.CurrentSaveName, caseId,
                timeoutSeconds = 600 });
        }
        string liveRunId = RebellionLiveRunId(runId, caseId, stage);
        ApiEnvelope started = await api.PostAsync("/tests/live/run/start", new
        {
            schemaVersion = 2, campaignId, runId = liveRunId,
            mode = "individual_chat",
            label = "Rebellion certification " + caseId + " " + stage,
            presentation = "visible", effects = "guarded", autoCompleteWhenIdle = true,
            enrollment = new
            {
                campaignId, campaignTestRunId, certificationRunId = runId,
                disposableSaveName = authorization.CurrentSaveName,
                protectedBaselineSaveName = authorization.BaselineSaveName,
                authorization.TimelineId, state.SourceFingerprint, state.ClientBuild,
                state.ServerBuild, state.ProviderConfigurationFingerprint,
                state.CatalogFingerprint, state.BannerlordVersion
            },
            caseId, stage, naturalLanguageRequired = language, steps
        }, cancellationToken).ConfigureAwait(false);
        state.Executions.Add(new RebellionCertificationExecution
        {
            CaseId = caseId, Stage = stage, Profile = profile, LiveRunId = liveRunId,
            NaturalLanguage = language, StartAccepted = started.Ok,
            StartedUtc = DateTime.UtcNow.ToString("O")
        });
        lock (RebellionStateGate) WriteRebellionState(options, state);
        return new Dictionary<string, object?>
        {
            ["ok"] = started.Ok, ["schema"] = "reign-rebellion-certification-start-v1",
            ["caseId"] = caseId, ["stage"] = stage, ["liveRunId"] = liveRunId,
            ["liveRun"] = started, ["state"] = state
        };
    }

    [McpServerTool(Name = "reign_evaluate_rebellion_release_readiness", ReadOnly = true,
        Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Evaluates strict Rebellion release readiness from immutable fingerprints, pass-once contracts, and durable organic/native/save-load/cleanup reports.")]
    public static async Task<IReadOnlyDictionary<string, object?>> EvaluateRebellionReleaseReadiness(
        ReignApiClient api, ReignMcpOptions options, CampaignTestService campaignTests,
        string campaignId, string campaignTestRunId, string runId,
        string sourceFingerprint, string clientBuild, string serverBuild,
        string providerConfigurationFingerprint, string catalogFingerprint,
        string bannerlordVersion = "v1.4.8", CancellationToken cancellationToken = default)
    {
        campaignId = RequiredIdentifier(campaignId, nameof(campaignId));
        campaignTestRunId = RequiredIdentifier(campaignTestRunId, nameof(campaignTestRunId));
        runId = RequiredIdentifier(runId, nameof(runId));
        CampaignTestAuthorization authorization = campaignTests.RequireEnrollment(
            campaignTestRunId, campaignId);
        RebellionCertificationState state = ReadRebellionState(options, runId);
        var current = new RebellionCertificationState
        {
            CampaignId = campaignId, CampaignTestRunId = campaignTestRunId,
            ProtectedBaselineSaveName = authorization.BaselineSaveName,
            DisposableSaveName = authorization.CurrentSaveName,
            SourceFingerprint = sourceFingerprint, ClientBuild = clientBuild,
            ServerBuild = serverBuild,
            ProviderConfigurationFingerprint = providerConfigurationFingerprint,
            CatalogFingerprint = catalogFingerprint, BannerlordVersion = bannerlordVersion
        };
        var passed = state.ContractPasses.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var reports = new List<object?>();
        foreach (RebellionCertificationExecution execution in state.Executions.AsEnumerable().Reverse())
        {
            string instance = RebellionExecutionInstance(execution);
            if (passed.Contains(instance)) continue;
            ApiEnvelope report = await api.GetAsync("/tests/live/run/report",
                new Dictionary<string, string?> { ["campaignId"] = campaignId,
                    ["runId"] = execution.LiveRunId }, cancellationToken).ConfigureAwait(false);
            bool completed = report.Ok && report.Data.HasValue
                && string.Equals(FindRebellionString(report.Data.Value, "status"),
                    "completed", StringComparison.OrdinalIgnoreCase);
            bool noBypass = !execution.NaturalLanguage || report.Data.HasValue
                && !RebellionReportContainsDirectBypass(report.Data.Value);
            bool evidenceBound = report.Data.HasValue
                && RebellionReportMatchesPreparedEvidence(report.Data.Value, state,
                    execution.LiveRunId);
            bool casePassed = execution.StartAccepted && completed && noBypass && evidenceBound;
            if (casePassed) passed.Add(instance);
            reports.Add(new Dictionary<string, object?>
            {
                ["instance"] = instance, ["liveRunId"] = execution.LiveRunId,
                ["completed"] = completed, ["noDirectDecisionBypass"] = noBypass,
                ["preparedEvidenceMatched"] = evidenceBound,
                ["passed"] = casePassed, ["report"] = report
            });
        }
        string[] missing = state.RequiredInstances.Where(instance =>
            instance.Equals("RB-NATIVE-001", StringComparison.OrdinalIgnoreCase)
                ? !passed.Contains(instance + "::prepare") || !passed.Contains(instance + "::verify")
                : !passed.Contains(instance)).ToArray();
        var blockers = new List<string>();
        if (!RebellionFingerprintsMatch(state, current))
            blockers.Add("Prepared enrollment or final fingerprints do not match.");
        if (missing.Length > 0)
            blockers.Add("Missing or failed required cases: " + string.Join(", ", missing));
        return new Dictionary<string, object?>
        {
            ["ok"] = true, ["schema"] = "reign-rebellion-release-readiness-v1",
            ["readyToRelease"] = blockers.Count == 0,
            ["requiredInstanceCount"] = state.RequiredInstances.Count,
            ["passedInstanceCount"] = state.RequiredInstances.Count - missing.Length,
            ["blockers"] = blockers, ["missingInstances"] = missing,
            ["reports"] = reports, ["state"] = state
        };
    }

    [McpServerTool(Name = "reign_start_rebellion_test", ReadOnly = false,
        Destructive = false, Idempotent = false, UseStructuredContent = true)]
    [Description("Starts a legacy deterministic Rebellion profile through the armed live-game bridge. Prefer prepared certification tools for release evidence.")]
    public static Task<ApiEnvelope> StartRebellionTest(ReignApiClient api, ReignMcpOptions options,
        string campaignId,
        [AllowedValues("smoke", "feature", "pledges", "reporting", "summons", "verdicts",
            "declaration", "save_prepare", "save_verify", "resolution", "cleanup")]
        string profile = "feature", string fixtureRunId = "rebellion_release_matrix",
        string disposableSaveName = "",
        [Description("Exact text required: start Reign rebellion test")] string confirmation = "",
        CancellationToken cancellationToken = default)
    {
        if (!options.AllowVerificationControl)
            throw new InvalidOperationException("Verification control is disabled.");
        InputGuard.RequireConfirmation(confirmation, "start Reign rebellion test");
        campaignId = RequiredIdentifier(campaignId, nameof(campaignId));
        fixtureRunId = RequiredIdentifier(fixtureRunId, nameof(fixtureRunId));
        profile = (profile ?? string.Empty).Trim().ToLowerInvariant();
        if (!RebellionProfiles.Contains(profile, StringComparer.Ordinal)
            || profile is "native_setup" or "language_observe" or "mixed_transfer"
                or "foreign_reintegration")
            throw new ArgumentException("Unsupported legacy rebellion profile.", nameof(profile));
        if (profile is not ("smoke" or "feature"))
            disposableSaveName = RequiredRebellionSaveName(disposableSaveName);
        var steps = new List<object> { new { operation = "rebellion_test", profile, fixtureRunId,
            disposableSaveName, timeoutSeconds = 300 } };
        if (profile is "save_prepare" or "cleanup")
            steps.Add(new { operation = "save_checkpoint", saveName = disposableSaveName,
                timeoutSeconds = 300 });
        return api.PostAsync("/tests/live/run/start", new { schemaVersion = 2, campaignId,
            mode = "individual_chat", label = "MCP rebellion " + profile,
            presentation = "visible", effects = "guarded", autoCompleteWhenIdle = true, steps },
            cancellationToken);
    }

    private static string RebellionManifestPath(ReignMcpOptions options)
    {
        string path = Path.GetFullPath(Path.Combine(options.WorkspaceRoot, "ReignServer", "tests", "ReignLiveTest", "Features", "WorldSimulation", "rebellion-certification-manifest.json"));
        ReignMcpOptions.EnsureWithin(options.WorkspaceRoot, path, "rebellionManifest");
        return path;
    }

    private static JsonElement LoadRebellionManifest(ReignMcpOptions options)
    {
        using JsonDocument document = JsonDocument.Parse(
            File.ReadAllText(RebellionManifestPath(options), Encoding.UTF8));
        return document.RootElement.Clone();
    }

    private static IEnumerable<string> RebellionInstances(JsonElement manifest)
    {
        foreach (string group in new[] { "languageCases", "deterministicCases", "nativeCases" })
            foreach (JsonElement item in manifest.GetProperty(group).EnumerateArray())
                yield return item.GetProperty("id").GetString() ?? string.Empty;
    }

    private static JsonElement FindRebellionCase(JsonElement manifest, string caseId)
    {
        foreach (string group in new[] { "languageCases", "deterministicCases", "nativeCases" })
            foreach (JsonElement item in manifest.GetProperty(group).EnumerateArray())
                if (string.Equals(item.GetProperty("id").GetString(), caseId,
                        StringComparison.OrdinalIgnoreCase)) return item.Clone();
        throw new ArgumentException("Unknown Rebellion certification case id.", nameof(caseId));
    }

    private static string RequiredRebellionFingerprint(string value, string name)
    {
        value = InputGuard.BoundedText(value, name, 300);
        if (value.Length == 0 || value.Equals("unknown", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(name + " must be a known immutable fingerprint.", name);
        return value;
    }

    private static string RequiredRebellionSaveName(string value)
    {
        value = InputGuard.BoundedText(value, "disposableSaveName", 120);
        if (!value.StartsWith("Reign_", StringComparison.OrdinalIgnoreCase)
            && !value.StartsWith("ReignTest_", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A namespaced disposable save is required.",
                "disposableSaveName");
        return value;
    }

    private static string RebellionLiveRunId(string runId, string caseId, string stage)
    {
        string compactRun = new string(runId.Where(char.IsAsciiLetterOrDigit).Take(24).ToArray());
        string compactCase = new string((caseId + "-" + stage)
            .Where(character => char.IsAsciiLetterOrDigit(character) || character == '-')
            .Take(40).ToArray()).Trim('-');
        return (compactRun + "-" + compactCase + "-" + Guid.NewGuid().ToString("N")[..8])
            .ToLowerInvariant();
    }

    private static string RebellionExecutionInstance(RebellionCertificationExecution execution) =>
        execution.CaseId.Equals("RB-NATIVE-001", StringComparison.OrdinalIgnoreCase)
            ? execution.CaseId + "::" + execution.Stage : execution.CaseId;

    private static string RebellionStatePath(ReignMcpOptions options, string runId)
    {
        string root = Path.Combine(options.WorkspaceRoot, ".codex-live-artifacts", "rebellion");
        string path = Path.Combine(root, RequiredIdentifier(runId, nameof(runId)) + ".json");
        ReignMcpOptions.EnsureWithin(root, path, nameof(runId));
        return path;
    }

    private static RebellionCertificationState ReadRebellionState(
        ReignMcpOptions options, string runId) => TryReadRebellionState(options, runId)
        ?? throw new InvalidOperationException("Unknown Rebellion certification run id.");

    private static RebellionCertificationState? TryReadRebellionState(
        ReignMcpOptions options, string runId)
    {
        string path = RebellionStatePath(options, runId);
        return File.Exists(path) ? JsonSerializer.Deserialize<RebellionCertificationState>(
            File.ReadAllText(path, Encoding.UTF8), RebellionJson) : null;
    }

    private static void WriteRebellionState(ReignMcpOptions options,
        RebellionCertificationState state)
    {
        state.UpdatedUtc = DateTime.UtcNow.ToString("O");
        string path = RebellionStatePath(options, state.RunId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(state, RebellionJson),
            new UTF8Encoding(false));
        File.Move(temporary, path, true);
    }

    private static bool RebellionImmutableInputsMatch(RebellionCertificationState left,
        RebellionCertificationState right) => RebellionFingerprintsMatch(left, right)
        && string.Equals(left.TimelineId, right.TimelineId, StringComparison.OrdinalIgnoreCase);

    private static bool RebellionFingerprintsMatch(RebellionCertificationState left,
        RebellionCertificationState right) =>
        string.Equals(left.CampaignId, right.CampaignId, StringComparison.OrdinalIgnoreCase)
        && string.Equals(left.CampaignTestRunId, right.CampaignTestRunId, StringComparison.OrdinalIgnoreCase)
        && string.Equals(left.ProtectedBaselineSaveName, right.ProtectedBaselineSaveName, StringComparison.OrdinalIgnoreCase)
        && string.Equals(left.DisposableSaveName, right.DisposableSaveName, StringComparison.OrdinalIgnoreCase)
        && string.Equals(left.SourceFingerprint, right.SourceFingerprint, StringComparison.Ordinal)
        && string.Equals(left.ClientBuild, right.ClientBuild, StringComparison.Ordinal)
        && string.Equals(left.ServerBuild, right.ServerBuild, StringComparison.Ordinal)
        && string.Equals(left.ProviderConfigurationFingerprint, right.ProviderConfigurationFingerprint, StringComparison.Ordinal)
        && string.Equals(left.CatalogFingerprint, right.CatalogFingerprint, StringComparison.Ordinal)
        && string.Equals(left.BannerlordVersion, right.BannerlordVersion, StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyDictionary<string, object?> RebellionStateEnvelope(
        RebellionCertificationState state, string status, JsonElement manifest) =>
        new Dictionary<string, object?>
        {
            ["ok"] = true, ["schema"] = "reign-rebellion-certification-state-v1",
            ["status"] = status, ["state"] = state,
            ["manifest"] = JsonSerializer.Deserialize<object>(manifest.GetRawText())
        };

    private static string FindRebellionString(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object)
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (property.NameEquals(name) && property.Value.ValueKind == JsonValueKind.String)
                    return property.Value.GetString() ?? string.Empty;
                string nested = FindRebellionString(property.Value, name);
                if (nested.Length > 0) return nested;
            }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (JsonElement item in element.EnumerateArray())
            {
                string nested = FindRebellionString(item, name);
                if (nested.Length > 0) return nested;
            }
        return string.Empty;
    }

    private static bool FindRebellionBool(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object)
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (property.NameEquals(name)) return property.Value.ValueKind == JsonValueKind.True;
                if (FindRebellionBool(property.Value, name)) return true;
            }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (JsonElement item in element.EnumerateArray())
                if (FindRebellionBool(item, name)) return true;
        return false;
    }

    private static int FindRebellionInt(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object)
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (property.NameEquals(name) && property.Value.TryGetInt32(out int value)) return value;
                int nested = FindRebellionInt(property.Value, name);
                if (nested >= 0) return nested;
            }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (JsonElement item in element.EnumerateArray())
            {
                int nested = FindRebellionInt(item, name);
                if (nested >= 0) return nested;
            }
        return -1;
    }

    private static bool RebellionReportContainsDirectBypass(JsonElement element)
    {
        string raw = element.GetRawText();
        return raw.Contains("operation\":\"action", StringComparison.OrdinalIgnoreCase)
            && (raw.Contains("resolve_rebellion_pledge", StringComparison.OrdinalIgnoreCase)
                || raw.Contains("resolve_rebellion_summons", StringComparison.OrdinalIgnoreCase));
    }

    private static bool RebellionReportMatchesPreparedEvidence(JsonElement report,
        RebellionCertificationState state, string liveRunId) =>
        RebellionReportHasExactString(report, liveRunId)
        && RebellionReportHasExactString(report, state.CampaignId)
        && RebellionReportHasExactString(report, state.CampaignTestRunId)
        && RebellionReportHasExactString(report, state.DisposableSaveName)
        && RebellionReportHasExactString(report, state.SourceFingerprint)
        && RebellionReportHasExactString(report, state.ClientBuild)
        && RebellionReportHasExactString(report, state.ServerBuild)
        && RebellionReportHasExactString(report, state.ProviderConfigurationFingerprint)
        && RebellionReportHasExactString(report, state.CatalogFingerprint)
        && RebellionReportHasExactString(report, state.BannerlordVersion);

    private static bool RebellionReportHasExactString(JsonElement element, string value)
    {
        if (element.ValueKind == JsonValueKind.String)
            return string.Equals(element.GetString(), value, StringComparison.OrdinalIgnoreCase);
        if (element.ValueKind == JsonValueKind.Object)
            return element.EnumerateObject().Any(property =>
                RebellionReportHasExactString(property.Value, value));
        return element.ValueKind == JsonValueKind.Array
            && element.EnumerateArray().Any(item => RebellionReportHasExactString(item, value));
    }
}

public sealed record RebellionCertificationState
{
    public string Schema { get; set; } = "reign-rebellion-certification-state-v1";
    public string RunId { get; set; } = "";
    public string CampaignId { get; set; } = "";
    public string CampaignTestRunId { get; set; } = "";
    public string TimelineId { get; set; } = "";
    public string ProtectedBaselineSaveName { get; set; } = "";
    public string DisposableSaveName { get; set; } = "";
    public string DisposableSavePrefix { get; set; } = "";
    public string SourceFingerprint { get; set; } = "";
    public string ClientBuild { get; set; } = "";
    public string ServerBuild { get; set; } = "";
    public string ProviderConfigurationFingerprint { get; set; } = "";
    public string CatalogFingerprint { get; set; } = "";
    public string BannerlordVersion { get; set; } = "";
    public string PreparedUtc { get; set; } = "";
    public string UpdatedUtc { get; set; } = "";
    public string ContractReportUtc { get; set; } = "";
    public string CarriedFromRunId { get; set; } = "";
    public List<string> RequiredInstances { get; set; } = [];
    public List<string> ContractPasses { get; set; } = [];
    public List<RebellionCertificationExecution> Executions { get; set; } = [];
}

public sealed record RebellionCertificationExecution
{
    public string CaseId { get; set; } = "";
    public string Stage { get; set; } = "";
    public string Profile { get; set; } = "";
    public string LiveRunId { get; set; } = "";
    public bool NaturalLanguage { get; set; }
    public bool StartAccepted { get; set; }
    public string StartedUtc { get; set; } = "";
}
