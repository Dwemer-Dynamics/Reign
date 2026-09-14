using Reign.Core.Contracts.Court;

namespace Reign.Mcp.Tests.Features.Court;

public sealed class RulerDocketRulesTests
{
    [Fact]
    public void LaunchHarnessExposesExplicitGuardrailScheduleMigrationAndSoakPhases()
    {
        string root = TestOptions.FindWorkspace();
        string fixture = File.ReadAllText(Path.Combine(root, "ReignBeta", "src", "Modules",
            "Court", "Court", "ReignRulerDocketLaunchTests.cs"));
        string dispatcher = File.ReadAllText(Path.Combine(root, "ReignBeta", "src", "Modules",
            "Court", "Court", "ReignRulerDocketInGameTests.cs"));
        string nobleFixture = File.ReadAllText(Path.Combine(root, "ReignBeta", "src", "Modules",
            "Court", "Court", "ReignNobleDocketInGameTests.cs"));
        string manager = File.ReadAllText(Path.Combine(root, "ReignBeta", "src", "Modules",
            "Court", "UI", "ReignCourtPetitionScreenManager.cs"));
        string nobleVm = File.ReadAllText(Path.Combine(root, "ReignBeta", "src", "Modules",
            "Court", "UI", "ViewModels", "ReignCourtNobleMatterScreenVM.cs"));
        string liveHost = File.ReadAllText(Path.Combine(root, "ReignBeta", "src", "Modules",
            "Court", "Campaign", "ReignLiveInteractionRulerDocketHost.cs"));
        string nobleBehavior = File.ReadAllText(Path.Combine(root, "ReignBeta", "src", "Modules",
            "Court", "Court", "ReignNobleDocketCampaignBehavior.cs"));
        string worldHistoryClient = File.ReadAllText(Path.Combine(root, "ReignBeta", "src", "Modules",
            "WorldSimulation", "Integration", "ReignWorldHistoryClient.cs"));
        string worldHistoryBehavior = File.ReadAllText(Path.Combine(root, "ReignBeta", "src", "Modules",
            "WorldSimulation", "Campaign", "ReignWorldHistoryCampaignBehavior.cs"));
        string serverClient = File.ReadAllText(Path.Combine(root, "ReignBeta", "src", "Modules",
            "Platform", "Integration", "ReignServerClient.cs"));
        string reputationServer = File.ReadAllText(Path.Combine(root, "ReignBetaServer", "src",
            "Modules", "Reputation", "SocialReputation.cs"));
        string courtReputationServer = File.ReadAllText(Path.Combine(root, "ReignBetaServer", "src",
            "Modules", "Reputation", "CourtSocialReputation.cs"));
        string mcp = File.ReadAllText(TestSourceLocator.Unique(Path.Combine(root, "ReignMcp"),
            "TestingTools.Court.cs"));
        int rulerDocketStart = mcp.IndexOf("StartRulerDocketTest(", StringComparison.Ordinal);
        int rulerDocketEnd = mcp.IndexOf("reign_get_royal_council_test_manifest",
            rulerDocketStart, StringComparison.Ordinal);
        Assert.True(rulerDocketStart >= 0 && rulerDocketEnd > rulerDocketStart);
        string rulerDocketMcp = mcp.Substring(rulerDocketStart,
            rulerDocketEnd - rulerDocketStart);

        foreach (string phase in new[] { "guardrail_insufficient", "invalidate_identity",
                     "technical_return", "chancellor_schedule_prepare", "chancellor_schedule_verify",
                     "chancellor_eligibility_dismissal", "legacy_migration", "natural_soak_prepare",
                     "natural_soak_verify" })
            Assert.Contains(phase, dispatcher + fixture, StringComparison.Ordinal);
        Assert.Contains("technical-return", manager, StringComparison.Ordinal);
        Assert.Contains("petition_insufficient", rulerDocketMcp, StringComparison.Ordinal);
        Assert.Contains("petition_invalid_identity", rulerDocketMcp, StringComparison.Ordinal);
        Assert.Contains("naturalSoakDays", rulerDocketMcp, StringComparison.Ordinal);
        Assert.Contains("[Range(7, 180)]", rulerDocketMcp, StringComparison.Ordinal);
        Assert.Contains("noble_template_case", rulerDocketMcp, StringComparison.Ordinal);
        Assert.Contains("noble_execution", rulerDocketMcp, StringComparison.Ordinal);
        Assert.Contains("noble_save_verify", rulerDocketMcp, StringComparison.Ordinal);
        foreach (string phase in new[] { "noble_preflight", "noble_prepare", "noble_open",
                     "noble_snapshot", "noble_decide", "noble_observe", "noble_reverse",
                     "noble_execute", "noble_stay_prepare", "noble_stay_verify",
                     "noble_investigation_verify", "noble_save_prepare", "noble_save_verify",
                     "royal_proclamation" })
            Assert.Contains(phase, dispatcher + nobleFixture, StringComparison.Ordinal);
        Assert.Contains("TryBuildNobleMatter(template", nobleFixture, StringComparison.Ordinal);
        Assert.Contains("ChangeRelationAction.ApplyRelationChangeBetweenHeroes", nobleFixture,
            StringComparison.Ordinal);
        Assert.Contains("FindAnyHero(matter.VictimHeroId)?.IsAlive == false", nobleFixture,
            StringComparison.Ordinal);
        Assert.Contains("naturalConversationPersisted", nobleFixture, StringComparison.Ordinal);
        Assert.Contains("evidenceGroundedByVisibleDialogue", nobleFixture, StringComparison.Ordinal);
        Assert.DoesNotContain("evidence.EvidenceId = matter.MatterId", nobleFixture,
            StringComparison.Ordinal);
        Assert.Contains("matter.Participants.Count >= template.MinimumPrincipals", nobleFixture,
            StringComparison.Ordinal);
        Assert.Contains("nativeFamilyState", nobleFixture, StringComparison.Ordinal);
        Assert.Contains("reciprocalSpouseState", nobleFixture, StringComparison.Ordinal);
        Assert.Contains("reciprocalSpouseStateCleared", nobleFixture, StringComparison.Ordinal);
        Assert.Contains("deniedDivorcePreservationRequired", nobleFixture,
            StringComparison.Ordinal);
        Assert.Contains("reciprocalSpouseStatePreserved", nobleFixture,
            StringComparison.Ordinal);
        Assert.Contains("matter.Ruling == ReignNobleRuling.SideB && matter.EffectsCommitted",
            nobleFixture, StringComparison.Ordinal);
        Assert.Contains("expectedReputationEffects", nobleFixture, StringComparison.Ordinal);
        Assert.Contains("sourceCorrelationId", nobleFixture, StringComparison.Ordinal);
        Assert.Contains("ExpectedNobleRelationshipEffects", nobleFixture, StringComparison.Ordinal);
        Assert.Contains("clanSpilloverEffectCount", nobleFixture, StringComparison.Ordinal);
        Assert.Contains("allEffectsDelivered", nobleFixture, StringComparison.Ordinal);
        Assert.Contains("matter.Ruling == ReignNobleRuling.DeferToChancellor", nobleFixture,
            StringComparison.Ordinal);
        Assert.Contains("AddExpectedRelationship(expected, principal.HeroId, -2);", nobleFixture,
            StringComparison.Ordinal);
        Assert.Contains("TryProcessPendingDocketRelationshipAdjustments();", nobleBehavior,
            StringComparison.Ordinal);
        Assert.Contains("VerifyNobleReputationEffectsAsync", liveHost, StringComparison.Ordinal);
        Assert.Contains("VerifyNobleDirectionalRelationshipEffectsAsync", liveHost,
            StringComparison.Ordinal);
        Assert.Contains("directionalRelationshipState", liveHost, StringComparison.Ordinal);
        Assert.Contains("durable delivery receipts", liveHost, StringComparison.Ordinal);
        Assert.Contains("ReignWorldHistoryTransport.FlushAndWait", liveHost,
            StringComparison.Ordinal);
        Assert.Contains("TimelineReady", liveHost, StringComparison.Ordinal);
        Assert.Contains("timelineReadyBeforeFlush", liveHost, StringComparison.Ordinal);
        Assert.Contains("reputationDeadline", liveHost, StringComparison.Ordinal);
        Assert.Contains("observationAttempts", liveHost, StringComparison.Ordinal);
        Assert.Contains("instead of racing it with a single read", liveHost,
            StringComparison.Ordinal);
        Assert.Contains("nativeReputationState", liveHost, StringComparison.Ordinal);
        Assert.Contains("GetSocialCharacterStatusAsync", worldHistoryClient,
            StringComparison.Ordinal);
        Assert.Contains("activation_source_correlation_id", reputationServer,
            StringComparison.Ordinal);
        Assert.Contains("directReputationProducer", nobleBehavior, StringComparison.Ordinal);
        Assert.Contains("GetCampaignBehavior<ReignWorldHistoryCampaignBehavior>", nobleBehavior,
            StringComparison.Ordinal);
        Assert.Contains("retainUntilTimelineReady: true", worldHistoryBehavior,
            StringComparison.Ordinal);
        Assert.Contains("NobleJudgmentDirectTagIds", courtReputationServer,
            StringComparison.Ordinal);
        Assert.Contains("directReputationProducers", courtReputationServer,
            StringComparison.Ordinal);
        Assert.Contains("court_noble_judgment_direct_reputation_tags", reputationServer,
            StringComparison.Ordinal);
        Assert.Contains("A denied divorce leaves the marriage intact", nobleBehavior,
            StringComparison.Ordinal);
        Assert.Contains("Dictionary<string, object> Phase", rulerDocketMcp, StringComparison.Ordinal);
        Assert.Contains("new List<Dictionary<string, object>>()", rulerDocketMcp, StringComparison.Ordinal);
        Assert.DoesNotContain("var steps = new List<object>();", rulerDocketMcp, StringComparison.Ordinal);
        int openCourt = rulerDocketMcp.IndexOf("steps.Add(OpenCourt());", StringComparison.Ordinal);
        int profileSwitch = rulerDocketMcp.IndexOf("switch (profile)", StringComparison.Ordinal);
        Assert.True(openCourt >= 0 && profileSwitch > openCourt,
            "Court-dependent ruler-docket profiles must open the production Court before their preflight or fixture phases.");
        Assert.Contains("[\"operation\"] = \"ui_open\"", rulerDocketMcp, StringComparison.Ordinal);
        Assert.Contains("[\"targetSearch\"] = \"court\"", rulerDocketMcp, StringComparison.Ordinal);
        Assert.Contains("\"noble_preflight\" or \"noble_template_case\"", rulerDocketMcp,
            StringComparison.Ordinal);
        Assert.Contains("AutomationExpectedReplyCount", manager, StringComparison.Ordinal);
        Assert.Contains("_nobleVm?.AutomationTranscript", manager, StringComparison.Ordinal);
        Assert.Contains("?? _vm?.AutomationTranscript", manager, StringComparison.Ordinal);
        Assert.True(manager.IndexOf("_nobleVm?.AutomationTranscript", StringComparison.Ordinal)
            < manager.IndexOf("?? _vm?.AutomationTranscript", StringComparison.Ordinal),
            "An active noble audience must own the automation transcript before any stale petition ViewModel.");
        Assert.Contains("if (_nobleVm != null) return _nobleVm.TryAutomationAction(action, value, out error);",
            manager, StringComparison.Ordinal);
        Assert.Contains("AutomationNaturalConversationComplete", manager, StringComparison.Ordinal);
        Assert.Contains("_conversationSendRevision", nobleVm, StringComparison.Ordinal);
        Assert.Contains("_completedConversationRevision", nobleVm, StringComparison.Ordinal);
        Assert.Contains("_conversationCompletedReplyCount++", nobleVm, StringComparison.Ordinal);
        Assert.Contains("_conversationCompletedReplyCount >= _conversationExpectedReplyCount", nobleVm,
            StringComparison.Ordinal);
        Assert.Contains("AutomationNaturalConversationComplete", liveHost, StringComparison.Ordinal);
        Assert.Contains("completionReceiptObserved", liveHost, StringComparison.Ordinal);
        Assert.Contains("exactRulerLinePersisted", liveHost, StringComparison.Ordinal);
        Assert.Contains("decisionReadyAfterConversation", liveHost, StringComparison.Ordinal);
        Assert.Contains("conversationStatus", liveHost, StringComparison.Ordinal);
        Assert.Contains("AutomationPersistedConversationLineCount", manager, StringComparison.Ordinal);
        Assert.Contains("AutomationHasPersistedPlayerLine", manager, StringComparison.Ordinal);
        Assert.Contains("_matter.ConversationLines.Any", nobleVm, StringComparison.Ordinal);
        Assert.Contains("string.Equals(x.Role, \"player\"", nobleVm, StringComparison.Ordinal);
        Assert.Contains("AutomationHasPersistedPlayerLine", liveHost, StringComparison.Ordinal);
        Assert.Contains("AutomationProviderBackedPhaseComplete", manager, StringComparison.Ordinal);
        Assert.Contains("_providerExpectedReplies", nobleVm, StringComparison.Ordinal);
        Assert.Contains("_providerCompletedReplies", nobleVm, StringComparison.Ordinal);
        Assert.Contains("_fallbackCompletedReplies", nobleVm, StringComparison.Ordinal);
        Assert.Contains("providerBackedDialogueRequired", liveHost, StringComparison.Ordinal);
        Assert.Contains("providerBackedOpeningProved", liveHost, StringComparison.Ordinal);
        Assert.Contains("providerBackedConversationProved", liveHost, StringComparison.Ordinal);
        Assert.Contains("providerBackedClosingProved", liveHost, StringComparison.Ordinal);
        Assert.Contains("expectedAcceptanceTier", liveHost, StringComparison.Ordinal);
        Assert.Contains("_matter.Participants.Count", nobleVm, StringComparison.Ordinal);
        Assert.Contains("Evidence this speaker can personally reveal", nobleVm,
            StringComparison.Ordinal);
        Assert.Contains("If the ruler's current words ask for evidence, proof, testimony", nobleVm,
            StringComparison.Ordinal);
        Assert.Contains("supportingQuote copied exactly from your visible reply", nobleVm,
            StringComparison.Ordinal);
        Assert.Contains("string suppliedAction = (action ?? string.Empty).Trim();", nobleVm,
            StringComparison.Ordinal);
        Assert.Contains("suppliedAction.Substring(separator + 1).Trim()", nobleVm,
            StringComparison.Ordinal);
        Assert.DoesNotContain("_selectedSuspectHeroId = normalized.Substring", nobleVm,
            StringComparison.Ordinal);
        Assert.Contains("bool nobleDocket = string.Equals(requestedMode, \"noble_docket\"", serverClient,
            StringComparison.Ordinal);
        Assert.Contains(": nobleDocket ? \"noble_docket\" : courtLife ? \"court_life\" : \"in_person\"", serverClient,
            StringComparison.Ordinal);
        Assert.Contains("payload[\"nobleDocketContext\"] = conversationContext.DeepClone();", serverClient,
            StringComparison.Ordinal);
        Assert.Contains("\"discoverableEvidence\", \"audienceTranscript\"", serverClient,
            StringComparison.Ordinal);
        Assert.Contains("EvidenceId = \"truth_chain\"", nobleBehavior, StringComparison.Ordinal);
        Assert.Contains("EvidenceId = \"complete_chain\"", nobleBehavior, StringComparison.Ordinal);
        Assert.Contains("EvidenceId = \"plausible_\" + plausibleEvidenceIndex++", nobleBehavior,
            StringComparison.Ordinal);
        Assert.DoesNotContain("EvidenceId = matter.MatterId + \"_complete_chain\"", nobleBehavior,
            StringComparison.Ordinal);
        Assert.Contains("The ruler deferred this matter to the Chancellor for further investigation.",
            nobleBehavior, StringComparison.Ordinal);
        Assert.Contains("Math.Min(480", liveHost, StringComparison.Ordinal);
        Assert.Contains("expectedReplyCount) * 120", liveHost, StringComparison.Ordinal);
        Assert.Contains("DateTime deadline = DateTime.UtcNow.AddSeconds(replyWindowSeconds);", liveHost, StringComparison.Ordinal);
        Assert.Contains("[\"timeoutSeconds\"] = 1800", rulerDocketMcp, StringComparison.Ordinal);
        Assert.Contains("MigrateLegacyDocketOnce();", fixture, StringComparison.Ordinal);
        Assert.Contains("TryScheduleChancellorActive(true", fixture, StringComparison.Ordinal);
        Assert.Contains("TryDismissChancellor(false", fixture, StringComparison.Ordinal);
        Assert.Contains("TryDismissChancellor(true", fixture, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(399.99, ReignPetitionSeverity.Minor)]
    [InlineData(200, ReignPetitionSeverity.Minor)]
    [InlineData(199.99, ReignPetitionSeverity.Serious)]
    [InlineData(100, ReignPetitionSeverity.Serious)]
    [InlineData(99.99, ReignPetitionSeverity.Severe)]
    [InlineData(400, ReignPetitionSeverity.None)]
    public void Food_thresholds_are_exact(double hearths, ReignPetitionSeverity expected)
        => Assert.Equal(expected, ReignRulerDocketRules.FoodSeverity(hearths));

    [Theory]
    [InlineData(4999, -1, ReignPetitionSeverity.Minor)]
    [InlineData(4999, 0, ReignPetitionSeverity.None)]
    [InlineData(3499, 0, ReignPetitionSeverity.Serious)]
    [InlineData(3499, 1, ReignPetitionSeverity.None)]
    [InlineData(1499, 100, ReignPetitionSeverity.Severe)]
    [InlineData(5000, -100, ReignPetitionSeverity.None)]
    public void Town_gold_uses_health_and_three_day_trend(double prosperity, double trend, ReignPetitionSeverity expected)
        => Assert.Equal(expected, ReignRulerDocketRules.TownGoldSeverity(prosperity, trend));

    [Theory]
    [InlineData(399, 0, 100, ReignPetitionSeverity.None)]
    [InlineData(400, 39.9, 100, ReignPetitionSeverity.Severe)]
    [InlineData(400, 40, 100, ReignPetitionSeverity.Serious)]
    [InlineData(400, 60, 100, ReignPetitionSeverity.Minor)]
    [InlineData(400, 80, 100, ReignPetitionSeverity.None)]
    public void Village_gold_defers_to_food_and_uses_expected_output(double hearths, double actual, double expectedOutput, ReignPetitionSeverity expected)
        => Assert.Equal(expected, ReignRulerDocketRules.VillageGoldSeverity(hearths, actual, expectedOutput));

    [Theory]
    [InlineData(79.99, false, ReignPetitionSeverity.Minor)]
    [InlineData(49.99, false, ReignPetitionSeverity.Serious)]
    [InlineData(24.99, false, ReignPetitionSeverity.Severe)]
    [InlineData(80, false, ReignPetitionSeverity.None)]
    [InlineData(0, true, ReignPetitionSeverity.None)]
    public void Soldier_thresholds_exclude_sieges(double security, bool besieged, ReignPetitionSeverity expected)
        => Assert.Equal(expected, ReignRulerDocketRules.SoldierSeverity(security, besieged));

    [Fact]
    public void Terms_match_the_locked_table_and_only_expose_relevant_effects()
    {
        ReignPetitionTerms food = ReignRulerDocketRules.Terms(ReignPetitionKind.Food, ReignPetitionSeverity.Severe);
        Assert.Equal(60, food.FoodStock);
        Assert.Equal(15, food.DurationDays);
        Assert.Equal(20, food.RequesterRelation);
        Assert.Equal(4, food.AssociatedRelation);
        Assert.Equal(4d, food.HearthDaily);
        Assert.Equal(0, food.Gold);
        Assert.Equal(0, food.Soldiers);

        ReignPetitionTerms village = ReignRulerDocketRules.Terms(ReignPetitionKind.VillageGold, ReignPetitionSeverity.Serious);
        Assert.Equal(7500, village.Gold);
        Assert.Equal(0.10d, village.VillageOutputFactor);
        Assert.Equal(0d, village.ProsperityDaily);

        ReignPetitionTerms soldiers = ReignRulerDocketRules.Terms(ReignPetitionKind.Soldiers, ReignPetitionSeverity.Minor);
        Assert.Equal(10, soldiers.Soldiers);
        Assert.Equal(5, soldiers.CasualtyPercentMaximum);
        Assert.Equal(0.5d, soldiers.SecurityDaily);
    }

    [Fact]
    public void Candidate_selection_is_stable_severity_first_and_one_per_town()
    {
        var candidates = new[]
        {
            new ReignPetitionCandidateScore { CandidateId = "a-food", TownId = "a", Severity = ReignPetitionSeverity.Minor, NormalizedNeed = .9 },
            new ReignPetitionCandidateScore { CandidateId = "a-gold", TownId = "a", Severity = ReignPetitionSeverity.Serious, NormalizedNeed = .1 },
            new ReignPetitionCandidateScore { CandidateId = "b", TownId = "b", Severity = ReignPetitionSeverity.Severe, NormalizedNeed = .2 },
            new ReignPetitionCandidateScore { CandidateId = "c", TownId = "c", Severity = ReignPetitionSeverity.Serious, NormalizedNeed = .8 }
        };
        var first = ReignRulerDocketRules.SelectCandidates(candidates, 5, "seed");
        var second = ReignRulerDocketRules.SelectCandidates(candidates.Reverse(), 5, "seed");
        Assert.Equal(new[] { "b", "c", "a-gold" }, first.Select(x => x.CandidateId));
        Assert.Equal(first.Select(x => x.CandidateId), second.Select(x => x.CandidateId));
    }

    [Fact]
    public void Daily_slots_costs_reserve_and_casualties_are_bounded_and_stable()
    {
        int slots = ReignRulerDocketRules.DailyOpportunityCount("campaign", "timeline", 42);
        Assert.InRange(slots, 1, 5);
        Assert.Equal(slots, ReignRulerDocketRules.DailyOpportunityCount("campaign", "timeline", 42));
        Assert.Equal(20 * 10 * 7 * 5, ReignRulerDocketRules.FoodGoldSubstitute(20, 7));
        Assert.Equal((1000 + 250) * 5, ReignRulerDocketRules.SoldierGoldSubstitute(1000, 250));
        Assert.Equal(50, ReignRulerDocketRules.MinimumHealthyGarrisonAfterDispatch(100));
        Assert.Equal(75, ReignRulerDocketRules.MinimumHealthyGarrisonAfterDispatch(300));
        Assert.InRange(ReignRulerDocketRules.CasualtyCount("expedition", 100, 40), 0, 40);
    }

    [Fact]
    public void Noble_catalog_is_complete_unique_and_partitioned_by_locked_severity_tables()
    {
        IReadOnlyList<ReignNobleMatterTemplate> catalog = ReignNobleDocketCatalog.Templates;
        Assert.Equal(68, catalog.Count);
        Assert.Equal(68, catalog.Select(x => x.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(20, catalog.Count(x => x.Severity == ReignNobleMatterSeverity.Petty));
        Assert.Equal(20, catalog.Count(x => x.Severity == ReignNobleMatterSeverity.Serious));
        Assert.Equal(18, catalog.Count(x => x.Severity == ReignNobleMatterSeverity.Grave));
        Assert.Equal(10, catalog.Count(x => x.Severity == ReignNobleMatterSeverity.Exceptional));

        ReignNobleMatterTemplate marriage = Assert.Single(catalog, x => x.Id == "serious-love-marriage-denied");
        Assert.True(marriage.RequiresUnmarriedPair);
        Assert.True(marriage.RequiresLoverAffinity);

        ReignNobleMatterTemplate divorce = Assert.Single(catalog, x => x.Id == "grave-divorce-refused");
        Assert.True(divorce.RequiresSpouses);
        Assert.Contains("divorcee", divorce.ApplicableNegativeTags);

        ReignNobleMatterTemplate adultery = Assert.Single(catalog,
            x => x.Id == "grave-adultery-accusation");
        Assert.True(adultery.NegativeTagsApplyOnSideA);
        Assert.False(adultery.NegativeTagsApplyOnSideB);
        Assert.True(adultery.ApplyNegativeTagsToAllRejectedParticipants);

        ReignNobleMatterTemplate paternity = Assert.Single(catalog,
            x => x.Id == "grave-illegitimate-heir");
        Assert.False(paternity.NegativeTagsApplyOnSideA);
        Assert.True(paternity.NegativeTagsApplyOnSideB);

        ReignNobleMatterTemplate forgedDeed = Assert.Single(catalog,
            x => x.Id == "grave-forged-deed");
        Assert.True(forgedDeed.NegativeTagsApplyOnSideA);
        Assert.True(forgedDeed.NegativeTagsApplyOnSideB);

        ReignNobleMatterTemplate secretMarriage = Assert.Single(catalog,
            x => x.Id == "grave-secret-marriage");
        Assert.False(secretMarriage.NegativeTagsApplyOnSideA);
        Assert.True(secretMarriage.NegativeTagsApplyOnSideB);
        Assert.True(secretMarriage.ApplyNegativeTagsToAllRejectedParticipants);

        ReignNobleMatterTemplate murder = Assert.Single(catalog, x => x.IsMurderInvestigation);
        Assert.Equal("exceptional-murder", murder.Id);
        Assert.Equal(4, murder.MinimumPrincipals);
        Assert.True(murder.CreatesStateOnActivation);
        Assert.True(murder.UsesHiddenTruth);
        Assert.Contains("convicted_murderer", murder.ApplicableNegativeTags);
        Assert.DoesNotContain("actual killer", murder.Premise, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("four living principals", murder.Premise, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("four suspects", murder.DemandA, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Noble_generation_is_deterministic_and_tracks_locked_mix_and_severity_weights()
    {
        int noble = 0, petty = 0, serious = 0, grave = 0, exceptional = 0;
        for (int day = 0; day < 100000; day++)
        {
            if (!ReignRulerDocketRules.IsNobleMatterSlot("campaign", "timeline", day, 0)) continue;
            noble++;
            ReignNobleMatterSeverity severity = ReignRulerDocketRules.NobleMatterSeverity("campaign", "timeline", day, 0);
            Assert.Equal(severity, ReignRulerDocketRules.NobleMatterSeverity("campaign", "timeline", day, 0));
            Assert.NotNull(ReignRulerDocketRules.SelectNobleMatterTemplate("campaign", "timeline", day, 0, severity));
            if (severity == ReignNobleMatterSeverity.Petty) petty++;
            else if (severity == ReignNobleMatterSeverity.Serious) serious++;
            else if (severity == ReignNobleMatterSeverity.Grave) grave++;
            else exceptional++;
        }

        Assert.InRange(noble / 100000d, .285d, .315d);
        Assert.InRange(petty / (double)noble, .47d, .53d);
        Assert.InRange(serious / (double)noble, .275d, .325d);
        Assert.InRange(grave / (double)noble, .13d, .17d);
        Assert.InRange(exceptional / (double)noble, .04d, .06d);
    }

    [Theory]
    [InlineData(ReignNobleMatterSeverity.Petty, 5, -6)]
    [InlineData(ReignNobleMatterSeverity.Serious, 9, -15)]
    [InlineData(ReignNobleMatterSeverity.Grave, 15, -30)]
    [InlineData(ReignNobleMatterSeverity.Exceptional, 23, -50)]
    public void Noble_relation_bands_and_acceptance_reductions_are_exact(
        ReignNobleMatterSeverity severity, int winner, int loser)
    {
        Assert.Equal(winner, ReignRulerDocketRules.NobleWinnerRelation(severity));
        Assert.Equal(loser, ReignRulerDocketRules.NobleLoserRelation(severity));
        Assert.Equal(loser, ReignRulerDocketRules.AcceptedLossRelation(loser, 0));
        Assert.Equal((int)Math.Round(loser * .60d, MidpointRounding.AwayFromZero),
            ReignRulerDocketRules.AcceptedLossRelation(loser, 1));
        Assert.Equal((int)Math.Round(loser * .25d, MidpointRounding.AwayFromZero),
            ReignRulerDocketRules.AcceptedLossRelation(loser, 2));
        Assert.Equal(0, ReignRulerDocketRules.AcceptedLossRelation(loser, 3));
        Assert.Equal((int)Math.Round(loser / 3d, MidpointRounding.AwayFromZero),
            ReignRulerDocketRules.ClanSpilloverRelation(loser));
    }

    [Theory]
    [InlineData(ReignNobleMatterCategory.Property, false, false, "house_a", "house_b", true)]
    [InlineData(ReignNobleMatterCategory.Family, false, false, "house_a", "HOUSE_A", false)]
    [InlineData(ReignNobleMatterCategory.Dynastic, true, false, "house_a", "house_a", false)]
    [InlineData(ReignNobleMatterCategory.Etiquette, true, false, "house_a", "house_b", true)]
    [InlineData(ReignNobleMatterCategory.Etiquette, false, false, "house_a", "house_b", false)]
    [InlineData(ReignNobleMatterCategory.Property, false, false, "", "house_b", false)]
    public void Opposing_clan_spillover_requires_two_distinct_clans(
        ReignNobleMatterCategory category, bool winnerIsLeader, bool loserIsLeader,
        string winnerClanId, string loserClanId, bool expected)
    {
        Assert.Equal(expected, ReignRulerDocketRules.ShouldApplyOpposingClanSpillover(
            category, winnerIsLeader, loserIsLeader, winnerClanId, loserClanId));
    }

    [Theory]
    [InlineData("I accept your judgment, though my grievance remains.", 1)]
    [InlineData("My grievance may remain, but I will abide by your judgment in this court.", 1)]
    [InlineData("My grievance will remain, but I will abide by your ruling, Sire.", 1)]
    [InlineData("My grievance will remain mine, but I will abide by it, Sire.", 1)]
    [InlineData("I will abide by it. I will keep my grievance, because obedience is not forgetfulness.", 1)]
    [InlineData("I will abide by it. I will not call a grievance dead merely because it was overruled.", 1)]
    [InlineData("I abide by your judgment and will set my grievance aside.", 2)]
    [InlineData("I will honor your ruling and put this matter to rest.", 2)]
    [InlineData("I shall be bound by the judgment and pursue it no further.", 2)]
    [InlineData("I accept your decision and will end the dispute.", 2)]
    [InlineData("I will honor your judgment. I will not move men or whispers to keep the quarrel alive.", 2)]
    [InlineData("I will honor your judgment and shall not renew this quarrel.", 2)]
    [InlineData("Your Grace, your judgment is given. The quarrel ends here for me. I will not keep it, renew it, continue it through another hand, or pursue it under another name.", 2)]
    [InlineData("Your judgment is heard. I will not keep it or pursue it further.", 2)]
    [InlineData("I accept your judgment. Wholeheartedly is a large word for court business, but I will end this quarrel.", 2)]
    [InlineData("Plainly, I accept it. With my whole heart? No. But I will not revive this quarrel or carry the claim to another hall.", 2)]
    [InlineData("I accept it as the king's judgment, though the grievance remains. I will not answer a lost hearing with hired blades. I will keep that grievance inside the bounds of your court.", 1)]
    [InlineData("I accept your judgment without resentment.", 3)]
    [InlineData("I accept your judgment wholeheartedly.", 3)]
    [InlineData("If neither house is dishonored, I can accept it and carry no resentment from this hall.", 3)]
    [InlineData("I will accept it as your judgment and carry no resentment from this hall.", 3)]
    [InlineData("If your judgment protects both houses, I will accept it as settled and bear no resentment in word or deed.", 3)]
    public void Noble_acceptance_tiers_require_natural_in_world_language(string quote, int tier)
    {
        Assert.True(ReignRulerDocketRules.VisibleNobleAcceptanceMatchesTier(quote, tier));
        Assert.Equal(tier, ReignRulerDocketRules.VisibleNobleAcceptanceTier(quote));
    }

    [Theory]
    [InlineData("I accept sixty percent of the relationship penalty.", 1)]
    [InlineData("I accept 25% of the relationship loss.", 2)]
    [InlineData("Apply acceptance tier 3 and no relation penalty.", 3)]
    [InlineData("I accept your judgment without resentment.", 2)]
    [InlineData("I can accept it, but I cannot promise no resentment.", 3)]
    [InlineData("I will accept it as your judgment, but I shall bear resentment.", 3)]
    [InlineData("I accept it, but I cannot swear that I feel no resentment.", 3)]
    [InlineData("I can accept your judgment as my king. But wholehearted? Without bitterness? Without a grudge? I will not bind my honor to feelings no court can command.", 3)]
    [InlineData("I will accept your ruling as my king's judgment. But resentment? Bitterness? A grudge? No. I will not dress a lie in velvet for the comfort of the room.", 3)]
    [InlineData("I accept your judgment. Wholeheartedly is a large word for court business, but I will end this quarrel.", 3)]
    [InlineData("I accept it, but I cannot promise that I will not revive this quarrel.", 2)]
    public void Noble_acceptance_rejects_statistics_mechanics_and_wrong_stances(string quote, int tier)
        => Assert.False(ReignRulerDocketRules.VisibleNobleAcceptanceMatchesTier(quote, tier));

    [Theory]
    [InlineData("I will not accept your judgment and will appeal it.")]
    [InlineData("I refuse the ruling and will continue this quarrel.")]
    [InlineData("I reject your judgment, Sire.")]
    [InlineData("Your judgment is given, but I refuse it and will appeal.")]
    public void Noble_acceptance_never_mistakes_explicit_refusal_for_obedience(string quote)
        => Assert.Equal(0, ReignRulerDocketRules.VisibleNobleAcceptanceTier(quote));

    [Theory]
    [InlineData("I will honor your ruling after three days.")]
    [InlineData("The second survey marks the old boundary; I accept your decision.")]
    public void Noble_acceptance_allows_in_world_numbers_that_are_not_statistics(string quote)
        => Assert.False(ReignRulerDocketRules.ContainsForbiddenNobleDocketStatistics(quote));

    [Theory]
    [InlineData("My relationship score will fall by ten.")]
    [InlineData("I accept half of the relation penalty.")]
    [InlineData("This is acceptance tier 2.")]
    [InlineData("I do not possess the hidden truth.")]
    [InlineData("The canonical truth was not supplied in my private fields.")]
    [InlineData("My system instructions forbid that answer.")]
    public void Noble_docket_visible_language_rejects_mechanical_statistics(string quote)
    {
        Assert.True(ReignRulerDocketRules.ContainsForbiddenNobleDocketStatistics(quote));
        Assert.Equal(0, ReignRulerDocketRules.VisibleNobleAcceptanceTier(quote));
    }

    [Theory]
    [InlineData("Zeonica\n\nI ask the court to hear me.", "Zeonica", "I ask the court to hear me.")]
    [InlineData("**Zeonica:**\r\nI object..", "Zeonica", "I object.")]
    [InlineData("# Zeonica —\n\n*He bows.*\nMy claim stands.", "Zeonica", "*He bows.*\nMy claim stands.")]
    [InlineData("Wait... I object..", "Zeonica", "Wait... I object.")]
    [InlineData("Zeonica", "Zeonica", "Zeonica")]
    [InlineData("Ortysia\nI bring news from the west.", "Zeonica", "Ortysia\nI bring news from the west.")]
    public void Noble_docket_visible_reply_removes_only_a_matching_location_heading_and_repairs_double_periods(
        string reply, string courtLocation, string expected)
        => Assert.Equal(expected, ReignRulerDocketRules.SanitizeVisibleNobleReply(reply, courtLocation));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(50, .20)]
    [InlineData(100, .40)]
    [InlineData(150, .60)]
    [InlineData(199, .796)]
    [InlineData(200, .80)]
    [InlineData(400, .80)]
    public void Chancellor_investigation_skill_average_is_capped_at_eighty_percent(int skill, double expected)
    {
        Assert.Equal(expected, ReignRulerDocketRules.ChancellorInvestigationChance(skill, skill, skill), 3);
        Assert.Equal(
            ReignRulerDocketRules.ChancellorInvestigationSucceeds("case", skill, skill, skill),
            ReignRulerDocketRules.ChancellorInvestigationSucceeds("case", skill, skill, skill));
    }
}
