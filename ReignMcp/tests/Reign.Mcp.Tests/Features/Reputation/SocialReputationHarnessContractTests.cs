namespace Reign.Mcp.Tests;

public sealed class SocialReputationHarnessContractTests
{
    [Fact]
    public void DedicatedMcpSurfaceKeepsPreparationAndMutationSeparated()
    {
        string root = TestOptions.FindWorkspace();
        string source = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignMcp"), "TestingTools.SocialReputation.cs"));
        string serverBridge = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignServer"), "LiveInteractionTest.cs", "/src/"));
        string clientHost = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignBeta"), "ReignLiveInteractionSocialBalanceHost.cs", "/src/"));
        string clientHarness = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignBeta"), "ReignSocialBalanceHarnessCampaignBehavior.cs", "/src/"));
        string liveHost = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignBeta"), "ReignLiveInteractionTestHost.cs", "/src/"));
        string individualVm = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignBeta"), "ReignIndividualChatScreenVM.cs", "/src/"));
        string serverClient = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignBeta"), "ReignServerClient.cs", "/src/Modules/Platform/"));
        string courtProducer = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignBeta"), "ReignCourtPersonalityReputationCampaignBehavior.cs", "/src/"));
        string socialBalance = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignServer"), "SocialBalanceTest.cs", "/src/"));
        string reputationServer = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignServer"), "SocialReputation.cs", "/src/"));
        string courtReputationServer = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignServer"), "CourtSocialReputation.cs", "/src/"));
        string worldHistoryServer = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignServer"), "WorldHistory.cs", "/src/"));
        string platform = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignServer"), "Program.cs", "/src/Modules/Platform/"));
        Assert.Contains("reign_get_social_reputation_test_manifest", source, StringComparison.Ordinal);
        Assert.Contains("reign_prepare_social_reputation_test", source, StringComparison.Ordinal);
        Assert.Contains("reign_start_social_reputation_test", source, StringComparison.Ordinal);
        Assert.Contains("reign_get_social_reputation_test_status", source, StringComparison.Ordinal);
        Assert.Contains("reign_get_social_reputation_test_report", source, StringComparison.Ordinal);
        Assert.Contains("reign_get_social_reputation_release_readiness", source, StringComparison.Ordinal);
        Assert.Contains("start Reign social reputation test on disposable save", source, StringComparison.Ordinal);
        Assert.Contains("confirmation = \"prepare\"", source, StringComparison.Ordinal);
        Assert.Contains("CampaignTestService campaignTests", source, StringComparison.Ordinal);
        Assert.Contains("RequireArmedOwnedSaveAsync", source, StringComparison.Ordinal);
        Assert.Contains("campaignTestRunId", source, StringComparison.Ordinal);
        Assert.Contains("disposableSaveName = authorization.CurrentSaveName", source, StringComparison.Ordinal);
        Assert.Contains("protectedBaselineSaveName = authorization.BaselineSaveName", source, StringComparison.Ordinal);
        Assert.Contains("social_reputation_profile", source, StringComparison.Ordinal);
        Assert.Contains("activation_source_event_id", reputationServer,
            StringComparison.Ordinal);
        Assert.Contains("activation_source_correlation_id", reputationServer,
            StringComparison.Ordinal);
        Assert.Contains("NobleJudgmentDirectTagIds", courtReputationServer,
            StringComparison.Ordinal);
        Assert.Contains("directReputationProducers", courtReputationServer,
            StringComparison.Ordinal);
        Assert.Contains("directProducerAllowed", courtReputationServer,
            StringComparison.Ordinal);
        Assert.Contains("ProcessDirectDynamicSocialOutcomesBeforeAcknowledgement",
            worldHistoryServer, StringComparison.Ordinal);
        Assert.Contains("directSocialOutcomesAcknowledged", worldHistoryServer,
            StringComparison.Ordinal);
        Assert.Contains("court_direct_judgment_acknowledged_with_world_history_ingest",
            reputationServer, StringComparison.Ordinal);
        Assert.Contains("promptOverrideAssisted", source, StringComparison.Ordinal);
        Assert.Contains("supported only for player_affair", source, StringComparison.Ordinal);
        Assert.Contains("forceRareAffairExposureAfterNaturalMiss", source, StringComparison.Ordinal);
        Assert.Contains("exact validated intimacy signal", source, StringComparison.Ordinal);
        Assert.Contains("\"social_reputation_profile\"", serverBridge, StringComparison.Ordinal);
        Assert.Contains("SocialReputationProfileAsync", clientHost, StringComparison.Ordinal);
        Assert.Contains("configuredDialogueModelUsed", clientHost, StringComparison.Ordinal);
        Assert.Contains("I am flirting with you right now", clientHost, StringComparison.Ordinal);
        Assert.Contains("If someone were attracted to you", clientHost, StringComparison.Ordinal);
        Assert.Contains("profile == \"unchaste\"", clientHost, StringComparison.Ordinal);
        Assert.Contains("UnchasteProfileAsync", clientHost, StringComparison.Ordinal);
        Assert.Contains("RecordUnchasteForSocialBalance", courtProducer, StringComparison.Ordinal);
        Assert.Contains("forceExposure, forcePromotion", courtProducer, StringComparison.Ordinal);
        Assert.Contains("unchaste_forced_public_parentage", clientHost, StringComparison.Ordinal);
        Assert.Contains("FlushAndWaitThroughSequence", clientHost, StringComparison.Ordinal);
        Assert.Contains("PlayerAffairProfileAsync", clientHost, StringComparison.Ordinal);
        Assert.Contains("AffairSendCommand", clientHost, StringComparison.Ordinal);
        Assert.Contains("guardedPromptOverrideDirective", clientHost, StringComparison.Ordinal);
        Assert.Contains("guarded_request_only", clientHost, StringComparison.Ordinal);
        Assert.Contains("guardedPromptOverrideOwnerCommandId", liveHost, StringComparison.Ordinal);
        Assert.Contains("guardedPromptOverrideOwnerCommandId", individualVm, StringComparison.Ordinal);
        Assert.Contains("payload[\"guardedPromptOverride\"] = true", serverClient, StringComparison.Ordinal);
        Assert.Contains("promptOverrideClearedAfterUse", clientHost, StringComparison.Ordinal);
        Assert.Contains("forceRareAffairExposureAfterNaturalMiss", clientHost, StringComparison.Ordinal);
        Assert.Contains("refusing will bring no punishment or loss", clientHost, StringComparison.Ordinal);
        Assert.Contains("BuildAffairNegotiationReply", clientHost, StringComparison.Ordinal);
        Assert.Contains("negotiationTurns", clientHost, StringComparison.Ordinal);
        Assert.Contains("AffairSexualConsentAccepted", clientHost, StringComparison.Ordinal);
        Assert.Contains("intent.Contains(\"sex\")", clientHost, StringComparison.Ordinal);
        Assert.Contains("player-affair-completion", clientHost, StringComparison.Ordinal);
        Assert.Contains("I accept the exact stipend, payment schedule, trial period", clientHost, StringComparison.Ordinal);
        Assert.Contains("after I ask once more and you still consent", clientHost, StringComparison.Ordinal);
        Assert.Contains("completionTurn", clientHost, StringComparison.Ordinal);
        Assert.Contains("The role is concrete", clientHost, StringComparison.Ordinal);
        Assert.Contains("It does not depend on intimacy", clientHost, StringComparison.Ordinal);
        Assert.Contains("If you do not want that, say no and I will stop asking", clientHost, StringComparison.Ordinal);
        Assert.Contains("RankSocialBalanceAffairCandidatesAsync", clientHost, StringComparison.Ordinal);
        Assert.Contains("qualifiesVeryLowHonorAndLoyalty", clientHost, StringComparison.Ordinal);
        Assert.Contains("player_affair_native_relation_restored", clientHost, StringComparison.Ordinal);
        Assert.Contains("player_affair_underlying_affinity_fixture", clientHost, StringComparison.Ordinal);
        Assert.Contains("player_affair_underlying_affinity_restored", clientHost, StringComparison.Ordinal);
        Assert.Contains("ResolveConfirmationAsync(true)", clientHost, StringComparison.Ordinal);
        Assert.Contains("RecordSocialBalanceProfileResultIfApplicable", socialBalance, StringComparison.Ordinal);
        Assert.Contains("operation == \"social_snapshot\"", socialBalance, StringComparison.Ordinal);
        Assert.Contains("SocialBalanceSnapshotApi(new Dictionary<string, object>", socialBalance, StringComparison.Ordinal);
        Assert.Contains("socialBalanceSnapshotStorage", socialBalance, StringComparison.Ordinal);
        Assert.Contains("RecordPlayerFlirtProfileResult", socialBalance, StringComparison.Ordinal);
        Assert.Contains("RecordPlayerAffairProfileResult", socialBalance, StringComparison.Ordinal);
        Assert.Contains("RecordUnchasteProfileResult", socialBalance, StringComparison.Ordinal);
        Assert.Contains("RecordNpcFavoringPresenceProfileResult", socialBalance, StringComparison.Ordinal);
        Assert.Contains("RecordPlayerFavoringDialogueProfileResult", socialBalance, StringComparison.Ordinal);
        Assert.Contains("RecordFavoringProjectionProfileResult", socialBalance, StringComparison.Ordinal);
        Assert.Contains("relocationAttempts", clientHost, StringComparison.Ordinal);
        Assert.Contains("if (colocated) colocatedFavorites.Add(candidate);", clientHost,
            StringComparison.Ordinal);
        Assert.Contains("could not co-locate two eligible same-kingdom favorites", clientHost,
            StringComparison.Ordinal);
        Assert.Contains("RecordFavoringJealousyCharmProfileResult", socialBalance, StringComparison.Ordinal);
        Assert.Contains("RecordPreflightProfileResult", socialBalance, StringComparison.Ordinal);
        Assert.Contains("SocialBalancePreflightSuiteTasks", socialBalance, StringComparison.Ordinal);
        Assert.Contains("suiteTask = Task.Run(() => RunRumorSubsystemSelfTests())", socialBalance, StringComparison.Ordinal);
        Assert.Contains("Every retry joins this same run-keyed task", socialBalance, StringComparison.Ordinal);
        Assert.Contains("twelveRulerThousandNpcProjectionWithinDeadline", socialBalance, StringComparison.Ordinal);
        Assert.Contains("if (recordedCases.Count >= 15) break;", socialBalance, StringComparison.Ordinal);
        Assert.Contains("RecordFavoringRebellionProfileResult", socialBalance, StringComparison.Ordinal);
        Assert.Contains("nativeRulerTransitionVerified", socialBalance, StringComparison.Ordinal);
        Assert.Contains("RecordLongitudinalProfileResult", socialBalance, StringComparison.Ordinal);
        Assert.Contains("social_balance_longitudinal_checkpoints", socialBalance, StringComparison.Ordinal);
        Assert.Contains("dayZeroThirtySixtyNinetyCheckpoints", socialBalance, StringComparison.Ordinal);
        Assert.Contains("[\"acceptance_harness\"]", socialBalance, StringComparison.Ordinal);
        Assert.Contains("releaseVerdict", socialBalance, StringComparison.Ordinal);
        Assert.Contains("blockerCount", socialBalance, StringComparison.Ordinal);
        Assert.Contains("FavoringRebellionProfileAsync", clientHost, StringComparison.Ordinal);
        Assert.Contains("RunPreparationHarnessProfile", clientHost, StringComparison.Ordinal);
        Assert.Contains("PlayerParityProfileAsync", clientHost, StringComparison.Ordinal);
        Assert.Contains("player_unchaste_parity_forced_public_parentage", clientHost, StringComparison.Ordinal);
        Assert.Contains("RecordPlayerParityProfileResult", socialBalance, StringComparison.Ordinal);
        Assert.Contains("court_unchaste_player_parity", socialBalance, StringComparison.Ordinal);
        Assert.Contains("SameImmutableBoundary", clientHarness, StringComparison.Ordinal);
        Assert.Contains("run_reenrollment", clientHarness, StringComparison.Ordinal);
        Assert.Contains("same_immutable_campaign_boundary", clientHarness, StringComparison.Ordinal);
        Assert.Contains("staleRunReceiptsCleared", clientHarness, StringComparison.Ordinal);
        Assert.Contains("staleRunOverridesCleared", clientHarness, StringComparison.Ordinal);
        Assert.Contains("different social-balance campaign boundary", clientHarness, StringComparison.Ordinal);
        Assert.Contains("guarded_campaign_test_reenrollment", clientHarness, StringComparison.Ordinal);
        Assert.Contains("ReignServerClient.ActiveNativeSaveName()", clientHarness, StringComparison.Ordinal);
        Assert.Contains("guardedCampaignTestExactSaveIdentityRequired", socialBalance, StringComparison.Ordinal);
        Assert.Contains("StagePersistenceMarker", clientHarness, StringComparison.Ordinal);
        Assert.Contains("VerifyPersistenceMarker", clientHarness, StringComparison.Ordinal);
        Assert.Contains("differentGameInstanceAfterReload", clientHarness, StringComparison.Ordinal);
        Assert.Contains("SocialBalancePersistenceSnapshot", courtProducer, StringComparison.Ordinal);
        Assert.Contains("SavePrepareProfileAsync", clientHost, StringComparison.Ordinal);
        Assert.Contains("SaveVerifyProfileAsync", clientHost, StringComparison.Ordinal);
        Assert.Contains("RecordSavePrepareProfileResult", socialBalance, StringComparison.Ordinal);
        Assert.Contains("RecordSaveVerifyProfileResult", socialBalance, StringComparison.Ordinal);
        Assert.Contains("core_save_reload_idempotency", socialBalance, StringComparison.Ordinal);
        Assert.Contains("signalIntimacyFavoringDatabaseUnchanged", socialBalance, StringComparison.Ordinal);
        Assert.Contains("legacyFavoredByRemainsDisabled", socialBalance, StringComparison.Ordinal);
        Assert.Contains("cleanup_marker", socialBalance, StringComparison.Ordinal);
        Assert.Contains("compatiblePlayerFlirtAffairAndNpcUnchasteEvidence", socialBalance, StringComparison.Ordinal);
        Assert.Contains("playerZeroRumorAndGenderSpecificEstablishedValue", socialBalance, StringComparison.Ordinal);
        Assert.Contains("npc_ruler_favoring_presence", socialBalance, StringComparison.Ordinal);
        Assert.Contains("player_ruler_favoring_dialogue", socialBalance, StringComparison.Ordinal);
        Assert.Contains("separationResetAndDayElevenThreshold", socialBalance, StringComparison.Ordinal);
        Assert.Contains("fivePercentBaseClanTierScaling", socialBalance, StringComparison.Ordinal);
        Assert.Contains("NpcFavoringPresenceProfileAsync", clientHost, StringComparison.Ordinal);
        Assert.Contains("PlayerFavoringDialogueProfileAsync", clientHost, StringComparison.Ordinal);
        Assert.Contains("GetDialogueHistoryAsync(candidate, 1)", clientHost, StringComparison.Ordinal);
        Assert.Contains("cleanConversationalSlateVerified", clientHost, StringComparison.Ordinal);
        Assert.Contains("primaryExchangeCount", clientHost, StringComparison.Ordinal);
        Assert.Contains("What final counsel would you give your ruler?", clientHost, StringComparison.Ordinal);
        Assert.Contains("PlayerFavoringTargetsHadCleanHistory", socialBalance, StringComparison.Ordinal);
        Assert.Contains("cleanTargetIsolation", socialBalance, StringComparison.Ordinal);
        Assert.DoesNotContain("EvaluatePlayerFavoringTranscriptQuality", socialBalance,
            StringComparison.Ordinal);
        Assert.Contains("[\"caseResults\"]", socialBalance, StringComparison.Ordinal);
        Assert.Contains("TryParseJsonObject(ReadString(row, \"evidence_json\", \"{}\"))", socialBalance, StringComparison.Ordinal);
        Assert.Contains("[\"player_favoring_dialogue\"]", socialBalance, StringComparison.Ordinal);
        Assert.Contains("ReignServer/src/Modules/Reputation/SocialBalanceTest.cs", socialBalance, StringComparison.Ordinal);
        Assert.Contains("ReignBeta/src/Modules/Reputation/Campaign/ReignLiveInteractionSocialBalanceHost.cs", socialBalance, StringComparison.Ordinal);
        Assert.Contains("[\"harness\"] = \"80401068d76f4b87f8e8884e\"", socialBalance, StringComparison.Ordinal);
        Assert.Contains("[\"player_favoring_dialogue\"] = \"d887a6fa777b5bba3ec61d4a\"", socialBalance, StringComparison.Ordinal);
        Assert.Contains("FavoringProjectionProfileAsync", clientHost, StringComparison.Ordinal);
        Assert.Contains("twoIndependentParameterizedFavorites", socialBalance, StringComparison.Ordinal);
        Assert.Contains("globalFavoringCostCollapsed", socialBalance, StringComparison.Ordinal);
        Assert.Contains("eachNamedFavoriteReceivesEstablishedBonus", socialBalance, StringComparison.Ordinal);
        Assert.Contains("sameKingdomJealousyAndForeignSuppression", socialBalance, StringComparison.Ordinal);
        Assert.Contains("observerResolutionCreatesNoRelationshipPair", socialBalance, StringComparison.Ordinal);
        Assert.Contains("nativeLowHighJealousyMathExact", socialBalance, StringComparison.Ordinal);
        Assert.Contains("jealousyFactorBoundariesHalfToOneAndHalf", socialBalance, StringComparison.Ordinal);
        Assert.Contains("subjectCharmMitigationExact", socialBalance, StringComparison.Ordinal);
        Assert.Contains("playerNeutralFavoriteBonusForeignSuppression", socialBalance, StringComparison.Ordinal);
        Assert.Contains("primaryExchangeCount", clientHost, StringComparison.Ordinal);
        Assert.Contains("perTargetIndependentCounters", socialBalance, StringComparison.Ordinal);
        Assert.Contains("tenPercentBaseClanTierScaling", socialBalance, StringComparison.Ordinal);
        Assert.Contains("profile is \"longitudinal_90_day\" or \"player_favoring_dialogue\" ? 3600 : 900", source, StringComparison.Ordinal);
        Assert.Contains("operation.Equals(\"social_reputation_profile\"", serverBridge, StringComparison.Ordinal);
        Assert.Contains("? 3600", serverBridge, StringComparison.Ordinal);
        Assert.Contains("social_profile_timeout_scope", serverBridge, StringComparison.Ordinal);
        Assert.Contains("InteractiveClient", serverClient, StringComparison.Ordinal);
        Assert.Contains("IsProviderBackedInteractiveRoute", serverClient, StringComparison.Ordinal);
        Assert.Contains("InteractiveProviderTimeoutSeconds = 900", serverClient, StringComparison.Ordinal);
        Assert.Contains("[\"interactiveProviderTimeoutSeconds\"]", serverClient, StringComparison.Ordinal);
        Assert.Contains("RunNpcRulerFavoringPresenceForSocialBalance", courtProducer, StringComparison.Ordinal);
        Assert.Contains("ProcessNpcRulerFavoringPair", courtProducer, StringComparison.Ordinal);
        Assert.Contains("if (record == null)", courtProducer, StringComparison.Ordinal);
        Assert.Contains("pairProcessorRevision", courtProducer, StringComparison.Ordinal);
        Assert.Contains("pairProcessorCallCount", socialBalance, StringComparison.Ordinal);
        Assert.Contains("dayTenProducedNoEvent", courtProducer, StringComparison.Ordinal);
        Assert.Contains("npc_ruler_favoring_presence_forced", courtProducer, StringComparison.Ordinal);
        Assert.Contains("court_unchaste_publicity_and_gender", socialBalance, StringComparison.Ordinal);
        Assert.Contains("femaleMinus50MaleMinus5Established", socialBalance, StringComparison.Ordinal);
        Assert.Contains("FROM world_history_events WHERE campaign_id=$campaign AND timeline_id=$timeline", socialBalance, StringComparison.Ordinal);
        Assert.Contains("correlation_id IN ($female,$male)", socialBalance, StringComparison.Ordinal);
        Assert.Contains("FROM social_outcome_receipts", socialBalance, StringComparison.Ordinal);
        Assert.Contains("receiptDeadline", socialBalance, StringComparison.Ordinal);
        Assert.Contains("[\"sourceReceipts\"] = sourceReceipts", socialBalance, StringComparison.Ordinal);
        Assert.Contains("sourceEventIds.All(eventId => occurrences.Any", socialBalance, StringComparison.Ordinal);
        Assert.Contains("[\"evidence\"] = evidence", socialBalance, StringComparison.Ordinal);
        Assert.Contains("court_intimacy_exposure_receipts", socialBalance, StringComparison.Ordinal);
        Assert.Contains("activeAffairOccurrencePersisted", socialBalance, StringComparison.Ordinal);
        Assert.Contains("priorUnderlyingAffinity", socialBalance, StringComparison.Ordinal);
        Assert.Contains("mutualAffinityFixtureRestored", socialBalance, StringComparison.Ordinal);
        Assert.Contains("SocialBalanceRankAffairCandidatesApi", socialBalance, StringComparison.Ordinal);
        Assert.Contains("veryLowHonorAndLoyaltyTarget", socialBalance, StringComparison.Ordinal);
        Assert.Contains("promptOverrideScopedAndCleared", socialBalance, StringComparison.Ordinal);
        Assert.Contains("promptOverrideReceipts", socialBalance, StringComparison.Ordinal);
        Assert.Contains("TryAuthorizeGuardedPromptOverride", platform, StringComparison.Ordinal);
        Assert.Contains("authorized_guarded_affair_capability_turn", platform, StringComparison.Ordinal);
        Assert.Contains("active_disposable_save_mismatch", platform, StringComparison.Ordinal);
        Assert.Contains("rareExposureOverrideConsumed", socialBalance, StringComparison.Ordinal);
        Assert.Contains("exact_enrolled_run_case_signal", socialBalance, StringComparison.Ordinal);
        Assert.Contains("productionChanceUnchanged", socialBalance, StringComparison.Ordinal);
        Assert.Contains("new Dictionary<string, object>(rareExposureOverride)", socialBalance, StringComparison.Ordinal);
        Assert.Contains("receiptResultJson", socialBalance, StringComparison.Ordinal);
        Assert.Contains("veryLowThreshold", socialBalance, StringComparison.Ordinal);
        Assert.Contains("/tests/social-balance/affair/candidates", platform, StringComparison.Ordinal);
        Assert.Contains("court_conversation_signal_receipts", socialBalance, StringComparison.Ordinal);
        Assert.Contains("court_flirt_targets", socialBalance, StringComparison.Ordinal);
        Assert.Contains("clanTierScalingCorrect", socialBalance, StringComparison.Ordinal);
        Assert.Contains("court_social_signal_evidence", socialBalance, StringComparison.Ordinal);
        string categorizedMemory = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignServer"), "CategorizedMemory.cs", "/src/Modules/Dialogue/"));
        Assert.Contains("compact[\"socialSignals\"] = socialSignals", categorizedMemory, StringComparison.Ordinal);
        Assert.Contains("speakerClanTier", categorizedMemory, StringComparison.Ordinal);
        string courtSocial = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignServer"), "CourtSocialReputation.cs", "/src/Modules/Reputation/"));
        Assert.Contains("opportunitySnapshot", courtSocial, StringComparison.Ordinal);
        Assert.Contains("[\"speakerClanTier\"] = speakerClanTier", courtSocial, StringComparison.Ordinal);
        Assert.Contains("rejectedSocialSignals", platform, StringComparison.Ordinal);
        int responseStart = platform.IndexOf("Dictionary<string, object> response = new Dictionary<string, object>", StringComparison.Ordinal);
        int responseEnd = platform.IndexOf("LogOperational(\"dialogue.respond\"", responseStart, StringComparison.Ordinal);
        Assert.True(responseStart >= 0 && responseEnd > responseStart);
        string responseEnvelope = platform.Substring(responseStart, responseEnd - responseStart);
        Assert.Contains("[\"socialSignals\"] = ReadDictionaryList(socialSignalValidation, \"signals\")", responseEnvelope, StringComparison.Ordinal);
        Assert.Contains("[\"rejectedSocialSignals\"] = ReadDictionaryList(socialSignalValidation, \"rejected\")", responseEnvelope, StringComparison.Ordinal);
        Assert.Contains("Always include socialSignals", platform, StringComparison.Ordinal);
        Assert.Contains("first-person present-tense statement", platform, StringComparison.Ordinal);
        Assert.DoesNotContain("passed = true", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProductionSourcesExposeSignalsFavoringAndObserverSpecificStanding()
    {
        string root = TestOptions.FindWorkspace();
        string court = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignServer"), "CourtSocialReputation.cs", "/src/"));
        string standing = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignServer"), "PublicStanding.cs", "/src/"));
        string relationships = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignServer"), "WorldRelationshipModel.cs", "/src/"));
        Assert.Contains("sexual_intimacy_completed", court, StringComparison.Ordinal);
        Assert.Contains("court_social_signal_evidence", court, StringComparison.Ordinal);
        Assert.Contains("ruler_favoring_dialogue", court, StringComparison.Ordinal);
        Assert.Contains("CollapseRulerFavoringPublicStanding", standing, StringComparison.Ordinal);
        Assert.Contains("ResolveObserverPublicStanding", relationships, StringComparison.Ordinal);
        Assert.Contains("jealousyFactor", relationships, StringComparison.Ordinal);
    }

    [Fact]
    public void PassOnceManifestCoversPlayerRegularLordRulerAndRebellionProfiles()
    {
        string root = TestOptions.FindWorkspace();
        string manifest = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignServer"), "SocialBalanceTest.cs", "/src/"));
        foreach (string required in new[]
        {
            "social_signal_contract", "player_affair_intimacy_discovery",
            "npc_ruler_favoring_presence", "player_ruler_favoring_dialogue",
            "favoring_collapsed_projection", "favoring_jealousy_charm_matrix",
            "court_unchaste_player_parity", "favoring_rebellion_successor",
            "longitudinal_90_day_social_rebellion_balance"
        }) Assert.Contains(required, manifest, StringComparison.Ordinal);
        Assert.Contains("Pass once per campaign/timeline", manifest, StringComparison.Ordinal);
        Assert.Contains("regular_lord", manifest, StringComparison.Ordinal);
        Assert.Contains("player", manifest, StringComparison.Ordinal);
        Assert.Contains("ruler", manifest, StringComparison.Ordinal);
    }

    [Fact]
    public void FocusedReportsAndMixedStandingRegressionRemainDiscoverable()
    {
        string root = TestOptions.FindWorkspace();
        string tools = File.ReadAllText(Path.Combine(root,
            "ReignMcp/src/Reign.Mcp.Server/Modules/Reputation/TestingTools.SocialReputation.cs"));
        string server = File.ReadAllText(Path.Combine(root,
            "ReignServer/src/Modules/Reputation/SocialBalanceTest.cs"));
        string selfTests = File.ReadAllText(Path.Combine(root,
            "ReignServer/src/Modules/Reputation/SocialReputation.cs"));
        Assert.Contains("bool includeSnapshots = true", tools, StringComparison.Ordinal);
        Assert.Contains("InputGuard.OptionalIdentifier(caseId, nameof(caseId))", tools, StringComparison.Ordinal);
        Assert.Contains("AND ($case='' OR case_id=$case)", server, StringComparison.Ordinal);
        Assert.Contains("if (!includeSnapshots)", server, StringComparison.Ordinal);
        Assert.Contains("SocialBalanceFavoringCostCollapsed(standing, rulerCharm)", server, StringComparison.Ordinal);
        foreach (string id in new[] { "favor_projection_accepts_unrelated_standing",
            "favor_projection_rejects_double_charge", "favor_projection_rejects_incorrect_total",
            "favor_projection_rejects_premature_rounding" })
            Assert.Contains(id, selfTests, StringComparison.Ordinal);
        using var catalog = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reign.testing.json")));
        Assert.Contains("includeSnapshots=false", catalog.RootElement.GetProperty("socialReputation")
            .GetProperty("focusedReportContract").GetString(), StringComparison.Ordinal);
    }
}
