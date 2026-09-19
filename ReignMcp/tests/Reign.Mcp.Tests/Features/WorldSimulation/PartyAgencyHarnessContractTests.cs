using System.Text.Json;
using Reign.Mcp.Server;

namespace Reign.Mcp.Tests;

public sealed class PartyAgencyHarnessContractTests
{
    [Fact]
    public void NativeReturnTimingReceiptSurvivesCompletedReturnAndUsesManifestVariants()
    {
        string root = TestOptions.FindWorkspace();
        string source = File.ReadAllText(Path.Combine(root, "ReignBeta", "src", "Modules",
            "WorldSimulation", "Campaign", "ReignLiveInteractionPartyAgencyFixtures.cs"));

        Assert.Contains("return_due_observed_on_returning_transition", source,
            StringComparison.Ordinal);
        Assert.Contains("returningRecord.ReturnDueDay - currentDay", source,
            StringComparison.Ordinal);
        Assert.Contains("plannedReturnHoursBasis", source, StringComparison.Ordinal);
        Assert.Contains("PartyAgencyReturnVariantMatches(variant, \"min\", \"minimum\")",
            source, StringComparison.Ordinal);
        Assert.Contains("PartyAgencyReturnVariantMatches(variant, \"max\", \"maximum\")",
            source, StringComparison.Ordinal);
    }

    [Fact]
    public void ManifestOwnsFortyNaturalLanguageAndNativeReleaseCases()
    {
        string root = TestOptions.FindWorkspace();
        string path = Path.Combine(root, "ReignServer", "tests", "ReignLiveTest", "scenarios",
            "party-agency-manifest.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement manifest = document.RootElement;
        Assert.Equal(2, manifest.GetProperty("schemaVersion").GetInt32());
        Assert.False(manifest.GetProperty("preparedOnly").GetBoolean());
        Assert.True(manifest.GetProperty("autonomous").GetBoolean());
        Assert.True(manifest.GetProperty("naturalLanguagePolicy")
            .GetProperty("directDialogueActionInvocationProhibited").GetBoolean());
        JsonElement[] cases = manifest.GetProperty("cases").EnumerateArray().ToArray();
        Assert.Equal(40, cases.Length);
        Assert.Equal(18, cases.Count(item => item.GetProperty("id").GetString()!
            .StartsWith("PA-LANG-", StringComparison.Ordinal)));
        Assert.All(cases.Where(item => item.GetProperty("id").GetString()!
            .StartsWith("PA-LANG-", StringComparison.Ordinal)), item =>
        {
            string[] turns = item.GetProperty("turns").EnumerateArray()
                .Select(turn => turn.GetString() ?? string.Empty).ToArray();
            Assert.NotEmpty(turns);
            Assert.All(turns, turn => Assert.False(string.IsNullOrWhiteSpace(turn)));
            Assert.DoesNotContain("RegularAcceptTemporaryPartyGuest", string.Join(" ", turns),
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("consentConfirmed", string.Join(" ", turns),
                StringComparison.OrdinalIgnoreCase);
        });
        Assert.Contains(cases, item => item.GetProperty("id").GetString() == "PA-NATIVE-017"
            && item.GetProperty("variants").GetArrayLength() == 6);
        Assert.Contains(cases, item => item.GetProperty("id").GetString() == "PA-NATIVE-019"
            && item.GetProperty("variants").EnumerateArray()
                .Any(variant => variant.GetString() == "naval"));
        Assert.Contains(cases, item => item.GetProperty("id").GetString() == "PA-NATIVE-021"
            && item.GetProperty("expected").GetProperty("guestEquipmentProtected").GetBoolean());
    }

    [Fact]
    public void McpSurfaceDerivesTheDisposableSaveAndProhibitsActionBypass()
    {
        string root = TestOptions.FindWorkspace();
        string source = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignMcp"), "TestingTools.PartyAgency.cs"));
        foreach (string tool in new[]
        {
            "reign_get_party_agency_test_manifest", "reign_prepare_party_agency_test",
            "reign_carry_forward_party_agency_passes",
            "reign_start_party_agency_test", "reign_verify_party_agency_save_roundtrip",
            "reign_evaluate_party_agency_release_readiness"
        }) Assert.Contains(tool, source, StringComparison.Ordinal);
        Assert.Contains("RequireArmedOwnedSaveAsync", source, StringComparison.Ordinal);
        Assert.Contains("selfContainedHostilityLanguageCase", source,
            StringComparison.Ordinal);
        Assert.Contains("caseId.Equals(\"PA-LANG-017\"", source, StringComparison.Ordinal);
        Assert.Contains("caseId.Equals(\"PA-LANG-018\"", source, StringComparison.Ordinal);
        Assert.Contains("fixtureRole = \"party_leader\"", source,
            StringComparison.Ordinal);
        Assert.Contains("manifest.GetProperty(\"nativeInvitationTemplates\")", source,
            StringComparison.Ordinal);
        Assert.Contains("operation = \"party_agency_prepare_hostility\"", source,
            StringComparison.Ordinal);
        Assert.Contains("authorization.CurrentSaveName", source, StringComparison.Ordinal);
        Assert.Contains("start Reign Party Agency test on disposable save", source,
            StringComparison.Ordinal);
        Assert.Contains("directDialogueActionInvocation", source, StringComparison.Ordinal);
        Assert.Contains("contextualTurnIndexes", source, StringComparison.Ordinal);
        Assert.Contains("contextualPartyAgencyResponse", source, StringComparison.Ordinal);
        Assert.Contains("prepare_party_agency_fixture", source, StringComparison.Ordinal);
        Assert.Contains("@party_agency_fixture", source, StringComparison.Ordinal);
        Assert.Contains("PartyAgencyDialogueOwner", source, StringComparison.Ordinal);
        Assert.Contains("temporary_guest_equipment_lock_only", source, StringComparison.Ordinal);
        Assert.Contains("temporary_guest_relationship_isolation_only", source,
            StringComparison.Ordinal);
        Assert.Contains("temporary_guest_waiting_camp_encounter_exclusion_only", source,
            StringComparison.Ordinal);
        Assert.Contains("party_agency_hostile_battle_fixture_only", source,
            StringComparison.Ordinal);
        Assert.Contains("party_agency_hostility_language_fixture_only", source,
            StringComparison.Ordinal);
        Assert.Contains("party_agency_partyless_invitation_fixture_only", source,
            StringComparison.Ordinal);
        Assert.Contains("party_agency_remaining_native_invitation_fixture_only", source,
            StringComparison.Ordinal);
        Assert.Contains("party_agency_ineligible_native_fixture_only", source,
            StringComparison.Ordinal);
        Assert.Contains("party_agency_companion_native_fixture_only", source,
            StringComparison.Ordinal);
        Assert.Contains("party_agency_native_time_driver_only", source,
            StringComparison.Ordinal);
        Assert.Contains("party_agency_save_roundtrip_state_driver_only", source,
            StringComparison.Ordinal);
        Assert.Contains("party_agency_accepted_lifecycle_router_only", source,
            StringComparison.Ordinal);
        Assert.Contains("party_agency_simultaneous_review_queue_only", source,
            StringComparison.Ordinal);
        Assert.Contains("party_agency_departure_language_fixture_only", source,
            StringComparison.Ordinal);
        Assert.Contains("party_agency_return_timing_evidence_only", source,
            StringComparison.Ordinal);
        Assert.Contains("party_agency_recovery_fixture_only", source,
            StringComparison.Ordinal);
        Assert.Contains("party_agency_missing_return_recovery_fixture_only", source,
            StringComparison.Ordinal);
        Assert.Contains("party_agency_final_fingerprint_harness_only", source,
            StringComparison.Ordinal);
        Assert.Contains("party_agency_transcript_evidence_parser_only", source,
            StringComparison.Ordinal);
        Assert.Contains("affectedMissingReturnRecoveryFixture", source,
            StringComparison.Ordinal);
        Assert.Contains("finalFingerprintHarnessOnly", source,
            StringComparison.Ordinal);
        Assert.Contains("caseId.Equals(\"PA-NATIVE-022\"", source,
            StringComparison.Ordinal);
        Assert.Contains("new Dictionary<string, object?>()", source,
            StringComparison.Ordinal);
        Assert.Contains("affectedHostileBattle", source, StringComparison.Ordinal);
        Assert.Contains("affectedWaitingCampEncounter", source, StringComparison.Ordinal);
        Assert.Contains("affectedHostilityLanguage", source, StringComparison.Ordinal);
        Assert.Contains("affectedPartylessInvitation", source, StringComparison.Ordinal);
        Assert.Contains("affectedRemainingNativeInvitation", source,
            StringComparison.Ordinal);
        Assert.Contains("affectedIneligibleNativeFixture", source,
            StringComparison.Ordinal);
        Assert.Contains("affectedCompanionNativeFixture", source,
            StringComparison.Ordinal);
        Assert.Contains("affectedNativeTimeDriver", source,
            StringComparison.Ordinal);
        Assert.Contains("affectedSaveRoundtripStateDriver", source,
            StringComparison.Ordinal);
        Assert.Contains("splitSaveRoundtripEvidence", source,
            StringComparison.Ordinal);
        Assert.Contains("transcriptReports.All", source,
            StringComparison.Ordinal);
        Assert.Contains("candidate.Data.HasValue && ContainsPartyAgencyTranscriptTurn", source,
            StringComparison.Ordinal);
        Assert.Contains("affectedSimultaneousReviewQueue", source,
            StringComparison.Ordinal);
        Assert.Contains("affectedDepartureLanguage", source,
            StringComparison.Ordinal);
        Assert.Contains("affectedReturnTimingEvidence", source,
            StringComparison.Ordinal);
        Assert.Contains("affectedRecoveryFixture", source,
            StringComparison.Ordinal);
        Assert.Contains("&& !partylessInvitationFixtureOnly", source,
            StringComparison.Ordinal);
        Assert.Contains("transcriptEvidenceParserOnly", source, StringComparison.Ordinal);
        Assert.Contains("relationshipIsolationOnly && !string.Equals(source.ClientBuild",
            source, StringComparison.Ordinal);
        Assert.Contains("PA-NATIVE-021", source, StringComparison.Ordinal);
        Assert.Contains("CarriedEvidence", source, StringComparison.Ordinal);
        Assert.Contains("ContainsPartyAgencyTranscriptTurn", source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("raw.Contains(turn", source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("disposableSaveName,", source.Split("StartPartyAgencyTest", 2,
            StringSplitOptions.None)[1].Split("EvaluatePartyAgencyReleaseReadiness", 2,
            StringSplitOptions.None)[0], StringComparison.Ordinal);
    }

    [Fact]
    public void ServerAllowsEveryPartyAgencyNativeHarnessOperation()
    {
        string root = TestOptions.FindWorkspace();
        string server = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignServer"), "LiveInteractionTest.cs",
            "/src/Modules/WorldSimulation/"));
        foreach (string operation in new[]
        {
            "prepare_party_agency_fixture", "party_agency_advance_time",
            "party_agency_aggregate_fixtures", "party_agency_review_provider_result",
            "party_agency_recovery_fixture", "party_agency_verify_unrelated_state",
            "party_agency_prepare_hostility", "party_agency_hostile_battle",
            "party_agency_stage_save_phase", "party_agency_verify_save_phase",
            "party_agency_test"
        }) Assert.Contains($"\"{operation}\"", server, StringComparison.Ordinal);
    }

    [Fact]
    public void PositiveInvitationCasesProvideCompleteContextAndRequestExplicitConsent()
    {
        string root = TestOptions.FindWorkspace();
        string path = Path.Combine(root, "ReignServer", "tests", "ReignLiveTest", "scenarios",
            "party-agency-manifest.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement[] cases = document.RootElement.GetProperty("cases").EnumerateArray().ToArray();
        string[] positiveInvitations =
        {
            "PA-LANG-001", "PA-LANG-002", "PA-LANG-003", "PA-LANG-004", "PA-LANG-005",
            "PA-LANG-006", "PA-LANG-007", "PA-LANG-008", "PA-LANG-013"
        };

        foreach (string caseId in positiveInvitations)
        {
            JsonElement item = Assert.Single(cases,
                candidate => candidate.GetProperty("id").GetString() == caseId);
            JsonElement contract = item.GetProperty("consentContract");
            Assert.True(contract.GetProperty("purposeStated").GetBoolean());
            Assert.True(contract.GetProperty("knownRiskStated").GetBoolean());
            Assert.True(contract.GetProperty("npcRelevanceStated").GetBoolean());
            Assert.True(contract.GetProperty("termOrReviewStated").GetBoolean());
            Assert.True(contract.GetProperty("askedNotOrdered").GetBoolean());
            Assert.True(contract.GetProperty("explicitAgreementRequested").GetBoolean());

            string dialogue = string.Join(" ", item.GetProperty("turns").EnumerateArray()
                .Select(turn => turn.GetString() ?? string.Empty));
            Assert.True(dialogue.Length >= 180,
                $"{caseId} must provide enough natural context for an informed decision.");
            Assert.Contains("agree", dialogue, StringComparison.OrdinalIgnoreCase);
            string[] turns = item.GetProperty("turns").EnumerateArray()
                .Select(turn => turn.GetString() ?? string.Empty).ToArray();
            Assert.True(turns.Length >= 2,
                $"{caseId} must allow a natural negotiation before final consent.");
            Assert.Contains("explicitly agree", turns[^1], StringComparison.OrdinalIgnoreCase);
            int[] contextualIndexes = item.GetProperty("contextualTurnIndexes")
                .EnumerateArray().Select(index => index.GetInt32()).ToArray();
            Assert.Contains(turns.Length - 1, contextualIndexes);
            JsonElement expected = item.GetProperty("expected");
            Assert.True(expected.TryGetProperty("termKind", out _),
                $"{caseId} must assert the persisted schedule kind.");
            Assert.True(expected.TryGetProperty("reviewIntervalDays", out _),
                $"{caseId} must assert the persisted review interval.");
        }

        JsonElement guardedFamilyMatter = Assert.Single(cases,
            candidate => candidate.GetProperty("id").GetString() == "PA-LANG-006");
        string guardedFamilyDialogue = string.Join(" ", guardedFamilyMatter.GetProperty("turns")
            .EnumerateArray().Select(turn => turn.GetString() ?? string.Empty));
        Assert.Contains("adult cousin", guardedFamilyDialogue, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("negotiation only", guardedFamilyDialogue, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("never an abduction", guardedFamilyDialogue, StringComparison.OrdinalIgnoreCase);

        JsonElement identifiedBorderMission = Assert.Single(cases,
            candidate => candidate.GetProperty("id").GetString() == "PA-LANG-005");
        string identifiedBorderDialogue = string.Join(" ", identifiedBorderMission.GetProperty("turns")
            .EnumerateArray().Select(turn => turn.GetString() ?? string.Empty));
        Assert.Contains("sovereign of fen Niabar", identifiedBorderDialogue,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Zeonica's western border", identifiedBorderDialogue,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("inspect the survivors' statements", identifiedBorderDialogue,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("withdraw and appeal to Zeonica's court", guardedFamilyDialogue,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not a quarrel with your empire", guardedFamilyDialogue,
            StringComparison.OrdinalIgnoreCase);

        foreach (string caseId in new[] { "PA-LANG-014", "PA-LANG-015", "PA-LANG-017" })
        {
            JsonElement item = Assert.Single(cases,
                candidate => candidate.GetProperty("id").GetString() == caseId);
            string[] turns = item.GetProperty("turns").EnumerateArray()
                .Select(turn => turn.GetString() ?? string.Empty).ToArray();
            Assert.True(turns.Length >= 2,
                $"{caseId} must allow a natural negotiation before final consent.");
            Assert.Contains("explicitly agree", turns[^1], StringComparison.OrdinalIgnoreCase);
            int[] contextualIndexes = item.GetProperty("contextualTurnIndexes")
                .EnumerateArray().Select(index => index.GetInt32()).ToArray();
            Assert.Contains(turns.Length - 1, contextualIndexes);
        }

        foreach (string caseId in new[]
        {
            "PA-LANG-009", "PA-LANG-010", "PA-LANG-011", "PA-LANG-012"
        })
        {
            JsonElement item = Assert.Single(cases,
                candidate => candidate.GetProperty("id").GetString() == caseId);
            Assert.False(item.TryGetProperty("consentContract", out _));
            Assert.Equal(0, item.GetProperty("expected").GetProperty("recordDelta").GetInt32());
        }
    }

    [Fact]
    public void ClientHarnessSupportsOneNpcFirstReviewAndEvidenceAssertions()
    {
        string root = TestOptions.FindWorkspace();
        string host = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignBeta"), "ReignLiveInteractionTestHost.cs",
            "/src/Modules/WorldSimulation/Campaign/"));
        Assert.Contains("targetedReview", host, StringComparison.Ordinal);
        Assert.Contains("OpenTargetedReview", host, StringComparison.Ordinal);
        Assert.Contains("naturalLanguageOnly", host, StringComparison.Ordinal);
        Assert.Contains("directDialogueActionInvocation", host, StringComparison.Ordinal);
        Assert.Contains("EvaluatePartyAgencyExpected", host, StringComparison.Ordinal);
        Assert.Contains("phase.Equals(\"before\", StringComparison.OrdinalIgnoreCase)\n"
            + "                ? new JObject()", host, StringComparison.Ordinal);
        Assert.Contains("BuildPartyAgencyCandidateMatrix", host, StringComparison.Ordinal);
        Assert.Contains("reviewIntervalDays", host, StringComparison.Ordinal);
        Assert.Contains("fixedTermDays", host, StringComparison.Ordinal);
        Assert.Contains("BuildPartyAgencyContextualPlayerResponse", host,
            StringComparison.Ordinal);
        Assert.Contains("\"give me yours\"", host, StringComparison.Ordinal);
        Assert.Contains("\"hear a name\"", host, StringComparison.Ordinal);
        Assert.Contains("\"name from your mouth\"", host, StringComparison.Ordinal);
        Assert.Contains("\"written record\"", host, StringComparison.Ordinal);
        Assert.Contains("\"addendum\"", host, StringComparison.Ordinal);
        Assert.Contains("I place them in your hands now for inspection", host,
            StringComparison.Ordinal);
        Assert.Contains("_lastNpcReplyText", host, StringComparison.Ordinal);
        Assert.Contains("[\"playerText\"] = text", host, StringComparison.Ordinal);
        Assert.Contains("PartyAgencyNativeEvidence", host, StringComparison.Ordinal);
        Assert.Contains("nativeEvidenceReceipt", host, StringComparison.Ordinal);

        string fixture = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignBeta"),
            "ReignLiveInteractionPartyAgencyFixtures.cs",
            "/src/Modules/WorldSimulation/Campaign/"));
        Assert.Contains("confirmDisposableCampaign", fixture, StringComparison.Ordinal);
        Assert.Contains("expectedSaveName", fixture, StringComparison.Ordinal);
        Assert.Contains("directGuestActionInvoked", fixture, StringComparison.Ordinal);
        Assert.Contains("IsDisposablePartyAgencyFixtureConversationActive", fixture,
            StringComparison.Ordinal);
        Assert.Contains("activeSave.StartsWith(\"ReignTest_\"", fixture,
            StringComparison.Ordinal);
        Assert.Contains("activeSave.EndsWith(\"_Current\"", fixture,
            StringComparison.Ordinal);
        Assert.Contains("CampaignTimeControlMode.Stop", fixture, StringComparison.Ordinal);
        Assert.Contains("TryStartPartyAgencyNativeSettlementWait", fixture,
            StringComparison.Ordinal);
        Assert.Contains("safe settlement wait menu", fixture, StringComparison.Ordinal);
        Assert.Contains("deferredUnrelatedReviewDueDays", fixture,
            StringComparison.Ordinal);
        Assert.Contains("nativeSettlementWaitUsed", fixture, StringComparison.Ordinal);
        Assert.Contains("deferredUnrelatedReviewCount", fixture, StringComparison.Ordinal);
        Assert.Contains("CreateLordParty", fixture, StringComparison.Ordinal);
        Assert.Contains("CreateArmy", fixture, StringComparison.Ordinal);
        Assert.Contains("party_agency_stage_save_phase", host, StringComparison.Ordinal);
        Assert.Contains("party_agency_verify_save_phase", host, StringComparison.Ordinal);
        Assert.Contains("requiresNativeCheckpointAndRestart", fixture,
            StringComparison.Ordinal);
        Assert.Contains("restartReceipt", fixture, StringComparison.Ordinal);
        Assert.Contains("financeAndInteractionSafety", fixture, StringComparison.Ordinal);
        Assert.Contains("guestEquipmentProtected", fixture, StringComparison.Ordinal);
        Assert.Contains("CanHeroEquipmentBeChanged()", fixture, StringComparison.Ordinal);
        Assert.Contains("battleEquipmentSignature", fixture, StringComparison.Ordinal);
        Assert.Contains("civilianEquipmentSignature", fixture, StringComparison.Ordinal);
        Assert.Contains("terminalGuestState", fixture, StringComparison.Ordinal);
        Assert.Contains("recoveryStartingPhase", fixture, StringComparison.Ordinal);
        Assert.Contains("naturalDepartureReachedReturn", fixture, StringComparison.Ordinal);
        Assert.Contains("recoveryFixtureStagedReturn", fixture, StringComparison.Ordinal);
        Assert.Contains("recoveryMutationAppliedBeforeReturn", fixture, StringComparison.Ordinal);
        Assert.Contains("recoveryNativeStateObservedBeforeReturn", fixture, StringComparison.Ordinal);
        Assert.Contains("record.SourcePartyStringId = string.Empty;", fixture,
            StringComparison.Ordinal);
        Assert.Contains("record.SourcePartyStringId = releasedSourceId;", fixture,
            StringComparison.Ordinal);
        Assert.Contains("variant == \"missing_source_party\" || variant == \"invalid_return_target\"", fixture,
            StringComparison.Ordinal);
        Assert.Contains("after preserving the natural departure response", fixture,
            StringComparison.Ordinal);
        Assert.Contains("sourcePartyDefaultBehavior", fixture, StringComparison.Ordinal);
        Assert.Contains("sourcePartyUsedByQuest", fixture, StringComparison.Ordinal);
        Assert.DoesNotContain("AcceptGuest(", fixture, StringComparison.Ordinal);
        Assert.DoesNotContain("RegularAcceptTemporaryPartyGuest", fixture,
            StringComparison.Ordinal);

        string guestBehavior = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignBeta"),
            "ReignTemporaryPartyGuestCampaignBehavior.cs",
            "/src/Modules/WorldSimulation/PartyAgency/"));
        Assert.Contains("IsDisposablePartyAgencyFixtureConversationActive(hero)", guestBehavior,
            StringComparison.Ordinal);
        Assert.Contains("CanHeroEquipmentBeChangedEvent.AddNonSerializedListener", guestBehavior,
            StringComparison.Ordinal);
        Assert.Contains("IsTemporaryGuestEquipmentLocked", guestBehavior, StringComparison.Ordinal);
        Assert.Contains("record.ProtectedCampMorale = party.Morale", guestBehavior,
            StringComparison.Ordinal);
        Assert.Contains("party.RecentEventsMorale += moraleDelta", guestBehavior,
            StringComparison.Ordinal);
        Assert.Contains("[\"protectedTroopSignature\"]", fixture,
            StringComparison.Ordinal);
        Assert.Contains("hero?.StringId, StringComparison.OrdinalIgnoreCase",
            fixture, StringComparison.Ordinal);
        Assert.Contains("\"sourcePartyId\", \"sourcePartyClanId\", \"protectedTroopSignature\"",
            fixture, StringComparison.Ordinal);

        string guestRecord = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignBeta"),
            "ReignTemporaryPartyGuestRecord.cs",
            "/src/Modules/WorldSimulation/PartyAgency/"));
        Assert.Contains("[SaveableField(27)] public float ProtectedCampMorale", guestRecord,
            StringComparison.Ordinal);
        Assert.Contains("[SaveableField(28)] public bool ProtectedCampMoraleCaptured", guestRecord,
            StringComparison.Ordinal);

        string validator = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignBeta"), "ReignActionValidator.cs",
            "/src/Modules/WorldSimulation/World/"));
        Assert.Contains("IsTemporaryGuestEquipmentLocked(from)", validator,
            StringComparison.Ordinal);
        string valueService = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignBeta"), "ReignValueService.cs",
            "/src/Modules/WorldSimulation/World/"));
        Assert.Contains("IsTemporaryGuestEquipmentLocked(from)", valueService,
            StringComparison.Ordinal);

        string patches = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignBeta"),
            "ReignTemporaryPartyGuestPatches.cs",
            "/src/Modules/WorldSimulation/PartyAgency/"));
        Assert.Contains("BuildProtectedCampSafetyEvidence", patches,
            StringComparison.Ordinal);
        Assert.Contains("protectedPartyExpenseSuppressed", patches,
            StringComparison.Ordinal);
        Assert.Contains("guestClanLeaderExpenseSuppressed", patches,
            StringComparison.Ordinal);
        Assert.Contains("encounterBlockedAsAttacker", patches,
            StringComparison.Ordinal);
        Assert.Contains("encounterBlockedAsDefender", patches,
            StringComparison.Ordinal);
        Assert.Contains("battleJoinBlocked", patches,
            StringComparison.Ordinal);
        Assert.Contains("nearbyReinforcementBlocked", patches,
            StringComparison.Ordinal);
        Assert.Contains("nameof(MapEvent.CanPartyJoinBattle)", patches,
            StringComparison.Ordinal);
        Assert.Contains("AddNearbyPartyToPlayerMapEvent", patches,
            StringComparison.Ordinal);

        string nativeFixtures = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignBeta"),
            "ReignLiveInteractionPartyAgencyFixtures.cs",
            "/src/Modules/WorldSimulation/Campaign/"));
        Assert.Contains("protectedCampExcludedFromBattle", nativeFixtures,
            StringComparison.Ordinal);
        Assert.Contains("protectedCampExcludedFromBattle\"] = protectedCampExcluded",
            nativeFixtures, StringComparison.Ordinal);
        Assert.Contains("playerSideWon", nativeFixtures, StringComparison.Ordinal);
        Assert.Contains("playerSafetyPreserved", nativeFixtures, StringComparison.Ordinal);
        Assert.Contains("mapEvent.WinningSide == mapEvent.PlayerSide", nativeFixtures,
            StringComparison.Ordinal);

        string partyChat = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignBeta"), "ReignPartyChatScreenVM.cs",
            "/src/Modules/Dialogue/UI/ViewModels/"));
        Assert.Contains("await WaitForAutomationReadyAsync()", partyChat,
            StringComparison.Ordinal);
        Assert.Contains("activeBatch == null || activeBatch.IsCompleted", partyChat,
            StringComparison.Ordinal);
        Assert.Contains("Timed out waiting for party chat to become ready", partyChat,
            StringComparison.Ordinal);
        string campaignTest = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignMcp"), "CampaignTestTools.cs"));
        Assert.Contains("if (!snapshot.FindBoolean(\"safeSettlement\"))",
            campaignTest, StringComparison.Ordinal);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(campaignTest,
            "requireSafeSettlement: false",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant).Cast<System.Text.RegularExpressions.Match>());
        Assert.Contains("ValidateSnapshot(state, activeSnapshot, allowBaseline: false,\n            requireSafeSettlement: false);",
            campaignTest, StringComparison.Ordinal);
    }

    [Fact]
    public void NativeEligibilityCasesUseGuardedFixturesAndNaturalInvitationTemplates()
    {
        string root = TestOptions.FindWorkspace();
        string path = Path.Combine(root, "ReignServer", "tests", "ReignLiveTest",
            "scenarios", "party-agency-manifest.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement manifest = document.RootElement;
        JsonElement templates = manifest.GetProperty("nativeInvitationTemplates");
        foreach (string template in new[]
        {
            "fixed", "partyless_mercenary_fixed", "remaining_native_fixed",
            "army_fixed", "remaining_native_army_fixed", "fixed_one",
            "remaining_native_fixed_one", "open_ended",
            "remaining_native_open_ended", "fixed_thirty",
            "remaining_native_fixed_thirty"
        })
        {
            JsonElement item = templates.GetProperty(template);
            string[] turns = item.GetProperty("turns").EnumerateArray()
                .Select(turn => turn.GetString() ?? string.Empty).ToArray();
            Assert.True(turns.Length >= 2);
            Assert.Contains("explicitly agree", turns[^1],
                StringComparison.OrdinalIgnoreCase);
            Assert.Contains(turns.Length - 1,
                item.GetProperty("contextualTurnIndexes").EnumerateArray()
                    .Select(index => index.GetInt32()));
        }
        JsonElement[] cases = manifest.GetProperty("cases").EnumerateArray().ToArray();
        JsonElement partylessTemplate = templates.GetProperty("partyless_mercenary_fixed");
        string partylessClosingTurn = partylessTemplate.GetProperty("turns")[1].GetString()!;
        Assert.Contains("My name is Egan", partylessClosingTurn,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("twenty thousand denars", partylessClosingTurn,
            StringComparison.OrdinalIgnoreCase);
        JsonElement partylessCase = Assert.Single(cases,
            candidate => candidate.GetProperty("id").GetString() == "PA-NATIVE-001");
        Assert.Equal("partyless_mercenary_fixed",
            partylessCase.GetProperty("invitationTemplate").GetString());
        foreach (string preservedCaseId in new[]
        {
            "PA-NATIVE-002", "PA-NATIVE-018", "PA-NATIVE-021"
        })
        {
            JsonElement preserved = Assert.Single(cases,
                candidate => candidate.GetProperty("id").GetString() == preservedCaseId);
            Assert.Equal("fixed", preserved.GetProperty("invitationTemplate").GetString());
        }
        foreach (string detailedCaseId in new[]
        {
            "PA-NATIVE-003", "PA-NATIVE-006", "PA-NATIVE-007", "PA-NATIVE-010",
            "PA-NATIVE-015", "PA-NATIVE-016", "PA-NATIVE-017", "PA-NATIVE-019",
            "PA-NATIVE-020"
        })
        {
            JsonElement detailed = Assert.Single(cases,
                candidate => candidate.GetProperty("id").GetString() == detailedCaseId);
            Assert.Equal("remaining_native_fixed",
                detailed.GetProperty("invitationTemplate").GetString());
        }
        foreach (int number in Enumerable.Range(1, 7))
        {
            string caseId = "PA-NATIVE-" + number.ToString("000");
            JsonElement item = Assert.Single(cases,
                candidate => candidate.GetProperty("id").GetString() == caseId);
            Assert.True(item.TryGetProperty("invitationTemplate", out _),
                $"{caseId} must perform the guest decision through natural dialogue.");
        }
        foreach (int number in Enumerable.Range(9, 4))
        {
            string caseId = "PA-NATIVE-" + number.ToString("000");
            JsonElement item = Assert.Single(cases,
                candidate => candidate.GetProperty("id").GetString() == caseId);
            Assert.True(item.TryGetProperty("fixtureRole", out _));
            Assert.True(item.TryGetProperty("invitationTemplate", out _));
        }
        foreach (string caseId in new[] { "PA-NATIVE-015", "PA-NATIVE-016" })
        {
            JsonElement item = Assert.Single(cases,
                candidate => candidate.GetProperty("id").GetString() == caseId);
            Assert.True(item.TryGetProperty("invitationTemplate", out _));
            Assert.True(item.TryGetProperty("departureTemplate", out _));
        }
    }

    [Fact]
    public void TemporaryGuestScheduleIsCanonicalizedAndProvedEndToEnd()
    {
        string root = TestOptions.FindWorkspace();
        string server = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignServer"), "Program.cs",
            "/src/Modules/Platform/"));
        string behavior = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignBeta"), "ReignTemporaryPartyGuestCampaignBehavior.cs",
            "/src/Modules/WorldSimulation/PartyAgency/"));
        Assert.Contains("NormalizeTemporaryGuestTermKind(terms, errors)", server,
            StringComparison.Ordinal);
        Assert.Contains("terms[\"termKind\"] = \"fixed\"", server,
            StringComparison.Ordinal);
        Assert.Contains("social role such as independent ally", server,
            StringComparison.Ordinal);
        Assert.Contains("pair.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)", server,
            StringComparison.Ordinal);
        Assert.Contains("new[] { \"fields\", \"arguments\" }", server,
            StringComparison.Ordinal);
        Assert.Contains("copy[\"terms\"] = terms", server,
            StringComparison.Ordinal);
        Assert.Contains("never use temporary_party_guest", server,
            StringComparison.Ordinal);
        Assert.Contains("set reviewIntervalDays=5, and omit durationDays", server,
            StringComparison.Ordinal);
        Assert.Contains("terms.Remove(\"durationDays\")", server,
            StringComparison.Ordinal);
        Assert.Contains("duration >= MinimumFixedTermDays", behavior,
            StringComparison.Ordinal);

        string manifestPath = Path.Combine(root, "ReignServer", "tests", "ReignLiveTest", "scenarios", "party-agency-manifest.json");
        string manifest = File.ReadAllText(manifestPath);
        Assert.Contains("There is no fixed end date", manifest, StringComparison.Ordinal);
        Assert.Contains("review every five days", manifest, StringComparison.Ordinal);
    }

    [Fact]
    public void AcceptedGuestInvitationSurvivesTheActionPlannerCandidateCut()
    {
        string root = TestOptions.FindWorkspace();
        string server = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignServer"), "Program.cs",
            "/src/Modules/Platform/"));

        Assert.Contains("TemporaryPartyGuestCandidateToPreserve(actionGate, playerText)", server,
            StringComparison.Ordinal);
        Assert.Contains("return \"accept_temporary_party_guest\"", server, StringComparison.Ordinal);
        Assert.Contains("return \"renew_temporary_party_guest\"", server, StringComparison.Ordinal);
        Assert.Contains("return \"end_temporary_party_guest\"", server, StringComparison.Ordinal);
        Assert.Contains("return \"acknowledge_own_faction_combat_risk\"", server,
            StringComparison.Ordinal);
        Assert.Contains("\"join my party\"", server, StringComparison.Ordinal);
        Assert.Contains("\"travel with my party\"", server, StringComparison.Ordinal);
        Assert.Contains("\"ride with my party\"", server, StringComparison.Ordinal);
        Assert.Contains("\"return to your people\"", server, StringComparison.Ordinal);
        Assert.Contains("\"renew our arrangement\"", server, StringComparison.Ordinal);
        Assert.Contains("\"accept banishment\"", server, StringComparison.Ordinal);
        Assert.Contains("temporaryGuestLifecycleCommands", server, StringComparison.Ordinal);
        Assert.Contains("retrieved.RemoveAll(candidate =>", server, StringComparison.Ordinal);
        Assert.Contains("!string.Equals(candidateCommand, temporaryGuestCommand", server,
            StringComparison.Ordinal);
        Assert.DoesNotContain("PlayerExplicitlyEndsTemporaryPartyGuest", server,
            StringComparison.Ordinal);
        Assert.DoesNotContain("playerAuthorizedDeparture", server, StringComparison.Ordinal);
        Assert.DoesNotContain("playerAuthorizedGuestDeparture", server, StringComparison.Ordinal);
        Assert.Contains("PlanActionsFromGate(campaignId, payload, hero, playerText, visibleReply, actionGate)",
            server, StringComparison.Ordinal);
    }

    [Fact]
    public void GuestLifecycleActionsBindToTheSpeakingNpcBeforeResolution()
    {
        string root = TestOptions.FindWorkspace();
        string server = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignServer"), "Program.cs",
            "/src/Modules/Platform/"));

        int binding = server.IndexOf(
            "ApplyTemporaryPartyGuestActorBinding(raw, terms, payload",
            StringComparison.Ordinal);
        int resolution = server.IndexOf(
            "ResolveActionReferences(raw, terms, payload",
            StringComparison.Ordinal);
        Assert.True(binding >= 0 && resolution > binding);
        Assert.Contains("action[\"actorHeroStringId\"] = speakerHeroId", server,
            StringComparison.Ordinal);
        Assert.Contains("terms[\"fromHeroStringId\"] = speakerHeroId", server,
            StringComparison.Ordinal);
        Assert.Contains("canonical != \"renew_temporary_party_guest\"", server,
            StringComparison.Ordinal);
        Assert.Contains("canonical != \"end_temporary_party_guest\"", server,
            StringComparison.Ordinal);
        Assert.Contains("canonical != \"acknowledge_own_faction_combat_risk\"", server,
            StringComparison.Ordinal);
    }

    [Fact]
    public void GuestLifecycleActionsDoNotCreateRelationshipEffects()
    {
        string root = TestOptions.FindWorkspace();
        string server = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignServer"), "Program.cs",
            "/src/Modules/Platform/"));
        string relationships = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignServer"), "Relationships.cs",
            "/src/Modules/Relationships/"));

        Assert.Contains("IsTemporaryPartyGuestLifecycleConversation(actionGate, playerText",
            server,
            StringComparison.Ordinal);
        Assert.Contains("TemporaryPartyGuestLifecycleCommandFromConversation(",
            server,
            StringComparison.Ordinal);
        Assert.Contains("relationshipAssessments.Clear();", server,
            StringComparison.Ordinal);
        Assert.Contains("relationshipUpdates.Clear();", server,
            StringComparison.Ordinal);
        Assert.Contains("IsTemporaryPartyGuestLifecycleAction(CanonicalCommand(actionType))",
            relationships,
            StringComparison.Ordinal);
        Assert.Contains("case \"accept_temporary_party_guest\"", relationships,
            StringComparison.Ordinal);
        Assert.Contains("case \"renew_temporary_party_guest\"", relationships,
            StringComparison.Ordinal);
        Assert.Contains("case \"end_temporary_party_guest\"", relationships,
            StringComparison.Ordinal);
        Assert.Contains("case \"acknowledge_own_faction_combat_risk\"", relationships,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AcceptedGuestLifecycleRecoversFromAnEmptySecondaryPlannerResult()
    {
        string root = TestOptions.FindWorkspace();
        string server = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignServer"), "Program.cs",
            "/src/Modules/Platform/"));

        Assert.Contains("BuildAcceptedTemporaryPartyGuestFallbackAction(", server,
            StringComparison.Ordinal);
        Assert.Contains("if (candidates.Count == 0)", server, StringComparison.Ordinal);
        Assert.Contains("accepted_temporary_guest_fallback", server, StringComparison.Ordinal);
        Assert.Contains("TemporaryPartyGuestCandidateToPreserve(actionGate, playerText)", server,
            StringComparison.Ordinal);
        Assert.Contains("replacedMismatchedPlannerAction", server,
            StringComparison.Ordinal);
        Assert.Contains("plannerReturnedAcceptedGuestCommand", server,
            StringComparison.Ordinal);
        Assert.True(server.IndexOf("return \"accept_temporary_party_guest\"",
                StringComparison.Ordinal)
            < server.IndexOf("return \"renew_temporary_party_guest\"",
                StringComparison.Ordinal),
            "An explicit initial join request must outrank incidental review wording.");
        Assert.True(server.IndexOf("return \"accept_temporary_party_guest\"",
                StringComparison.Ordinal)
            < server.IndexOf("return \"end_temporary_party_guest\"",
                StringComparison.Ordinal),
            "An explicit initial join request must outrank future departure safeguards in an open-ended agreement.");
        Assert.Contains("terms[\"consentConfirmed\"] = true", server,
            StringComparison.Ordinal);
        Assert.Contains("ExtractItemAmount(acceptedExchange, \"days?\")", server,
            StringComparison.Ordinal);
        Assert.Contains("durationDays < 1 || durationDays > 30", server,
            StringComparison.Ordinal);
        Assert.Contains("terms[\"reviewIntervalDays\"] = 5", server,
            StringComparison.Ordinal);
        Assert.Contains("[\"actorHeroStringId\"] = speakerHeroId", server,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ApplicationTicksQueueEveryPastDueGuestBeforeShowingAReview()
    {
        string root = TestOptions.FindWorkspace();
        string behavior = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignBeta"),
            "ReignTemporaryPartyGuestCampaignBehavior.cs",
            "/src/Modules/WorldSimulation/PartyAgency/"));

        int promote = behavior.IndexOf("PromotePastDueReviews();",
            StringComparison.Ordinal);
        int show = behavior.IndexOf("TryShowDueReview();", StringComparison.Ordinal);
        Assert.True(promote >= 0 && show > promote,
            "Every already-due agreement must enter the review queue before the first popup locks campaign time.");
        Assert.Contains("now >= x.ReviewDueDay", behavior, StringComparison.Ordinal);
        Assert.Contains("record.Phase = ReignTemporaryGuestPhase.ReviewDue", behavior,
            StringComparison.Ordinal);
    }

    [Fact]
    public void InPersonGuestConsentRecognizesThePlayersSettlementMenuPresence()
    {
        string root = TestOptions.FindWorkspace();
        string behavior = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignBeta"), "ReignTemporaryPartyGuestCampaignBehavior.cs",
            "/src/Modules/WorldSimulation/PartyAgency/"));

        Assert.Contains("Hero.MainHero?.CurrentSettlement", behavior, StringComparison.Ordinal);
        Assert.Contains("hero.CurrentSettlement == playerSettlement", behavior,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CampaignTestStartDoesNotTrustAStaleCampaignHeartbeatAfterTheGameStops()
    {
        string root = TestOptions.FindWorkspace();
        string service = File.ReadAllText(Path.Combine(root, "ReignMcp", "src",
            "Reign.Mcp.Server", "Modules", "Platform", "CampaignTestTools.cs"));

        Assert.Contains("status.FindBoolean(\"running\")", service,
            StringComparison.Ordinal);
        Assert.Contains("!string.Equals(lifecycleStatus, \"campaign_ready\"",
            service, StringComparison.Ordinal);
        Assert.Contains("status.FindString(\"activeSaveName\"), expectedSave",
            service, StringComparison.Ordinal);
        Assert.Contains("ValidateSnapshot(state, snapshot, allowBaseline: !state.Armed);",
            service, StringComparison.Ordinal);
        Assert.Contains("\"game\", \"start\", \"--save\", expectedSave", service,
            StringComparison.Ordinal);
    }
}
