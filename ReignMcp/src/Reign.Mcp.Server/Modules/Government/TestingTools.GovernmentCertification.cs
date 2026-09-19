using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Reign.Mcp.Server;

public static partial class TestingTools
{
    private const string GovernmentCertificationMemoryIsolationDirective =
        "Government certification clean-room scope: treat this as the first discussion of this government-power reduction with this NPC. Ignore only dialogue memories or claims created by earlier Government certification runs. Preserve the NPC's native personality, relationship, party posture, realm facts, and consent standards. This instruction is outcome-neutral: it never requests, prefers, predicts, or authorizes agreement or refusal. Do not infer a desired answer from certification metadata. Respond naturally from the supplied state and current conversation.";
    private static readonly object GovernmentCertificationGate = new();
    private static readonly JsonSerializerOptions GovernmentCertificationJson = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    [McpServerTool(Name = "reign_prepare_government_certification", ReadOnly = false,
        Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Binds Government release certification to one guarded campaign-test enrollment and immutable source, build, provider, catalog, Bannerlord, validation, and deployment evidence. Preparation never starts Bannerlord or mutates a campaign.")]
    public static IReadOnlyDictionary<string, object?> PrepareGovernmentCertification(
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
        string validationReportPath,
        string deploymentEvidencePath,
        string bannerlordVersion = "v1.4.8")
    {
        campaignId = RequiredGovernmentCertificationToken(campaignId, nameof(campaignId));
        campaignTestRunId = RequiredGovernmentCertificationToken(campaignTestRunId, nameof(campaignTestRunId));
        runId = RequiredGovernmentCertificationToken(runId, nameof(runId));
        CampaignTestAuthorization authorization = campaignTests.RequireEnrollment(campaignTestRunId, campaignId);
        JsonElement manifest = LoadGovernmentCertificationManifest(options);
        var requested = new GovernmentCertificationState
        {
            RunId = runId,
            CampaignId = campaignId,
            CampaignTestRunId = campaignTestRunId,
            TimelineId = authorization.TimelineId,
            ProtectedBaselineSaveName = authorization.BaselineSaveName,
            DisposableSaveName = authorization.CurrentSaveName,
            DisposableSavePrefix = authorization.SavePrefix,
            SourceFingerprint = RequiredGovernmentFingerprint(sourceFingerprint, nameof(sourceFingerprint)),
            ClientBuild = RequiredGovernmentFingerprint(clientBuild, nameof(clientBuild)),
            ServerBuild = RequiredGovernmentFingerprint(serverBuild, nameof(serverBuild)),
            ProviderConfigurationFingerprint = RequiredGovernmentFingerprint(
                providerConfigurationFingerprint, nameof(providerConfigurationFingerprint)),
            CatalogFingerprint = RequiredGovernmentFingerprint(catalogFingerprint, nameof(catalogFingerprint)),
            BannerlordVersion = RequiredGovernmentFingerprint(bannerlordVersion, nameof(bannerlordVersion), requireSha256: false),
            ValidationReportPath = RequiredGovernmentEvidencePath(options, validationReportPath,
                nameof(validationReportPath)),
            DeploymentEvidencePath = RequiredGovernmentEvidencePath(options, deploymentEvidencePath,
                nameof(deploymentEvidencePath)),
            RequiredInstances = GovernmentRequiredInstances(manifest).ToList(),
            PreparedUtc = DateTime.UtcNow.ToString("O"),
            UpdatedUtc = DateTime.UtcNow.ToString("O")
        };

        lock (GovernmentCertificationGate)
        {
            GovernmentCertificationState? existing = TryReadGovernmentCertificationState(options, runId);
            if (existing is not null)
            {
                if (!GovernmentImmutableInputsMatch(existing, requested))
                    throw new InvalidOperationException("The Government certification run id is already bound to different immutable enrollment, build, validation, or deployment evidence.");
                return GovernmentCertificationEnvelope(existing, "already_prepared", manifest);
            }
            WriteGovernmentCertificationState(options, requested);
        }
        return GovernmentCertificationEnvelope(requested, "prepared", manifest);
    }

    [McpServerTool(Name = "reign_get_government_certification_status", ReadOnly = true,
        Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Returns durable Government certification enrollment, fingerprints, case executions, external evidence, and currently missing required case IDs without starting Bannerlord or reading live reports.")]
    public static IReadOnlyDictionary<string, object?> GetGovernmentCertificationStatus(
        ReignMcpOptions options,
        string runId)
    {
        runId = RequiredGovernmentCertificationToken(runId, nameof(runId));
        GovernmentCertificationState state = ReadGovernmentCertificationState(options, runId);
        HashSet<string> started = state.Executions.Select(item => item.Instance)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new Dictionary<string, object?>
        {
            ["ok"] = true,
            ["schema"] = "reign-government-certification-status-v1",
            ["requiredInstanceCount"] = state.RequiredInstances.Count,
            ["startedInstanceCount"] = started.Count,
            ["missingUnstartedInstances"] = state.RequiredInstances.Where(item => !started.Contains(item)).ToArray(),
            ["statePath"] = GovernmentCertificationStatePath(options, runId),
            ["state"] = state
        };
    }

    [McpServerTool(Name = "reign_carry_forward_government_passes", ReadOnly = false,
        Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Revalidates exact durable Government reports and carries only explicitly selected passes into a new fingerprint-bound run under one audited repair scope. Each scope has a fail-closed case allowlist. It never starts Bannerlord or mutates a campaign.")]
    public static async Task<IReadOnlyDictionary<string, object?>> CarryForwardGovernmentPasses(
        ReignApiClient api,
        ReignMcpOptions options,
        string sourceRunId,
        string destinationRunId,
        [Description("Semicolon-separated exact instances such as GOV-LANG-001 or GOV-NATIVE-036::empire.")]
        string caseInstances,
        [AllowedValues("government_hidden_language_and_release_recovery_only",
            "government_lifecycle_prune_evidence_only",
            "government_consent_persistence_harness_only")]
        string changeScope,
        [Description("Exact text required: carry forward unaffected Reign Government passes")]
        string confirmation = "",
        CancellationToken cancellationToken = default)
    {
        InputGuard.RequireConfirmation(confirmation,
            "carry forward unaffected Reign Government passes");
        sourceRunId = RequiredGovernmentCertificationToken(sourceRunId, nameof(sourceRunId));
        destinationRunId = RequiredGovernmentCertificationToken(destinationRunId,
            nameof(destinationRunId));
        string[] supportedScopes =
        {
            "government_hidden_language_and_release_recovery_only",
            "government_lifecycle_prune_evidence_only",
            "government_consent_persistence_harness_only"
        };
        if (!supportedScopes.Contains(changeScope, StringComparer.Ordinal))
            throw new ArgumentException("An audited Government carry-forward scope is required.",
                nameof(changeScope));

        GovernmentCertificationState source = ReadGovernmentCertificationState(options, sourceRunId);
        GovernmentCertificationState destination = ReadGovernmentCertificationState(options,
            destinationRunId);
        if (!string.Equals(source.CampaignId, destination.CampaignId,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(source.CampaignTestRunId, destination.CampaignTestRunId,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(source.TimelineId, destination.TimelineId,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(source.ProtectedBaselineSaveName,
                destination.ProtectedBaselineSaveName, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(source.DisposableSaveName, destination.DisposableSaveName,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(source.ProviderConfigurationFingerprint,
                destination.ProviderConfigurationFingerprint, StringComparison.Ordinal)
            || !string.Equals(source.BannerlordVersion, destination.BannerlordVersion,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Government carry-forward requires identical guarded enrollment, provider configuration, and Bannerlord version.");

        JsonElement manifest = LoadGovernmentCertificationManifest(options);
        string[] requested = InputGuard.BoundedText(caseInstances ?? string.Empty,
                nameof(caseInstances), 6000)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (requested.Length == 0)
            throw new ArgumentException("At least one exact Government case instance is required.",
                nameof(caseInstances));
        var carried = new List<object>();
        foreach (string instance in requested)
        {
            if (!destination.RequiredInstances.Contains(instance,
                    StringComparer.OrdinalIgnoreCase))
                throw new ArgumentException("Unknown or non-required Government instance '"
                    + instance + "'.", nameof(caseInstances));
            string[] parts = instance.Split(new[] { "::" }, 2, StringSplitOptions.None);
            string caseId = RequiredGovernmentCaseId(parts[0]);
            bool scopeAllowsCase = changeScope switch
            {
                "government_hidden_language_and_release_recovery_only" =>
                    caseId is not ("GOV-NATIVE-022" or "GOV-NATIVE-030"
                        or "GOV-NATIVE-031" or "GOV-NATIVE-032"
                        or "GOV-NATIVE-033" or "GOV-NATIVE-034"
                        or "GOV-NATIVE-035"),
                "government_lifecycle_prune_evidence_only" =>
                    caseId is "GOV-NATIVE-030" or "GOV-NATIVE-031",
                "government_consent_persistence_harness_only" =>
                    caseId is "GOV-NATIVE-022" or "GOV-NATIVE-032"
                        or "GOV-NATIVE-033" or "GOV-NATIVE-034"
                        or "GOV-NATIVE-035",
                _ => false
            };
            if (!scopeAllowsCase)
                throw new InvalidOperationException(instance
                    + " is not eligible under the selected audited Government repair scope.");
            string variant = parts.Length == 2 ? parts[1] : string.Empty;
            JsonElement testCase = FindGovernmentCertificationCase(manifest, caseId);
            GovernmentCertificationExecution? execution = source.Executions.AsEnumerable()
                .Reverse().FirstOrDefault(item =>
                    item.CaseId.Equals(caseId, StringComparison.OrdinalIgnoreCase)
                    && item.Variant.Equals(variant, StringComparison.OrdinalIgnoreCase));
            if (execution is null)
                throw new InvalidOperationException("The source run has no execution for "
                    + instance + ".");
            ApiEnvelope report = await api.GetAsync("/tests/live/run/report",
                new Dictionary<string, string?>
                {
                    ["campaignId"] = source.CampaignId,
                    ["runId"] = execution.LiveRunId
                }, cancellationToken).ConfigureAwait(false);
            bool completed = report.Ok && report.Data.HasValue
                && string.Equals(FindGovernmentReportString(report.Data.Value, "status"),
                    "completed", StringComparison.OrdinalIgnoreCase);
            string reportPath = report.Data.HasValue
                ? FindGovernmentReportString(report.Data.Value, "reportPath") : string.Empty;
            bool durableReportPresent = reportPath.Length > 0 && File.Exists(reportPath);
            string reportSha256 = durableReportPresent
                ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(reportPath)))
                : string.Empty;
            string[] requiredAssertions = GovernmentRequiredAssertions(testCase);
            bool assertionsPassed = report.Data.HasValue && (requiredAssertions.Length > 0
                ? requiredAssertions.All(id => HasGovernmentPassedAssertion(report.Data.Value, id))
                : execution.NaturalLanguage
                    ? GovernmentNaturalLanguageCoreAssertionsPassed(report.Data.Value)
                    : GovernmentDefaultCaseAssertionPassed(report.Data.Value, testCase, caseId));
            bool transcriptPassed = !execution.NaturalLanguage || report.Data.HasValue
                && ContainsGovernmentNaturalLanguageCaseSend(report.Data.Value, caseId);
            bool tagPassed = !execution.NaturalLanguage || report.Data.HasValue
                && HasGovernmentPassedAssertion(report.Data.Value,
                    "government_language_expected_tag");
            bool qualityPassed = !execution.NaturalLanguage || report.Data.HasValue
                && GovernmentDialogueContextQuality(report.Data.Value, testCase, tagPassed);
            bool memoryIsolationPassed = !execution.NaturalLanguage || report.Data.HasValue
                && ContainsGovernmentText(report.Data.Value,
                    "authorized_government_certification_memory_isolation");
            bool noDirectBypass = !report.Data.HasValue
                || !ContainsGovernmentDirectActionBypass(report.Data.Value);
            string[] requiredExternalEvidence = testCase.TryGetProperty("requiredExternalEvidence",
                    out JsonElement requiredEvidenceElement)
                ? requiredEvidenceElement.EnumerateArray()
                    .Select(item => item.GetString() ?? string.Empty)
                    .Where(item => item.Length > 0).ToArray()
                : Array.Empty<string>();
            GovernmentCertificationEvidence[] sourceEvidence = source.ExternalEvidence.Where(item =>
                    item.CaseId.Equals(caseId, StringComparison.OrdinalIgnoreCase)
                    && requiredExternalEvidence.Contains(item.Kind,
                        StringComparer.OrdinalIgnoreCase)
                    && File.Exists(item.Path))
                .ToArray();
            bool externalEvidenceComplete = requiredExternalEvidence.All(kind =>
                sourceEvidence.Any(item => item.Kind.Equals(kind,
                    StringComparison.OrdinalIgnoreCase)));
            if (!completed || !durableReportPresent || reportSha256.Length != 64
                || !assertionsPassed || !transcriptPassed || !tagPassed
                || !qualityPassed || !memoryIsolationPassed || !noDirectBypass
                || !externalEvidenceComplete)
                throw new InvalidOperationException("The durable source report for " + instance
                    + " does not pass the current structural, safety, context, memory-isolation, and evidence checks.");

            if (!destination.Executions.Any(item => item.LiveRunId.Equals(
                    execution.LiveRunId, StringComparison.OrdinalIgnoreCase)))
                destination.Executions.Add(execution);
            foreach (GovernmentCertificationEvidence evidence in sourceEvidence)
                if (!destination.ExternalEvidence.Any(item => item.CaseId.Equals(
                        evidence.CaseId, StringComparison.OrdinalIgnoreCase)
                    && item.Kind.Equals(evidence.Kind, StringComparison.OrdinalIgnoreCase)
                    && item.Path.Equals(evidence.Path, StringComparison.OrdinalIgnoreCase)))
                    destination.ExternalEvidence.Add(evidence);
            if (!destination.CarriedEvidence.Any(item => item.Instance.Equals(instance,
                    StringComparison.OrdinalIgnoreCase)
                && item.SourceRunId.Equals(sourceRunId, StringComparison.OrdinalIgnoreCase)))
                destination.CarriedEvidence.Add(new GovernmentCertificationCarriedEvidence
                {
                    Instance = instance,
                    SourceRunId = sourceRunId,
                    SourceFingerprint = source.SourceFingerprint,
                    SourceClientBuild = source.ClientBuild,
                    SourceServerBuild = source.ServerBuild,
                    SourceCatalogFingerprint = source.CatalogFingerprint,
                    LiveRunId = execution.LiveRunId,
                    ReportPath = reportPath,
                    ReportSha256 = reportSha256,
                    ChangeScope = changeScope,
                    CarriedUtc = DateTime.UtcNow.ToString("O")
                });
            carried.Add(new { instance, execution.LiveRunId, sourceRunId,
                reportPath, reportSha256,
                source.SourceFingerprint, source.ClientBuild, source.ServerBuild,
                source.CatalogFingerprint });
        }
        destination.UpdatedUtc = DateTime.UtcNow.ToString("O");
        lock (GovernmentCertificationGate)
            WriteGovernmentCertificationState(options, destination);
        return new Dictionary<string, object?>
        {
            ["ok"] = true,
            ["schema"] = "reign-government-carried-evidence-v1",
            ["changeScope"] = changeScope,
            ["carried"] = carried,
            ["state"] = destination
        };
    }

    [McpServerTool(Name = "reign_start_government_certification_case", ReadOnly = false,
        Destructive = true, Idempotent = false, UseStructuredContent = true)]
    [Description("Starts one manifest-owned Government release case through the visible production bridge after proving the exact armed campaign-test Current. Natural-language cases use ordinary Individual Chat turns; structured commands only create prerequisites and observe results.")]
    public static async Task<IReadOnlyDictionary<string, object?>> StartGovernmentCertificationCase(
        ReignApiClient api,
        ReignMcpOptions options,
        CampaignTestService campaignTests,
        string campaignId,
        string campaignTestRunId,
        string runId,
        string caseId,
        string targetSearch = "",
        string expectedFeatureFingerprint = "",
        string previousGameInstanceId = "",
        [Description("Required after a restart for soak_verify: the retained completed GOV-NATIVE-030 live-run id whose preparation evidence will be validated and replayed read-only into the verifier.")]
        string soakPreparationLiveRunId = "",
        [Description("Exact text required: start Reign government certification case on disposable save")]
        string confirmation = "",
        CancellationToken cancellationToken = default)
    {
        if (!options.AllowVerificationControl)
            throw new InvalidOperationException("Verification control is disabled. Set REIGN_MCP_ALLOW_VERIFICATION_CONTROL=true in the trusted MCP configuration.");
        InputGuard.RequireConfirmation(confirmation,
            "start Reign government certification case on disposable save");
        campaignId = RequiredGovernmentCertificationToken(campaignId, nameof(campaignId));
        campaignTestRunId = RequiredGovernmentCertificationToken(campaignTestRunId, nameof(campaignTestRunId));
        runId = RequiredGovernmentCertificationToken(runId, nameof(runId));
        caseId = RequiredGovernmentCaseId(caseId);
        targetSearch = InputGuard.BoundedText(targetSearch ?? string.Empty, nameof(targetSearch), 200);
        expectedFeatureFingerprint = InputGuard.BoundedText(expectedFeatureFingerprint ?? string.Empty,
            nameof(expectedFeatureFingerprint), 64);
        previousGameInstanceId = InputGuard.BoundedText(previousGameInstanceId ?? string.Empty,
            nameof(previousGameInstanceId), 120);
        soakPreparationLiveRunId = InputGuard.BoundedText(soakPreparationLiveRunId ?? string.Empty,
            nameof(soakPreparationLiveRunId), 240);

        GovernmentCertificationState state = ReadGovernmentCertificationState(options, runId);
        if (!string.Equals(state.CampaignId, campaignId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(state.CampaignTestRunId, campaignTestRunId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The Government certification case does not match its prepared campaign-test enrollment.");
        CampaignTestAuthorization authorization = await campaignTests.RequireArmedOwnedSaveAsync(
            campaignTestRunId, campaignId, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(authorization.CurrentSaveName, state.DisposableSaveName,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The prepared Government disposable Current no longer matches campaign-test enrollment.");

        JsonElement manifest = LoadGovernmentCertificationManifest(options);
        JsonElement testCase = FindGovernmentCertificationCase(manifest, caseId);
        string group = GovernmentCaseGroup(testCase);
        if (group == "nativeCases" && GovernmentRequiredAssertions(testCase).Length == 0)
            throw new InvalidOperationException("Every native Government certification case must declare one or more case-specific required assertions.");
        string profile = testCase.TryGetProperty("profile", out JsonElement profileElement)
            ? profileElement.GetString() ?? string.Empty : string.Empty;
        string variant = testCase.TryGetProperty("variant", out JsonElement variantElement)
            ? variantElement.GetString() ?? string.Empty : string.Empty;
        string fixture = testCase.TryGetProperty("fixture", out JsonElement fixtureElement)
            ? fixtureElement.GetString() ?? string.Empty : string.Empty;
        string expectedTag = testCase.TryGetProperty("expectedTag", out JsonElement expectedTagElement)
            ? expectedTagElement.GetString() ?? string.Empty : string.Empty;
        string expectedPosture = testCase.TryGetProperty("expectedPosture", out JsonElement postureElement)
            ? postureElement.GetString() ?? string.Empty : string.Empty;
        string playerText = testCase.TryGetProperty("playerText", out JsonElement playerTextElement)
            ? playerTextElement.GetString() ?? string.Empty : string.Empty;
        string followUpText = testCase.TryGetProperty("followUpText", out JsonElement followUpTextElement)
            ? followUpTextElement.GetString() ?? string.Empty : string.Empty;
        bool followUpWhenNoConsent = testCase.TryGetProperty("followUpWhenNoConsent",
            out JsonElement followUpConditionElement) && followUpConditionElement.ValueKind == JsonValueKind.True;
        string target = testCase.TryGetProperty("target", out JsonElement targetElement)
            ? targetElement.GetString() ?? string.Empty : string.Empty;
        if (group == "naturalLanguageCases" && string.IsNullOrWhiteSpace(targetSearch))
            throw new ArgumentException("Natural-language Government cases require a real target hero id or search selected from preflight evidence.", nameof(targetSearch));
        if (profile == "save_verify"
            && (expectedFeatureFingerprint.Length != 64 || previousGameInstanceId.Length == 0))
            throw new ArgumentException("A Government save_verify case requires the exact save_prepare fingerprint and prior native game-instance ID.");

        JsonElement? soakPreparation = null;
        if (profile == "soak_verify")
        {
            string preparationRunId = soakPreparationLiveRunId;
            if (preparationRunId.Length == 0)
                preparationRunId = state.Executions.AsEnumerable().Reverse()
                    .FirstOrDefault(item => item.Profile.Equals("soak_prepare",
                        StringComparison.OrdinalIgnoreCase))?.LiveRunId ?? string.Empty;
            if (preparationRunId.Length == 0 || expectedFeatureFingerprint.Length != 64)
                throw new ArgumentException("A Government soak_verify case requires the retained soak_prepare live-run id and its exact feature fingerprint.");
            ApiEnvelope preparationReport = await api.GetAsync("/tests/live/run/report",
                new Dictionary<string, string?>
                {
                    ["campaignId"] = campaignId,
                    ["runId"] = preparationRunId
                }, cancellationToken).ConfigureAwait(false);
            soakPreparation = RequireGovernmentSoakPreparation(preparationReport,
                expectedFeatureFingerprint);
        }

        string instance = caseId + (string.IsNullOrWhiteSpace(variant) ? string.Empty : "::" + variant);
        int attempt = state.Executions.Count(item =>
            item.Instance.Equals(instance, StringComparison.OrdinalIgnoreCase)) + 1;
        string liveRunId = GovernmentLiveRunId(runId, instance, attempt);
        var steps = new List<object>();
        if (group == "naturalLanguageCases")
        {
            steps.Add(GovernmentCertificationStep("case_prepare", caseId, fixture, targetSearch,
                expectedTag, expectedPosture, authorization.CurrentSaveName, "", ""));
            steps.Add(new
            {
                operation = "open", mode = "individual_chat",
                targetSearches = new[] { targetSearch }, presentation = "visible", effects = "full",
                timeoutSeconds = 600
            });
            steps.Add(new
            {
                operation = "send", mode = "individual_chat", text = playerText,
                sceneIndex = 0, turnIndex = 0, naturalLanguageCase = true,
                governmentCertificationMemoryIsolation = true,
                guardedPromptOverride = true,
                guardedPromptOverrideDirective = GovernmentCertificationMemoryIsolationDirective,
                governmentCertificationCaseId = caseId, timeoutSeconds = 1200
            });
            if (!string.IsNullOrWhiteSpace(followUpText))
                steps.Add(new
                {
                    operation = "send", mode = "individual_chat", text = followUpText,
                    sceneIndex = 0, turnIndex = 1, naturalLanguageCase = true,
                    naturalLanguageFollowUp = true,
                    governmentCertificationMemoryIsolation = true,
                    guardedPromptOverride = true,
                    guardedPromptOverrideDirective = GovernmentCertificationMemoryIsolationDirective,
                    skipWhenPriorDialogueActionQueued = followUpWhenNoConsent
                        ? "consent_government_reduction" : string.Empty,
                    governmentCertificationCaseId = caseId, timeoutSeconds = 1200
                });
            steps.Add(new { operation = "close", mode = "individual_chat", timeoutSeconds = 300 });
            steps.Add(GovernmentCertificationStep("case_observe", caseId, fixture, targetSearch,
                expectedTag, expectedPosture, authorization.CurrentSaveName, "", ""));
        }
        else if (group == "deterministicCases")
        {
            steps.Add(GovernmentCertificationStep(profile, caseId, "", targetSearch,
                "", "", authorization.CurrentSaveName, "", ""));
        }
        else if (string.Equals(testCase.GetProperty("kind").GetString(), "ui",
                     StringComparison.OrdinalIgnoreCase))
        {
            steps.Add(new { operation = "ui_open", targetSearch = target, timeoutSeconds = 120 });
            steps.Add(new { operation = "ui_snapshot", targetSearch = target, timeoutSeconds = 120 });
            steps.Add(new { operation = "ui_close", timeoutSeconds = 60 });
            steps.Add(GovernmentCertificationStep("case_execute", caseId, fixture, targetSearch,
                expectedTag, expectedPosture, authorization.CurrentSaveName, "", ""));
        }
        else
        {
            steps.Add(GovernmentCertificationStep(profile, caseId, fixture, targetSearch,
                expectedTag, expectedPosture, authorization.CurrentSaveName,
                expectedFeatureFingerprint, previousGameInstanceId, soakPreparation));
        }

        ApiEnvelope liveRun = await api.PostAsync("/tests/live/run/start", new
        {
            schemaVersion = 2,
            campaignId,
            runId = liveRunId,
            mode = group == "naturalLanguageCases" ? "individual_chat" : "government",
            label = "Government release certification " + instance,
            presentation = "visible",
            effects = "full",
            autoCompleteWhenIdle = true,
            steps
        }, cancellationToken).ConfigureAwait(false);
        lock (GovernmentCertificationGate)
        {
            state = ReadGovernmentCertificationState(options, runId);
            state.Executions.Add(new GovernmentCertificationExecution
            {
                CaseId = caseId,
                Instance = instance,
                Group = group,
                Profile = profile,
                Variant = variant,
                LiveRunId = liveRunId,
                NaturalLanguage = group == "naturalLanguageCases",
                Attempt = attempt,
                StartAccepted = liveRun.Ok,
                StartedUtc = DateTime.UtcNow.ToString("O")
            });
            state.UpdatedUtc = DateTime.UtcNow.ToString("O");
            WriteGovernmentCertificationState(options, state);
        }
        return new Dictionary<string, object?>
        {
            ["ok"] = liveRun.Ok,
            ["schema"] = "reign-government-certification-case-start-v1",
            ["caseInstance"] = instance,
            ["liveRunId"] = liveRunId,
            ["attempt"] = attempt,
            ["naturalLanguage"] = group == "naturalLanguageCases",
            ["liveRun"] = liveRun,
            ["state"] = state
        };
    }

    [McpServerTool(Name = "reign_record_government_certification_evidence", ReadOnly = false,
        Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Records one already-created, hash-verified workspace evidence artifact against an exact Government certification case. It cannot create screenshots, fabricate observations, start UI, or mutate a campaign.")]
    public static IReadOnlyDictionary<string, object?> RecordGovernmentCertificationEvidence(
        ReignMcpOptions options,
        string runId,
        string caseId,
        [AllowedValues("native_screenshot", "mouse_interaction", "manual_observation", "soak", "validation", "deployment")]
        string evidenceKind,
        string evidencePath,
        string expectedSha256,
        string reviewNote,
        [Description("Exact text required: record reviewed Reign Government acceptance evidence")]
        string confirmation = "")
    {
        InputGuard.RequireConfirmation(confirmation,
            "record reviewed Reign Government acceptance evidence");
        runId = RequiredGovernmentCertificationToken(runId, nameof(runId));
        caseId = RequiredGovernmentCaseId(caseId);
        evidenceKind = RequiredGovernmentCertificationToken(evidenceKind, nameof(evidenceKind));
        string path = RequiredGovernmentEvidencePath(options, evidencePath, nameof(evidencePath));
        string expected = RequiredGovernmentFingerprint(expectedSha256, nameof(expectedSha256));
        string actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The Government evidence SHA-256 does not match the retained file.");
        reviewNote = InputGuard.BoundedText(reviewNote ?? string.Empty, nameof(reviewNote), 1000);
        if (reviewNote.Length < 12)
            throw new ArgumentException("A concrete review note is required.", nameof(reviewNote));

        GovernmentCertificationState state;
        lock (GovernmentCertificationGate)
        {
            state = ReadGovernmentCertificationState(options, runId);
            JsonElement manifest = LoadGovernmentCertificationManifest(options);
            _ = FindGovernmentCertificationCase(manifest, caseId);
            GovernmentCertificationEvidence? existing = state.ExternalEvidence.FirstOrDefault(item =>
                item.CaseId.Equals(caseId, StringComparison.OrdinalIgnoreCase)
                && item.Kind.Equals(evidenceKind, StringComparison.OrdinalIgnoreCase)
                && item.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                state.ExternalEvidence.Add(new GovernmentCertificationEvidence
                {
                    CaseId = caseId,
                    Kind = evidenceKind,
                    Path = path,
                    Sha256 = actual,
                    ReviewNote = reviewNote,
                    RecordedUtc = DateTime.UtcNow.ToString("O")
                });
            }
            state.UpdatedUtc = DateTime.UtcNow.ToString("O");
            WriteGovernmentCertificationState(options, state);
        }
        return new Dictionary<string, object?>
        {
            ["ok"] = true,
            ["schema"] = "reign-government-certification-evidence-v1",
            ["caseId"] = caseId,
            ["kind"] = evidenceKind,
            ["path"] = path,
            ["sha256"] = actual,
            ["state"] = state
        };
    }

    [McpServerTool(Name = "reign_evaluate_government_release_readiness", ReadOnly = true,
        Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Evaluates strict Government release readiness from immutable fingerprints, durable live reports, natural-language transcripts, action/tag assertions, native evidence, persistence, soak, validation, deployment, and cleanup. Missing or failed evidence remains an explicit blocker.")]
    public static async Task<IReadOnlyDictionary<string, object?>> EvaluateGovernmentReleaseReadiness(
        ReignApiClient api,
        ReignMcpOptions options,
        CampaignTestService campaignTests,
        string campaignId,
        string campaignTestRunId,
        string runId,
        CancellationToken cancellationToken = default)
    {
        campaignId = RequiredGovernmentCertificationToken(campaignId, nameof(campaignId));
        campaignTestRunId = RequiredGovernmentCertificationToken(campaignTestRunId, nameof(campaignTestRunId));
        runId = RequiredGovernmentCertificationToken(runId, nameof(runId));
        CampaignTestAuthorization authorization = campaignTests.RequireEnrollment(campaignTestRunId, campaignId);
        GovernmentCertificationState state = ReadGovernmentCertificationState(options, runId);
        if (!string.Equals(state.DisposableSaveName, authorization.CurrentSaveName,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(state.ProtectedBaselineSaveName, authorization.BaselineSaveName,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Government readiness enrollment no longer matches the guarded campaign test.");

        JsonElement manifest = LoadGovernmentCertificationManifest(options);
        var passed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var reports = new List<object?>();
        int languageRequired = manifest.GetProperty("releaseCertification")
            .GetProperty("naturalLanguageCases").GetArrayLength();
        int languageCorrect = 0;
        int languageQuality = 0;
        int falsePositiveCount = 0;
        int directBypassCount = 0;
        foreach (GovernmentCertificationExecution execution in state.Executions.AsEnumerable().Reverse())
        {
            if (passed.Contains(execution.Instance)) continue;
            ApiEnvelope report = await api.GetAsync("/tests/live/run/report",
                new Dictionary<string, string?>
                {
                    ["campaignId"] = campaignId,
                    ["runId"] = execution.LiveRunId
                }, cancellationToken).ConfigureAwait(false);
            JsonElement testCase = FindGovernmentCertificationCase(manifest, execution.CaseId);
            bool completed = report.Ok && report.Data.HasValue
                && string.Equals(FindGovernmentReportString(report.Data.Value, "status"),
                    "completed", StringComparison.OrdinalIgnoreCase);
            string[] requiredAssertions = GovernmentRequiredAssertions(testCase);
            bool assertionsPassed = report.Data.HasValue
                && requiredAssertions.All(id => HasGovernmentPassedAssertion(report.Data.Value, id));
            if (requiredAssertions.Length == 0 && report.Data.HasValue)
                assertionsPassed = execution.NaturalLanguage
                    ? GovernmentNaturalLanguageCoreAssertionsPassed(report.Data.Value)
                    : GovernmentDefaultCaseAssertionPassed(report.Data.Value, testCase,
                        execution.CaseId);
            bool transcriptComplete = true;
            bool expectedTagPassed = true;
            bool contextualQualityPassed = true;
            bool memoryIsolationPassed = !execution.NaturalLanguage;
            bool noDirectBypass = !report.Data.HasValue
                || !ContainsGovernmentDirectActionBypass(report.Data.Value);
            string[] requiredExternalEvidence = testCase.TryGetProperty("requiredExternalEvidence",
                    out JsonElement requiredEvidenceElement)
                ? requiredEvidenceElement.EnumerateArray()
                    .Select(item => item.GetString() ?? string.Empty)
                    .Where(item => item.Length > 0).ToArray()
                : Array.Empty<string>();
            bool externalEvidenceComplete = requiredExternalEvidence.All(kind =>
                state.ExternalEvidence.Any(item =>
                    item.CaseId.Equals(execution.CaseId, StringComparison.OrdinalIgnoreCase)
                    && item.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase)
                    && File.Exists(item.Path)));
            if (execution.NaturalLanguage)
            {
                string playerText = testCase.GetProperty("playerText").GetString() ?? string.Empty;
                bool carriedHiddenLanguageEvidence = state.CarriedEvidence.Any(item =>
                    item.Instance.Equals(execution.Instance, StringComparison.OrdinalIgnoreCase)
                    && item.LiveRunId.Equals(execution.LiveRunId,
                        StringComparison.OrdinalIgnoreCase)
                    && item.ChangeScope.Equals(
                        "government_hidden_language_and_release_recovery_only",
                        StringComparison.Ordinal));
                transcriptComplete = report.Data.HasValue
                    && (ContainsGovernmentText(report.Data.Value, playerText)
                        || carriedHiddenLanguageEvidence
                        && ContainsGovernmentNaturalLanguageCaseSend(report.Data.Value,
                            execution.CaseId));
                expectedTagPassed = report.Data.HasValue
                    && HasGovernmentPassedAssertion(report.Data.Value,
                        "government_language_expected_tag");
                contextualQualityPassed = report.Data.HasValue
                    && GovernmentDialogueContextQuality(report.Data.Value, testCase,
                        expectedTagPassed);
                memoryIsolationPassed = report.Data.HasValue
                    && ContainsGovernmentText(report.Data.Value,
                        "authorized_government_certification_memory_isolation");
                if (completed && transcriptComplete && expectedTagPassed && noDirectBypass)
                    languageCorrect++;
                if (completed && transcriptComplete && contextualQualityPassed)
                    languageQuality++;
                string expectedTag = testCase.GetProperty("expectedTag").GetString() ?? "none";
                if (expectedTag.Equals("none", StringComparison.OrdinalIgnoreCase)
                    && !expectedTagPassed) falsePositiveCount++;
            }
            if (!noDirectBypass) directBypassCount++;
            bool casePassed = completed && assertionsPassed && transcriptComplete
                && expectedTagPassed && contextualQualityPassed && noDirectBypass
                && memoryIsolationPassed && externalEvidenceComplete;
            if (casePassed) passed.Add(execution.Instance);
            reports.Add(new Dictionary<string, object?>
            {
                ["instance"] = execution.Instance,
                ["liveRunId"] = execution.LiveRunId,
                ["completed"] = completed,
                ["requiredAssertionsPassed"] = assertionsPassed,
                ["naturalLanguageTranscriptComplete"] = transcriptComplete,
                ["expectedTagOrSafetyResultPassed"] = expectedTagPassed,
                ["contextualQualityPassed"] = contextualQualityPassed,
                ["governmentCertificationMemoryIsolationPassed"] = memoryIsolationPassed,
                ["noDirectDialogueActionBypass"] = noDirectBypass,
                ["requiredExternalEvidence"] = requiredExternalEvidence,
                ["externalEvidenceComplete"] = externalEvidenceComplete,
                ["passed"] = casePassed,
                ["report"] = report
            });
        }

        double correctness = languageRequired == 0 ? 0d : (double)languageCorrect / languageRequired;
        double quality = languageRequired == 0 ? 0d : (double)languageQuality / languageRequired;
        JsonElement thresholds = manifest.GetProperty("releaseCertification").GetProperty("thresholds");
        double requiredQuality = thresholds.GetProperty("naturalLanguageContextQuality").GetDouble();
        string[] missing = state.RequiredInstances.Where(item => !passed.Contains(item)).ToArray();
        var blockers = new List<string>();
        if (missing.Length > 0)
            blockers.Add("Missing or failed required Government case instances: " + string.Join(", ", missing));
        if (correctness < 1d)
            blockers.Add($"Natural-language action/safety correctness is {correctness:P1}; release requires 100%.");
        if (quality + 0.0000001d < requiredQuality)
            blockers.Add($"Natural-language contextual quality is {quality:P1}; release requires at least {requiredQuality:P0}.");
        if (falsePositiveCount > 0)
            blockers.Add("One or more no-consent language cases produced a false-positive tag.");
        if (directBypassCount > 0)
            blockers.Add("One or more dialogue cases used a direct action operation rather than production dialogue.");
        foreach ((string label, string value) in new[]
        {
            ("source", state.SourceFingerprint), ("client", state.ClientBuild),
            ("server", state.ServerBuild), ("provider", state.ProviderConfigurationFingerprint),
            ("catalog", state.CatalogFingerprint), ("Bannerlord", state.BannerlordVersion)
        })
            if (string.IsNullOrWhiteSpace(value) || value.Equals("unknown", StringComparison.OrdinalIgnoreCase))
                blockers.Add("The " + label + " fingerprint is missing or unknown.");
        if (!File.Exists(state.ValidationReportPath)) blockers.Add("The bound validation report is missing.");
        if (!File.Exists(state.DeploymentEvidencePath)) blockers.Add("The bound deployment evidence is missing.");

        return new Dictionary<string, object?>
        {
            ["ok"] = true,
            ["schema"] = "reign-government-release-readiness-v1",
            ["readyToRelease"] = blockers.Count == 0,
            ["requiredInstanceCount"] = state.RequiredInstances.Count,
            ["passedInstanceCount"] = passed.Count,
            ["naturalLanguageRequiredCaseCount"] = languageRequired,
            ["naturalLanguageCorrectCaseCount"] = languageCorrect,
            ["naturalLanguageContextQualityCaseCount"] = languageQuality,
            ["naturalLanguageActionAndSafetyAccuracy"] = correctness,
            ["naturalLanguageContextQuality"] = quality,
            ["falsePositiveConsentCount"] = falsePositiveCount,
            ["directDialogueActionBypassCount"] = directBypassCount,
            ["missingInstances"] = missing,
            ["blockers"] = blockers,
            ["reports"] = reports,
            ["state"] = state
        };
    }

    [McpServerTool(Name = "reign_get_government_certification_report", ReadOnly = true,
        Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Returns the durable Government certification state and retained evidence paths for recovery, audit, and handoff without contacting Bannerlord.")]
    public static IReadOnlyDictionary<string, object?> GetGovernmentCertificationReport(
        ReignMcpOptions options,
        string runId)
    {
        runId = RequiredGovernmentCertificationToken(runId, nameof(runId));
        GovernmentCertificationState state = ReadGovernmentCertificationState(options, runId);
        return new Dictionary<string, object?>
        {
            ["ok"] = true,
            ["schema"] = "reign-government-certification-report-v1",
            ["statePath"] = GovernmentCertificationStatePath(options, runId),
            ["validationReportPath"] = state.ValidationReportPath,
            ["deploymentEvidencePath"] = state.DeploymentEvidencePath,
            ["externalEvidence"] = state.ExternalEvidence,
            ["executions"] = state.Executions,
            ["state"] = state
        };
    }

    private static object GovernmentCertificationStep(string profile, string caseId,
        string fixture, string targetSearch, string expectedTag, string expectedPosture,
        string expectedSaveName, string expectedFeatureFingerprint, string previousGameInstanceId,
        JsonElement? soakPreparation = null) => new
    {
        operation = "government_test",
        profile,
        fixtureRunId = caseId,
        caseId,
        fixture,
        targetSearch,
        expectedTag,
        expectedPosture,
        expectedSaveName,
        expectedFeatureFingerprint,
        previousGameInstanceId,
        soakPreparation,
        timeoutSeconds = profile is "soak_verify" or "case_execute" ? 600 : 180
    };

    private static JsonElement RequireGovernmentSoakPreparation(ApiEnvelope report,
        string expectedFeatureFingerprint)
    {
        if (!report.Ok || !report.Data.HasValue
            || !string.Equals(FindGovernmentReportString(report.Data.Value, "status"),
                "completed", StringComparison.OrdinalIgnoreCase)
            || !HasGovernmentPassedAssertion(report.Data.Value, "government_soak_prepare"))
            throw new InvalidOperationException("The retained Government soak preparation report is unavailable or did not pass its preparation gate.");
        JsonElement soak = FindGovernmentReportObject(report.Data.Value, "soak");
        if (soak.ValueKind != JsonValueKind.Object
            || !soak.TryGetProperty("Fingerprint", out JsonElement fingerprint)
            || !string.Equals(fingerprint.GetString(), expectedFeatureFingerprint,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The retained Government soak preparation fingerprint does not match the requested verification evidence.");
        string[] required = { "StartDay", "GovernmentCount", "PartyCount", "SeatCount",
            "ResolutionCount", "PressureCount", "MeetingCount" };
        if (required.Any(name => !soak.TryGetProperty(name, out JsonElement value)
            || value.ValueKind != JsonValueKind.Number))
            throw new InvalidOperationException("The retained Government soak preparation report is missing required bounded-state evidence.");
        return soak.Clone();
    }

    private static JsonElement FindGovernmentReportObject(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty(name, out JsonElement value)
                && value.ValueKind == JsonValueKind.Object) return value;
            foreach (JsonProperty property in element.EnumerateObject())
            {
                JsonElement nested = FindGovernmentReportObject(property.Value, name);
                if (nested.ValueKind == JsonValueKind.Object) return nested;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (JsonElement item in element.EnumerateArray())
            {
                JsonElement nested = FindGovernmentReportObject(item, name);
                if (nested.ValueKind == JsonValueKind.Object) return nested;
            }
        return default;
    }

    private static string[] GovernmentRequiredInstances(JsonElement manifest)
    {
        JsonElement release = manifest.GetProperty("releaseCertification");
        return new[] { "deterministicCases", "naturalLanguageCases", "nativeCases" }
            .SelectMany(group => release.GetProperty(group).EnumerateArray())
            .Select(item =>
            {
                string id = item.GetProperty("id").GetString() ?? string.Empty;
                string variant = item.TryGetProperty("variant", out JsonElement value)
                    ? value.GetString() ?? string.Empty : string.Empty;
                return id + (string.IsNullOrWhiteSpace(variant) ? string.Empty : "::" + variant);
            }).ToArray();
    }

    private static JsonElement FindGovernmentCertificationCase(JsonElement manifest, string caseId)
    {
        JsonElement release = manifest.GetProperty("releaseCertification");
        foreach (string group in new[] { "deterministicCases", "naturalLanguageCases", "nativeCases" })
            foreach (JsonElement item in release.GetProperty(group).EnumerateArray())
                if (string.Equals(item.GetProperty("id").GetString(), caseId,
                        StringComparison.OrdinalIgnoreCase))
                    return item;
        throw new ArgumentException("The requested Government case is not owned by the certification manifest.", nameof(caseId));
    }

    private static string GovernmentCaseGroup(JsonElement testCase)
    {
        if (testCase.TryGetProperty("playerText", out _)) return "naturalLanguageCases";
        if (testCase.GetProperty("id").GetString()?.StartsWith("GOV-DET-",
                StringComparison.OrdinalIgnoreCase) == true) return "deterministicCases";
        return "nativeCases";
    }

    private static string[] GovernmentRequiredAssertions(JsonElement testCase) =>
        testCase.TryGetProperty("requiredAssertions", out JsonElement values)
        && values.ValueKind == JsonValueKind.Array
            ? values.EnumerateArray().Select(item => item.GetString() ?? string.Empty)
                .Where(item => item.Length > 0).ToArray()
            : Array.Empty<string>();

    private static bool GovernmentDefaultCaseAssertionPassed(JsonElement report,
        JsonElement testCase, string caseId)
    {
        string profile = testCase.TryGetProperty("profile", out JsonElement profileElement)
            ? profileElement.GetString() ?? string.Empty : string.Empty;
        return profile switch
        {
            "preflight" => HasGovernmentPassedAssertion(report, "government_all_kingdoms_initialized"),
            "save_prepare" => HasGovernmentPassedAssertion(report, "government_save_prepare_fingerprint"),
            "save_verify" => HasGovernmentPassedAssertion(report, "government_save_verify_fingerprint"),
            "soak_prepare" => HasGovernmentPassedAssertion(report, "government_soak_prepare"),
            "soak_verify" => HasGovernmentPassedAssertion(report, "government_soak_verify"),
            "cleanup" => HasGovernmentPassedAssertion(report, "government_certification_cleanup"),
            "case_execute" => false,
            _ => false
        };
    }

    private static bool GovernmentNaturalLanguageCoreAssertionsPassed(JsonElement report) =>
        HasGovernmentPassedAssertion(report, "government_language_fixture_player_ruler")
        && HasGovernmentPassedAssertion(report, "government_language_fixture_target")
        && HasGovernmentPassedAssertion(report, "government_language_action_direction")
        && HasGovernmentPassedAssertion(report, "government_language_fixture_party_power")
        && HasGovernmentPassedAssertion(report,
            "government_language_fixture_posture_visible_to_production")
        && HasGovernmentPassedAssertion(report, "government_language_fixture_prepared")
        && HasGovernmentPassedAssertion(report, "government_language_expected_tag")
        && HasGovernmentPassedAssertion(report, "government_language_no_native_influence");

    private static bool ContainsGovernmentNaturalLanguageCaseSend(JsonElement element,
        string caseId)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            bool matchingSend = element.TryGetProperty("operation", out JsonElement operation)
                && operation.ValueKind == JsonValueKind.String
                && operation.GetString()?.Equals("send",
                    StringComparison.OrdinalIgnoreCase) == true
                && element.TryGetProperty("naturalLanguageCase",
                    out JsonElement naturalLanguageCase)
                && naturalLanguageCase.ValueKind == JsonValueKind.True
                && element.TryGetProperty("governmentCertificationCaseId",
                    out JsonElement certificationCaseId)
                && certificationCaseId.ValueKind == JsonValueKind.String
                && certificationCaseId.GetString()?.Equals(caseId,
                    StringComparison.OrdinalIgnoreCase) == true
                && element.TryGetProperty("text", out JsonElement text)
                && text.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(text.GetString());
            if (matchingSend) return true;
            foreach (JsonProperty property in element.EnumerateObject())
                if (ContainsGovernmentNaturalLanguageCaseSend(property.Value, caseId))
                    return true;
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (JsonElement item in element.EnumerateArray())
                if (ContainsGovernmentNaturalLanguageCaseSend(item, caseId)) return true;
        return false;
    }

    private static bool HasGovernmentPassedAssertion(JsonElement element, string assertionId)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            bool idMatches = element.TryGetProperty("id", out JsonElement id)
                && id.ValueKind == JsonValueKind.String
                && string.Equals(id.GetString(), assertionId, StringComparison.OrdinalIgnoreCase);
            if (idMatches && element.TryGetProperty("passed", out JsonElement passed)
                && passed.ValueKind == JsonValueKind.True) return true;
            foreach (JsonProperty property in element.EnumerateObject())
                if (HasGovernmentPassedAssertion(property.Value, assertionId)) return true;
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (JsonElement item in element.EnumerateArray())
                if (HasGovernmentPassedAssertion(item, assertionId)) return true;
        return false;
    }

    private static bool ContainsGovernmentText(JsonElement element, string expected)
    {
        if (string.IsNullOrWhiteSpace(expected)) return false;
        if (element.ValueKind == JsonValueKind.String)
            return (element.GetString() ?? string.Empty).Contains(expected,
                StringComparison.OrdinalIgnoreCase);
        if (element.ValueKind == JsonValueKind.Object)
            return element.EnumerateObject().Any(property => ContainsGovernmentText(property.Value, expected));
        if (element.ValueKind == JsonValueKind.Array)
            return element.EnumerateArray().Any(item => ContainsGovernmentText(item, expected));
        return false;
    }

    private static bool ContainsGovernmentDirectActionBypass(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("operation", out JsonElement operation)
                && operation.ValueKind == JsonValueKind.String
                && (operation.GetString() ?? string.Empty).Contains(
                    "consent_government_reduction", StringComparison.OrdinalIgnoreCase))
                return true;
            foreach (JsonProperty property in element.EnumerateObject())
                if (ContainsGovernmentDirectActionBypass(property.Value)) return true;
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (JsonElement item in element.EnumerateArray())
                if (ContainsGovernmentDirectActionBypass(item)) return true;
        return false;
    }

    private static bool GovernmentDialogueContextQuality(JsonElement report,
        JsonElement testCase, bool expectedTagPassed)
    {
        string reply = FindGovernmentReportString(report, "reply");
        if (string.IsNullOrWhiteSpace(reply)) return false;
        string normalized = reply.ToLowerInvariant();
        string expectedTag = testCase.GetProperty("expectedTag").GetString() ?? "none";
        string posture = testCase.TryGetProperty("expectedPosture", out JsonElement postureElement)
            ? postureElement.GetString() ?? string.Empty : string.Empty;
        bool governmentContext = new[]
        {
            "government", "senate", "council", "veche", "oenach", "majlis",
            "kurultai", "thing", "authority", "power", "ruler", "throne", "clan"
        }.Any(normalized.Contains);
        if (!governmentContext) return false;
        if (!expectedTag.Equals("none", StringComparison.OrdinalIgnoreCase))
            return expectedTagPassed && new[]
            {
                "agree", "consent", "support", "with you", "stand with", "my clan", "our clan"
            }.Any(normalized.Contains);
        if (!expectedTagPassed) return false;
        return posture switch
        {
            "refusal" => new[] { " no", "not ", "refuse", "cannot", "can't", "will not", "won't", "never" }
                .Any(normalized.Contains),
            "question" => reply.Contains('?') || new[] { "why", "how", "what", "which", "who" }
                .Any(normalized.Contains),
            "concern" => new[] { "concern", "worry", "risk", "unrest", "cost", "danger", "fear" }
                .Any(normalized.Contains),
            "conditional" => new[] { " if ", "provided", "condition", "once ", "after ", "in return" }
                .Any(normalized.Contains),
            _ => true
        };
    }

    private static string FindGovernmentReportString(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty(name, out JsonElement value)
                && value.ValueKind == JsonValueKind.String) return value.GetString() ?? string.Empty;
            foreach (JsonProperty property in element.EnumerateObject())
            {
                string nested = FindGovernmentReportString(property.Value, name);
                if (nested.Length > 0) return nested;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (JsonElement item in element.EnumerateArray())
            {
                string nested = FindGovernmentReportString(item, name);
                if (nested.Length > 0) return nested;
            }
        return string.Empty;
    }

    private static JsonElement LoadGovernmentCertificationManifest(ReignMcpOptions options)
    {
        string path = Path.Combine(options.WorkspaceRoot, "ReignServer", "tests", "ReignLiveTest",
            "scenarios", "government-system-manifest.json");
        if (!File.Exists(path)) throw new FileNotFoundException(
            "The authoritative Government certification manifest is missing.", path);
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = document.RootElement.Clone();
        if (!root.TryGetProperty("releaseCertification", out _))
            throw new InvalidDataException("The Government manifest does not contain a releaseCertification contract.");
        return root;
    }

    private static string GovernmentCertificationRoot(ReignMcpOptions options) =>
        Path.Combine(options.WorkspaceRoot, ".codex-build", "government-certification");

    private static string GovernmentCertificationStatePath(ReignMcpOptions options, string runId) =>
        Path.Combine(GovernmentCertificationRoot(options), runId, "state.json");

    private static GovernmentCertificationState ReadGovernmentCertificationState(
        ReignMcpOptions options, string runId) => TryReadGovernmentCertificationState(options, runId)
        ?? throw new FileNotFoundException("The Government certification run has not been prepared.",
            GovernmentCertificationStatePath(options, runId));

    private static GovernmentCertificationState? TryReadGovernmentCertificationState(
        ReignMcpOptions options, string runId)
    {
        string path = GovernmentCertificationStatePath(options, runId);
        if (!File.Exists(path)) return null;
        return JsonSerializer.Deserialize<GovernmentCertificationState>(File.ReadAllText(path),
            GovernmentCertificationJson);
    }

    private static void WriteGovernmentCertificationState(ReignMcpOptions options,
        GovernmentCertificationState state)
    {
        string path = GovernmentCertificationStatePath(options, state.RunId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(state, GovernmentCertificationJson));
        File.Move(temporary, path, overwrite: true);
    }

    private static IReadOnlyDictionary<string, object?> GovernmentCertificationEnvelope(
        GovernmentCertificationState state, string status, JsonElement manifest) =>
        new Dictionary<string, object?>
        {
            ["ok"] = true,
            ["schema"] = "reign-government-certification-prepare-v1",
            ["status"] = status,
            ["requiredInstanceCount"] = state.RequiredInstances.Count,
            ["manifestSchema"] = manifest.GetProperty("releaseCertification").GetProperty("schema").GetString(),
            ["state"] = state
        };

    private static bool GovernmentImmutableInputsMatch(GovernmentCertificationState first,
        GovernmentCertificationState second) =>
        new[]
        {
            first.CampaignId, first.CampaignTestRunId, first.TimelineId,
            first.ProtectedBaselineSaveName, first.DisposableSaveName, first.DisposableSavePrefix,
            first.SourceFingerprint, first.ClientBuild, first.ServerBuild,
            first.ProviderConfigurationFingerprint, first.CatalogFingerprint,
            first.BannerlordVersion, first.ValidationReportPath, first.DeploymentEvidencePath
        }.SequenceEqual(new[]
        {
            second.CampaignId, second.CampaignTestRunId, second.TimelineId,
            second.ProtectedBaselineSaveName, second.DisposableSaveName, second.DisposableSavePrefix,
            second.SourceFingerprint, second.ClientBuild, second.ServerBuild,
            second.ProviderConfigurationFingerprint, second.CatalogFingerprint,
            second.BannerlordVersion, second.ValidationReportPath, second.DeploymentEvidencePath
        }, StringComparer.OrdinalIgnoreCase);

    private static string RequiredGovernmentCertificationToken(string value, string name)
    {
        string token = InputGuard.BoundedText(value ?? string.Empty, name, 120).Trim();
        if (token.Length == 0 || token.Any(ch => !char.IsLetterOrDigit(ch) && ch is not '_' and not '-'))
            throw new ArgumentException("A non-empty letters/digits/dash/underscore token is required.", name);
        return token;
    }

    private static string RequiredGovernmentCaseId(string value)
    {
        string id = InputGuard.BoundedText(value ?? string.Empty, nameof(value), 80).Trim().ToUpperInvariant();
        if (!id.StartsWith("GOV-", StringComparison.Ordinal)
            || id.Any(ch => !char.IsLetterOrDigit(ch) && ch != '-'))
            throw new ArgumentException("A manifest-owned GOV-* case id is required.", nameof(value));
        return id;
    }

    private static string RequiredGovernmentFingerprint(string value, string name,
        bool requireSha256 = true)
    {
        string fingerprint = InputGuard.BoundedText(value ?? string.Empty, name,
            requireSha256 ? 64 : 120).Trim();
        if (requireSha256 && (fingerprint.Length != 64
            || fingerprint.Any(ch => !Uri.IsHexDigit(ch))))
            throw new ArgumentException("A 64-character SHA-256 fingerprint is required.", name);
        if (!requireSha256 && fingerprint.Length == 0)
            throw new ArgumentException("A non-empty immutable version fingerprint is required.", name);
        return fingerprint;
    }

    private static string RequiredGovernmentEvidencePath(ReignMcpOptions options,
        string value, string name)
    {
        string path = Path.GetFullPath(InputGuard.BoundedText(value ?? string.Empty, name, 500));
        string root = Path.GetFullPath(options.WorkspaceRoot).TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
            throw new ArgumentException("Government evidence must be an existing file inside the Reign workspace.", name);
        return path;
    }

    private static string GovernmentLiveRunId(string runId, string instance, int attempt)
    {
        string raw = "government-cert-" + runId + "-" + instance;
        string safe = new(raw.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_'
            ? char.ToLowerInvariant(ch) : '-').ToArray());
        string suffix = attempt <= 1 ? string.Empty : "-attempt-" + attempt;
        int baseLength = 118 - suffix.Length;
        if (safe.Length > baseLength) safe = safe[..baseLength];
        return safe + suffix;
    }

    private sealed class GovernmentCertificationState
    {
        public string RunId { get; set; } = string.Empty;
        public string CampaignId { get; set; } = string.Empty;
        public string CampaignTestRunId { get; set; } = string.Empty;
        public string TimelineId { get; set; } = string.Empty;
        public string ProtectedBaselineSaveName { get; set; } = string.Empty;
        public string DisposableSaveName { get; set; } = string.Empty;
        public string DisposableSavePrefix { get; set; } = string.Empty;
        public string SourceFingerprint { get; set; } = string.Empty;
        public string ClientBuild { get; set; } = string.Empty;
        public string ServerBuild { get; set; } = string.Empty;
        public string ProviderConfigurationFingerprint { get; set; } = string.Empty;
        public string CatalogFingerprint { get; set; } = string.Empty;
        public string BannerlordVersion { get; set; } = string.Empty;
        public string ValidationReportPath { get; set; } = string.Empty;
        public string DeploymentEvidencePath { get; set; } = string.Empty;
        public List<string> RequiredInstances { get; set; } = new();
        public List<GovernmentCertificationExecution> Executions { get; set; } = new();
        public List<GovernmentCertificationEvidence> ExternalEvidence { get; set; } = new();
        public List<GovernmentCertificationCarriedEvidence> CarriedEvidence { get; set; } = new();
        public string PreparedUtc { get; set; } = string.Empty;
        public string UpdatedUtc { get; set; } = string.Empty;
    }

    private sealed class GovernmentCertificationExecution
    {
        public string CaseId { get; set; } = string.Empty;
        public string Instance { get; set; } = string.Empty;
        public string Group { get; set; } = string.Empty;
        public string Profile { get; set; } = string.Empty;
        public string Variant { get; set; } = string.Empty;
        public string LiveRunId { get; set; } = string.Empty;
        public bool NaturalLanguage { get; set; }
        public int Attempt { get; set; }
        public bool StartAccepted { get; set; }
        public string StartedUtc { get; set; } = string.Empty;
    }

    private sealed class GovernmentCertificationEvidence
    {
        public string CaseId { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string Sha256 { get; set; } = string.Empty;
        public string ReviewNote { get; set; } = string.Empty;
        public string RecordedUtc { get; set; } = string.Empty;
    }

    private sealed class GovernmentCertificationCarriedEvidence
    {
        public string Instance { get; set; } = string.Empty;
        public string SourceRunId { get; set; } = string.Empty;
        public string SourceFingerprint { get; set; } = string.Empty;
        public string SourceClientBuild { get; set; } = string.Empty;
        public string SourceServerBuild { get; set; } = string.Empty;
        public string SourceCatalogFingerprint { get; set; } = string.Empty;
        public string LiveRunId { get; set; } = string.Empty;
        public string ReportPath { get; set; } = string.Empty;
        public string ReportSha256 { get; set; } = string.Empty;
        public string ChangeScope { get; set; } = string.Empty;
        public string CarriedUtc { get; set; } = string.Empty;
    }
}
