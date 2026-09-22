using System.Text.Json;
using Reign.Mcp.Server;

namespace Reign.Mcp.Tests.Features.Court;

public sealed class CourtLifeHarnessContractTests
{
    [Fact]
    public void DecisionEvidenceHandlesNonInternationalJsonNullAndChecksResolvedEffects()
    {
        string root = TestOptions.FindWorkspace();
        string native = File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/Court/Court/ReignCourtLifeInGameTests.cs"));
        Assert.Contains("JObject international = native?[\"international\"] as JObject", native, StringComparison.Ordinal);
        Assert.DoesNotContain("native?[\"international\"]?[\"nativeDiplomaticAction\"]", native, StringComparison.Ordinal);
        Assert.Contains("else if (decisionExpectation == \"resolved\")", native, StringComparison.Ordinal);
        Assert.Contains("AddCourtLifeDecisionAssertions(decisionAssertions, marker, matter)", native, StringComparison.Ordinal);
    }

    [Fact]
    public void DiplomaticCompletionCatalogRequiresIndependentNativeCompletion()
    {
        string root = TestOptions.FindWorkspace();
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reign.testing.json")));
        var completion = catalog.RootElement.GetProperty("rulerDocket").GetProperty("courtLifeDiplomaticCompletion");
        string native = File.ReadAllText(Path.Combine(root, "ReignBeta/src/Modules/Court/Court/ReignCourtLifeInGameTests.cs"));
        foreach (var assertion in completion.GetProperty("assertions").EnumerateArray())
            Assert.Contains(assertion.GetString()!, native);
        foreach (string file in new[] { "docs/agent/TESTING_TOOL_GUIDE.md", "ReignMcp/docs/security-model.md", "ReignMcp/docs/tool-catalog.md" })
            Assert.Contains("nativeDiplomaticAction", TestingDocumentation.Read(Path.Combine(root, file)));
        Assert.Contains("government_hearing_pending", completion.GetProperty("contract").GetString());
        Assert.Contains("pending_diplomacy_confirmation_has_no_settlement_effects", completion.GetProperty("pendingConfirmation").GetString());
        Assert.Contains("pending_diplomacy_confirmation_has_no_settlement_effects", native);
        string government = File.ReadAllText(Path.Combine(root, "ReignBeta/src/Modules/Government/Campaign/ReignGovernmentBusinessAdapters.cs"));
        foreach (string field in new[] { "governmentBusinessId", "governmentKingdomId", "governmentStatus" })
        {
            Assert.Contains(field, completion.GetProperty("governmentAuthority").GetString());
            Assert.Contains(field, government);
        }
    }

    [Theory]
    [InlineData("court_life_preflight", "International", -1, -1, false, "", "")]
    [InlineData("court_life_prepare", "Family", -1, -1, false, "", "")]
    [InlineData("court_life_prepare", "International", 0, -1, false, "", "")]
    [InlineData("court_life_prepare", "Patronage", -1, 50, false, "", "")]
    [InlineData("court_life_prepare", "International", -1, -1, true, "", "")]
    [InlineData("court_life_prepare", "International", -1, -1, false, "captive", "")]
    [InlineData("court_life_prepare", "International", -1, -1, false, "", "participant")]
    public async Task ExistingBindingRejectsIncompatibleInputsBeforeCampaignAccess(string profile, string source,
        int gold, int loyalty, bool age, string captive, string participant)
    {
        var options = new ReignMcpOptions { WorkspaceRoot = TestOptions.FindWorkspace(), BuildRoot = Path.GetTempPath(),
            ServerBaseUri = new Uri("http://127.0.0.1:5101"), AllowVerificationControl = true };
        var error = await Assert.ThrowsAsync<ArgumentException>(() => TestingTools.StartRulerDocketTest(null!, options, null!,
            "campaign", "owned-run", profile: profile, confirmation: "start Reign ruler docket test on disposable save",
            courtLifeSource: source, courtLifeExistingMatterId: "exact_matter", courtLifeFixtureGold: gold,
            courtLifeFixtureLoyalty: loyalty, courtLifeFixtureAgeRulerTo34: age,
            courtLifeFixtureCaptiveHeroId: captive, courtLifeHeroId: participant));
        Assert.Contains("Existing matter binding", error.Message);
    }

    [Fact]
    public void ExistingBindingCatalogDocumentsExactIdentityAndIndependentNativeProof()
    {
        string root = TestOptions.FindWorkspace();
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reign.testing.json")));
        var binding = catalog.RootElement.GetProperty("rulerDocket").GetProperty("courtLifeExistingMatter");
        var input = typeof(TestingTools).GetMethod(nameof(TestingTools.StartRulerDocketTest))!.GetParameters()
            .Single(p => p.Name == binding.GetProperty("input").GetString());
        Assert.Equal("", input.DefaultValue);
        Assert.True(TestingTools.GetRulerDocketTestManifest().ContainsKey("courtLifeExistingMatter"));
        foreach (string file in new[] { "docs/agent/TESTING_TOOL_GUIDE.md", "ReignMcp/docs/security-model.md", "ReignMcp/docs/tool-catalog.md" })
        {
            string document = TestingDocumentation.Read(Path.Combine(root, file));
            Assert.Contains("courtLifeExistingMatterId", document);
            Assert.Contains("pendingMatterInventory", document);
        }
        string native = File.ReadAllText(Path.Combine(root, "ReignBeta/src/Modules/Court/Court/ReignCourtLifeInGameTests.cs"));
        Assert.Contains("existing_matter_binding_preserves_matter_and_native_state", native);
        Assert.Contains("[\"limit\"] = 100", native);
        Assert.Contains("payload[\"player\"]?.Value<string>(\"leaderHeroId\") == Hero.MainHero?.StringId", native);
    }

    [Fact]
    public void PersistenceContractKeepsFamilyValuesWhileIgnoringDatabaseRowOrder()
    {
        string root = TestOptions.FindWorkspace();
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reign.testing.json")));
        string contract = catalog.RootElement.GetProperty("rulerDocket").GetProperty("courtLifeFingerprintContract").GetString()!;
        Assert.Contains("hero_id", contract, StringComparison.Ordinal);
        Assert.Contains("visit_id", contract, StringComparison.Ordinal);
        string native = File.ReadAllText(Path.Combine(root, "ReignBeta/src/Modules/Court/Court/ReignCourtLifeInGameTests.cs"));
        foreach (string assertion in new[] { "family_row_order_does_not_change_fingerprint", "family_value_change_changes_fingerprint" })
        {
            Assert.Contains(assertion, contract, StringComparison.Ordinal);
            Assert.Contains(assertion, native, StringComparison.Ordinal);
        }
        Assert.Contains("AddCourtLifeFingerprintContractAssertions(assertions)", native, StringComparison.Ordinal);
    }

    [Fact]
    public void VisitorPreparationContractRequiresImmediateNativePlacement()
    {
        string root = TestOptions.FindWorkspace();
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reign.testing.json")));
        string timing = catalog.RootElement.GetProperty("rulerDocket").GetProperty("courtLifeTimeContract").GetString()!;
        Assert.Contains("immediately", timing, StringComparison.Ordinal);
        Assert.DoesNotContain("advancement to arrival", timing, StringComparison.Ordinal);
        string native = File.ReadAllText(Path.Combine(root, "ReignBeta/src/Modules/Court/Court/ReignCourtLifeInGameTests.cs"));
        Assert.Contains("visitors_placed_immediately_without_time_advance", native, StringComparison.Ordinal);
        string visitors = File.ReadAllText(Path.Combine(root, "ReignBeta/src/Modules/Court/Court/ReignNobleVisitorCampaignBehavior.cs"));
        Assert.DoesNotContain("AmbassadorTravelDays", visitors, StringComparison.Ordinal);
        Assert.DoesNotContain("now < stay.ArrivalDay", visitors, StringComparison.Ordinal);
    }

    [Fact]
    public void CourtLifeDatesRetainNativePrecisionThroughEligibilityAndEvidence()
    {
        string root = TestOptions.FindWorkspace();
        string directory = Path.Combine(root, "ReignBeta/src/Modules/Court/Court");
        foreach (string file in new[] { "ReignCourtLifeCampaignBehavior.cs", "ReignCourtLifeInGameTests.cs",
            "ReignCourtLifeFamilyFixture.cs", "ReignNobleVisitorCampaignBehavior.cs",
            "ReignPatronageCampaignBehavior.cs", "ReignPatronageHistory.cs" })
        {
            string source = File.ReadAllText(Path.Combine(directory, file));
            Assert.DoesNotContain("CurrentDayFloat()", source, StringComparison.Ordinal);
            Assert.Contains("CurrentCourtLifeDay()", source, StringComparison.Ordinal);
        }
        string behavior = File.ReadAllText(Path.Combine(directory, "ReignCourtLifeCampaignBehavior.cs"));
        Assert.Contains("private static double CurrentCourtLifeDay() => CampaignTime.Now.ToDays;", behavior, StringComparison.Ordinal);
        Assert.Contains("x.AvailableDay <= CurrentCourtLifeDay()", behavior, StringComparison.Ordinal);
        Assert.Contains("matter.AvailableDay > CurrentCourtLifeDay()", behavior, StringComparison.Ordinal);
        Assert.Contains("CurrentCourtLifeDay() >= matter.ExpiresDay", behavior, StringComparison.Ordinal);
    }

    [Fact]
    public void PatronageSpeakerContextIsBoundToTheCurrentCommission()
    {
        string root = TestOptions.FindWorkspace();
        string patronage = File.ReadAllText(Path.Combine(root, "ReignBeta/src/Modules/Court/Court/ReignPatronageCampaignBehavior.cs"));
        Assert.Contains("This audience concerns only the current commission", patronage, StringComparison.Ordinal);
        Assert.Contains("Do not introduce a subject, passage, attribution question, proposed line, objective, or dilemma from another patronage commission", patronage, StringComparison.Ordinal);
        Assert.Contains("Current commission premise:", patronage, StringComparison.Ordinal);
        Assert.Contains("explicitly label it as fiction before narrating it", patronage, StringComparison.Ordinal);
        Assert.Contains("Never state or imply that invented events actually happened in the ruler's reign", patronage, StringComparison.Ordinal);
        Assert.Contains("PrivateContext = BuildPatronagePrivateContext(template, state, creator.ActorId, ruler)", patronage, StringComparison.Ordinal);
        Assert.Contains("if (!matter.IsPending) continue;", patronage, StringComparison.Ordinal);
        Assert.Contains("participant.PrivateContext = BuildPatronagePrivateContext(template, state, participant.ActorId, Hero.MainHero)", patronage, StringComparison.Ordinal);
    }

    private static readonly string[] Profiles = { "court_life_preflight", "court_life_family_fixture", "court_life_prepare", "court_life_open", "court_life_converse",
        "court_life_choose_confirm", "court_life_snapshot", "court_life_observe", "court_life_clock_observe", "court_life_save_prepare", "court_life_save_verify" };

    [Fact]
    public void ManifestCatalogAndNativeHandlersAgreeOnCourtLifeProfiles()
    {
        var manifest = TestingTools.GetRulerDocketTestManifest();
        string[] profiles = Assert.IsType<string[]>(manifest["profiles"]);
        string root = TestOptions.FindWorkspace();
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reign.testing.json")));
        var docket = catalog.RootElement.GetProperty("rulerDocket");
        string[] catalogProfiles = docket.GetProperty("profiles").EnumerateArray().Select(x => x.GetString()!).ToArray();
        Assert.Equal(Profiles.Order(), profiles.Where(x => x.StartsWith("court_life_", StringComparison.Ordinal)).Order());
        Assert.Equal(Profiles.Order(), catalogProfiles.Where(x => x.StartsWith("court_life_", StringComparison.Ordinal)).Order());
        Assert.Equal("reign-court-life-evidence-v1", docket.GetProperty("courtLifeEvidenceSchema").GetString());
        Assert.Equal("reign-court-life-evidence-v1", manifest["courtLifeEvidenceSchema"]);
        string native = File.ReadAllText(Path.Combine(root, "ReignBeta/src/Modules/Court/Court/ReignCourtLifeInGameTests.cs"));
        string host = File.ReadAllText(Path.Combine(root, "ReignBeta/src/Modules/Court/Campaign/ReignLiveInteractionCourtLifeHost.cs"));
        string guide = TestingDocumentation.Read(Path.Combine(root, "docs/agent/TESTING_TOOL_GUIDE.md"));
        foreach (string profile in Profiles)
        { Assert.Contains(profile, native + host, StringComparison.Ordinal); Assert.Contains(profile, guide, StringComparison.Ordinal); }
    }

    [Fact]
    public void HouseholdFixtureRetainsNativeCreationAndDisposableGuards()
    {
        string root = TestOptions.FindWorkspace();
        string fixture = File.ReadAllText(Path.Combine(root, "ReignBeta/src/Modules/Court/Court/ReignCourtLifeFamilyFixture.cs"));
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reign.testing.json")));
        Assert.Equal("court_life_family_fixture", catalog.RootElement.GetProperty("rulerDocket").GetProperty("courtLifeFamilyFixture").GetProperty("profile").GetString());
        Assert.Contains("RulerDocketFixtureConfirmation", fixture, StringComparison.Ordinal);
        Assert.Contains("IsCoupleSuitableForMarriage", fixture, StringComparison.Ordinal);
        Assert.Contains("MarriageAction.Apply", fixture, StringComparison.Ordinal);
        Assert.Contains("HeroCreator.CreateChild", fixture, StringComparison.Ordinal);
        Assert.Contains("child.IsNotSpawned", fixture, StringComparison.Ordinal);
        Assert.Contains("familyFixtureChildren", fixture, StringComparison.Ordinal);
        Assert.Contains("father.Culture.LordTemplates", fixture, StringComparison.Ordinal);
        Assert.Contains("!father.Clan.Kingdom.IsAtWarWith(c.Kingdom)", fixture, StringComparison.Ordinal);
        Assert.Contains("familyFixtureOriginClanId", fixture, StringComparison.Ordinal);
        Assert.Contains("at peace", catalog.RootElement.GetProperty("rulerDocket").GetProperty("courtLifeFamilyFixture").GetProperty("originSelection").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain("child.ChangeState", fixture, StringComparison.Ordinal);
        Assert.DoesNotContain("DeliverOffSpring", fixture, StringComparison.Ordinal);
        Assert.DoesNotContain("OnGivenBirth", fixture, StringComparison.Ordinal);
        Assert.DoesNotContain("SetRelation", fixture, StringComparison.Ordinal);
        var ageOption = typeof(TestingTools).GetMethods().Single(m => m.Name == nameof(TestingTools.StartRulerDocketTest))
            .GetParameters().Single(p => p.Name == "courtLifeFixtureAgeRulerTo34");
        Assert.Equal(false, ageOption.DefaultValue);
        Assert.Contains("familyFixturePlayerAgeBefore", fixture, StringComparison.Ordinal);
        Assert.Contains("familyFixturePlayerAgeAdjusted", fixture, StringComparison.Ordinal);
        Assert.Contains("Retry must retain the original ruler-age setup option", fixture, StringComparison.Ordinal);
        Assert.Contains("courtLifeFixtureAgeRulerTo34", catalog.RootElement.GetProperty("rulerDocket").GetProperty("courtLifeFamilyFixture").GetProperty("ageAdjustment").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void DocketClockIsolationIsDocumentedAndConfinedToEnrolledSetup()
    {
        string root = TestOptions.FindWorkspace();
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reign.testing.json")));
        string contract = catalog.RootElement.GetProperty("rulerDocket").GetProperty("courtLifeClockIsolation").GetString()!;
        Assert.Contains("Observation phases never advance or pause time", contract, StringComparison.Ordinal);
        string host = File.ReadAllText(Path.Combine(root, "ReignBeta/src/Modules/WorldSimulation/Campaign/ReignLiveInteractionUiCalibrationHost.cs"));
        string setup = host[..host.IndexOf("private static", host.IndexOf("private static async Task<LiveCommandResult> UiOpenAsync", StringComparison.Ordinal) + 1, StringComparison.Ordinal)];
        Assert.Contains("command.Value<string>(\"campaignTestRunId\")", setup, StringComparison.Ordinal);
        Assert.Contains("command.Value<string>(\"expectedSaveName\")", setup, StringComparison.Ordinal);
        Assert.Contains("clock.Value<double>(\"worldDayAfter\") != docketWorldDayBefore", setup, StringComparison.Ordinal);
        foreach (string document in new[] { "docs/agent/TESTING_TOOL_GUIDE.md", "ReignMcp/README.md" })
            Assert.Contains("docketClock", TestingDocumentation.Read(Path.Combine(root, document)), StringComparison.Ordinal);
    }

    [Fact]
    public void NaturalHarnessRequiresExactVisibleMatterAndSeparateReviewedConfirmation()
    {
        string root = TestOptions.FindWorkspace();
        string native = File.ReadAllText(Path.Combine(root, "ReignBeta/src/Modules/Court/Court/ReignCourtLifeInGameTests.cs"));
        string host = File.ReadAllText(Path.Combine(root, "ReignBeta/src/Modules/Court/Campaign/ReignLiveInteractionCourtLifeHost.cs"));
        Assert.Contains("RulerDocketFixtureConfirmation", native, StringComparison.Ordinal);
        Assert.Contains("AutomationCourtLifeMatterId", host, StringComparison.Ordinal);
        Assert.Contains("AutomationHasPersistedPlayerLine(text)", host, StringComparison.Ordinal);
        Assert.Contains("CompletedPlayerTurnIds.Count == before + 1", host, StringComparison.Ordinal);
        Assert.Contains("AutomationProviderBackedPhaseComplete(\"conversation\")", host, StringComparison.Ordinal);
        Assert.Contains("matter == null || !matter.IsPending || matter.EffectsCommitted", host, StringComparison.Ordinal);
        Assert.Contains("already resolved and cannot accept another natural turn", host, StringComparison.Ordinal);
        Assert.Contains("TryExecuteAutomationAction(\"choose\"", native, StringComparison.Ordinal);
        Assert.Contains("TryExecuteAutomationAction(\"confirm\"", native, StringComparison.Ordinal);
        Assert.Contains("HasPendingFamilyVisitWork", native, StringComparison.Ordinal);
        Assert.Contains("HasPendingInternationalCourtLifeWork", native, StringComparison.Ordinal);
        Assert.Contains("different_game_instance", native, StringComparison.Ordinal);
        Assert.Contains("state_fingerprint_survived_reload", native, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyCourtLifeDecision(", host + native, StringComparison.Ordinal);
        Assert.DoesNotContain("CampaignTime.Now =", host + native, StringComparison.Ordinal);
    }

    [Fact]
    public void CourtLifeDialoguePinsEveryReplyAndStageDirectionToTheActiveSpeaker()
    {
        string root = TestOptions.FindWorkspace();
        string viewModel = File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/Court/UI/ViewModels/ReignCourtLifeScreenVM.cs"));
        Assert.Contains("context[\"activeSpeakerName\"] = person.Name", viewModel, StringComparison.Ordinal);
        Assert.Contains("You are \" + person.Name + \", the active speaker", viewModel, StringComparison.Ordinal);
        Assert.Contains("stage directions may describe only \" + person.Name + \"'s own", viewModel, StringComparison.Ordinal);
        Assert.Contains("Never attribute another attendee's gesture, clothing, equipment or action to the active speaker", viewModel, StringComparison.Ordinal);
        Assert.Contains("Do not write any stage direction that names, uses a pronoun for, or describes another attendee", viewModel, StringComparison.Ordinal);
        Assert.Contains("another person's visible reaction belongs only in that person's own reply", viewModel, StringComparison.Ordinal);
        Assert.Contains("Do not prefix the reply with a standalone speaker name, settlement or location name", viewModel, StringComparison.Ordinal);
        Assert.Contains("Keep dialogue, narration and stage directions in that same language", viewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void ReopeningCourtLifeAudienceReusesPersistedOpeningWithoutAnotherProviderTurn()
    {
        string root = TestOptions.FindWorkspace();
        string viewModel = File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/Court/UI/ViewModels/ReignCourtLifeScreenVM.cs"));
        Assert.Contains("RecoverPersistedOpeningReceipts(matter);", viewModel, StringComparison.Ordinal);
        Assert.Contains("!HasCompleteOpeningReceipts(matter)", viewModel, StringComparison.Ordinal);
        Assert.Contains("matter.CompletedReplyKeys.Contains(\"opening|\" + person.ActorId)", viewModel, StringComparison.Ordinal);
        Assert.Contains("matter.ReplyTextsByKey[key] = matter.ConversationLines[index].Text", viewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void CourtLifeHarnessReopensOnlyTheExactPendingAudienceAfterTechnicalFailure()
    {
        string root = TestOptions.FindWorkspace();
        string host = File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/Court/Campaign/ReignLiveInteractionCourtLifeHost.cs"));
        Assert.Contains("string.Equals(providerStatus, \"technical-failure\"", host, StringComparison.Ordinal);
        Assert.Contains("AutomationCourtLifeMatterId != matter.MatterId", host, StringComparison.Ordinal);
        Assert.Contains("AutomationCanTechnicalReturn", host, StringComparison.Ordinal);
        Assert.Contains("TryExecuteAutomationAction(\"technical-return\"", host, StringComparison.Ordinal);
        Assert.Contains("TryOpenForAutomation(court, matter", host, StringComparison.Ordinal);
        Assert.Contains("technicalAudienceRecovered", host, StringComparison.Ordinal);
    }

    [Fact]
    public void CourtLifeDialogueNormalizesAnAccidentalDoubleTerminalPeriodWithoutFlatteningEllipses()
    {
        string root = TestOptions.FindWorkspace();
        string viewModel = File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/Court/UI/ViewModels/ReignCourtLifeScreenVM.cs"));
        Assert.Contains("visible = NormalizeAssistantReply(visible);", viewModel, StringComparison.Ordinal);
        Assert.Contains("normalized.EndsWith(\"..\", StringComparison.Ordinal)", viewModel, StringComparison.Ordinal);
        Assert.Contains("!normalized.EndsWith(\"...\", StringComparison.Ordinal)", viewModel, StringComparison.Ordinal);
        Assert.Contains("normalized.Substring(0, normalized.Length - 1)", viewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void InternationalDialogueDoesNotSubstituteTheRulerForAClaimantOrCargoOwner()
    {
        string root = TestOptions.FindWorkspace();
        string viewModel = File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/Court/UI/ViewModels/ReignCourtLifeScreenVM.cs"));
        Assert.Contains("The ruler is the adjudicator, not the owner, customer, traveler, debtor, claimant, accused party, or actor", viewModel, StringComparison.Ordinal);
        Assert.Contains("A possible payment from the ruler's treasury is a proposed remedy only", viewModel, StringComparison.Ordinal);
        Assert.Contains("Do not replace them with the ruler because the ruler may decide or fund the remedy", viewModel, StringComparison.Ordinal);
        Assert.Contains("is unresolved and independent of every earlier hearing", viewModel, StringComparison.Ordinal);
        Assert.Contains("Never describe this matter's money as already paid or its terms as already accepted", viewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void ConstructiveCatalogOffersRetainTheForeignRulersStartingAuthority()
    {
        string root = TestOptions.FindWorkspace();
        string docket = File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/Court/Court/ReignInternationalDocket.cs"));
        string decisions = File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/Court/Court/ReignInternationalDocketDecisions.cs"));
        Assert.Contains("data[\"sovereignAuthorized\"] = template?.Constructive == true", docket, StringComparison.Ordinal);
        Assert.Contains("data.Property(\"sovereignAuthorized\") == null && savedTemplate?.Constructive == true", docket, StringComparison.Ordinal);
        Assert.Contains("data.Value<bool?>(\"counterProposed\") != true", docket, StringComparison.Ordinal);
        Assert.Contains("string.IsNullOrWhiteSpace(data.Value<string>(\"referralId\")) && data[\"candidate\"] == null", docket, StringComparison.Ordinal);
        Assert.Contains("A standing-charter permission marked false limits your ability to invent or independently change terms", docket, StringComparison.Ordinal);
        Assert.Contains("Do not claim that the represented ruler must approve these same exact terms again", docket, StringComparison.Ordinal);
        Assert.Contains("bool sovereignTerms = data.Value<bool?>(\"sovereignAuthorized\") == true", decisions, StringComparison.Ordinal);
        Assert.Contains("data.Value<string>(\"remedy\") != \"goodwill\"", decisions, StringComparison.Ordinal);
    }

    [Fact]
    public void RepeatedExtortionKeepsTheCurrentMatterSeparateFromEarlierPayments()
    {
        string root = TestOptions.FindWorkspace();
        string docket = File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/Court/Court/ReignInternationalDocket.cs"));
        Assert.Contains("current matter \" + matter.MatterId", docket, StringComparison.Ordinal);
        Assert.Contains("unresolved and distinct from every earlier demand", docket, StringComparison.Ordinal);
        Assert.Contains("A payment or settlement recorded for another matter does not satisfy this one", docket, StringComparison.Ordinal);
        Assert.Contains("Do not say this current demand was already paid", docket, StringComparison.Ordinal);
    }

    [Fact]
    public void PoliticalIncidentAudienceRetainsTheNpcRulerChoiceAndAppliedPressure()
    {
        string root = TestOptions.FindWorkspace();
        string docket = File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/Court/Court/ReignInternationalDocket.cs"));
        Assert.Contains("continues an already generated political incident involving the player kingdom", docket, StringComparison.Ordinal);
        Assert.Contains("already reduced the foreign kingdom's diplomatic pressure toward the player kingdom", docket, StringComparison.Ordinal);
        Assert.Contains("already applied the incident's diplomatic pressure toward the player kingdom", docket, StringComparison.Ordinal);
        Assert.Contains("Do not say that no incident reached the ruler or that no ruling occurred", docket, StringComparison.Ordinal);
        Assert.Contains("The pressure consequence is already committed", docket, StringComparison.Ordinal);
        Assert.Contains("The player's response remains pending", docket, StringComparison.Ordinal);
        Assert.Contains("it does not create a treaty, payment, tariff exemption, caravan guarantee", docket, StringComparison.Ordinal);
    }

    [Fact]
    public void LateForeignRulerReplyUsesItsExistingDocketReservation()
    {
        string root = TestOptions.FindWorkspace();
        string docket = File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/Court/Court/ReignInternationalDocket.cs"));
        Assert.Contains("reservedDay.HasValue && reservedDay.Value <= CurrentDay()", docket, StringComparison.Ordinal);
        Assert.DoesNotContain("Value<int?>(\"replyReservedDay\") == CurrentDay()", docket, StringComparison.Ordinal);
    }

    [Fact]
    public void FamilyExpectationsRequireExactVisitTransitionsAndRetainedNeglectEvidence()
    {
        string root = TestOptions.FindWorkspace();
        string native = File.ReadAllText(Path.Combine(root, "ReignBeta/src/Modules/Court/Court/ReignCourtLifeInGameTests.cs"));
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reign.testing.json")));
        string contract = catalog.RootElement.GetProperty("rulerDocket").GetProperty("courtLifeFamilyTransitionContract").GetString()!;
        Assert.Equal(contract, TestingTools.GetRulerDocketTestManifest()["courtLifeFamilyTransitionContract"]);
        Assert.Contains("resolved or busy audience", contract, StringComparison.Ordinal);
        Assert.Contains("neither a completed turn nor decision authority", contract, StringComparison.Ordinal);
        foreach (string document in new[] { "docs/agent/TESTING_TOOL_GUIDE.md", "ReignMcp/docs/security-model.md", "ReignMcp/docs/tool-catalog.md" })
            Assert.Contains("resolved or busy audience", TestingDocumentation.Read(Path.Combine(root, document)), StringComparison.Ordinal);
        Assert.Contains("familyBaseline", native, StringComparison.Ordinal);
        Assert.Contains("SameCourtLifeFamilyObservationScope", native, StringComparison.Ordinal);
        Assert.Contains("exact_visit_new_dismissal_and_counter_increment", native, StringComparison.Ordinal);
        Assert.Contains("exact_visit_crossed_patience_into_neglect", native, StringComparison.Ordinal);
        Assert.Contains("reconciliation_follows_proven_neglect_for_exact_visit", native, StringComparison.Ordinal);
        Assert.Contains("member.Value<int?>(\"dismissals\") == before.Value<int?>(\"dismissals\") + 1", native, StringComparison.Ordinal);
        Assert.Contains("marker[\"familyNeglectEvidence\"] = current.DeepClone()", native, StringComparison.Ordinal);
        Assert.DoesNotContain("family?.Value<int?>(\"dismissals\") > 0", native, StringComparison.Ordinal);
        int snapshotStart = native.IndexOf("if (phase == \"court_life_snapshot\")", StringComparison.Ordinal);
        int snapshotEnd = native.IndexOf("if (phase == \"court_life_open\")", snapshotStart, StringComparison.Ordinal);
        Assert.DoesNotContain("StoreRulerDocketMarker", native[snapshotStart..snapshotEnd], StringComparison.Ordinal);
        Assert.Contains("familyNeglectEvidence", TestingDocumentation.Read(Path.Combine(root, "docs/agent/TESTING_TOOL_GUIDE.md")), StringComparison.Ordinal);
    }
}
