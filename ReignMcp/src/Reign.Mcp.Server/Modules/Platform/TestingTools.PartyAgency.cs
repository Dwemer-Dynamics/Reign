using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Reign.Mcp.Server;

public static partial class TestingTools
{
    private static readonly object PartyAgencyStateGate = new();
    private static readonly JsonSerializerOptions PartyAgencyJson = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private static readonly string[] PartyAgencyProfiles =
    {
        "preflight", "language_contract", "eligibility_detachment", "waiting_camp",
        "reviews", "returns", "save_roundtrip", "hostility", "recovery_regression", "evaluate"
    };

    [McpServerTool(Name = "reign_get_party_agency_test_manifest", ReadOnly = true,
        Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Returns the autonomous Temporary Noble Party Guest natural-language case matrix, native variants, safety gates, evidence contract, and release thresholds without starting or mutating a campaign.")]
    public static IReadOnlyDictionary<string, object?> GetPartyAgencyTestManifest(
        ReignMcpOptions options)
    {
        JsonElement manifest = LoadPartyAgencyManifest(options);
        return new Dictionary<string, object?>
        {
            ["ok"] = true,
            ["schema"] = "reign-party-agency-certification-manifest-v2",
            ["manifestPath"] = PartyAgencyManifestPath(options),
            ["manifest"] = JsonSerializer.Deserialize<object>(manifest.GetRawText())
        };
    }

    [McpServerTool(Name = "reign_prepare_party_agency_test", ReadOnly = false,
        Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Binds Party Agency certification to an existing guarded campaign-test enrollment and immutable build, provider, catalog, and Bannerlord fingerprints. Preparation never starts Bannerlord or mutates the campaign.")]
    public static IReadOnlyDictionary<string, object?> PreparePartyAgencyTest(
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
        campaignId = RequiredPartyAgencyIdentifier(campaignId, nameof(campaignId));
        campaignTestRunId = RequiredPartyAgencyIdentifier(campaignTestRunId, nameof(campaignTestRunId));
        runId = RequiredPartyAgencyIdentifier(runId, nameof(runId));
        CampaignTestAuthorization authorization = campaignTests.RequireEnrollment(
            campaignTestRunId, campaignId);
        JsonElement manifest = LoadPartyAgencyManifest(options);
        string[] requiredInstances = RequiredPartyAgencyInstances(manifest).ToArray();
        var requested = new PartyAgencyCertificationState
        {
            RunId = runId,
            CampaignId = campaignId,
            CampaignTestRunId = campaignTestRunId,
            TimelineId = authorization.TimelineId,
            ProtectedBaselineSaveName = authorization.BaselineSaveName,
            DisposableSaveName = authorization.CurrentSaveName,
            DisposableSavePrefix = authorization.SavePrefix,
            SourceFingerprint = RequiredPartyAgencyFingerprint(sourceFingerprint, nameof(sourceFingerprint)),
            ClientBuild = RequiredPartyAgencyFingerprint(clientBuild, nameof(clientBuild)),
            ServerBuild = RequiredPartyAgencyFingerprint(serverBuild, nameof(serverBuild)),
            ProviderConfigurationFingerprint = RequiredPartyAgencyFingerprint(
                providerConfigurationFingerprint, nameof(providerConfigurationFingerprint)),
            CatalogFingerprint = RequiredPartyAgencyFingerprint(catalogFingerprint, nameof(catalogFingerprint)),
            BannerlordVersion = RequiredPartyAgencyFingerprint(bannerlordVersion, nameof(bannerlordVersion)),
            RequiredInstances = requiredInstances.ToList(),
            PreparedUtc = DateTime.UtcNow.ToString("O"),
            UpdatedUtc = DateTime.UtcNow.ToString("O")
        };

        lock (PartyAgencyStateGate)
        {
            PartyAgencyCertificationState? existing = TryReadPartyAgencyState(options, runId);
            if (existing is not null)
            {
                if (!PartyAgencyImmutableInputsMatch(existing, requested))
                    throw new InvalidOperationException("The Party Agency run id is already prepared with different immutable enrollment or fingerprint inputs.");
                return PartyAgencyStateEnvelope(existing, "already_prepared", manifest);
            }
            WritePartyAgencyState(options, requested);
        }
        return PartyAgencyStateEnvelope(requested, "prepared", manifest);
    }

    [McpServerTool(Name = "reign_carry_forward_party_agency_passes", ReadOnly = false,
        Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Revalidates durable reports for explicitly selected Party Agency cases and carries only passes unaffected by an audited equipment-lock, guest-relationship-isolation, protected-camp-encounter-exclusion-only, hostile-battle-fixture-only, hostility-language-fixture-only, partyless-invitation-fixture-only, remaining-native-invitation-fixture-only, ineligible-native-fixture-only, companion-native-fixture-only, native-time-driver-only, save-roundtrip-state-driver-only, accepted-lifecycle-router-only, simultaneous-review-queue-only, departure-language-fixture-only, return-timing-evidence-only, recovery-fixture-only, missing-return-recovery-fixture-only, final-fingerprint-harness-only, or transcript-evidence-parser-only change into a new fingerprint-bound certification run with full provenance. It never starts Bannerlord or mutates a campaign.")]
    public static async Task<IReadOnlyDictionary<string, object?>> CarryForwardPartyAgencyPasses(
        ReignApiClient api,
        ReignMcpOptions options,
        string sourceRunId,
        string destinationRunId,
        [Description("Semicolon-separated exact case instances such as PA-LANG-001 or PA-NATIVE-001::partyless.")]
        string caseInstances,
        [AllowedValues("temporary_guest_equipment_lock_only",
            "temporary_guest_relationship_isolation_only",
            "temporary_guest_waiting_camp_encounter_exclusion_only",
            "party_agency_hostile_battle_fixture_only",
            "party_agency_hostility_language_fixture_only",
            "party_agency_partyless_invitation_fixture_only",
            "party_agency_remaining_native_invitation_fixture_only",
            "party_agency_ineligible_native_fixture_only",
            "party_agency_companion_native_fixture_only",
            "party_agency_native_time_driver_only",
            "party_agency_save_roundtrip_state_driver_only",
            "party_agency_accepted_lifecycle_router_only",
            "party_agency_simultaneous_review_queue_only",
            "party_agency_departure_language_fixture_only",
            "party_agency_return_timing_evidence_only",
            "party_agency_recovery_fixture_only",
            "party_agency_missing_return_recovery_fixture_only",
            "party_agency_final_fingerprint_harness_only",
            "party_agency_transcript_evidence_parser_only")]
        string changeScope,
        [Description("Exact text required: carry forward unaffected Reign Party Agency passes")]
        string confirmation = "",
        CancellationToken cancellationToken = default)
    {
        InputGuard.RequireConfirmation(confirmation,
            "carry forward unaffected Reign Party Agency passes");
        sourceRunId = RequiredPartyAgencyIdentifier(sourceRunId, nameof(sourceRunId));
        destinationRunId = RequiredPartyAgencyIdentifier(destinationRunId, nameof(destinationRunId));
        bool equipmentLockOnly = string.Equals(changeScope,
            "temporary_guest_equipment_lock_only", StringComparison.Ordinal);
        bool relationshipIsolationOnly = string.Equals(changeScope,
            "temporary_guest_relationship_isolation_only", StringComparison.Ordinal);
        bool waitingCampEncounterExclusionOnly = string.Equals(changeScope,
            "temporary_guest_waiting_camp_encounter_exclusion_only", StringComparison.Ordinal);
        bool hostileBattleFixtureOnly = string.Equals(changeScope,
            "party_agency_hostile_battle_fixture_only", StringComparison.Ordinal);
        bool hostilityLanguageFixtureOnly = string.Equals(changeScope,
            "party_agency_hostility_language_fixture_only", StringComparison.Ordinal);
        bool partylessInvitationFixtureOnly = string.Equals(changeScope,
            "party_agency_partyless_invitation_fixture_only", StringComparison.Ordinal);
        bool remainingNativeInvitationFixtureOnly = string.Equals(changeScope,
            "party_agency_remaining_native_invitation_fixture_only", StringComparison.Ordinal);
        bool ineligibleNativeFixtureOnly = string.Equals(changeScope,
            "party_agency_ineligible_native_fixture_only", StringComparison.Ordinal);
        bool companionNativeFixtureOnly = string.Equals(changeScope,
            "party_agency_companion_native_fixture_only", StringComparison.Ordinal);
        bool nativeTimeDriverOnly = string.Equals(changeScope,
            "party_agency_native_time_driver_only", StringComparison.Ordinal);
        bool saveRoundtripStateDriverOnly = string.Equals(changeScope,
            "party_agency_save_roundtrip_state_driver_only", StringComparison.Ordinal);
        bool acceptedLifecycleRouterOnly = string.Equals(changeScope,
            "party_agency_accepted_lifecycle_router_only", StringComparison.Ordinal);
        bool simultaneousReviewQueueOnly = string.Equals(changeScope,
            "party_agency_simultaneous_review_queue_only", StringComparison.Ordinal);
        bool departureLanguageFixtureOnly = string.Equals(changeScope,
            "party_agency_departure_language_fixture_only", StringComparison.Ordinal);
        bool returnTimingEvidenceOnly = string.Equals(changeScope,
            "party_agency_return_timing_evidence_only", StringComparison.Ordinal);
        bool recoveryFixtureOnly = string.Equals(changeScope,
            "party_agency_recovery_fixture_only", StringComparison.Ordinal);
        bool missingReturnRecoveryFixtureOnly = string.Equals(changeScope,
            "party_agency_missing_return_recovery_fixture_only", StringComparison.Ordinal);
        bool finalFingerprintHarnessOnly = string.Equals(changeScope,
            "party_agency_final_fingerprint_harness_only", StringComparison.Ordinal);
        bool transcriptEvidenceParserOnly = string.Equals(changeScope,
            "party_agency_transcript_evidence_parser_only", StringComparison.Ordinal);
        if (!equipmentLockOnly && !relationshipIsolationOnly
            && !waitingCampEncounterExclusionOnly && !hostileBattleFixtureOnly
            && !hostilityLanguageFixtureOnly && !partylessInvitationFixtureOnly
            && !remainingNativeInvitationFixtureOnly
            && !ineligibleNativeFixtureOnly
            && !companionNativeFixtureOnly
            && !nativeTimeDriverOnly
            && !saveRoundtripStateDriverOnly
            && !acceptedLifecycleRouterOnly
            && !simultaneousReviewQueueOnly
            && !departureLanguageFixtureOnly
            && !returnTimingEvidenceOnly
            && !recoveryFixtureOnly
            && !missingReturnRecoveryFixtureOnly
            && !finalFingerprintHarnessOnly
            && !transcriptEvidenceParserOnly)
            throw new ArgumentException("Only audited temporary guest equipment-lock, relationship-isolation, waiting-camp-encounter-exclusion, hostile-battle-fixture, hostility-language-fixture, partyless-invitation-fixture, remaining-native-invitation-fixture, ineligible-native-fixture, companion-native-fixture, native-time-driver, save-roundtrip-state-driver, accepted-lifecycle-router, simultaneous-review-queue, departure-language-fixture, return-timing-evidence, recovery-fixture, missing-return-recovery-fixture, final-fingerprint-harness, and transcript-evidence-parser scopes are supported.",
                nameof(changeScope));
        PartyAgencyCertificationState source = ReadPartyAgencyState(options, sourceRunId);
        PartyAgencyCertificationState destination = ReadPartyAgencyState(options, destinationRunId);
        if (!string.Equals(source.CampaignId, destination.CampaignId,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(source.CampaignTestRunId, destination.CampaignTestRunId,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(source.ProtectedBaselineSaveName,
                destination.ProtectedBaselineSaveName, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(source.DisposableSaveName, destination.DisposableSaveName,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(source.ProviderConfigurationFingerprint,
                destination.ProviderConfigurationFingerprint, StringComparison.Ordinal)
            || (equipmentLockOnly && !string.Equals(source.ServerBuild,
                destination.ServerBuild, StringComparison.Ordinal))
            || (relationshipIsolationOnly && !string.Equals(source.ClientBuild,
                destination.ClientBuild, StringComparison.Ordinal))
            || (waitingCampEncounterExclusionOnly && !string.Equals(source.ServerBuild,
                destination.ServerBuild, StringComparison.Ordinal))
            || (hostileBattleFixtureOnly && !string.Equals(source.ServerBuild,
                destination.ServerBuild, StringComparison.Ordinal))
            || (hostilityLanguageFixtureOnly
                && (!string.Equals(source.ClientBuild, destination.ClientBuild,
                        StringComparison.Ordinal)
                    || !string.Equals(source.ServerBuild, destination.ServerBuild,
                        StringComparison.Ordinal)))
            || (partylessInvitationFixtureOnly
                && (!string.Equals(source.ClientBuild, destination.ClientBuild,
                        StringComparison.Ordinal)
                    || !string.Equals(source.ServerBuild, destination.ServerBuild,
                        StringComparison.Ordinal)))
            || (remainingNativeInvitationFixtureOnly
                && (!string.Equals(source.ClientBuild, destination.ClientBuild,
                        StringComparison.Ordinal)
                    || !string.Equals(source.ServerBuild, destination.ServerBuild,
                        StringComparison.Ordinal)))
            || (ineligibleNativeFixtureOnly && !string.Equals(source.ServerBuild,
                destination.ServerBuild, StringComparison.Ordinal))
            || (companionNativeFixtureOnly && !string.Equals(source.ServerBuild,
                destination.ServerBuild, StringComparison.Ordinal))
            || (nativeTimeDriverOnly && !string.Equals(source.ServerBuild,
                destination.ServerBuild, StringComparison.Ordinal))
            || (saveRoundtripStateDriverOnly && !string.Equals(source.ServerBuild,
                destination.ServerBuild, StringComparison.Ordinal))
            || (acceptedLifecycleRouterOnly && !string.Equals(source.ClientBuild,
                destination.ClientBuild, StringComparison.Ordinal))
            || (simultaneousReviewQueueOnly && !string.Equals(source.ServerBuild,
                destination.ServerBuild, StringComparison.Ordinal))
            || (departureLanguageFixtureOnly
                && (!string.Equals(source.ClientBuild, destination.ClientBuild,
                        StringComparison.Ordinal)
                    || !string.Equals(source.ServerBuild, destination.ServerBuild,
                        StringComparison.Ordinal)))
            || (returnTimingEvidenceOnly && !string.Equals(source.ServerBuild,
                destination.ServerBuild, StringComparison.Ordinal))
            || (recoveryFixtureOnly && !string.Equals(source.ServerBuild,
                destination.ServerBuild, StringComparison.Ordinal))
            || (missingReturnRecoveryFixtureOnly && !string.Equals(source.ServerBuild,
                destination.ServerBuild, StringComparison.Ordinal))
            || (finalFingerprintHarnessOnly
                && (!string.Equals(source.ClientBuild, destination.ClientBuild,
                        StringComparison.Ordinal)
                    || !string.Equals(source.ServerBuild, destination.ServerBuild,
                        StringComparison.Ordinal)))
            || (transcriptEvidenceParserOnly
                && (!string.Equals(source.ClientBuild, destination.ClientBuild,
                        StringComparison.Ordinal)
                    || !string.Equals(source.ServerBuild, destination.ServerBuild,
                        StringComparison.Ordinal)))
            || !string.Equals(source.BannerlordVersion, destination.BannerlordVersion,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Carry-forward requires identical enrollment, provider, Bannerlord, and unaffected runtime fingerprints.");

        JsonElement manifest = LoadPartyAgencyManifest(options);
        string[] requested = InputGuard.BoundedText(caseInstances ?? string.Empty,
                nameof(caseInstances), 4000)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (requested.Length == 0) throw new ArgumentException(
            "At least one exact case instance is required.", nameof(caseInstances));
        var carried = new List<object>();
        foreach (string instance in requested)
        {
            if (!destination.RequiredInstances.Contains(instance,
                    StringComparer.OrdinalIgnoreCase))
                throw new ArgumentException("Unknown or non-required Party Agency instance '"
                    + instance + "'.", nameof(caseInstances));
            bool affectedRegression = !waitingCampEncounterExclusionOnly
                && !hostileBattleFixtureOnly
                && !hostilityLanguageFixtureOnly
                && !partylessInvitationFixtureOnly
                && !remainingNativeInvitationFixtureOnly
                && !ineligibleNativeFixtureOnly
                && !companionNativeFixtureOnly
                && !nativeTimeDriverOnly
                && !saveRoundtripStateDriverOnly
                && !acceptedLifecycleRouterOnly
                && !simultaneousReviewQueueOnly
                && !departureLanguageFixtureOnly
                && !returnTimingEvidenceOnly
                && !recoveryFixtureOnly
                && !missingReturnRecoveryFixtureOnly
                && !finalFingerprintHarnessOnly
                && !transcriptEvidenceParserOnly
                && instance.StartsWith("PA-NATIVE-021", StringComparison.OrdinalIgnoreCase);
            bool affectedHostileBattle = hostileBattleFixtureOnly
                && (instance.StartsWith("PA-NATIVE-018", StringComparison.OrdinalIgnoreCase)
                    || instance.StartsWith("PA-NATIVE-019", StringComparison.OrdinalIgnoreCase));
            bool affectedWaitingCampEncounter = waitingCampEncounterExclusionOnly
                && (instance.StartsWith("PA-NATIVE-009", StringComparison.OrdinalIgnoreCase)
                    || instance.StartsWith("PA-NATIVE-010", StringComparison.OrdinalIgnoreCase)
                    || instance.StartsWith("PA-NATIVE-018", StringComparison.OrdinalIgnoreCase)
                    || instance.StartsWith("PA-NATIVE-019", StringComparison.OrdinalIgnoreCase));
            bool affectedHostilityLanguage = hostilityLanguageFixtureOnly
                && (instance.Equals("PA-LANG-017", StringComparison.OrdinalIgnoreCase)
                    || instance.Equals("PA-LANG-018", StringComparison.OrdinalIgnoreCase));
            bool affectedPartylessInvitation = partylessInvitationFixtureOnly
                && instance.Equals("PA-NATIVE-001::partyless",
                    StringComparison.OrdinalIgnoreCase);
            bool affectedRemainingNativeInvitation = remainingNativeInvitationFixtureOnly
                && instance.StartsWith("PA-NATIVE-", StringComparison.OrdinalIgnoreCase)
                && !instance.StartsWith("PA-NATIVE-001", StringComparison.OrdinalIgnoreCase)
                && !instance.StartsWith("PA-NATIVE-002", StringComparison.OrdinalIgnoreCase)
                && !instance.StartsWith("PA-NATIVE-018", StringComparison.OrdinalIgnoreCase)
                && !instance.StartsWith("PA-NATIVE-021", StringComparison.OrdinalIgnoreCase);
            bool affectedIneligibleNativeFixture = ineligibleNativeFixtureOnly
                && instance.StartsWith("PA-NATIVE-008", StringComparison.OrdinalIgnoreCase);
            bool affectedCompanionNativeFixture = companionNativeFixtureOnly
                && instance.Equals("PA-NATIVE-008::companion",
                    StringComparison.OrdinalIgnoreCase);
            bool affectedNativeTimeDriver = nativeTimeDriverOnly
                && (instance.StartsWith("PA-NATIVE-009", StringComparison.OrdinalIgnoreCase)
                    || instance.StartsWith("PA-NATIVE-010", StringComparison.OrdinalIgnoreCase)
                    || instance.StartsWith("PA-NATIVE-011", StringComparison.OrdinalIgnoreCase)
                    || instance.StartsWith("PA-NATIVE-012", StringComparison.OrdinalIgnoreCase)
                    || instance.StartsWith("PA-NATIVE-013", StringComparison.OrdinalIgnoreCase)
                    || instance.StartsWith("PA-NATIVE-014", StringComparison.OrdinalIgnoreCase)
                    || instance.StartsWith("PA-NATIVE-015", StringComparison.OrdinalIgnoreCase)
                    || instance.StartsWith("PA-NATIVE-016", StringComparison.OrdinalIgnoreCase));
            bool affectedSaveRoundtripStateDriver = saveRoundtripStateDriverOnly
                && (instance.Equals("PA-NATIVE-017::Returning", StringComparison.OrdinalIgnoreCase)
                    || instance.Equals("PA-NATIVE-017::Completed", StringComparison.OrdinalIgnoreCase));
            bool affectedSimultaneousReviewQueue = simultaneousReviewQueueOnly
                && instance.StartsWith("PA-NATIVE-013", StringComparison.OrdinalIgnoreCase);
            bool affectedDepartureLanguage = departureLanguageFixtureOnly
                && (instance.StartsWith("PA-NATIVE-015", StringComparison.OrdinalIgnoreCase)
                    || instance.StartsWith("PA-NATIVE-016", StringComparison.OrdinalIgnoreCase)
                    || instance.StartsWith("PA-NATIVE-017", StringComparison.OrdinalIgnoreCase)
                    || instance.StartsWith("PA-NATIVE-020", StringComparison.OrdinalIgnoreCase));
            bool affectedReturnTimingEvidence = returnTimingEvidenceOnly
                && instance.StartsWith("PA-NATIVE-015", StringComparison.OrdinalIgnoreCase);
            bool affectedRecoveryFixture = recoveryFixtureOnly
                && instance.StartsWith("PA-NATIVE-020", StringComparison.OrdinalIgnoreCase);
            bool affectedMissingReturnRecoveryFixture = missingReturnRecoveryFixtureOnly
                && (instance.Equals("PA-NATIVE-020::missing_source_party",
                        StringComparison.OrdinalIgnoreCase)
                    || instance.Equals("PA-NATIVE-020::invalid_return_target",
                        StringComparison.OrdinalIgnoreCase));
            if (affectedRegression || affectedHostileBattle || affectedWaitingCampEncounter
                || affectedHostilityLanguage
                || affectedPartylessInvitation
                || affectedRemainingNativeInvitation
                || affectedIneligibleNativeFixture
                || affectedCompanionNativeFixture
                || affectedNativeTimeDriver
                || affectedSaveRoundtripStateDriver
                || affectedSimultaneousReviewQueue
                || affectedDepartureLanguage
                || affectedReturnTimingEvidence
                || affectedRecoveryFixture
                || affectedMissingReturnRecoveryFixture
                || instance.StartsWith("PA-NATIVE-022", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(instance
                    + " is affected by the audited guest change or final fingerprint and cannot be carried forward.");
            string[] parts = instance.Split(new[] { "::" }, 2, StringSplitOptions.None);
            string caseId = RequiredPartyAgencyCaseId(parts[0]);
            string variant = parts.Length == 2
                ? OptionalPartyAgencyToken(parts[1], nameof(caseInstances)) : string.Empty;
            JsonElement testCase = FindPartyAgencyCase(manifest, caseId);
            ValidatePartyAgencyVariant(testCase, variant);
            PartyAgencyExecution[] related = source.Executions.Where(item =>
                    string.Equals(item.CaseId, caseId, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(item.Variant, variant, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            PartyAgencyExecution? execution = related.AsEnumerable().Reverse()
                .FirstOrDefault(item => string.Equals(item.CaseId, caseId,
                        StringComparison.OrdinalIgnoreCase)
                    && string.Equals(item.Variant, variant,
                        StringComparison.OrdinalIgnoreCase));
            if (execution is null) throw new InvalidOperationException(
                "The source run has no execution for " + instance + ".");
            ApiEnvelope report = await api.GetAsync("/tests/live/run/report",
                new Dictionary<string, string?>
                {
                    ["campaignId"] = source.CampaignId,
                    ["runId"] = execution.LiveRunId
                }, cancellationToken).ConfigureAwait(false);
            bool completed = report.Ok && report.Data.HasValue
                && string.Equals(FindPartyAgencyString(report.Data.Value, "status"),
                    "completed", StringComparison.OrdinalIgnoreCase);
            string[] requiredTurns = PartyAgencyAllRequiredTurns(manifest, testCase, variant);
            bool splitSaveRoundtripEvidence = caseId.Equals("PA-NATIVE-017",
                StringComparison.OrdinalIgnoreCase);
            var transcriptReports = new List<ApiEnvelope> { report };
            if (splitSaveRoundtripEvidence)
                foreach (PartyAgencyExecution relatedExecution in related)
                {
                    if (string.Equals(relatedExecution.LiveRunId, execution.LiveRunId,
                            StringComparison.OrdinalIgnoreCase)) continue;
                    transcriptReports.Add(await api.GetAsync("/tests/live/run/report",
                        new Dictionary<string, string?>
                        {
                            ["campaignId"] = source.CampaignId,
                            ["runId"] = relatedExecution.LiveRunId
                        }, cancellationToken).ConfigureAwait(false));
                }
            bool transcriptComplete = requiredTurns.All(turn => transcriptReports.Any(
                candidate => candidate.Data.HasValue && ContainsPartyAgencyTranscriptTurn(
                    candidate.Data.Value, turn)));
            bool noDirectBypass = transcriptReports.All(candidate => !candidate.Data.HasValue
                || !ContainsPartyAgencyDirectActionOperation(candidate.Data.Value));
            if (!completed || !transcriptComplete || !noDirectBypass)
                throw new InvalidOperationException("The durable source report for " + instance
                    + " does not pass the current manifest and bypass checks.");

            foreach (PartyAgencyExecution item in related)
                if (!destination.Executions.Any(existing => string.Equals(existing.LiveRunId,
                        item.LiveRunId, StringComparison.OrdinalIgnoreCase)))
                    destination.Executions.Add(item);
            if (!destination.CarriedEvidence.Any(item =>
                    string.Equals(item.Instance, instance, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(item.SourceRunId, sourceRunId,
                        StringComparison.OrdinalIgnoreCase)))
                destination.CarriedEvidence.Add(new PartyAgencyCarriedEvidence
                {
                    Instance = instance,
                    SourceRunId = sourceRunId,
                    SourceFingerprint = source.SourceFingerprint,
                    SourceClientBuild = source.ClientBuild,
                    SourceCatalogFingerprint = source.CatalogFingerprint,
                    LiveRunId = execution.LiveRunId,
                    ChangeScope = changeScope,
                    CarriedUtc = DateTime.UtcNow.ToString("O")
                });
            carried.Add(new { instance, execution.LiveRunId, sourceRunId,
                source.SourceFingerprint, source.ClientBuild, source.CatalogFingerprint });
        }
        lock (PartyAgencyStateGate) WritePartyAgencyState(options, destination);
        return new Dictionary<string, object?>
        {
            ["ok"] = true,
            ["schema"] = "reign-party-agency-carried-evidence-v1",
            ["changeScope"] = changeScope,
            ["carried"] = carried,
            ["state"] = destination
        };
    }

    [McpServerTool(Name = "reign_start_party_agency_test", ReadOnly = false,
        Destructive = true, Idempotent = false, UseStructuredContent = true)]
    [Description("Starts one manifest-owned Party Agency case through the visible live bridge after proving the exact armed campaign-test Current save. Guest decisions use only the case's natural-language production dialogue turns.")]
    public static async Task<IReadOnlyDictionary<string, object?>> StartPartyAgencyTest(
        ReignApiClient api,
        ReignMcpOptions options,
        CampaignTestService campaignTests,
        string campaignId,
        string campaignTestRunId,
        string runId,
        [AllowedValues("preflight", "language_contract", "eligibility_detachment", "waiting_camp",
            "reviews", "returns", "save_roundtrip", "hostility", "recovery_regression", "evaluate")]
        string profile,
        string caseId,
        string variant = "",
        string targetSearch = "",
        [Description("Durable MCP or campaign-test evidence receipt required by longitudinal, save/load, battle, recovery, regression, and final-evaluation cases.")]
        string evidenceReceipt = "",
        [Description("Exact text required: start Reign Party Agency test on disposable save")]
        string confirmation = "",
        CancellationToken cancellationToken = default)
    {
        if (!options.AllowVerificationControl)
            throw new InvalidOperationException("Verification control is disabled. Set REIGN_MCP_ALLOW_VERIFICATION_CONTROL=true in the trusted MCP configuration.");
        InputGuard.RequireConfirmation(confirmation,
            "start Reign Party Agency test on disposable save");
        campaignId = RequiredPartyAgencyIdentifier(campaignId, nameof(campaignId));
        campaignTestRunId = RequiredPartyAgencyIdentifier(campaignTestRunId, nameof(campaignTestRunId));
        runId = RequiredPartyAgencyIdentifier(runId, nameof(runId));
        profile = NormalizePartyAgencyProfile(profile);
        caseId = RequiredPartyAgencyCaseId(caseId);
        variant = OptionalPartyAgencyToken(variant, nameof(variant));
        targetSearch = InputGuard.BoundedText(targetSearch ?? string.Empty,
            nameof(targetSearch), 200);
        evidenceReceipt = InputGuard.BoundedText(evidenceReceipt ?? string.Empty,
            nameof(evidenceReceipt), 500);

        PartyAgencyCertificationState state = ReadPartyAgencyState(options, runId);
        if (!string.Equals(state.CampaignId, campaignId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(state.CampaignTestRunId, campaignTestRunId,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The requested Party Agency case does not match its prepared guarded campaign-test enrollment.");
        CampaignTestAuthorization authorization = await campaignTests.RequireArmedOwnedSaveAsync(
            campaignTestRunId, campaignId, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(authorization.CurrentSaveName, state.DisposableSaveName,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The prepared Party Agency disposable save no longer matches campaign-test enrollment.");

        JsonElement manifest = LoadPartyAgencyManifest(options);
        JsonElement testCase = FindPartyAgencyCase(manifest, caseId);
        string manifestProfile = testCase.GetProperty("profile").GetString() ?? string.Empty;
        if (!string.Equals(profile, manifestProfile, StringComparison.Ordinal))
            throw new ArgumentException("The requested profile does not own the selected Party Agency case.", nameof(profile));
        ValidatePartyAgencyVariant(testCase, variant);
        bool nativeCase = caseId.StartsWith("PA-NATIVE-", StringComparison.Ordinal);
        bool requiresTarget = testCase.TryGetProperty("requiresTarget", out JsonElement requiresTargetElement)
            && requiresTargetElement.ValueKind == JsonValueKind.True;
        if (requiresTarget && !nativeCase && string.IsNullOrWhiteSpace(targetSearch))
            throw new ArgumentException("This Party Agency case requires a real target hero search or string id.", nameof(targetSearch));

        string dialogueMode = testCase.TryGetProperty("dialogueMode", out JsonElement dialogueModeElement)
            ? dialogueModeElement.GetString() ?? string.Empty : string.Empty;
        string reviewContext = testCase.TryGetProperty("reviewContext", out JsonElement reviewElement)
            ? reviewElement.GetString() ?? string.Empty : string.Empty;
        JsonElement expected = testCase.GetProperty("expected");
        string liveRunId = PartyAgencyLiveRunId(runId, caseId, variant);
        var steps = new List<object>();
        JsonElement dialogueOwner = PartyAgencyDialogueOwner(manifest, testCase);
        string[] naturalTurns = PartyAgencyNaturalTurns(dialogueOwner);
        HashSet<int> contextualTurnIndexes = PartyAgencyContextualTurnIndexes(dialogueOwner);
        bool selfContainedHostilityLanguageCase =
            caseId.Equals("PA-LANG-017", StringComparison.Ordinal)
            || caseId.Equals("PA-LANG-018", StringComparison.Ordinal);
        string effectiveTargetSearch = nativeCase || selfContainedHostilityLanguageCase
            ? "@party_agency_fixture" : targetSearch;
        bool simultaneousNativeReview = nativeCase
            && caseId.Equals("PA-NATIVE-013", StringComparison.Ordinal);
        if (simultaneousNativeReview)
        {
            string[] fixtureVariants = { variant + "-guest-1", variant + "-guest-2" };
            for (int guestIndex = 0; guestIndex < fixtureVariants.Length; guestIndex++)
            {
                steps.Add(new
                {
                    operation = "prepare_party_agency_fixture",
                    caseId,
                    variant = fixtureVariants[guestIndex],
                    fixtureRole = "partyless",
                    candidateHint = guestIndex == 0 ? targetSearch : string.Empty,
                    expectedSaveName = authorization.CurrentSaveName,
                    confirmDisposableCampaign = true,
                    timeoutSeconds = 600
                });
                if (guestIndex == 0)
                    steps.Add(PartyAgencySnapshotStep(profile, caseId, variant,
                        effectiveTargetSearch, "before", expected, evidenceReceipt));
                steps.Add(new
                {
                    operation = "open", mode = "individual_chat",
                    targetSearches = new[] { effectiveTargetSearch },
                    targetedReview = false, reviewContext = string.Empty,
                    presentation = "visible", effects = "full", timeoutSeconds = 600
                });
                for (int turnIndex = 0; turnIndex < naturalTurns.Length; turnIndex++)
                    steps.Add(new
                    {
                        operation = "send", mode = "individual_chat",
                        text = naturalTurns[turnIndex], sceneIndex = guestIndex,
                        turnIndex, naturalLanguageCase = true,
                        contextualPartyAgencyResponse = contextualTurnIndexes.Contains(turnIndex),
                        timeoutSeconds = 1200
                    });
                steps.Add(new { operation = "close", mode = "individual_chat",
                    timeoutSeconds = 300 });
            }
            steps.Add(new
            {
                operation = "party_agency_aggregate_fixtures",
                caseId, variant, fixtureVariants,
                expectedSaveName = authorization.CurrentSaveName,
                confirmDisposableCampaign = true,
                timeoutSeconds = 300
            });
            steps.AddRange(PartyAgencyNativeExerciseSteps(manifest, testCase,
                caseId, variant, authorization.CurrentSaveName));
            steps.Add(PartyAgencySnapshotStep(profile, caseId, variant,
                effectiveTargetSearch, "after", expected, evidenceReceipt));
        }
        else
        {
            if (nativeCase && !caseId.Equals("PA-NATIVE-022", StringComparison.Ordinal))
            {
                string fixtureRole = testCase.TryGetProperty("fixtureRole",
                        out JsonElement fixtureRoleElement)
                    ? fixtureRoleElement.GetString() ?? variant : variant;
                steps.Add(new
                {
                    operation = "prepare_party_agency_fixture",
                    caseId, variant, fixtureRole, candidateHint = targetSearch,
                    expectedSaveName = authorization.CurrentSaveName,
                    confirmDisposableCampaign = true,
                    timeoutSeconds = 600
                });
            }

            if (naturalTurns.Length > 0)
            {
                if (selfContainedHostilityLanguageCase)
                {
                    steps.Add(new
                    {
                        operation = "prepare_party_agency_fixture",
                        caseId, variant, fixtureRole = "party_leader",
                        candidateHint = targetSearch,
                        expectedSaveName = authorization.CurrentSaveName,
                        confirmDisposableCampaign = true,
                        timeoutSeconds = 600
                    });
                    JsonElement invitation = manifest.GetProperty("nativeInvitationTemplates")
                        .GetProperty("fixed");
                    string[] invitationTurns = PartyAgencyNaturalTurns(invitation);
                    HashSet<int> invitationContextual = PartyAgencyContextualTurnIndexes(invitation);
                    steps.Add(new
                    {
                        operation = "open", mode = "individual_chat",
                        targetSearches = new[] { effectiveTargetSearch },
                        targetedReview = false, reviewContext = string.Empty,
                        presentation = "visible", effects = "full", timeoutSeconds = 600
                    });
                    for (int invitationIndex = 0;
                         invitationIndex < invitationTurns.Length;
                         invitationIndex++)
                        steps.Add(new
                        {
                            operation = "send", mode = "individual_chat",
                            text = invitationTurns[invitationIndex], sceneIndex = 0,
                            turnIndex = invitationIndex, naturalLanguageCase = true,
                            contextualPartyAgencyResponse =
                                invitationContextual.Contains(invitationIndex),
                            timeoutSeconds = 1200
                        });
                    steps.Add(new { operation = "close", mode = "individual_chat",
                        timeoutSeconds = 300 });
                    steps.Add(new
                    {
                        operation = "party_agency_prepare_hostility",
                        caseId, variant,
                        expectedSaveName = authorization.CurrentSaveName,
                        confirmDisposableCampaign = true,
                        timeoutSeconds = 300
                    });
                }
                steps.Add(PartyAgencySnapshotStep(profile, caseId, variant, effectiveTargetSearch,
                    "before", expected, evidenceReceipt));
                string templateMode = dialogueOwner.TryGetProperty("dialogueMode",
                        out JsonElement templateModeElement)
                    ? templateModeElement.GetString() ?? string.Empty : string.Empty;
                if (!string.IsNullOrWhiteSpace(templateMode)) dialogueMode = templateMode;
                bool targetedReview = dialogueMode.Equals("targeted_review", StringComparison.Ordinal);
                string nativeDialogueMode = targetedReview ? "party_chat" : "individual_chat";
                steps.Add(new
                {
                    operation = "open", mode = nativeDialogueMode,
                    targetSearches = new[] { effectiveTargetSearch }, targetedReview,
                    reviewContext, presentation = "visible", effects = "full", timeoutSeconds = 600
                });
                for (int index = 0; index < naturalTurns.Length; index++)
                    steps.Add(new
                    {
                        operation = "send", mode = nativeDialogueMode,
                        text = naturalTurns[index], sceneIndex = 0, turnIndex = index,
                        naturalLanguageCase = true,
                        contextualPartyAgencyResponse = contextualTurnIndexes.Contains(index),
                        timeoutSeconds = 1200
                    });
                if (nativeCase)
                {
                    steps.Add(new { operation = "close", mode = nativeDialogueMode,
                        timeoutSeconds = 300 });
                    steps.AddRange(PartyAgencyNativeExerciseSteps(manifest, testCase,
                        caseId, variant, authorization.CurrentSaveName));
                    steps.Add(PartyAgencySnapshotStep(profile, caseId, variant,
                        effectiveTargetSearch, "after", expected, evidenceReceipt));
                }
                else
                {
                    steps.Add(PartyAgencySnapshotStep(profile, caseId, variant,
                        effectiveTargetSearch, "after", expected, evidenceReceipt));
                    steps.Add(new { operation = "close", mode = nativeDialogueMode,
                        timeoutSeconds = 300 });
                }
            }
            else
                steps.Add(PartyAgencySnapshotStep(profile, caseId, variant, effectiveTargetSearch,
                    "observe", expected, evidenceReceipt));
        }

        ApiEnvelope liveRun = await api.PostAsync("/tests/live/run/start", new
        {
            schemaVersion = 2,
            campaignId,
            runId = liveRunId,
            mode = "party_agency",
            label = "MCP Party Agency " + runId + " " + caseId
                + (variant.Length == 0 ? string.Empty : " " + variant),
            presentation = "visible",
            effects = nativeCase || naturalTurns.Length > 0 ? "full" : "observed",
            autoCompleteWhenIdle = true,
            enrollment = new
            {
                campaignId, campaignTestRunId, certificationRunId = runId,
                disposableSaveName = authorization.CurrentSaveName,
                disposableSavePrefix = authorization.SavePrefix,
                protectedBaselineSaveName = authorization.BaselineSaveName,
                timelineId = authorization.TimelineId,
                state.SourceFingerprint, state.ClientBuild, state.ServerBuild,
                state.ProviderConfigurationFingerprint, state.CatalogFingerprint,
                state.BannerlordVersion
            },
            caseId,
            variant,
            profile,
            naturalLanguageRequired = naturalTurns.Length > 0,
            steps
        }, cancellationToken).ConfigureAwait(false);

        lock (PartyAgencyStateGate)
        {
            state = ReadPartyAgencyState(options, runId);
            state.Executions.Add(new PartyAgencyExecution
            {
                CaseId = caseId,
                Variant = variant,
                Profile = profile,
                LiveRunId = liveRunId,
                NaturalLanguage = naturalTurns.Length > 0,
                StartAccepted = liveRun.Ok,
                StartedUtc = DateTime.UtcNow.ToString("O")
            });
            if (state.Executions.Count > 300)
                state.Executions.RemoveRange(0, state.Executions.Count - 300);
            WritePartyAgencyState(options, state);
        }

        return new Dictionary<string, object?>
        {
            ["ok"] = liveRun.Ok,
            ["schema"] = "reign-party-agency-case-start-v1",
            ["caseInstance"] = PartyAgencyInstanceKey(caseId, variant),
            ["naturalLanguageTurns"] = naturalTurns,
            ["directDialogueActionInvocation"] = false,
            ["liveRun"] = liveRun,
            ["state"] = state
        };
    }

    [McpServerTool(Name = "reign_verify_party_agency_save_roundtrip", ReadOnly = false,
        Destructive = true, Idempotent = false, UseStructuredContent = true)]
    [Description("Verifies one staged Temporary Noble Party Guest lifecycle phase after the guarded campaign-test service has checkpointed and restarted the exact disposable Current save.")]
    public static async Task<IReadOnlyDictionary<string, object?>> VerifyPartyAgencySaveRoundtrip(
        ReignApiClient api,
        ReignMcpOptions options,
        CampaignTestService campaignTests,
        string campaignId,
        string campaignTestRunId,
        string runId,
        string variant,
        string restartReceipt,
        [Description("Exact text required: verify Reign Party Agency save reload on disposable save")]
        string confirmation = "",
        CancellationToken cancellationToken = default)
    {
        if (!options.AllowVerificationControl)
            throw new InvalidOperationException("Verification control is disabled.");
        InputGuard.RequireConfirmation(confirmation,
            "verify Reign Party Agency save reload on disposable save");
        campaignId = RequiredPartyAgencyIdentifier(campaignId, nameof(campaignId));
        campaignTestRunId = RequiredPartyAgencyIdentifier(campaignTestRunId,
            nameof(campaignTestRunId));
        runId = RequiredPartyAgencyIdentifier(runId, nameof(runId));
        restartReceipt = RequiredPartyAgencyIdentifier(restartReceipt, nameof(restartReceipt));
        variant = OptionalPartyAgencyToken(variant, nameof(variant));
        PartyAgencyCertificationState state = ReadPartyAgencyState(options, runId);
        if (!string.Equals(state.CampaignId, campaignId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(state.CampaignTestRunId, campaignTestRunId,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "The Party Agency reload verification does not match its prepared enrollment.");
        campaignTests.RequireRestartReceipt(campaignTestRunId, campaignId, restartReceipt);
        CampaignTestAuthorization authorization = await campaignTests.RequireArmedOwnedSaveAsync(
            campaignTestRunId, campaignId, cancellationToken).ConfigureAwait(false);
        JsonElement manifest = LoadPartyAgencyManifest(options);
        JsonElement testCase = FindPartyAgencyCase(manifest, "PA-NATIVE-017");
        ValidatePartyAgencyVariant(testCase, variant);
        string liveRunId = PartyAgencyLiveRunId(runId, "PA-NATIVE-017-reload", variant);
        object[] steps =
        {
            new
            {
                operation = "party_agency_verify_save_phase",
                caseId = "PA-NATIVE-017", variant,
                expectedSaveName = authorization.CurrentSaveName,
                confirmDisposableCampaign = true,
                restartVerified = true, restartReceipt, timeoutSeconds = 300
            },
            PartyAgencySnapshotStep("save_roundtrip", "PA-NATIVE-017", variant,
                "@party_agency_fixture", "after", testCase.GetProperty("expected"), restartReceipt)
        };
        ApiEnvelope liveRun = await api.PostAsync("/tests/live/run/start", new
        {
            schemaVersion = 2, campaignId, runId = liveRunId, mode = "party_agency",
            label = "MCP Party Agency save reload " + runId + " " + variant,
            presentation = "visible", effects = "observed", autoCompleteWhenIdle = true,
            enrollment = new
            {
                campaignId, campaignTestRunId, certificationRunId = runId,
                disposableSaveName = authorization.CurrentSaveName,
                disposableSavePrefix = authorization.SavePrefix,
                protectedBaselineSaveName = authorization.BaselineSaveName,
                timelineId = authorization.TimelineId,
                state.SourceFingerprint, state.ClientBuild, state.ServerBuild,
                state.ProviderConfigurationFingerprint, state.CatalogFingerprint,
                state.BannerlordVersion, restartReceipt
            },
            caseId = "PA-NATIVE-017", variant, profile = "save_roundtrip",
            naturalLanguageRequired = false, steps
        }, cancellationToken).ConfigureAwait(false);
        lock (PartyAgencyStateGate)
        {
            state = ReadPartyAgencyState(options, runId);
            state.Executions.Add(new PartyAgencyExecution
            {
                CaseId = "PA-NATIVE-017", Variant = variant,
                Profile = "save_roundtrip", LiveRunId = liveRunId,
                NaturalLanguage = false, StartAccepted = liveRun.Ok,
                StartedUtc = DateTime.UtcNow.ToString("O")
            });
            WritePartyAgencyState(options, state);
        }
        return new Dictionary<string, object?>
        {
            ["ok"] = liveRun.Ok,
            ["schema"] = "reign-party-agency-save-reload-start-v1",
            ["caseInstance"] = PartyAgencyInstanceKey("PA-NATIVE-017", variant),
            ["restartReceipt"] = restartReceipt,
            ["liveRun"] = liveRun,
            ["state"] = state
        };
    }

    [McpServerTool(Name = "reign_evaluate_party_agency_release_readiness", ReadOnly = true,
        Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Evaluates Party Agency release readiness from the prepared final fingerprints and durable live-run reports. Every required case variant, natural-language transcript, native assertion, and save-isolation gate must be complete and green.")]
    public static async Task<IReadOnlyDictionary<string, object?>> EvaluatePartyAgencyReleaseReadiness(
        ReignApiClient api,
        ReignMcpOptions options,
        CampaignTestService campaignTests,
        string campaignId,
        string campaignTestRunId,
        string runId,
        CancellationToken cancellationToken = default)
    {
        campaignId = RequiredPartyAgencyIdentifier(campaignId, nameof(campaignId));
        campaignTestRunId = RequiredPartyAgencyIdentifier(campaignTestRunId, nameof(campaignTestRunId));
        runId = RequiredPartyAgencyIdentifier(runId, nameof(runId));
        CampaignTestAuthorization authorization = campaignTests.RequireEnrollment(
            campaignTestRunId, campaignId);
        PartyAgencyCertificationState state = ReadPartyAgencyState(options, runId);
        if (!string.Equals(state.DisposableSaveName, authorization.CurrentSaveName,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(state.ProtectedBaselineSaveName, authorization.BaselineSaveName,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Party Agency readiness enrollment no longer matches the guarded campaign test.");

        JsonElement manifest = LoadPartyAgencyManifest(options);
        var passed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var passedLanguage = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var reports = new List<object?>();
        var blockers = new List<string>();
        foreach (PartyAgencyExecution execution in state.Executions.AsEnumerable().Reverse())
        {
            string instance = PartyAgencyInstanceKey(execution.CaseId, execution.Variant);
            if (passed.Contains(instance)) continue;
            ApiEnvelope report = await api.GetAsync("/tests/live/run/report",
                new Dictionary<string, string?>
                {
                    ["campaignId"] = campaignId,
                    ["runId"] = execution.LiveRunId
                }, cancellationToken).ConfigureAwait(false);
            bool completed = report.Ok && report.Data.HasValue
                && string.Equals(FindPartyAgencyString(report.Data.Value, "status"),
                    "completed", StringComparison.OrdinalIgnoreCase);
            JsonElement testCase = FindPartyAgencyCase(manifest, execution.CaseId);
            string[] requiredTurns = PartyAgencyAllRequiredTurns(manifest, testCase,
                execution.Variant);
            var transcriptReports = new List<JsonElement>();
            if (report.Data.HasValue) transcriptReports.Add(report.Data.Value);
            if (execution.CaseId.Equals("PA-NATIVE-017", StringComparison.OrdinalIgnoreCase)
                && completed)
            {
                foreach (PartyAgencyExecution stage in state.Executions.Where(item =>
                    item != execution
                    && item.CaseId.Equals(execution.CaseId, StringComparison.OrdinalIgnoreCase)
                    && item.Variant.Equals(execution.Variant, StringComparison.OrdinalIgnoreCase)
                    && item.NaturalLanguage))
                {
                    ApiEnvelope stageReport = await api.GetAsync("/tests/live/run/report",
                        new Dictionary<string, string?>
                        {
                            ["campaignId"] = campaignId,
                            ["runId"] = stage.LiveRunId
                        }, cancellationToken).ConfigureAwait(false);
                    if (stageReport.Data.HasValue)
                        transcriptReports.Add(stageReport.Data.Value);
                }
            }
            bool transcriptComplete = requiredTurns.All(turn =>
                transcriptReports.Any(reportElement =>
                    ContainsPartyAgencyTranscriptTurn(reportElement, turn)));
            bool noDirectBypass = !report.Data.HasValue
                || !ContainsPartyAgencyDirectActionOperation(report.Data.Value);
            bool casePassed = completed && transcriptComplete && noDirectBypass;
            if (casePassed)
            {
                passed.Add(instance);
                if (requiredTurns.Length > 0) passedLanguage.Add(instance);
            }
            reports.Add(new Dictionary<string, object?>
            {
                ["instance"] = instance,
                ["liveRunId"] = execution.LiveRunId,
                ["completed"] = completed,
                ["naturalLanguageTranscriptComplete"] = transcriptComplete,
                ["noDirectDialogueActionBypass"] = noDirectBypass,
                ["passed"] = casePassed,
                ["report"] = report
            });
        }

        const string finalInstance = "PA-NATIVE-022::final_fingerprint";
        string[] missingBeforeFinal = state.RequiredInstances
            .Where(instance => !instance.Equals(finalInstance,
                    StringComparison.OrdinalIgnoreCase)
                && !passed.Contains(instance)).ToArray();
        if (missingBeforeFinal.Length > 0)
            blockers.Add("Missing or failed required case instances: "
                + string.Join(", ", missingBeforeFinal));
        foreach ((string label, string value) in new[]
        {
            ("source", state.SourceFingerprint), ("client", state.ClientBuild),
            ("server", state.ServerBuild), ("provider", state.ProviderConfigurationFingerprint),
            ("catalog", state.CatalogFingerprint), ("Bannerlord", state.BannerlordVersion)
        })
            if (string.IsNullOrWhiteSpace(value) || value.Equals("unknown", StringComparison.OrdinalIgnoreCase))
                blockers.Add("The " + label + " fingerprint is missing or unknown.");

        if (blockers.Count == 0) passed.Add(finalInstance);
        string[] missing = state.RequiredInstances
            .Where(instance => !passed.Contains(instance)).ToArray();
        if (missing.Length > 0 && missingBeforeFinal.Length == 0)
            blockers.Add("The final-fingerprint readiness case has not passed.");

        HashSet<string> requiredLanguage = manifest.GetProperty("cases").EnumerateArray()
            .Where(item => PartyAgencyAllRequiredTurns(manifest, item, string.Empty).Length > 0)
            .SelectMany(ExpandPartyAgencyCaseInstances)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool ready = blockers.Count == 0;
        return new Dictionary<string, object?>
        {
            ["ok"] = true,
            ["schema"] = "reign-party-agency-release-readiness-v1",
            ["readyToDeployForNativeCertification"] = true,
            ["readyToRelease"] = ready,
            ["requiredInstanceCount"] = state.RequiredInstances.Count,
            ["passedInstanceCount"] = passed.Count,
            ["naturalLanguageRequiredInstanceCount"] = requiredLanguage.Count,
            ["naturalLanguagePassedInstanceCount"] = passedLanguage.Count,
            ["naturalLanguageAccuracy"] = requiredLanguage.Count == 0
                ? 0d : (double)passedLanguage.Count / requiredLanguage.Count,
            ["falsePositiveConsentTolerance"] = 0,
            ["directDialogueActionBypassTolerance"] = 0,
            ["blockers"] = blockers,
            ["missingInstances"] = missing,
            ["reports"] = reports,
            ["state"] = state
        };
    }

    private static object PartyAgencySnapshotStep(string profile, string caseId,
        string variant, string targetSearch, string phase, JsonElement expected,
        string evidenceReceipt)
    {
        // The native client can prove its runtime contracts, but it cannot prove MCP-owned
        // certification history. The readiness evaluator below owns prior-report and immutable-
        // fingerprint verification, so the final native observation intentionally has no
        // external-evidence assertion.
        object? snapshotExpected = caseId.Equals("PA-NATIVE-022",
            StringComparison.OrdinalIgnoreCase)
            ? new Dictionary<string, object?>()
            : JsonSerializer.Deserialize<object>(expected.GetRawText());
        return new
        {
            operation = "party_agency_test", profile, caseId, variant, targetSearch, phase,
            expected = snapshotExpected, evidenceReceipt, timeoutSeconds = 180
        };
    }

    private static JsonElement PartyAgencyDialogueOwner(JsonElement manifest,
        JsonElement testCase)
    {
        if (!testCase.TryGetProperty("invitationTemplate", out JsonElement templateNameElement)
            || templateNameElement.ValueKind != JsonValueKind.String)
            return testCase;
        string templateName = templateNameElement.GetString() ?? string.Empty;
        if (manifest.TryGetProperty("nativeInvitationTemplates", out JsonElement templates)
            && templates.ValueKind == JsonValueKind.Object
            && templates.TryGetProperty(templateName, out JsonElement template))
            return template;
        throw new InvalidDataException(
            "Party Agency case references unknown invitation template '" + templateName + "'.");
    }

    private static string[] PartyAgencyNaturalTurns(JsonElement owner) =>
        owner.TryGetProperty("turns", out JsonElement turns)
        && turns.ValueKind == JsonValueKind.Array
            ? turns.EnumerateArray().Select(item => item.GetString() ?? string.Empty)
                .Where(text => !string.IsNullOrWhiteSpace(text)).ToArray()
            : Array.Empty<string>();

    private static HashSet<int> PartyAgencyContextualTurnIndexes(JsonElement owner) =>
        owner.TryGetProperty("contextualTurnIndexes", out JsonElement indexes)
        && indexes.ValueKind == JsonValueKind.Array
            ? indexes.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Number)
                .Select(item => item.GetInt32()).ToHashSet()
            : new HashSet<int>();

    private static string[] PartyAgencyAllRequiredTurns(JsonElement manifest,
        JsonElement testCase, string variant)
    {
        var turns = new List<string>(PartyAgencyNaturalTurns(
            PartyAgencyDialogueOwner(manifest, testCase)));
        if (testCase.TryGetProperty("departureTemplate", out JsonElement nameElement)
            && nameElement.ValueKind == JsonValueKind.String
            && manifest.TryGetProperty("nativeDepartureTemplates", out JsonElement templates)
            && templates.TryGetProperty(nameElement.GetString() ?? string.Empty,
                out JsonElement template))
            turns.AddRange(PartyAgencyNaturalTurns(template));
        if (testCase.TryGetProperty("hostilityTemplate", out JsonElement hostilityName)
            && hostilityName.ValueKind == JsonValueKind.String
            && manifest.TryGetProperty("nativeHostilityTemplates", out JsonElement hostilityTemplates)
            && hostilityTemplates.TryGetProperty(hostilityName.GetString() ?? string.Empty,
                out JsonElement hostilityTemplate))
            turns.AddRange(PartyAgencyNaturalTurns(hostilityTemplate));
        bool lifecycleDeparture = variant.Equals("Departing", StringComparison.OrdinalIgnoreCase)
            || variant.Equals("Returning", StringComparison.OrdinalIgnoreCase)
            || variant.Equals("Completed", StringComparison.OrdinalIgnoreCase);
        if (lifecycleDeparture
            && testCase.TryGetProperty("lifecycleDepartureTemplate", out JsonElement lifecycleDepartureName)
            && manifest.TryGetProperty("nativeDepartureTemplates", out JsonElement lifecycleDepartures)
            && lifecycleDepartures.TryGetProperty(
                lifecycleDepartureName.GetString() ?? string.Empty,
                out JsonElement lifecycleDepartureTemplate))
            turns.AddRange(PartyAgencyNaturalTurns(lifecycleDepartureTemplate));
        if (variant.Equals("HostileBanishmentPending", StringComparison.OrdinalIgnoreCase)
            && testCase.TryGetProperty("lifecycleHostilityTemplate", out JsonElement lifecycleHostilityName)
            && manifest.TryGetProperty("nativeHostilityTemplates", out JsonElement lifecycleHostilities)
            && lifecycleHostilities.TryGetProperty(
                lifecycleHostilityName.GetString() ?? string.Empty,
                out JsonElement lifecycleHostilityTemplate))
            turns.AddRange(PartyAgencyNaturalTurns(lifecycleHostilityTemplate));
        return turns.ToArray();
    }

    private static IEnumerable<object> PartyAgencyNativeExerciseSteps(JsonElement manifest,
        JsonElement testCase, string caseId, string variant, string disposableSaveName)
    {
        if (caseId == "PA-NATIVE-017")
        {
            bool departurePhase = variant.Equals("Departing", StringComparison.OrdinalIgnoreCase)
                || variant.Equals("Returning", StringComparison.OrdinalIgnoreCase)
                || variant.Equals("Completed", StringComparison.OrdinalIgnoreCase);
            if (departurePhase
                && testCase.TryGetProperty("lifecycleDepartureTemplate", out JsonElement phaseDepartureName)
                && manifest.TryGetProperty("nativeDepartureTemplates", out JsonElement phaseDepartures)
                && phaseDepartures.TryGetProperty(phaseDepartureName.GetString() ?? string.Empty,
                    out JsonElement phaseDepartureTemplate))
            {
                string[] turns = PartyAgencyNaturalTurns(phaseDepartureTemplate);
                yield return new
                {
                    operation = "open", mode = "party_chat",
                    targetSearches = new[] { "@party_agency_fixture" }, targetedReview = true,
                    reviewContext = "The player is ending the temporary agreement before staging its save/reload phase.",
                    presentation = "visible", effects = "full", timeoutSeconds = 600
                };
                for (int index = 0; index < turns.Length; index++)
                    yield return new
                    {
                        operation = "send", mode = "party_chat", text = turns[index],
                        sceneIndex = 1, turnIndex = index, naturalLanguageCase = true,
                        contextualPartyAgencyResponse = false, timeoutSeconds = 1200
                    };
                yield return new { operation = "close", mode = "party_chat", timeoutSeconds = 300 };
            }
            if (variant.Equals("HostileBanishmentPending", StringComparison.OrdinalIgnoreCase))
            {
                yield return new
                {
                    operation = "party_agency_prepare_hostility", caseId, variant,
                    expectedSaveName = disposableSaveName,
                    confirmDisposableCampaign = true, timeoutSeconds = 300
                };
                string templateName = testCase.GetProperty("lifecycleHostilityTemplate").GetString()
                    ?? string.Empty;
                JsonElement warningTemplate = manifest.GetProperty("nativeHostilityTemplates")
                    .GetProperty(templateName);
                string[] turns = PartyAgencyNaturalTurns(warningTemplate);
                HashSet<int> contextual = PartyAgencyContextualTurnIndexes(warningTemplate);
                yield return new
                {
                    operation = "open", mode = "party_chat",
                    targetSearches = new[] { "@party_agency_fixture" }, targetedReview = true,
                    reviewContext = "The guest must decide whether to remain after hearing the own-faction banishment warning.",
                    presentation = "visible", effects = "full", timeoutSeconds = 600
                };
                for (int index = 0; index < turns.Length; index++)
                    yield return new
                    {
                        operation = "send", mode = "party_chat", text = turns[index],
                        sceneIndex = 1, turnIndex = index, naturalLanguageCase = true,
                        contextualPartyAgencyResponse = contextual.Contains(index),
                        timeoutSeconds = 1200
                    };
                yield return new { operation = "close", mode = "party_chat", timeoutSeconds = 300 };
            }
            yield return new
            {
                operation = "party_agency_stage_save_phase", caseId, variant,
                expectedSaveName = disposableSaveName,
                confirmDisposableCampaign = true, timeoutSeconds = 300
            };
            yield break;
        }
        if (caseId == "PA-NATIVE-018" || caseId == "PA-NATIVE-019")
        {
            yield return new
            {
                operation = "party_agency_prepare_hostility",
                caseId,
                variant,
                expectedSaveName = disposableSaveName,
                confirmDisposableCampaign = true,
                timeoutSeconds = 300
            };
            if (caseId == "PA-NATIVE-019"
                && testCase.TryGetProperty("hostilityTemplate", out JsonElement warningName)
                && manifest.TryGetProperty("nativeHostilityTemplates", out JsonElement warnings)
                && warnings.TryGetProperty(warningName.GetString() ?? string.Empty,
                    out JsonElement warningTemplate))
            {
                string[] warningTurns = PartyAgencyNaturalTurns(warningTemplate);
                HashSet<int> warningContextual = PartyAgencyContextualTurnIndexes(warningTemplate);
                yield return new
                {
                    operation = "open", mode = "party_chat",
                    targetSearches = new[] { "@party_agency_fixture" },
                    targetedReview = true,
                    reviewContext = "The guest's own faction is now hostile. The guest speaks first and must explicitly choose whether to remain after hearing the banishment consequence.",
                    presentation = "visible", effects = "full", timeoutSeconds = 600
                };
                for (int index = 0; index < warningTurns.Length; index++)
                    yield return new
                    {
                        operation = "send", mode = "party_chat",
                        text = warningTurns[index], sceneIndex = 1, turnIndex = index,
                        naturalLanguageCase = true,
                        contextualPartyAgencyResponse = warningContextual.Contains(index),
                        timeoutSeconds = 1200
                    };
                yield return new { operation = "close", mode = "party_chat", timeoutSeconds = 300 };
            }
            yield return new
            {
                operation = "party_agency_hostile_battle",
                caseId,
                variant,
                expectedSaveName = disposableSaveName,
                confirmDisposableCampaign = true,
                timeoutSeconds = 900
            };
        }
        if (testCase.TryGetProperty("departureTemplate", out JsonElement departureName)
            && manifest.TryGetProperty("nativeDepartureTemplates", out JsonElement departures)
            && departures.TryGetProperty(departureName.GetString() ?? string.Empty,
                out JsonElement departureTemplate))
        {
            string[] departureTurns = PartyAgencyNaturalTurns(departureTemplate);
            yield return new
            {
                operation = "open", mode = "party_chat",
                targetSearches = new[] { "@party_agency_fixture" },
                targetedReview = true,
                reviewContext = "The agreed purpose is complete. The player is releasing the temporary noble guest to return home.",
                presentation = "visible", effects = "full", timeoutSeconds = 600
            };
            for (int index = 0; index < departureTurns.Length; index++)
                yield return new
                {
                    operation = "send", mode = "party_chat",
                    text = departureTurns[index], sceneIndex = 1, turnIndex = index,
                    naturalLanguageCase = true,
                    contextualPartyAgencyResponse = false,
                    timeoutSeconds = 1200
                };
            yield return new { operation = "close", mode = "party_chat", timeoutSeconds = 300 };
        }
        if (caseId == "PA-NATIVE-020")
            yield return new
            {
                operation = "party_agency_recovery_fixture",
                caseId,
                variant,
                expectedSaveName = disposableSaveName,
                confirmDisposableCampaign = true,
                timeoutSeconds = 300
            };
        if (caseId == "PA-NATIVE-021")
            yield return new
            {
                operation = "party_agency_verify_unrelated_state",
                caseId,
                variant,
                expectedSaveName = disposableSaveName,
                confirmDisposableCampaign = true,
                timeoutSeconds = 300
            };
        double days = caseId switch
        {
            "PA-NATIVE-009" => 30.05d,
            "PA-NATIVE-010" => 5d,
            "PA-NATIVE-011" => 1.05d,
            "PA-NATIVE-012" => 5.05d,
            "PA-NATIVE-013" => 1.05d,
            "PA-NATIVE-014" => 1.05d,
            "PA-NATIVE-015" => 0.55d,
            "PA-NATIVE-016" => 0.55d,
            _ => 0d
        };
        if (days <= 0d) yield break;
        yield return new
        {
            operation = "party_agency_advance_time",
            caseId,
            variant,
            days,
            expectedSaveName = disposableSaveName,
            confirmDisposableCampaign = true,
            requireProtectedCampFidelity = caseId != "PA-NATIVE-015"
                && caseId != "PA-NATIVE-016",
            timeoutSeconds = caseId == "PA-NATIVE-009" ? 3600 : 1200
        };
        if (caseId == "PA-NATIVE-014")
            yield return new
            {
                operation = "party_agency_review_provider_result",
                caseId,
                variant,
                expectedSaveName = disposableSaveName,
                confirmDisposableCampaign = true,
                timeoutSeconds = 300
            };
    }

    private static JsonElement LoadPartyAgencyManifest(ReignMcpOptions options)
    {
        string path = PartyAgencyManifestPath(options);
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
        return document.RootElement.Clone();
    }

    private static string PartyAgencyManifestPath(ReignMcpOptions options)
    {
        string path = Path.Combine(options.WorkspaceRoot, "ReignBetaServer", "ReignLiveTest",
            "scenarios", "party-agency-manifest.json");
        ReignMcpOptions.EnsureWithin(options.WorkspaceRoot, path, "partyAgencyManifest");
        return path;
    }

    private static JsonElement FindPartyAgencyCase(JsonElement manifest, string caseId)
    {
        foreach (JsonElement item in manifest.GetProperty("cases").EnumerateArray())
            if (string.Equals(item.GetProperty("id").GetString(), caseId,
                    StringComparison.OrdinalIgnoreCase))
                return item.Clone();
        throw new ArgumentException("Unknown Party Agency case id.", nameof(caseId));
    }

    private static IEnumerable<string> RequiredPartyAgencyInstances(JsonElement manifest)
    {
        foreach (JsonElement item in manifest.GetProperty("cases").EnumerateArray())
        {
            if (!item.TryGetProperty("required", out JsonElement required)
                || required.ValueKind != JsonValueKind.True) continue;
            foreach (string instance in ExpandPartyAgencyCaseInstances(item))
                yield return instance;
        }
    }

    private static IEnumerable<string> ExpandPartyAgencyCaseInstances(JsonElement item)
    {
        string caseId = item.GetProperty("id").GetString() ?? string.Empty;
        if (item.TryGetProperty("variants", out JsonElement variants)
            && variants.ValueKind == JsonValueKind.Array)
            foreach (JsonElement variant in variants.EnumerateArray())
                yield return PartyAgencyInstanceKey(caseId, variant.GetString() ?? string.Empty);
        else yield return PartyAgencyInstanceKey(caseId, string.Empty);
    }

    private static void ValidatePartyAgencyVariant(JsonElement testCase, string variant)
    {
        if (!testCase.TryGetProperty("variants", out JsonElement variants)
            || variants.ValueKind != JsonValueKind.Array)
        {
            if (variant.Length > 0)
                throw new ArgumentException("The selected Party Agency case has no variants.", nameof(variant));
            return;
        }
        string[] allowed = variants.EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToArray();
        if (variant.Length == 0 || !allowed.Contains(variant, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("A supported Party Agency case variant is required: "
                + string.Join(", ", allowed), nameof(variant));
    }

    private static string PartyAgencyLiveRunId(string runId, string caseId, string variant)
    {
        string compactRun = new string(runId.Where(char.IsAsciiLetterOrDigit).Take(24).ToArray());
        string compactCase = new string((caseId + "-" + variant)
            .Where(character => char.IsAsciiLetterOrDigit(character) || character == '-')
            .Take(48).ToArray()).Trim('-');
        return (compactRun + "-" + compactCase + "-" + Guid.NewGuid().ToString("N")[..8])
            .ToLowerInvariant();
    }

    private static string PartyAgencyInstanceKey(string caseId, string variant) =>
        variant.Length == 0 ? caseId : caseId + "::" + variant;

    private static string NormalizePartyAgencyProfile(string value)
    {
        string profile = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (!PartyAgencyProfiles.Contains(profile, StringComparer.Ordinal))
            throw new ArgumentException("Unsupported Party Agency profile.", nameof(value));
        return profile;
    }

    private static string RequiredPartyAgencyIdentifier(string value, string name)
    {
        string result = InputGuard.OptionalIdentifier(value, name);
        if (result.Length == 0) throw new ArgumentException(name + " is required.", name);
        return result;
    }

    private static string RequiredPartyAgencyCaseId(string value)
    {
        value = InputGuard.BoundedText(value, nameof(value), 80).Trim().ToUpperInvariant();
        if (value.Length == 0 || value.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character != '-'))
            throw new ArgumentException("caseId must contain only ASCII letters, digits, and hyphen.", nameof(value));
        return value;
    }

    private static string OptionalPartyAgencyToken(string value, string name)
    {
        value = InputGuard.BoundedText(value ?? string.Empty, name, 80).Trim();
        if (value.Any(character => !char.IsAsciiLetterOrDigit(character)
                && character != '-' && character != '_'))
            throw new ArgumentException(name + " contains unsupported characters.", name);
        return value;
    }

    private static string RequiredPartyAgencyFingerprint(string value, string name)
    {
        value = InputGuard.BoundedText(value ?? string.Empty, name, 256).Trim();
        if (value.Length == 0 || value.Equals("unknown", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(name + " must be a concrete final fingerprint.", name);
        return value;
    }

    private static string PartyAgencyStatePath(ReignMcpOptions options, string runId)
    {
        string root = Path.Combine(options.WorkspaceRoot, ".codex-live-artifacts", "party-agency");
        string path = Path.Combine(root, RequiredPartyAgencyIdentifier(runId, nameof(runId)) + ".json");
        ReignMcpOptions.EnsureWithin(root, path, nameof(runId));
        return path;
    }

    private static PartyAgencyCertificationState ReadPartyAgencyState(
        ReignMcpOptions options, string runId) => TryReadPartyAgencyState(options, runId)
        ?? throw new InvalidOperationException("Unknown Party Agency certification run id.");

    private static PartyAgencyCertificationState? TryReadPartyAgencyState(
        ReignMcpOptions options, string runId)
    {
        string path = PartyAgencyStatePath(options, runId);
        return File.Exists(path)
            ? JsonSerializer.Deserialize<PartyAgencyCertificationState>(
                File.ReadAllText(path, Encoding.UTF8), PartyAgencyJson)
            : null;
    }

    private static void WritePartyAgencyState(ReignMcpOptions options,
        PartyAgencyCertificationState state)
    {
        state.UpdatedUtc = DateTime.UtcNow.ToString("O");
        string path = PartyAgencyStatePath(options, state.RunId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(state, PartyAgencyJson),
            new UTF8Encoding(false));
        File.Move(temporary, path, true);
    }

    private static bool PartyAgencyImmutableInputsMatch(PartyAgencyCertificationState left,
        PartyAgencyCertificationState right) =>
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

    private static IReadOnlyDictionary<string, object?> PartyAgencyStateEnvelope(
        PartyAgencyCertificationState state, string status, JsonElement manifest) =>
        new Dictionary<string, object?>
        {
            ["ok"] = true,
            ["schema"] = "reign-party-agency-certification-state-v1",
            ["status"] = status,
            ["state"] = state,
            ["manifest"] = JsonSerializer.Deserialize<object>(manifest.GetRawText())
        };

    private static string FindPartyAgencyString(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object)
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (property.NameEquals(name) && property.Value.ValueKind == JsonValueKind.String)
                    return property.Value.GetString() ?? string.Empty;
                string nested = FindPartyAgencyString(property.Value, name);
                if (nested.Length > 0) return nested;
            }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (JsonElement item in element.EnumerateArray())
            {
                string nested = FindPartyAgencyString(item, name);
                if (nested.Length > 0) return nested;
            }
        return string.Empty;
    }

    private static bool ContainsPartyAgencyTranscriptTurn(JsonElement element, string turn)
    {
        if (element.ValueKind == JsonValueKind.String)
            return (element.GetString() ?? string.Empty).Contains(turn,
                StringComparison.Ordinal);
        if (element.ValueKind == JsonValueKind.Object)
            foreach (JsonProperty property in element.EnumerateObject())
                if (ContainsPartyAgencyTranscriptTurn(property.Value, turn)) return true;
        if (element.ValueKind == JsonValueKind.Array)
            foreach (JsonElement item in element.EnumerateArray())
                if (ContainsPartyAgencyTranscriptTurn(item, turn)) return true;
        return false;
    }

    private static bool ContainsPartyAgencyDirectActionOperation(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (property.NameEquals("operation")
                    && property.Value.ValueKind == JsonValueKind.String
                    && new[]
                    {
                        "accept_temporary_party_guest", "renew_temporary_party_guest",
                        "end_temporary_party_guest", "acknowledge_own_faction_combat_risk"
                    }.Contains(property.Value.GetString() ?? string.Empty,
                        StringComparer.OrdinalIgnoreCase)) return true;
                if (ContainsPartyAgencyDirectActionOperation(property.Value)) return true;
            }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (JsonElement item in element.EnumerateArray())
                if (ContainsPartyAgencyDirectActionOperation(item)) return true;
        return false;
    }
}

public sealed record PartyAgencyCertificationState
{
    public string Schema { get; set; } = "reign-party-agency-certification-state-v1";
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
    public List<string> RequiredInstances { get; set; } = [];
    public List<PartyAgencyExecution> Executions { get; set; } = [];
    public List<PartyAgencyCarriedEvidence> CarriedEvidence { get; set; } = [];
}

public sealed record PartyAgencyCarriedEvidence
{
    public string Instance { get; set; } = "";
    public string SourceRunId { get; set; } = "";
    public string SourceFingerprint { get; set; } = "";
    public string SourceClientBuild { get; set; } = "";
    public string SourceCatalogFingerprint { get; set; } = "";
    public string LiveRunId { get; set; } = "";
    public string ChangeScope { get; set; } = "";
    public string CarriedUtc { get; set; } = "";
}

public sealed record PartyAgencyExecution
{
    public string CaseId { get; set; } = "";
    public string Variant { get; set; } = "";
    public string Profile { get; set; } = "";
    public string LiveRunId { get; set; } = "";
    public bool NaturalLanguage { get; set; }
    public bool StartAccepted { get; set; }
    public string StartedUtc { get; set; } = "";
}
