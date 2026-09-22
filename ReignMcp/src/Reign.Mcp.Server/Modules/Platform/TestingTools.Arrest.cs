using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Reign.Mcp.Server;

public static partial class TestingTools
{
    private static readonly object ArrestCertificationStateGate = new();
    private static readonly JsonSerializerOptions ArrestCertificationJson =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private static readonly string[] ArrestDeterministicProfiles =
    {
        "smoke", "feature", "save_prepare", "save_verify", "relationship",
        "evidence", "reputation", "town", "castle", "player_party", "surrender",
        "escape", "duel", "battle", "sovereignty", "evaluation", "cleanup"
    };

    [McpServerTool(Name = "reign_get_arrest_test_manifest", ReadOnly = true,
        Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Returns the durable deterministic and organic conversation-arrest test profiles, isolation rules, and release coverage without starting a run.")]
    public static IReadOnlyDictionary<string, object?> GetArrestTestManifest(
        ReignMcpOptions options)
    {
        string path = ArrestManifestPath(options);
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
        return new Dictionary<string, object?>
        {
            ["ok"] = true,
            ["schema"] = "reign-arrest-equivalence-manifest-v2",
            ["schemaVersion"] = 2,
            ["manifestPath"] = path,
            ["cliEntryPoint"] = "ReignLiveTest.exe arrest --profile <profile>",
            ["deterministicProfiles"] = ArrestDeterministicProfiles,
            ["organicProfiles"] = new[] { "town_guard", "castle_guard", "player_party",
                "surrender_duel", "party_battle", "evidence_agent", "rescind_release", "full" },
            ["requires"] = new[] { "visible Reign server", "armed exactly-once live bridge",
                "loaded aligned campaign", "exact named disposable save for destructive or organic profiles" },
            ["isolation"] = new[] { "hooks are unreachable unless the live bridge is explicitly armed",
                "forced cases use an arrest_test_<fixture> namespace", "cleanup removes only exact namespaced fixtures",
                "organic actions retain native diplomacy, crime, battle, captivity, and save behavior" },
            ["coverage"] = new[] { "phrase corpus", "two-turn confirmation", "cause and hidden evidence",
                "all severity relationship cells", "unique accusation reputations", "rescission and release",
                "town and castle dungeons", "player-party custody", "surrender and refusal",
                "nonlethal duel capture/escape", "native party battle/capture failure", "protected and normal escape",
                "ownership and sovereignty changes", "save/load every phase", "history and memory", "cleanup" },
            ["artifactFields"] = new[] { "scenario", "case and evidence ids", "save identifier",
                "deployed fingerprint and hashes", "pre/post custody and relationship snapshots",
                "reputation state", "action receipts", "logs", "screenshots", "durations", "result", "cleanup status" },
            ["manifest"] = JsonSerializer.Deserialize<object>(document.RootElement.GetRawText())
        };
    }

    [McpServerTool(Name = "reign_run_arrest_contract_tests", ReadOnly = true,
        Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Runs the server-authoritative Arrest equivalence contract: the bounded 36-utterance language corpus, evidence authority, policy correction, idempotency, reclassification, and rescission rules. It does not start Bannerlord or mutate a campaign.")]
    public static Task<ApiEnvelope> RunArrestContractTests(
        ReignApiClient api,
        CancellationToken cancellationToken = default) =>
        api.PostAsync("/arrests/test/contracts", new { }, cancellationToken);

    [McpServerTool(Name = "reign_prepare_arrest_certification", ReadOnly = false,
        Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Binds Arrest certification to an existing guarded campaign-test enrollment and immutable source, build, provider, catalog, and Bannerlord fingerprints. Preparation does not start Bannerlord or mutate the campaign.")]
    public static IReadOnlyDictionary<string, object?> PrepareArrestCertification(
        ReignMcpOptions options,
        CampaignTestService campaignTests,
        string campaignId,
        string campaignTestRunId,
        string runId,
        string sourceFingerprint,
        string clientBuild,
        string serverBuild,
        string providerConfigurationFingerprint,
        string catalogFingerprint,
        string bannerlordVersion = "v1.4.8")
    {
        campaignId = RequiredIdentifier(campaignId, nameof(campaignId));
        campaignTestRunId = RequiredIdentifier(campaignTestRunId, nameof(campaignTestRunId));
        runId = RequiredIdentifier(runId, nameof(runId));
        CampaignTestAuthorization authorization = campaignTests.RequireEnrollment(
            campaignTestRunId, campaignId);
        JsonElement manifest = LoadArrestManifest(options);
        var requested = new ArrestCertificationState
        {
            RunId = runId,
            CampaignId = campaignId,
            CampaignTestRunId = campaignTestRunId,
            TimelineId = authorization.TimelineId,
            ProtectedBaselineSaveName = authorization.BaselineSaveName,
            DisposableSaveName = authorization.CurrentSaveName,
            DisposableSavePrefix = authorization.SavePrefix,
            SourceFingerprint = RequiredArrestFingerprint(sourceFingerprint, nameof(sourceFingerprint)),
            ClientBuild = RequiredArrestFingerprint(clientBuild, nameof(clientBuild)),
            ServerBuild = RequiredArrestFingerprint(serverBuild, nameof(serverBuild)),
            ProviderConfigurationFingerprint = RequiredArrestFingerprint(
                providerConfigurationFingerprint, nameof(providerConfigurationFingerprint)),
            CatalogFingerprint = RequiredArrestFingerprint(catalogFingerprint, nameof(catalogFingerprint)),
            BannerlordVersion = RequiredArrestFingerprint(bannerlordVersion, nameof(bannerlordVersion)),
            RequiredInstances = RequiredArrestInstances(manifest).ToList(),
            PreparedUtc = DateTime.UtcNow.ToString("O"),
            UpdatedUtc = DateTime.UtcNow.ToString("O")
        };
        lock (ArrestCertificationStateGate)
        {
            ArrestCertificationState? existing = TryReadArrestCertificationState(options, runId);
            if (existing is not null)
            {
                if (!ArrestImmutableInputsMatch(existing, requested))
                    throw new InvalidOperationException(
                        "The Arrest run id is already prepared with different immutable enrollment or fingerprint inputs.");
                return ArrestStateEnvelope(existing, "already_prepared", manifest);
            }
            WriteArrestCertificationState(options, requested);
        }
        return ArrestStateEnvelope(requested, "prepared", manifest);
    }

    [McpServerTool(Name = "reign_run_arrest_certification_contract", ReadOnly = false,
        Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Runs and records the fingerprint-bound 36-utterance Arrest language contract plus compatible pass-once deterministic policy cases. It does not start Bannerlord or mutate a campaign.")]
    public static async Task<IReadOnlyDictionary<string, object?>> RunArrestCertificationContract(
        ReignApiClient api,
        ReignMcpOptions options,
        string runId,
        CancellationToken cancellationToken = default)
    {
        runId = RequiredIdentifier(runId, nameof(runId));
        ArrestCertificationState state = ReadArrestCertificationState(options, runId);
        ApiEnvelope report = await RunArrestContractTests(api, cancellationToken)
            .ConfigureAwait(false);
        bool passed = report.Ok && report.Data.HasValue
            && FindArrestBoolean(report.Data.Value, "ok")
            && FindArrestInteger(report.Data.Value, "languageCaseCount") == 36;
        if (passed)
        {
            JsonElement manifest = LoadArrestManifest(options);
            var passOnce = manifest.GetProperty("languageCases").EnumerateArray()
                .Select(item => item.GetProperty("id").GetString() ?? string.Empty)
                .Concat(manifest.GetProperty("deterministicCases").EnumerateArray()
                    .Where(item => item.TryGetProperty("passOnce", out JsonElement flag)
                        && flag.ValueKind == JsonValueKind.True)
                    .Select(item => item.GetProperty("id").GetString() ?? string.Empty));
            foreach (string instance in passOnce.Where(item => item.Length > 0))
                state.ContractPasses.Add(instance);
            state.ContractPasses = state.ContractPasses.Distinct(
                StringComparer.OrdinalIgnoreCase).ToList();
            state.ContractReportUtc = DateTime.UtcNow.ToString("O");
            lock (ArrestCertificationStateGate)
                WriteArrestCertificationState(options, state);
        }
        return new Dictionary<string, object?>
        {
            ["ok"] = passed,
            ["schema"] = "reign-arrest-certification-contract-v1",
            ["recordedPasses"] = passed ? state.ContractPasses : [],
            ["report"] = report,
            ["state"] = state
        };
    }

    [McpServerTool(Name = "reign_carry_forward_arrest_passes", ReadOnly = false,
        Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Carries only fingerprint-compatible Arrest language and deterministic pass-once evidence between prepared runs. Native, cleanup, and save/reload evidence is never carried.")]
    public static IReadOnlyDictionary<string, object?> CarryForwardArrestPasses(
        ReignMcpOptions options,
        string sourceRunId,
        string destinationRunId,
        [Description("Audited compatibility scope. arrest_sovereignty_fixture_only covers only the client sovereignty-migration behavior and its native fixture, plus the MCP/catalog carry audit; it never carries native evidence.")]
        [AllowedValues("identical_fingerprints", "campaign_checkpoint_restore_harness_only",
            "arrest_sovereignty_fixture_only")]
        string changeScope = "identical_fingerprints",
        [Description("Exact text required: carry forward compatible Reign Arrest passes")]
        string confirmation = "")
    {
        InputGuard.RequireConfirmation(confirmation,
            "carry forward compatible Reign Arrest passes");
        ArrestCertificationState source = ReadArrestCertificationState(options,
            RequiredIdentifier(sourceRunId, nameof(sourceRunId)));
        ArrestCertificationState destination = ReadArrestCertificationState(options,
            RequiredIdentifier(destinationRunId, nameof(destinationRunId)));
        changeScope = (changeScope ?? string.Empty).Trim().ToLowerInvariant();
        bool compatible = changeScope == "identical_fingerprints"
            ? ArrestFingerprintsMatch(source, destination)
            : changeScope == "campaign_checkpoint_restore_harness_only"
                ? ArrestCheckpointRestoreCarryInputsMatch(source, destination)
                : changeScope == "arrest_sovereignty_fixture_only"
                    && ArrestSovereigntyMigrationCarryInputsMatch(source, destination);
        if (!compatible)
            throw new InvalidOperationException(
                "Arrest pass-once evidence cannot cross enrollment or fingerprint changes.");
        destination.ContractPasses = destination.ContractPasses
            .Concat(source.ContractPasses).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        destination.CarriedFromRunId = source.RunId;
        destination.CarryForwardScope = changeScope;
        lock (ArrestCertificationStateGate)
            WriteArrestCertificationState(options, destination);
        return new Dictionary<string, object?>
        {
            ["ok"] = true,
            ["schema"] = "reign-arrest-carried-evidence-v1",
            ["changeScope"] = changeScope,
            ["carried"] = destination.ContractPasses,
            ["state"] = destination
        };
    }

    [McpServerTool(Name = "reign_start_arrest_certification_case", ReadOnly = false,
        Destructive = true, Idempotent = false, UseStructuredContent = true)]
    [Description("Starts one manifest-owned Arrest cleanup or native case through the visible live bridge, deriving the exact disposable Current save from its guarded campaign-test enrollment.")]
    public static async Task<IReadOnlyDictionary<string, object?>> StartArrestCertificationCase(
        ReignApiClient api,
        ReignMcpOptions options,
        CampaignTestService campaignTests,
        string campaignId,
        string campaignTestRunId,
        string runId,
        string caseId,
        [AllowedValues("run", "prepare", "verify")] string stage = "run",
        string targetHeroId = "",
        [Description("Exact text required: start Reign arrest certification case on disposable save")]
        string confirmation = "",
        CancellationToken cancellationToken = default)
    {
        RequireArrestVerificationControl(options);
        InputGuard.RequireConfirmation(confirmation,
            "start Reign arrest certification case on disposable save");
        campaignId = RequiredIdentifier(campaignId, nameof(campaignId));
        campaignTestRunId = RequiredIdentifier(campaignTestRunId, nameof(campaignTestRunId));
        runId = RequiredIdentifier(runId, nameof(runId));
        caseId = RequiredIdentifier(caseId, nameof(caseId)).ToUpperInvariant();
        stage = (stage ?? string.Empty).Trim().ToLowerInvariant();
        if (stage is not ("run" or "prepare" or "verify"))
            throw new ArgumentException("Unsupported Arrest certification stage.", nameof(stage));
        ArrestCertificationState state = ReadArrestCertificationState(options, runId);
        if (!string.Equals(state.CampaignId, campaignId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(state.CampaignTestRunId, campaignTestRunId,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "The requested Arrest case does not match its prepared guarded campaign-test enrollment.");
        CampaignTestAuthorization authorization = await campaignTests.RequireArmedOwnedSaveAsync(
            campaignTestRunId, campaignId, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(state.DisposableSaveName, authorization.CurrentSaveName,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "The prepared Arrest disposable save no longer matches campaign-test enrollment.");
        JsonElement testCase = FindArrestCase(LoadArrestManifest(options), caseId);
        string profile = testCase.GetProperty("profile").GetString() ?? string.Empty;
        bool saveRoundtrip = profile.Equals("save_roundtrip", StringComparison.Ordinal);
        if (saveRoundtrip)
        {
            if (stage == "run")
                throw new ArgumentException("Save-roundtrip requires prepare and verify stages.", nameof(stage));
            profile = stage == "prepare" ? "save_prepare" : "save_verify";
        }
        else if (stage != "run")
            throw new ArgumentException("Only the save-roundtrip case supports staged execution.", nameof(stage));
        if (!ArrestDeterministicProfiles.Contains(profile, StringComparer.Ordinal))
            throw new InvalidOperationException("The manifest maps this case to an unsupported profile.");
        string liveRunId = ArrestLiveRunId(runId, caseId, stage);
        var steps = new List<object>
        {
            new { operation = "arrest_test", profile,
                fixtureRunId = runId, targetHeroId = InputGuard.OptionalIdentifier(
                    targetHeroId, nameof(targetHeroId)),
                disposableSaveName = authorization.CurrentSaveName,
                requireDisposableSave = true, timeoutSeconds = 600 }
        };
        ApiEnvelope started = await api.PostAsync("/tests/live/run/start", new
        {
            schemaVersion = 2, campaignId, runId = liveRunId,
            mode = "individual_chat", label = "Arrest certification " + caseId + " " + stage,
            presentation = "visible", effects = "guarded", autoCompleteWhenIdle = true,
            steps
        }, cancellationToken).ConfigureAwait(false);
        state.Executions.Add(new ArrestCertificationExecution
        {
            CaseId = caseId, Stage = stage, Profile = profile, LiveRunId = liveRunId,
            StartAccepted = started.Ok, StartedUtc = DateTime.UtcNow.ToString("O")
        });
        lock (ArrestCertificationStateGate)
            WriteArrestCertificationState(options, state);
        return new Dictionary<string, object?>
        {
            ["ok"] = started.Ok,
            ["schema"] = "reign-arrest-certification-start-v1",
            ["caseId"] = caseId,
            ["stage"] = stage,
            ["liveRunId"] = liveRunId,
            ["liveRun"] = started,
            ["state"] = state
        };
    }

    [McpServerTool(Name = "reign_evaluate_arrest_release_readiness", ReadOnly = true,
        Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Evaluates Arrest release readiness from prepared fingerprints, compatible pass-once contract evidence, and durable cleanup/native live reports. A real MapEvent receipt is mandatory for native battle proof.")]
    public static async Task<IReadOnlyDictionary<string, object?>> EvaluateArrestReleaseReadiness(
        ReignApiClient api,
        ReignMcpOptions options,
        CampaignTestService campaignTests,
        string campaignId,
        string campaignTestRunId,
        string runId,
        string sourceFingerprint,
        string clientBuild,
        string serverBuild,
        string providerConfigurationFingerprint,
        string catalogFingerprint,
        string bannerlordVersion = "v1.4.8",
        CancellationToken cancellationToken = default)
    {
        campaignId = RequiredIdentifier(campaignId, nameof(campaignId));
        campaignTestRunId = RequiredIdentifier(campaignTestRunId, nameof(campaignTestRunId));
        runId = RequiredIdentifier(runId, nameof(runId));
        CampaignTestAuthorization authorization = campaignTests.RequireEnrollment(
            campaignTestRunId, campaignId);
        ArrestCertificationState state = ReadArrestCertificationState(options, runId);
        var current = new ArrestCertificationState
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
        foreach (ArrestCertificationExecution execution in state.Executions.AsEnumerable().Reverse())
        {
            string instance = ArrestExecutionInstance(execution);
            if (passed.Contains(instance)) continue;
            ApiEnvelope report = await api.GetAsync("/tests/live/run/report",
                new Dictionary<string, string?>
                {
                    ["campaignId"] = campaignId,
                    ["runId"] = execution.LiveRunId
                }, cancellationToken).ConfigureAwait(false);
            bool completed = report.Ok && report.Data.HasValue
                && string.Equals(FindArrestString(report.Data.Value, "status"),
                    "completed", StringComparison.OrdinalIgnoreCase);
            bool actualMapBattle = !execution.CaseId.Equals("AR-NATIVE-006",
                StringComparison.OrdinalIgnoreCase) || report.Data.HasValue
                && FindArrestBoolean(report.Data.Value, "actualMapBattleProven");
            bool casePassed = execution.StartAccepted && completed && actualMapBattle;
            if (casePassed) passed.Add(instance);
            reports.Add(new Dictionary<string, object?>
            {
                ["instance"] = instance, ["liveRunId"] = execution.LiveRunId,
                ["completed"] = completed, ["actualMapBattleProven"] = actualMapBattle,
                ["passed"] = casePassed, ["report"] = report
            });
        }
        string[] missing = state.RequiredInstances.Where(instance =>
        {
            if (instance.Equals("AR-NATIVE-009", StringComparison.OrdinalIgnoreCase))
                return !passed.Contains(instance + "::prepare")
                    || !passed.Contains(instance + "::verify");
            return !passed.Contains(instance);
        }).ToArray();
        var blockers = new List<string>();
        if (!ArrestFingerprintsMatch(state, current))
            blockers.Add("Prepared enrollment or final fingerprints do not match.");
        if (missing.Length > 0)
            blockers.Add("Missing or failed required cases: " + string.Join(", ", missing));
        return new Dictionary<string, object?>
        {
            ["ok"] = true,
            ["schema"] = "reign-arrest-release-readiness-v1",
            ["readyToRelease"] = blockers.Count == 0,
            ["requiredInstanceCount"] = state.RequiredInstances.Count,
            ["passedInstanceCount"] = state.RequiredInstances.Count - missing.Length,
            ["blockers"] = blockers,
            ["missingInstances"] = missing,
            ["reports"] = reports,
            ["state"] = state
        };
    }

    [McpServerTool(Name = "reign_start_arrest_test", ReadOnly = false,
        Destructive = false, Idempotent = false, UseStructuredContent = true)]
    [Description("Starts a deterministic arrest profile through the armed live-game bridge. save_prepare and cleanup require the exact loaded disposable save name.")]
    public static Task<ApiEnvelope> StartArrestTest(
        ReignApiClient api,
        ReignMcpOptions options,
        string campaignId,
        [AllowedValues("smoke", "feature", "save_prepare", "save_verify", "relationship",
            "evidence", "reputation", "town", "castle", "player_party", "surrender",
            "escape", "duel", "battle", "sovereignty", "evaluation", "cleanup")]
        string profile = "feature",
        string fixtureRunId = "arrest_release_matrix",
        string targetHeroId = "",
        string disposableSaveName = "",
        [Description("Exact text required: start Reign arrest test")]
        string confirmation = "",
        CancellationToken cancellationToken = default)
    {
        RequireArrestVerificationControl(options);
        InputGuard.RequireConfirmation(confirmation, "start Reign arrest test");
        campaignId = RequiredIdentifier(campaignId, nameof(campaignId));
        fixtureRunId = RequiredIdentifier(fixtureRunId, nameof(fixtureRunId));
        targetHeroId = InputGuard.OptionalIdentifier(targetHeroId, nameof(targetHeroId));
        profile = (profile ?? string.Empty).Trim().ToLowerInvariant();
        if (!ArrestDeterministicProfiles.Contains(profile, StringComparer.Ordinal))
            throw new ArgumentException("Unsupported arrest profile.", nameof(profile));
        bool requiresDisposableSave = profile is "save_prepare" or "cleanup" or "town"
            or "castle" or "player_party" or "surrender" or "escape" or "duel"
            or "battle" or "sovereignty";
        if (requiresDisposableSave)
            disposableSaveName = RequiredArrestSaveName(disposableSaveName);
        var steps = new List<object>
        {
            new { operation = "arrest_test", profile, fixtureRunId, targetHeroId,
                disposableSaveName, requireDisposableSave = requiresDisposableSave,
                timeoutSeconds = 300 }
        };
        if (profile == "save_prepare")
            steps.Add(new { operation = "save_checkpoint", saveName = disposableSaveName,
                timeoutSeconds = 300 });
        return api.PostAsync("/tests/live/run/start", new
        {
            schemaVersion = 2, campaignId, mode = "individual_chat",
            label = "MCP conversation arrest " + profile, presentation = "visible",
            effects = "guarded", autoCompleteWhenIdle = true, steps
        }, cancellationToken);
    }

    [McpServerTool(Name = "reign_start_arrest_organic_test", ReadOnly = false,
        Destructive = true, Idempotent = false, UseStructuredContent = true)]
    [Description("Starts an organic free-form arrest acceptance flow using production conversation actions. It requires the exact loaded disposable save name and retains native diplomatic, duel, battle, and captivity consequences.")]
    public static Task<ApiEnvelope> StartArrestOrganicTest(
        ReignApiClient api,
        ReignMcpOptions options,
        string campaignId,
        [AllowedValues("town_guard", "castle_guard", "player_party", "surrender_duel",
            "party_battle", "evidence_agent", "rescind_release", "full")]
        string profile,
        string targetSearch,
        string disposableSaveName,
        string accusation = "treason and espionage against this realm",
        string fixtureRunId = "arrest_organic_release",
        [Description("Exact text required: start Reign organic arrest test on disposable save")]
        string confirmation = "",
        CancellationToken cancellationToken = default)
    {
        RequireArrestVerificationControl(options);
        InputGuard.RequireConfirmation(confirmation,
            "start Reign organic arrest test on disposable save");
        campaignId = RequiredIdentifier(campaignId, nameof(campaignId));
        fixtureRunId = RequiredIdentifier(fixtureRunId, nameof(fixtureRunId));
        targetSearch = RequiredArrestText(targetSearch, nameof(targetSearch), 200);
        accusation = RequiredArrestText(accusation, nameof(accusation), 500);
        disposableSaveName = RequiredArrestSaveName(disposableSaveName);
        profile = (profile ?? string.Empty).Trim().ToLowerInvariant();
        string[] supported = { "town_guard", "castle_guard", "player_party", "surrender_duel",
            "party_battle", "evidence_agent", "rescind_release", "full" };
        if (!supported.Contains(profile, StringComparer.Ordinal))
            throw new ArgumentException("Unsupported organic arrest profile.", nameof(profile));

        var steps = new List<object>
        {
            new { operation = "arrest_test", profile = "smoke", fixtureRunId,
                disposableSaveName, requireDisposableSave = true, timeoutSeconds = 120 },
            new { operation = "open", targetSearch, timeoutSeconds = 180 },
            new { operation = "send", text = "Guards, arrest them for " + accusation + ".",
                timeoutSeconds = 180 },
            new { operation = "send", text = "Guards, take them away now.",
                timeoutSeconds = 180 }
        };
        if (profile == "surrender_duel" || profile == "full")
            steps.Add(new { operation = "send", text = "Then settle this by a nonlethal duel for your surrender.", timeoutSeconds = 180 });
        if (profile == "party_battle" || profile == "full")
            steps.Add(new { operation = "send", text = "I order my men to attack your party and take you alive.", timeoutSeconds = 180 });
        if (profile == "rescind_release" || profile == "full")
            steps.Add(new { operation = "send", text = "I rescind the accusation, clear your name, and order your release.", timeoutSeconds = 180 });
        steps.Add(new { operation = "close", timeoutSeconds = 60 });
        steps.Add(new { operation = "save_checkpoint", saveName = disposableSaveName,
            timeoutSeconds = 300 });
        return api.PostAsync("/tests/live/run/start", new
        {
            schemaVersion = 2, campaignId, mode = "individual_chat",
            label = "MCP organic arrest " + profile, presentation = "visible",
            effects = "full", autoCompleteWhenIdle = true, steps
        }, cancellationToken);
    }

    private static void RequireArrestVerificationControl(ReignMcpOptions options)
    {
        if (!options.AllowVerificationControl)
            throw new InvalidOperationException("Verification control is disabled. Set REIGN_MCP_ALLOW_VERIFICATION_CONTROL=true in the trusted MCP configuration.");
    }

    private static string RequiredArrestSaveName(string value)
    {
        value = RequiredArrestText(value, nameof(value), 120);
        bool supportedNamespace = value.StartsWith("Reign_", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("ReignTest_", StringComparison.OrdinalIgnoreCase);
        if (!supportedNamespace)
            throw new ArgumentException(
                "The disposable save must use a Reign_ feature namespace or an enrolled ReignTest_ campaign-test namespace.",
                nameof(value));
        return value;
    }

    private static string RequiredArrestText(string value, string parameterName,
        int maximumLength)
    {
        value = InputGuard.BoundedText(value, parameterName, maximumLength);
        if (value.Length == 0)
            throw new ArgumentException(parameterName + " is required.", parameterName);
        return value;
    }

    private static string ArrestManifestPath(ReignMcpOptions options)
    {
        string path = Path.Combine(options.WorkspaceRoot, "ReignServer", "tests", "ReignLiveTest",
            "Features", "WorldSimulation", "arrest-equivalence-manifest.json");
        ReignMcpOptions.EnsureWithin(options.WorkspaceRoot, path, "arrestManifest");
        return path;
    }

    private static JsonElement LoadArrestManifest(ReignMcpOptions options)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(
            ArrestManifestPath(options), Encoding.UTF8));
        return document.RootElement.Clone();
    }

    private static IEnumerable<string> RequiredArrestInstances(JsonElement manifest)
    {
        foreach (string propertyName in new[]
        {
            "languageCases", "deterministicCases", "nativeCases"
        })
            foreach (JsonElement item in manifest.GetProperty(propertyName).EnumerateArray())
                yield return item.GetProperty("id").GetString() ?? string.Empty;
    }

    private static JsonElement FindArrestCase(JsonElement manifest, string caseId)
    {
        foreach (string propertyName in new[] { "deterministicCases", "nativeCases" })
            foreach (JsonElement item in manifest.GetProperty(propertyName).EnumerateArray())
                if (string.Equals(item.GetProperty("id").GetString(), caseId,
                        StringComparison.OrdinalIgnoreCase))
                    return item.Clone();
        throw new ArgumentException("Unknown executable Arrest case id.", nameof(caseId));
    }

    private static string ArrestLiveRunId(string runId, string caseId, string stage)
    {
        string compactRun = new string(runId.Where(char.IsAsciiLetterOrDigit)
            .Take(24).ToArray());
        string compactCase = new string((caseId + "-" + stage)
            .Where(character => char.IsAsciiLetterOrDigit(character) || character == '-')
            .Take(40).ToArray()).Trim('-');
        return (compactRun + "-" + compactCase + "-"
            + Guid.NewGuid().ToString("N")[..8]).ToLowerInvariant();
    }

    private static string ArrestExecutionInstance(ArrestCertificationExecution execution) =>
        execution.CaseId.Equals("AR-NATIVE-009", StringComparison.OrdinalIgnoreCase)
            ? execution.CaseId + "::" + execution.Stage
            : execution.CaseId;

    private static string RequiredArrestFingerprint(string value, string name)
    {
        value = InputGuard.BoundedText(value, name, 300);
        if (value.Length == 0 || value.Equals("unknown", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(name + " must be a known immutable fingerprint.", name);
        return value;
    }

    private static string ArrestCertificationStatePath(ReignMcpOptions options, string runId)
    {
        string root = Path.Combine(options.WorkspaceRoot, ".codex-live-artifacts", "arrest");
        string path = Path.Combine(root, RequiredIdentifier(runId, nameof(runId)) + ".json");
        ReignMcpOptions.EnsureWithin(root, path, nameof(runId));
        return path;
    }

    private static ArrestCertificationState ReadArrestCertificationState(
        ReignMcpOptions options, string runId) =>
        TryReadArrestCertificationState(options, runId)
        ?? throw new InvalidOperationException("Unknown Arrest certification run id.");

    private static ArrestCertificationState? TryReadArrestCertificationState(
        ReignMcpOptions options, string runId)
    {
        string path = ArrestCertificationStatePath(options, runId);
        return File.Exists(path)
            ? JsonSerializer.Deserialize<ArrestCertificationState>(
                File.ReadAllText(path, Encoding.UTF8), ArrestCertificationJson)
            : null;
    }

    private static void WriteArrestCertificationState(ReignMcpOptions options,
        ArrestCertificationState state)
    {
        state.UpdatedUtc = DateTime.UtcNow.ToString("O");
        string path = ArrestCertificationStatePath(options, state.RunId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(state,
            ArrestCertificationJson), new UTF8Encoding(false));
        File.Move(temporary, path, true);
    }

    private static bool ArrestImmutableInputsMatch(ArrestCertificationState left,
        ArrestCertificationState right) => ArrestFingerprintsMatch(left, right)
        && string.Equals(left.TimelineId, right.TimelineId, StringComparison.OrdinalIgnoreCase);

    private static bool ArrestFingerprintsMatch(ArrestCertificationState left,
        ArrestCertificationState right) =>
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

    private static bool ArrestCheckpointRestoreCarryInputsMatch(
        ArrestCertificationState left, ArrestCertificationState right) =>
        string.Equals(left.CampaignId, right.CampaignId, StringComparison.OrdinalIgnoreCase)
        && string.Equals(left.CampaignTestRunId, right.CampaignTestRunId, StringComparison.OrdinalIgnoreCase)
        && string.Equals(left.ProtectedBaselineSaveName, right.ProtectedBaselineSaveName, StringComparison.OrdinalIgnoreCase)
        && string.Equals(left.DisposableSaveName, right.DisposableSaveName, StringComparison.OrdinalIgnoreCase)
        && string.Equals(left.ClientBuild, right.ClientBuild, StringComparison.Ordinal)
        && string.Equals(left.ServerBuild, right.ServerBuild, StringComparison.Ordinal)
        && string.Equals(left.ProviderConfigurationFingerprint, right.ProviderConfigurationFingerprint, StringComparison.Ordinal)
        && string.Equals(left.BannerlordVersion, right.BannerlordVersion, StringComparison.OrdinalIgnoreCase);

    private static bool ArrestSovereigntyMigrationCarryInputsMatch(
        ArrestCertificationState left, ArrestCertificationState right) =>
        string.Equals(left.CampaignId, right.CampaignId, StringComparison.OrdinalIgnoreCase)
        && string.Equals(left.CampaignTestRunId, right.CampaignTestRunId, StringComparison.OrdinalIgnoreCase)
        && string.Equals(left.ProtectedBaselineSaveName, right.ProtectedBaselineSaveName, StringComparison.OrdinalIgnoreCase)
        && string.Equals(left.DisposableSaveName, right.DisposableSaveName, StringComparison.OrdinalIgnoreCase)
        && !string.Equals(left.ClientBuild, right.ClientBuild, StringComparison.Ordinal)
        && string.Equals(left.ServerBuild, right.ServerBuild, StringComparison.Ordinal)
        && string.Equals(left.ProviderConfigurationFingerprint, right.ProviderConfigurationFingerprint, StringComparison.Ordinal)
        && string.Equals(left.BannerlordVersion, right.BannerlordVersion, StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyDictionary<string, object?> ArrestStateEnvelope(
        ArrestCertificationState state, string status, JsonElement manifest) =>
        new Dictionary<string, object?>
        {
            ["ok"] = true,
            ["schema"] = "reign-arrest-certification-state-v1",
            ["status"] = status,
            ["state"] = state,
            ["manifest"] = JsonSerializer.Deserialize<object>(manifest.GetRawText())
        };

    private static string FindArrestString(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object)
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (property.NameEquals(name) && property.Value.ValueKind == JsonValueKind.String)
                    return property.Value.GetString() ?? string.Empty;
                string nested = FindArrestString(property.Value, name);
                if (nested.Length > 0) return nested;
            }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (JsonElement item in element.EnumerateArray())
            {
                string nested = FindArrestString(item, name);
                if (nested.Length > 0) return nested;
            }
        return string.Empty;
    }

    private static bool FindArrestBoolean(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object)
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (property.NameEquals(name))
                    return property.Value.ValueKind == JsonValueKind.True;
                if (FindArrestBoolean(property.Value, name)) return true;
            }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (JsonElement item in element.EnumerateArray())
                if (FindArrestBoolean(item, name)) return true;
        return false;
    }

    private static int FindArrestInteger(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object)
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (property.NameEquals(name) && property.Value.TryGetInt32(out int value))
                    return value;
                int nested = FindArrestInteger(property.Value, name);
                if (nested >= 0) return nested;
            }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (JsonElement item in element.EnumerateArray())
            {
                int nested = FindArrestInteger(item, name);
                if (nested >= 0) return nested;
            }
        return -1;
    }
}

public sealed record ArrestCertificationState
{
    public string Schema { get; set; } = "reign-arrest-certification-state-v1";
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
    public string CarryForwardScope { get; set; } = "";
    public List<string> RequiredInstances { get; set; } = [];
    public List<string> ContractPasses { get; set; } = [];
    public List<ArrestCertificationExecution> Executions { get; set; } = [];
}

public sealed record ArrestCertificationExecution
{
    public string CaseId { get; set; } = "";
    public string Stage { get; set; } = "";
    public string Profile { get; set; } = "";
    public string LiveRunId { get; set; } = "";
    public bool StartAccepted { get; set; }
    public string StartedUtc { get; set; } = "";
}
