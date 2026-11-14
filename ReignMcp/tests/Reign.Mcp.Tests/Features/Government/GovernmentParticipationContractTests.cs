using Reign.Core.Contracts.Actions;
using Reign.Core.Contracts.Government;
using System.Text.Json;

namespace Reign.Mcp.Tests;

public sealed class GovernmentParticipationContractTests
{
    [Fact]
    public void PoliticalProfileValidationPreservesTheCompleteModelWithoutPerScanAllocation()
    {
        Assert.Equal(Enumerable.Range(0, 16), Enum.GetValues<ReignGovernmentPlank>().Select(x => (int)x));
        Assert.Equal(typeof(int), Enum.GetUnderlyingType(typeof(ReignGovernmentPlank)));
        var profile = new GovernmentPoliticalProfile { CampaignId = "campaign", HeroId = "hero", Revision = 1, ModelVersion = GovernmentPoliticalBeliefs.ModelVersion,
            Values = Enum.GetValues<ReignGovernmentPlank>().Reverse().Select(x => new GovernmentPoliticalValue {
                Value = x, Stance = (int)x % 2 == 0 ? -100 : 100, Importance = (int)x % 2 == 0 ? 1 : 100 }).ToList() };
        Assert.True(GovernmentPoliticalBeliefs.Valid(profile));
        // Warm the same fixed kernel before measuring; keep test assertions and
        // tier-zero/OSR compilation outside the measured caller.
        MeasurePoliticalValidationAllocations(profile);
        MeasurePoliticalValidationAllocations(profile);
        Assert.Equal(0, MeasurePoliticalValidationAllocations(profile));
        var first = profile.Values[0];
        ReignGovernmentPlank original = first.Value;
        foreach (int invalid in new[] { -1, 16, 32, int.MinValue, int.MaxValue, (int)profile.Values[1].Value })
        { first.Value = (ReignGovernmentPlank)invalid; Assert.False(GovernmentPoliticalBeliefs.Valid(profile)); }
        first.Value = original;
        foreach (int invalid in new[] { -101, 101 }) { first.Stance = invalid; Assert.False(GovernmentPoliticalBeliefs.Valid(profile)); }
        first.Stance = 0;
        foreach (int invalid in new[] { 0, 101 }) { first.Importance = invalid; Assert.False(GovernmentPoliticalBeliefs.Valid(profile)); }
        first.Importance = 50;
        profile.Values[0] = null!;
        Assert.False(GovernmentPoliticalBeliefs.Valid(profile));
        profile.Values[0] = first;
        Assert.True(GovernmentPoliticalBeliefs.Valid(profile));
        profile.Revision = 0; Assert.False(GovernmentPoliticalBeliefs.Valid(profile));
        profile.Revision = 1; profile.ModelVersion++; Assert.False(GovernmentPoliticalBeliefs.Valid(profile));
        profile.ModelVersion = GovernmentPoliticalBeliefs.ModelVersion;
        profile.Values.RemoveAt(0); Assert.False(GovernmentPoliticalBeliefs.Valid(profile));
        profile.Values.Insert(0, first); profile.Values.Add(first); Assert.False(GovernmentPoliticalBeliefs.Valid(profile));
        profile.Values.RemoveAt(16);
        profile.HeroId = " "; Assert.False(GovernmentPoliticalBeliefs.Valid(profile));
        profile.HeroId = "hero"; profile.CampaignId = ""; Assert.False(GovernmentPoliticalBeliefs.Valid(profile));
        profile.CampaignId = "campaign"; profile.Changes = null!; Assert.False(GovernmentPoliticalBeliefs.Valid(profile));
        profile.Changes = new(); profile.SeenEvidence = null!; Assert.False(GovernmentPoliticalBeliefs.Valid(profile));
    }

    [Fact]
    public void PlayerFlowFixturesAreDocumentedGuardedProductionPreparationWithoutVotesOrProviders()
    {
        string root = TestOptions.FindWorkspace();
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reign.testing.json")));
        var fixture = catalog.RootElement.GetProperty("structuredActionAgreements").GetProperty("governmentPlayerFlowFixtures");
        Assert.Equal("fixture.government_capital_visit", fixture.GetProperty("capitalActionId").GetString());
        Assert.Equal("fixture.government_action_composer", fixture.GetProperty("composerActionId").GetString());
        Assert.Equal(3, fixture.GetProperty("capitalArguments").GetArrayLength());
        Assert.Equal(2, fixture.GetProperty("composerArguments").GetArrayLength());
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
            "ReignServer/tests/ReignLiveTest/scenarios/government-system-manifest.json")));
        var contract = manifest.RootElement.GetProperty("contracts").GetProperty("governmentPlayerFlowFixtures");
        foreach (string guard in new[] { "exactPausedOwnedCurrent", "retainedRecoveryCheckpoint", "productionChatOnly",
            "actualCapitalOnly", "noProviderOrVote", "conservationRequired", "partialFailureNeverReplayed" })
            Assert.True(contract.GetProperty(guard).GetBoolean());
        Assert.Equal(16, manifest.RootElement.GetProperty("profiles").GetArrayLength());
        string source = File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/Government/Testing/ReignGovernmentPlayerFlowTests.cs"));
        Assert.Contains("GovernmentCapital(kingdom)", source);
        Assert.Contains("AutomationCalibrationFixture", source);
        Assert.Contains("TryOpenForHost(ScreenManager.TopScreen", source);
        Assert.Contains("JToken.DeepEquals(before, after)", source);
        Assert.Contains("return retained;", source);
        Assert.Contains("\"uncertain\"", source);
        Assert.DoesNotContain("OpenForCalibration", source);
        Assert.DoesNotContain("ExecuteSend", source);
        Assert.DoesNotContain("SubmitBallot", source);
        Assert.DoesNotContain("_business.Add", source);
        string host = File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/WorldSimulation/Campaign/ReignLiveInteractionStructuredActionHost.cs"));
        int route = host.IndexOf("else if (c.Value<string>(\"actionId\") == \"fixture.government_capital_visit\"", StringComparison.Ordinal);
        string routed = host[route..host.IndexOf("else throw new InvalidOperationException(\"Choose a registered", route, StringComparison.Ordinal)];
        Assert.Contains("recoveryCheckpointVerified", routed);
        Assert.Contains("PrepareGovernmentPlayerFlowFixture", routed);
    }

    [Fact]
    public void FreeRenewalProtectsTheComingSelectionWithoutExtendingSignedNpcPromises()
    {
        string root = TestOptions.FindWorkspace();
        string campaign = File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/Government/Campaign/ReignGovernmentPetitions.cs"));
        string local = campaign[campaign.IndexOf("string localPrefix =", StringComparison.Ordinal)..
            campaign.IndexOf("if (petition != null && petition.Consents.Any", StringComparison.Ordinal)];
        Assert.Contains("actor == Hero.MainHero && counterpart == actor", local);
        Assert.Contains("receiptId == localPrefix + petitionId", local);
        Assert.Contains("LocalConsentCoversNextSelection(petition, actor.StringId, operation, interval)", local);
        Assert.Contains("GovernmentParticipationRules.HasConsent(petition, actorId, operation, interval + 1)", campaign);
        Assert.Contains("The original agreement receipt is already recorded.", campaign);
        string view = File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/Government/UI/ViewModels/ReignGovernmentParticipationVM.cs"));
        Assert.Contains("_behavior.PetitionConsentValidity(petition, consent)", view);
        Assert.Contains("Record or renew my sponsorship", view);
        string native = File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/Government/Testing/ReignGovernmentInGameTests.cs"));
        Assert.Contains("government_local_renewal_covers_next_selection", native);
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
            "ReignServer/tests/ReignLiveTest/scenarios/government-system-manifest.json")));
        var renewal = manifest.RootElement.GetProperty("contracts").GetProperty("localConsentRenewal");
        Assert.True(renewal.GetProperty("coversNextSelection").GetBoolean());
        Assert.False(renewal.GetProperty("automaticallyRenewsNpcConsent").GetBoolean());
    }

    [Fact]
    public void GuardedSnapshotObservesRealEdictModelsAndUnfiledCrimeCandidatesWithoutAuthority()
    {
        string root = TestOptions.FindWorkspace();
        string native = File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/Government/Testing/ReignGovernmentInGameTests.cs"));
        string model = File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/Government/Campaign/ReignGovernmentEdictsModels.cs"));
        Assert.Contains("CalculateTownIncomeFromTariffs(settlement.OwnerClan, settlement.Town, false)", native);
        Assert.Contains("GetEffectiveConversionSpeedOfProduction(workshop, 1f, true)", native);
        Assert.Contains("settlement.OwnerClan != Hero.MainHero?.Clan", native);
        Assert.Contains("Take(3)", native);
        Assert.Contains("tariffsBefore == settlement.Town.TradeTaxAccumulated", native);
        Assert.Contains("edictsBefore == JsonConvert.SerializeObject(_edicts)", native);
        Assert.Contains("nonzeroTariffExercised", native);
        Assert.Contains("nonzeroWorkshopExercised", native);
        Assert.Contains("finally { _observation = null; _observationThreadId = 0; }", model);
        Assert.Contains("System.Threading.Thread.CurrentThread.ManagedThreadId", model);
        Assert.DoesNotContain("Unpatch", model);
        Assert.DoesNotContain("Suppress", model);
        string crime = native[native.IndexOf("private JObject ObserveTownCrimeTargets", StringComparison.Ordinal)..
            native.IndexOf("private JObject GovernmentAuthorityContracts", StringComparison.Ordinal)];
        Assert.Contains("CreateResolution(kingdom, state, party, ReignGovernmentResolutionCatalog.Find(\"R029\")", crime);
        Assert.DoesNotContain("_resolutions.Add", crime);
        Assert.DoesNotContain("_business.Add", crime);
        Assert.Contains("businessBefore == JsonConvert.SerializeObject(_business)", crime);
        Assert.Contains("kingdoms.Take(32)", crime);
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
            "ReignServer/tests/ReignLiveTest/scenarios/government-system-manifest.json")));
        var observation = manifest.RootElement.GetProperty("contracts").GetProperty("nativeModelObservation");
        Assert.Equal(3, observation.GetProperty("maximumWorkshops").GetInt32());
        Assert.False(observation.GetProperty("applyWithdrawals").GetBoolean());
        Assert.True(observation.GetProperty("playerOwnedCurrentTownOnly").GetBoolean());
    }

    [Fact]
    public void TownCrimeEvidenceAndTargetShareTheSecurityThresholdWithoutCastleFallback()
    {
        string root = TestOptions.FindWorkspace();
        string source = File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/Government/Campaign/ReignGovernmentResolutions.cs"));
        Assert.Contains("SelectTownCrimeTargetId(fiefs.Where(x => x.Settlement.IsTown)", source);
        Assert.Contains("SelectTownCrimeTargetId(owned.Where(x => x.IsTown && x.Town != null)", source);
        Assert.DoesNotContain("Add(tags, \"low_security\", \"criminal_activity\"", source);
        string selector = source[source.IndexOf("if (Same(tag, \"criminal_activity\"))", StringComparison.Ordinal)..
            source.IndexOf("if ((tag ?? string.Empty).IndexOf(\"village\"", StringComparison.Ordinal)];
        Assert.Contains("return owned.FirstOrDefault(x => Same(x.StringId, targetId));", selector);
        Assert.DoesNotContain("??", selector);
        string native = File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/Government/Testing/ReignGovernmentInGameTests.cs"));
        Assert.Contains("government_crime_target_matches_town_security_evidence", native);
        Assert.Contains("SelectTownCrimeTargetId(Enumerable.Reverse(crimeTowns)) == \"a_town\"", native);
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
            "ReignServer/tests/ReignLiveTest/scenarios/government-system-manifest.json")));
        var contract = manifest.RootElement.GetProperty("contracts").GetProperty("townCrimeTarget");
        Assert.Equal(40, contract.GetProperty("securityStrictlyBelow").GetInt32());
        Assert.True(contract.GetProperty("ownedTownsOnly").GetBoolean());
        Assert.True(contract.GetProperty("noEligibleTargetMeansNoEvidence").GetBoolean());
        Assert.True(contract.GetProperty("existingFiledTargetsUnchanged").GetBoolean());
    }

    [Fact]
    public void DecisionArchiveBindsRecordedStateAndCannotShowDeclarationsForAnUnvotedClosure()
    {
        string root = TestOptions.FindWorkspace();
        string source = File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/Government/UI/ViewModels/ReignGovernmentScreenVM.cs"));
        var xml = System.Xml.Linq.XDocument.Load(Path.Combine(root, "ReignBeta/GUI/Prefabs/ReignGovernmentScreen.xml"));
        Assert.Contains("GovernmentBusinessRules.IsClosed(record.Status) && Array(record.VotesJson).Count == 0", source);
        Assert.Contains("if (!HasUnrecordedClosure) Shares.Add", source);
        Assert.Contains("if (!HasUnrecordedClosure && undecided > 0)", source);
        Assert.Contains("if (!HasUnrecordedClosure && abstained > 0)", source);
        Assert.Contains("if (!HasUnrecordedClosure && vacant > 0)", source);
        Assert.Contains(xml.Descendants("TextWidget"), x => (string?)x.Attribute("Text") == "@DecisionResultHeading");
        Assert.Contains(xml.Descendants("TextWidget"), x => (string?)x.Attribute("Text") == "@DecisionVoteCaption");
        Assert.DoesNotContain(xml.Descendants("TextWidget"), x => (string?)x.Attribute("Text") == "Final recorded vote");
        string native = File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/Government/Testing/ReignGovernmentInGameTests.cs"));
        Assert.Contains("government_closed_vote_presentation_is_truthful", native);
        Assert.Contains("finalOption.ShareText == \"30% recorded vote\"", native);
        Assert.Contains("unvotedOption.ShareText == \"No vote recorded\"", native);
    }

    [Fact]
    public void UnchangedPoliticalRefreshCannotReorderProfilesOrRecalculateGovernment()
    {
        string source = File.ReadAllText(Path.Combine(TestOptions.FindWorkspace(),
            "ReignBeta/src/Modules/Government/Campaign/ReignGovernmentPoliticalProfiles.cs"));
        string refresh = source[source.IndexOf("private async Task CompletePoliticalProfileSync", StringComparison.Ordinal)..
            source.IndexOf("private int PersonalBusinessAlignment", StringComparison.Ordinal)];
        Assert.DoesNotContain("Profiles.RemoveAll", refresh);
        Assert.Contains("GovernmentPoliticalBatchPreparation.Prepare(payload, response)", refresh);
        Assert.Contains("else if (item.ChangedFromSent)", refresh);
        Assert.DoesNotContain("JToken.DeepEquals", refresh);
        Assert.DoesNotContain("ToObject<GovernmentPoliticalProfile>", refresh);
        Assert.Contains("Profiles[_participation.Profiles.IndexOf(current)] = profile", refresh);
        Assert.Contains("if (politicalStateChanged)", refresh);
        Assert.Contains("_politicalLastSync[profile.HeroId] = DateTime.UtcNow", refresh);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining
        | System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]
    private static long MeasurePoliticalValidationAllocations(GovernmentPoliticalProfile profile)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100000; i++) GovernmentPoliticalBeliefs.Valid(profile);
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    [Fact]
    public void PendingAndEmptyChambersCannotInventDominantPartiesOrFailOccupiedChamberChecks()
    {
        string root = TestOptions.FindWorkspace();
        string source = File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/Government/Testing/ReignGovernmentInGameTests.cs"));
        string preflight = source[source.IndexOf("private JObject GovernmentPreflight", StringComparison.Ordinal)..
            source.IndexOf("private static JObject GovernmentContracts", StringComparison.Ordinal)];
        Assert.Contains("if (IsCharterPending(kingdom.StringId))", preflight);
        Assert.Contains("parties.Count == 0 && seats.Count == 0", preflight);
        Assert.Contains("parties.All(party => !party.IsDominant && !party.IsGoverningCoalition)", preflight);
        Assert.Contains("coalitionSeats * 2 > seats.Count", preflight);
        string campaign = File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/Government/Campaign/ReignGovernmentCampaignBehavior.cs"));
        Assert.Contains("dominant = parties.Where(x => x.SeatCount > 0)", campaign);
    }

    [Fact]
    public void VacancyPresentationSeparatesUnassignedSharesAndKeepsFinalAbstentionsReachable()
    {
        string root = TestOptions.FindWorkspace();
        string sessions = File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/Government/Campaign/ReignGovernmentSessions.cs"));
        string view = File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/Government/UI/ViewModels/ReignGovernmentScreenVM.cs"));
        Assert.Contains("positions.Where(x => x.Value<bool?>(\"vacant\") == true)", sessions);
        Assert.Contains("[\"undecidedWeight\"] = final ? 0m : uncastWeight - vacantWeight", sessions);
        Assert.Contains("[\"abstainedWeight\"] = final ? uncastWeight - vacantWeight : 0m", sessions);
        Assert.Contains("[\"vacantWeight\"] = vacantWeight", sessions);
        Assert.Contains("Vacant share", view);
        Assert.Contains("This frozen share is vacant and abstains. Its weight is not redistributed.", view);
        Assert.Contains("member.FilterPartyName = party?.Name", view);
        Assert.Contains("x.FilterPartyName == _partyFilter", view);
        Assert.Contains("x.Position == \"Vacant\" || x.Position == \"Abstained\"", view);
        Assert.Contains("new ReignGovernmentShareVM(\"Vacant\", vacant, 2)", view);
        Assert.DoesNotContain("GetRelation", view);
    }

    [Fact]
    public void PrisonerTargetsAreGatedAtNominationFilingDecisionAndLegacyObligationEvaluation()
    {
        string root = TestOptions.FindWorkspace();
        string Read(string file) => File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/Government/Campaign/" + file + ".cs"));
        Assert.Contains("CanPrepareSeasonalPrisonerTarget(kingdom, x)", Read("ReignGovernmentParticipationSessions"));
        Assert.Contains("!CanPrepareSeasonalPrisonerTarget(kingdom, template)", Read("ReignGovernmentPetitions"));
        Assert.Contains("HasRecordedSeasonalPrisonerTarget(resolution, template)", Read("ReignGovernmentBusinessAdapters"));
        string resolutions = Read("ReignGovernmentResolutions");
        Assert.Contains("!HasRecordedSeasonalPrisonerTarget(record, template)", resolutions);
        Assert.Contains("closed without a failure penalty", resolutions);
        string native = File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/Government/Testing/ReignGovernmentInGameTests.cs"));
        Assert.Contains("government_prisoner_remedies_require_recorded_target", native);
    }

    [Fact]
    public void PausedFoundingResolvesTheActualRulerBeforeDraftSelectionAndHonorsFailure()
    {
        string root = TestOptions.FindWorkspace();
        string behavior = File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/Government/Campaign/ReignGovernmentParticipation.cs"));
        string campaign = File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/Government/Campaign/ReignGovernmentCampaignBehavior.cs"));
        string view = File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/Government/UI/ViewModels/ReignGovernmentParticipationVM.cs"));
        Assert.Contains("ReconcileFoundingConstitution(kingdom, EnsureConstitution(state));", campaign);
        Assert.Contains("ReconcileFoundingConstitution(kingdom, constitution);", behavior);
        Assert.Contains("constitution.Source != \"new_kingdom\"", behavior);
        Assert.Contains("kingdom.Leader == Hero.MainHero", behavior);
        Assert.Contains("actor != kingdom?.Leader", behavior);
        Assert.Contains("constitution.Source != \"player_foundation\"", behavior);
        Assert.Matches(@"if \(!_behavior\.SetCharterDraft\([^;]+\)\)\s*\{ StatusText = result; return; \}\s*ShowCharterAuthority\(\)", view);
        Assert.Matches(@"if \(!_behavior\.SetCharterDraft\([^;]+\)\)\s*\{ StatusText = result; return; \}\s*ShowCharterReview\(\)", view);
        string resume = view[view.IndexOf("public void ExecuteResumeFoundingCharter()", StringComparison.Ordinal)..
            view.IndexOf("private void ShowCharterAuthority()", StringComparison.Ordinal)];
        Assert.DoesNotContain("SetCharterDraft", resume);
        Assert.Contains("_kingdom.Leader != Hero.MainHero", resume);
        Assert.Contains("charter.Source != \"player_foundation\"", resume);
        Assert.Contains("CanConfirmCharter(charter.DraftAuthority", resume);
        Assert.Contains("ShowCharterReview();", resume);
        Assert.Contains(" — unavailable", view);
        Assert.Contains("Requires at least one eligible member.", view);
    }

    [Fact]
    public void ReloadEvidenceCoversSavedParticipationWithoutPublishingPrivateProfiles()
    {
        string source = File.ReadAllText(Path.Combine(TestOptions.FindWorkspace(),
            "ReignBeta/src/Modules/Government/Testing/ReignGovernmentInGameTests.cs"));
        string fingerprint = source[source.IndexOf("private string GovernmentFeatureFingerprint()", StringComparison.Ordinal)..
            source.IndexOf("private static JObject GovernmentTestResult", StringComparison.Ordinal)];
        Assert.Contains("[\"participation\"] = JObject.FromObject(_participation)", fingerprint);
        string readback = source[source.IndexOf("private JObject GovernmentSavedConstitutionEvidence()", StringComparison.Ordinal)..
            source.IndexOf("private string GovernmentFeatureFingerprint()", StringComparison.Ordinal)];
        Assert.Contains("Clan.PlayerClan?.Kingdom?.StringId", readback);
        foreach (string field in new[] { "available", "kingdomId", "institutionId", "draftAuthority", "confirmed", "source", "revision" })
            Assert.Contains("[\"" + field + "\"]", readback);
        Assert.DoesNotContain("Profiles", readback);
        Assert.DoesNotContain("JObject.FromObject", readback);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(source,
            "result\\[\"constitution\"\\] = GovernmentSavedConstitutionEvidence\\(\\);").Count);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(source,
            "result\\[\"featureFingerprintSchema\"\\] = \"reign-government-persistence-v2\";").Count);
    }

    [Fact]
    public void ReloadDiagnosticsAreBoundedPrivateObservationsAndNeverReplaceStrictComparison()
    {
        string root = TestOptions.FindWorkspace();
        string Read(string file) => File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/Government", file));
        string source = Read("Testing/ReignGovernmentInGameTests.cs");
        Assert.Contains("expected.Length == 64 && string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase)", source);
        Assert.Contains("government_persistence_diagnostics_private_and_observational", source);
        Assert.Contains("diagnosticOriginal == diagnosticSource.ToString(Formatting.None)", source);
        Assert.Contains("!diagnosticAfter.ToString().Contains(\"private-sentinel\")", source);
        Assert.Contains("value.Properties().Take(16)", source);
        Assert.Contains("savedParticipation.Properties().Take(48)", source);
        Assert.Contains("_governmentLoadFingerprintPhases.Count >= 4", source);
        Assert.DoesNotContain("error.Message", source[source.IndexOf("private void CaptureGovernmentLoadFingerprintPhase", StringComparison.Ordinal)..]);
        Assert.Contains("CaptureGovernmentLoadFingerprintPhase(\"sync_loaded_before_prune\", true)", Read("Campaign/ReignGovernmentCampaignBehavior.cs"));
        Assert.Contains("CaptureGovernmentLoadFingerprintPhase(\"sync_loaded_after_prune\")", Read("Campaign/ReignGovernmentCampaignBehavior.cs"));
        Assert.Contains("CaptureGovernmentLoadFingerprintPhase(\"session_launched\")", Read("Campaign/ReignGovernmentCampaignBehavior.cs"));
        Assert.Contains("CaptureGovernmentLoadFingerprintPhase(\"session_calendar_restored\")", Read("Campaign/ReignGovernmentSessions.cs"));
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reign.testing.json")));
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
            "ReignServer/tests/ReignLiveTest/scenarios/government-system-manifest.json")));
        var expected = manifest.RootElement.GetProperty("contracts").GetProperty("persistenceDiagnostics");
        Assert.Equal("reign-government-persistence-diagnostics-v1", expected.GetProperty("schema").GetString());
        Assert.Equal(4, expected.GetProperty("maximumLoadPhases").GetInt32());
        Assert.Equal(16, expected.GetProperty("maximumRootComponents").GetInt32());
        Assert.Equal(48, expected.GetProperty("maximumParticipationComponents").GetInt32());
        Assert.True(expected.GetProperty("strictV2Unchanged").GetBoolean());
        Assert.True(expected.GetProperty("digestsAndCountsOnly").GetBoolean());
        Assert.Contains("reign-government-persistence-diagnostics-v1", catalog.RootElement.GetProperty("government").GetProperty("persistence").GetString());
    }

    [Fact]
    public void MeetingRetentionRunsAtRecordingAndBeforeNativeSaveReferencesAreCaptured()
    {
        string root = TestOptions.FindWorkspace();
        string campaign = File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/Government/Campaign/ReignGovernmentCampaignBehavior.cs"));
        string sync = campaign[campaign.IndexOf("public override void SyncData", StringComparison.Ordinal)..
            campaign.IndexOf("public ReignGovernmentStateRecord GetGovernment", StringComparison.Ordinal)];
        Assert.True(sync.IndexOf("if (dataStore.IsSaving) PruneHistory();", StringComparison.Ordinal)
            < sync.IndexOf("SyncGovernmentBusiness(dataStore);", StringComparison.Ordinal));
        Assert.Contains("if (dataStore.IsLoading) PruneHistory();", sync);
        Assert.DoesNotContain("dataStore.IsSaving || dataStore.IsLoading", sync);
        Assert.Contains("_meetings = RetainGovernmentMeetingHistory(_meetings);", campaign);
        string sessions = File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/Government/Campaign/ReignGovernmentResolutions.cs"));
        Assert.Contains("RecordGovernmentMeeting(meeting);", sessions);
        Assert.DoesNotContain("_meetings.Add(meeting);", sessions);
        string native = File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/Government/Testing/ReignGovernmentInGameTests.cs"));
        Assert.Contains("government_meeting_history_retention_is_stable_before_save", native);
        Assert.Contains("originalMeetingHistory == JsonConvert.SerializeObject(meetingHistory)", native);
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
            "ReignServer/tests/ReignLiveTest/scenarios/government-system-manifest.json")));
        var contract = manifest.RootElement.GetProperty("contracts").GetProperty("meetingRetention");
        Assert.Equal(64, contract.GetProperty("maximumRecords").GetInt32());
        Assert.True(contract.GetProperty("enforcedWhenRecorded").GetBoolean());
        Assert.True(contract.GetProperty("beforeNativeSaveReferences").GetBoolean());
        Assert.True(contract.GetProperty("strictV2Unchanged").GetBoolean());
    }

    private static GovernmentPartyCaucus NominationCaucus() => new()
    {
        KingdomId = "empire", PartyId = "markets", TargetInterval = 10,
        Roll = new() { "first", "second", "third" }, Nominations = new() { "old", "new" }
    };

    [Fact]
    public void LaterAlternativeCanReplaceAnEarlyPreferenceWithoutAddingVotes()
    {
        var c = NominationCaucus();
        Assert.True(GovernmentParticipationRules.EndorseNomination(c, "first", "old", c.Roll, 1));
        Assert.True(GovernmentParticipationRules.EndorseNomination(c, "first", "new", c.Roll, 2));
        Assert.Single(c.Endorsements);
        Assert.False(GovernmentParticipationRules.EndorseNomination(c, "newcomer", "new", c.Roll.Append("newcomer").ToArray(), 2));
        Assert.True(GovernmentParticipationRules.EndorseNomination(c, "second", "new", c.Roll, 2));
        Assert.Equal("new", GovernmentParticipationRules.SelectNomination(c, c.Roll, c.Nominations,
            new Dictionary<string, long> { ["old"] = 1, ["new"] = 2 }));
        Assert.Equal(3, c.SelectionBallots.Count);
        Assert.Equal(2, c.SelectionBallots.Count(v => v.PetitionId == "new" && v.PetitionRevision == 2));
    }

    [Fact]
    public void StaleRevisionAndLostMembershipCannotCreateASelectionMajority()
    {
        var c = NominationCaucus();
        GovernmentParticipationRules.EndorseNomination(c, "first", "new", c.Roll, 1);
        GovernmentParticipationRules.EndorseNomination(c, "second", "new", c.Roll, 2);
        GovernmentParticipationRules.EndorseNomination(c, "third", "new", c.Roll, 2);
        Assert.Equal("", GovernmentParticipationRules.SelectNomination(c, new[] { "first", "second" }, c.Nominations,
            new Dictionary<string, long> { ["new"] = 2, ["old"] = 1 }));
        Assert.Equal("", c.SelectionBallots.Single(v => v.ActorId == "first").PetitionId);
        Assert.False(c.SelectionBallots.Single(v => v.ActorId == "third").Eligible);
        Assert.Equal("new", c.SelectionBallots.Single(v => v.ActorId == "second").PetitionId);
    }

    [Fact]
    public void LosingNominationVoteFulfillsAfterOfficeLossAndReloadWithoutInventingLegacyVotes()
    {
        var c = NominationCaucus();
        GovernmentParticipationRules.EndorseNomination(c, "first", "new", c.Roll, 2);
        GovernmentParticipationRules.EndorseNomination(c, "second", "old", c.Roll, 1);
        GovernmentParticipationRules.EndorseNomination(c, "third", "old", c.Roll, 1);
        Assert.False(GovernmentParticipationRules.ObserveNominationVote(c, GovernmentParticipationRules.CaucusId(c), "first", "new", 2, true));
        Assert.Equal("old", GovernmentParticipationRules.SelectNomination(c, c.Roll, c.Nominations,
            new Dictionary<string, long> { ["old"] = 1, ["new"] = 2 }));
        var restored = JsonSerializer.Deserialize<GovernmentPartyCaucus>(JsonSerializer.Serialize(c))!;
        Assert.True(GovernmentParticipationRules.ObserveNominationVote(restored, GovernmentParticipationRules.CaucusId(restored), "first", "new", 2, false));
        string before = JsonSerializer.Serialize(restored.SelectionBallots);
        Assert.False(GovernmentParticipationRules.EndorseNomination(restored, "first", "old", restored.Roll, 1));
        GovernmentParticipationRules.SelectNomination(restored, Array.Empty<string>(), Array.Empty<string>(), new Dictionary<string, long>());
        Assert.Equal(before, JsonSerializer.Serialize(restored.SelectionBallots));
        var legacy = JsonSerializer.Deserialize<GovernmentPartyCaucus>("{\"KingdomId\":\"empire\",\"PartyId\":\"markets\",\"TargetInterval\":10,\"Closed\":true,\"AdoptedPetitionId\":\"new\"}")!;
        Assert.Empty(legacy.SelectionBallots);
        Assert.Throws<InvalidOperationException>(() => GovernmentParticipationRules.ObserveNominationVote(legacy, GovernmentParticipationRules.CaucusId(legacy), "first", "new", 2, true));
    }

    [Theory]
    [InlineData("other|markets|10", "first", "new", 2)]
    [InlineData("empire|markets|11", "first", "new", 2)]
    [InlineData("empire|markets|10", "newcomer", "new", 2)]
    [InlineData("empire|markets|10", "first", "old", 2)]
    [InlineData("empire|markets|10", "first", "new", 3)]
    public void SelectionEvidenceRejectsForeignStaleOrDifferentPromises(string scope, string actor, string petition, long revision)
    {
        var c = NominationCaucus();
        GovernmentParticipationRules.EndorseNomination(c, "first", "new", c.Roll, 2);
        GovernmentParticipationRules.SelectNomination(c, c.Roll, c.Nominations, new Dictionary<string, long> { ["new"] = 2 });
        Assert.Throws<InvalidOperationException>(() => GovernmentParticipationRules.ObserveNominationVote(c, scope, actor, petition, revision, true));
    }

    [Fact]
    public void NominationConsentHashesAndConflictChecksBindSessionAndRevision()
    {
        ActionClause Vote(string petition, string revision = "1", string scope = "empire|markets|10") => new()
        {
            ActionId = "government.support_nomination", ActorId = "npc", RecipientId = "player",
            Arguments = new() { ["petitionId"] = petition, ["petitionRevision"] = revision, ["caucusId"] = scope }
        };
        var a = new ActionAgreement { PlayerId = "player", NpcId = "npc", ProposerId = "player", Clauses = new() { Vote("old") } };
        Assert.Empty(AgreementRules.Validate(a, ReignActionCatalog.Find, 0));
        string hash = a.TermsHash();
        a.Clauses[0].Arguments["petitionRevision"] = "2";
        Assert.NotEqual(hash, a.TermsHash());
        hash = a.TermsHash(); a.Clauses[0].Arguments["caucusId"] = "empire|markets|11";
        Assert.NotEqual(hash, a.TermsHash());
        a.Clauses = new() { Vote("old"), Vote("new") };
        Assert.Contains(AgreementRules.Validate(a, ReignActionCatalog.Find, 0), e => e.Contains("opposing nominations"));
        a.Clauses = new() { Vote("old"), Vote("old", "2") };
        Assert.Contains(AgreementRules.Validate(a, ReignActionCatalog.Find, 0), e => e.Contains("opposing nominations"));
        a.Clauses = new() { Vote("old"), Vote("new", scope: "empire|markets|11") };
        Assert.Empty(AgreementRules.Validate(a, ReignActionCatalog.Find, 0));
        var d = ReignActionCatalog.Find("government.support_nomination")!;
        Assert.Equal("governmentNomination", d.Parameters.Single(p => p.Key == "petitionId").Kind);
        Assert.All(d.Parameters.Where(p => p.Key != "petitionId"), p => Assert.True(p.Required && p.Automatic));
        Assert.Equal(4000, GovernmentParticipationBalance.PoliticalServiceReference(0, 0, 0, d.Id));
    }

    [Fact]
    public void SelectedDecisionAndWholeKingdomLedgerHaveSeparateBindings()
    {
        var doc = System.Xml.Linq.XDocument.Load(Path.Combine(TestOptions.FindWorkspace(),
            "ReignBeta/GUI/Prefabs/ReignGovernmentScreen.xml"));
        var selected = doc.Descendants("ListPanel").Single(x => (string?)x.Attribute("Id") == "DecisionObligationList");
        var ledger = doc.Descendants("ListPanel").Single(x => (string?)x.Attribute("Id") == "ObligationLedgerList");
        Assert.Equal("{DecisionObligations}", (string?)selected.Attribute("DataSource"));
        Assert.Equal("{Obligations}", (string?)ledger.Attribute("DataSource"));
        Assert.Equal("@DecisionObligationCount", (string?)selected.Ancestors("ReignGovernmentMemberScrollPanel").Single().Attribute("ItemCount"));
        Assert.Equal("@ObligationCount", (string?)ledger.Ancestors("ReignGovernmentMemberScrollPanel").Single().Attribute("ItemCount"));
        var empty = doc.Descendants("TextWidget").Single(x => (string?)x.Attribute("Text") == "No outstanding deliveries for this record.");
        Assert.Equal("@HasNoDecisionObligations", (string?)empty.Attribute("IsVisible"));
    }

    [Fact]
    public void GovernmentPageDecorationCannotCoverNavigationOrOwnPointerEvents()
    {
        var doc = System.Xml.Linq.XDocument.Load(Path.Combine(TestOptions.FindWorkspace(),
            "ReignBeta/GUI/Prefabs/ReignGovernmentScreen.xml"));
        var canvas = doc.Descendants("Widget").Single(x => (string?)x.Attribute("Id") == "GovernmentCanvas");
        Assert.All(canvas.DescendantsAndSelf("Widget"), x =>
        {
            var owner = x.Parent?.Parent;
            bool isScrollbarHandle = owner?.Name.LocalName == "ScrollbarWidget"
                && (string?)owner.Attribute("Handle") == (string?)x.Attribute("Id");
            // Bannerlord drags the scrollbar only while its Handle.IsPressed.
            // It is an input control, even though its visual is a plain Widget.
            Assert.Equal(isScrollbarHandle ? "false" : "true", (string?)x.Attribute("DoNotAcceptEvents"));
        });
        var children = canvas.Element("Children")!.Elements().ToList();
        int lastPage = children.FindLastIndex(x => x.Name.LocalName == "Widget" && x.Attribute("IsVisible") != null);
        foreach (string id in new[] { "BusinessTab", "HearingTab", "MembersTab", "ArchiveTab", "Close" })
        {
            var button = children.Single(x => (string?)x.Attribute("Id") == "Government" + id);
            Assert.True(children.IndexOf(button) > lastPage, id + " must remain above every page body.");
            Assert.Equal("true", (string?)button.Attribute("DoNotPassEventsToChildren"));
            Assert.Null(button.Attribute("IsEnabled"));
        }
        // The full-window owner still consumes empty-space clicks so they
        // cannot order the party to move on the map beneath the modal.
        var modal = doc.Root!.Element("Window")!.Element("Widget")!;
        Assert.NotEqual("true", (string?)modal.Attribute("DoNotAcceptEvents"));
    }

    [Fact]
    public void GovernmentLongTermsHaveACompleteReadingFieldAndQueuedMattersDoNotClaimPresentation()
    {
        string workspace = TestOptions.FindWorkspace();
        var doc = System.Xml.Linq.XDocument.Load(Path.Combine(workspace, "ReignBeta/GUI/Prefabs/ReignGovernmentScreen.xml"));
        var overview = doc.Descendants().Single(x => (string?)x.Attribute("Id") == "AgendaSummary");
        Assert.Contains(overview.Descendants("TextWidget"), x => (string?)x.Attribute("Text") == "@MatterOverview");
        var record = doc.Descendants().Single(x => (string?)x.Attribute("Id") == "AgendaPublicRecordBody");
        var terms = record.Descendants("TextWidget").Single(x => (string?)x.Attribute("Text") == "@ReadingRecordText");
        Assert.Equal("CoverChildren", (string?)terms.Attribute("HeightSizePolicy"));
        Assert.Null(terms.Attribute("IsVisible"));
        Assert.Contains(terms.Ancestors("ScrollablePanel"), x => (string?)x.Attribute("IsVisible") == "@ShowReadingRecord");
        string readerVm = File.ReadAllText(Path.Combine(workspace,
            "ReignBeta/src/Modules/Government/UI/ViewModels/ReignGovernmentScreenVM.cs"));
        Assert.Contains("ReadingRecordText => ShowOwnPosition ? OwnPositionRecordText : MatterSummary", readerVm);
        Assert.Contains(doc.Descendants("ReignGovernmentMemberScrollPanel"), x =>
            (string?)x.Attribute("Id") == "AgendaDiscussionPanel" && (string?)x.Attribute("IsVisible") == "@ShowPublicDiscussion");
        Assert.Contains(doc.Descendants("ImageWidget"), x => (string?)x.Attribute("Sprite") == "reign_government_v3_timeline"
            && (string?)x.Attribute("IsVisible") == "@HasPresentedMatter");
        string host = File.ReadAllText(Path.Combine(workspace,
            "ReignBeta/src/Modules/WorldSimulation/Campaign/ReignLiveInteractionUiCalibrationHost.cs"));
        int governmentRoute = host.IndexOf("if (target == \"government\")", StringComparison.Ordinal);
        int courtGate = host.IndexOf("if (!court.IsRuleModeActive)", StringComparison.Ordinal);
        Assert.True(governmentRoute >= 0 && governmentRoute < courtGate,
            "Public visitors and members must not pass through the ruler-only court gate.");
        Assert.Contains("Pause and drain the campaign before opening Government.", host);
    }

    [Fact]
    public void GovernmentFiltersAndReadingCardsPreserveReachableContent()
    {
        var doc = System.Xml.Linq.XDocument.Load(Path.Combine(TestOptions.FindWorkspace(),
            "ReignBeta/GUI/Prefabs/ReignGovernmentScreen.xml"));
        Assert.Contains(doc.Descendants("ReignGovernmentMemberScrollPanel"), x =>
            (string?)x.Attribute("ResetRevision") == "@MemberListRevision");
        var picker = doc.Descendants("ReignGovernmentMemberScrollPanel").Single(x => (string?)x.Attribute("Id") == "ProposalPickerPanel");
        Assert.Equal("@PolicyListRevision", (string?)picker.Attribute("ResetRevision"));
        string participationVm = File.ReadAllText(Path.Combine(TestOptions.FindWorkspace(),
            "ReignBeta/src/Modules/Government/UI/ViewModels/ReignGovernmentParticipationVM.cs"));
        Assert.Contains("PolicyChoices.Clear(); _policyListRevision++;", participationVm);
        var choice = doc.Descendants("Widget").Single(x => (string?)x.Attribute("Id") == "ProposalChoiceCard");
        Assert.Null(choice.Attribute("IsEnabled"));
        var description = choice.Descendants("ScrollablePanel").Single();
        Assert.DoesNotContain(description.Ancestors(), x => x.Attribute("IsEnabled") != null);
        var descriptionAction = description.Descendants("ButtonWidget").Single();
        var flow = description.Descendants("ListPanel").Single();
        Assert.Equal("ProposalDetailsBody", (string?)flow.Attribute("Id"));
        Assert.Equal("VerticalTopToBottom", (string?)flow.Attribute("StackLayout.LayoutMethod"));
        Assert.Equal("true", (string?)flow.Attribute("DoNotAcceptEvents"));
        Assert.Null(flow.Attribute("IsEnabled"));
        Assert.Contains(descriptionAction, flow.Descendants("ButtonWidget"));
        Assert.Equal("ExecuteSelect", (string?)descriptionAction.Attribute("Command.Click"));
        Assert.Equal((string?)description.Attribute("SuggestedHeight"), (string?)descriptionAction.Attribute("MinHeight"));
        var scrollbar = choice.Descendants("ScrollbarWidget").Single();
        Assert.DoesNotContain(scrollbar.Ancestors(), x => x.Attribute("IsEnabled") != null);
        foreach (string id in new[] { "ActiveLawsPanel", "GovernmentNewsPanel" })
        {
            var panel = doc.Descendants().Single(x => (string?)x.Attribute("Id") == id);
            var row = panel.Descendants("ItemTemplate").Single().Elements("ButtonWidget").Single();
            Assert.Equal(int.Parse((string)panel.Attribute("SuggestedHeight")!),
                int.Parse((string)row.Attribute("SuggestedHeight")!) * int.Parse((string)panel.Attribute("VisibleRows")!));
            var reader = row.Descendants("ScrollablePanel").Single(x => (string?)x.Attribute("Id") == "RecordDetail");
            Assert.True(int.Parse((string)reader.Attribute("SuggestedHeight")!) >= 100,
                "A law's effects must be readable without a one-line scroll window.");
            if (id == "GovernmentNewsPanel")
                foreach (string field in new[] { "Status", "Detail" })
                {
                    var fieldReader = row.Descendants("ScrollablePanel").Single(x => (string?)x.Attribute("Id") == "Record" + field);
                    var fieldFlow = fieldReader.Descendants("ListPanel").Single();
                    Assert.Equal("true", (string?)fieldFlow.Attribute("DoNotAcceptEvents"));
                    var open = fieldFlow.Descendants("ButtonWidget").Single();
                    Assert.Equal("News" + field + "Action", (string?)open.Attribute("Id"));
                    Assert.Equal("ExecuteOpen", (string?)open.Attribute("Command.Click"));
                    Assert.Equal((string?)fieldReader.Attribute("SuggestedHeight"), (string?)open.Attribute("MinHeight"));
                    Assert.Equal("@" + field, (string?)open.Descendants("TextWidget").Single().Attribute("Text"));
                    Assert.DoesNotContain(open.Ancestors(), x => x.Attribute("IsEnabled") != null);
                }
        }
        foreach (string scope in new[] { "Agenda", "Hearing" })
        {
            var ownPosition = doc.Descendants("ButtonWidget").Single(x => (string?)x.Attribute("Id") == scope + "OwnPositionAction");
            Assert.Equal("ExecuteOwnPosition", (string?)ownPosition.Attribute("Command.Click"));
            Assert.Equal("@OwnPositionStatus", (string?)ownPosition.Descendants("TextWidget").Single().Attribute("Text"));
            Assert.DoesNotContain(doc.Descendants("ScrollablePanel"), x => (string?)x.Attribute("Id") == scope + "OwnPositionPanel");
        }
        var voteReader = doc.Descendants("ScrollablePanel").Single(x => (string?)x.Attribute("Id") == "AgendaTranscriptPanel");
        Assert.Equal("@ShowReadingRecord", (string?)voteReader.Attribute("IsVisible"));
        Assert.True(int.Parse((string)voteReader.Attribute("SuggestedHeight")!) >= 148,
            "Full declaration and ballot names need a genuine reading field, not a clipped status line.");
        Assert.Equal("@ReadingRecordText", (string?)voteReader.Descendants("TextWidget").Single().Attribute("Text"));
        Assert.Equal("CoverChildren", (string?)voteReader.Descendants("TextWidget").Single().Attribute("HeightSizePolicy"));
        var ownRecordTab = doc.Descendants("ButtonWidget").Single(x => (string?)x.Attribute("Id") == "AgendaOwnPositionTab");
        Assert.Equal("ExecuteOwnPosition", (string?)ownRecordTab.Attribute("Command.Click"));
        var readerAction = doc.Descendants("ButtonWidget").Single(x => (string?)x.Attribute("Command.Click") == "ExecuteRecordPosition");
        Assert.Equal("@CanRecordPosition", (string?)readerAction.Attribute("IsEnabled"));
        Assert.All(readerAction.Descendants("TextWidget"), x => Assert.Equal("@RecordPositionText", (string?)x.Attribute("Text")));
        string readerBehavior = File.ReadAllText(Path.Combine(TestOptions.FindWorkspace(),
            "ReignBeta/src/Modules/Government/UI/ViewModels/ReignGovernmentScreenVM.cs"));
        Assert.Contains("if (CanReturnToHearing) { ExecuteHearingTab(); return; }", readerBehavior);
        Assert.Contains("hearing?.BusinessId == _ownPositionHearingId", readerBehavior);
        Assert.Contains("if (!returning) _pendingOptionId = null;", readerBehavior);
        var snapshot = File.ReadAllText(Path.Combine(TestOptions.FindWorkspace(),
            "ReignBeta/src/Modules/UI/UI/Calibration/ReignUiCalibrationService.cs"));
        Assert.Contains("mappedFont =", snapshot);
        Assert.Contains("GetMappedFontForLocalization", snapshot);
        Assert.Contains("fontTextureReady =", snapshot);
        Assert.Contains("textOutlineAmount =", snapshot);
        Assert.Contains("textShadowOffset =", snapshot);
        Assert.DoesNotContain("textWidget?.Brush?", snapshot);
    }

    [Fact]
    public void PoliticalAccessPricesReflectServiceStakesWithoutOverflowOrNegativeDiscounts()
    {
        int Price(int income, int gold, int fiefs, string action) =>
            Reign.Core.Contracts.Government.GovernmentParticipationBalance.PoliticalServiceReference(income, gold, fiefs, "government." + action);
        Assert.Equal(2000, Price(0, 0, 0, "endorse_petition"));
        Assert.Equal(2000, Price(-100, -100, -100, "endorse_petition"));
        Assert.Equal(7500, Price(0, 10000, 20, "endorse_petition"));
        Assert.Equal(30000, Price(0, 10000, 20, "sponsor_petition"));
        Assert.Equal(45000, Price(0, 10000, 20, "seek_petition_adoption"));
        Assert.Equal(15000, Price(0, 10000, 20, "present_petition"));
        Assert.Equal(90000, Price(0, 10000, 20, "transfer_mandate"));
        Assert.Equal(180000, Price(int.MaxValue, int.MaxValue, int.MaxValue, "transfer_mandate"));
        Assert.True(Price(100, 0, 0, "sponsor_petition") > Price(0, 0, 0, "sponsor_petition"));
        Assert.Throws<ArgumentException>(() => Price(0, 0, 0, "automatic_membership"));
        // The reference governs requested exchanges; principled consent has no fee.
        Assert.Equal(90, Reign.Core.Contracts.Government.GovernmentParticipationBalance.Willingness(40, 15, 10, 10, int.MaxValue, 0));
        Assert.Equal(90, Reign.Core.Contracts.Government.GovernmentParticipationBalance.Willingness(40, 15, 10, 10, 15, 0));
    }

    [Fact]
    public void RestoredConstituenciesAreReadyBeforeReleaseWithoutGrantingNewTerritoryMigrationCredit()
    {
        string workspace = TestOptions.FindWorkspace();
        string Read(string name) => File.ReadAllText(Path.Combine(workspace,
            "ReignBeta/src/Modules/Government/Campaign", name));
        string sessions = Read("ReignGovernmentSessions.cs");
        int start = sessions.IndexOf("internal void RestoreSessionCalendar()", StringComparison.Ordinal);
        int end = sessions.IndexOf("private string BusinessIntroductionSlot", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        string restore = sessions[start..end];
        Assert.True(restore.IndexOf("MigrateGovernmentSessions();", StringComparison.Ordinal)
            < restore.IndexOf("RestoreParticipationSeats();", StringComparison.Ordinal));
        Assert.Contains("RestoreParticipationSeats();", restore);

        string participation = Read("ReignGovernmentParticipation.cs");
        Assert.Contains("IsEligibleKingdom(kingdom) && GetSeats(kingdom.StringId).Count > 0", participation);
        Assert.Contains("!_participation.Constituencies.Any(row => row.KingdomId == kingdom.StringId)", participation);
        Assert.Contains("ReconcileMembership(kingdom, state, false);", participation);
        Assert.DoesNotContain("initial || migration", participation);
        Assert.DoesNotContain("all.Count == 0", participation);
        Assert.Contains("Status = initial || migrated != null ? \"active\" : \"pending\"", participation);
        Assert.Contains("x.SeatSourceValue == (int)source", participation);
        Assert.Contains("x.ClanStringId == clan.StringId : x.SettlementStringId == settlement.StringId", participation);
        Assert.Contains("row.Status == \"pending\" && _accreditingSession", participation);
        Assert.Contains("row.EligibleSinceDay + GovernmentParticipationBalance.TerritorialStabilityDays", participation);
        string preflight = File.ReadAllText(Path.Combine(workspace,
            "ReignBeta/src/Modules/Government/Testing/ReignGovernmentInGameTests.cs"));
        Assert.Contains("row.Source == ReignGovernmentSeatSource.LandholdingClanLeader", preflight);
        Assert.Contains("? row.ClanId == seat.ClanStringId : row.SettlementId == seat.SettlementStringId", preflight);
    }

    [Fact]
    public void TypedConversationWaitsForExistingOpeningAndRechecksItsRunViewAndNpc()
    {
        string workspace = TestOptions.FindWorkspace();
        string view = File.ReadAllText(Path.Combine(workspace,
            "ReignBeta/src/Modules/Dialogue/UI/ViewModels/ReignIndividualChatScreenVM.cs"));
        string host = File.ReadAllText(Path.Combine(workspace,
            "ReignBeta/src/Modules/WorldSimulation/Campaign/ReignLiveInteractionTestHost.cs"));
        Assert.Contains("_presentationInitializationTask = InitializePresentationAsync();", view);
        Assert.Contains("await OpenNpcInitiatedConversationAsync()", view);
        Assert.Contains("(_presentationInitializing && !npcOpening)", view);
        Assert.Contains("WaitForIndividualPresentationAsync(_activeRunId, _individualVm, _individualVm.ConversationHero)", host);
        Assert.Contains("Task<bool> opening = viewModel.PresentationInitialization;", host);
        Assert.Contains("Task.Delay(TimeSpan.FromSeconds(300))", host);
        Assert.Contains("_activeRunId == runId && ReferenceEquals(_individualVm, viewModel)", host);
        Assert.Contains("viewModel.ConversationHero == hero", host);
        Assert.Contains("!ReferenceEquals(_individualVm, ReignIndividualChatScreenManager.ActiveViewModel)", host);
    }
}
