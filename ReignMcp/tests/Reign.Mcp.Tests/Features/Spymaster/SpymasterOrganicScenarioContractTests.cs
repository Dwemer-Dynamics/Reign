using System.Text.Json;

namespace Reign.Mcp.Tests;

public sealed class SpymasterOrganicScenarioContractTests
{
    [Fact]
    public void OrganicManifestReferencesRunnableProductionScenarios()
    {
        string root = Path.Combine(TestOptions.FindWorkspace(),
            "ReignServer", "tests", "ReignLiveTest", "scenarios");
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(root, "spymaster-organic-manifest.json")));
        Assert.True(manifest.RootElement.GetProperty("preparedOnly").GetBoolean());

        string[] scenarioNames = manifest.RootElement.GetProperty("orderedProfiles")
            .EnumerateArray()
            .Select(item => item.GetString() ?? string.Empty)
            .Where(item => item.StartsWith("spymaster-organic-", StringComparison.Ordinal)
                && item.EndsWith(".json", StringComparison.Ordinal))
            .ToArray();
        Assert.NotEmpty(scenarioNames);
        Assert.All(scenarioNames, name => Assert.True(File.Exists(Path.Combine(root, name)), name));
        string[] supplementaryNames = manifest.RootElement.GetProperty("supplementaryProfiles")
            .EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToArray();
        Assert.Equal(new[]
        {
            "spymaster-organic-war-setup.json",
            "spymaster-organic-agent-cycle.json",
            "spymaster-organic-agent-operation-window.json"
        }, supplementaryNames);
        Assert.All(supplementaryNames, name => Assert.True(File.Exists(Path.Combine(root, name)), name));
        string[] controlledDestructive = manifest.RootElement.GetProperty("orderedProfiles")
            .EnumerateArray().Select(item => item.GetString() ?? string.Empty)
            .Where(item => item.StartsWith("spymaster-controlled-", StringComparison.Ordinal)
                && item.EndsWith(".json", StringComparison.Ordinal)).ToArray();
        Assert.Equal(new[]
        {
            "spymaster-controlled-assassination-success.json",
            "spymaster-controlled-capture-breakout-success.json"
        }, controlledDestructive);
        Assert.All(controlledDestructive, name => Assert.True(File.Exists(Path.Combine(root, name)), name));

        string[] allScenarios = Directory.GetFiles(root, "spymaster-organic-*.json")
            .Where(path => !path.EndsWith("spymaster-organic-manifest.json", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Assert.All(allScenarios, path =>
        {
            string json = File.ReadAllText(path);
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement scenario = document.RootElement;
            Assert.Equal(2, scenario.GetProperty("schemaVersion").GetInt32());
            string fileName = Path.GetFileName(path);
            string expectedMode = fileName.Equals("spymaster-organic-war-setup.json", StringComparison.OrdinalIgnoreCase)
                ? "kingdom_event"
                : "spymaster";
            Assert.Equal(expectedMode, scenario.GetProperty("mode").GetString());
            Assert.DoesNotContain("spytest_", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("forcedRoll", json, StringComparison.OrdinalIgnoreCase);
            Assert.All(scenario.GetProperty("steps").EnumerateArray(), step =>
            {
                string operation = step.GetProperty("operation").GetString() ?? string.Empty;
                if (operation == "world_advance")
                {
                    Assert.Equal("passive_world", step.GetProperty("mode").GetString());
                    Assert.True(step.GetProperty("timeoutSeconds").GetInt32() >= 7200);
                }
                if (operation == "world_acknowledge_native_diplomacy_notices")
                {
                    Assert.Equal(
                        "acknowledge Bannerlord diplomacy notices on disposable save",
                        step.GetProperty("confirmation").GetString());
                }
            });
        });

        string warSetup = File.ReadAllText(Path.Combine(root,
            "spymaster-organic-war-setup.json"));
        string agentCycle = File.ReadAllText(Path.Combine(root,
            "spymaster-organic-agent-cycle.json"));
        Assert.Contains("world_acknowledge_native_diplomacy_notices",
            warSetup, StringComparison.Ordinal);
        Assert.Contains("world_acknowledge_native_diplomacy_notices",
            agentCycle, StringComparison.Ordinal);
    }

    [Fact]
    public void OrganicSafetyConfirmationsStaySeparated()
    {
        string root = Path.Combine(TestOptions.FindWorkspace(),
            "ReignServer", "tests", "ReignLiveTest", "scenarios");
        string prepare = File.ReadAllText(Path.Combine(root, "spymaster-organic-prepare.json"));
        string destructive = File.ReadAllText(Path.Combine(root, "spymaster-organic-destructive-attempt.json"));
        Assert.Contains("prepare organic Spymaster test on disposable save", prepare, StringComparison.Ordinal);
        Assert.DoesNotContain("run irreversible Reign Spymaster test on disposable save", prepare, StringComparison.Ordinal);
        Assert.DoesNotContain("save_checkpoint", prepare, StringComparison.Ordinal);
        Assert.DoesNotContain("Reign_Spymaster_Organic_Baseline", prepare, StringComparison.Ordinal);
        string savePrepare = File.ReadAllText(Path.Combine(root,
            "spymaster-organic-save-prepare.json"));
        Assert.DoesNotContain("save_checkpoint", savePrepare, StringComparison.Ordinal);
        Assert.DoesNotContain("Reign_Spymaster_Organic_Midrun_A", savePrepare,
            StringComparison.Ordinal);
        string manifest = File.ReadAllText(Path.Combine(root,
            "spymaster-organic-manifest.json"));
        Assert.Contains("guarded_campaign_test_current", manifest, StringComparison.Ordinal);
        Assert.DoesNotContain("Reign_Spymaster_Organic_Midrun_A", manifest,
            StringComparison.Ordinal);
        Assert.Contains("run irreversible Reign Spymaster test on disposable save", destructive, StringComparison.Ordinal);

        string passiveWorldHost = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(TestOptions.FindWorkspace(), "ReignBeta"),
            "ReignLiveInteractionPassiveWorldHost.cs", "/src/"));
        Assert.Contains("Reign_Spymaster_Organic_", passiveWorldHost, StringComparison.Ordinal);
        Assert.Contains("Reign_Spymaster_Organic_Midrun_A", passiveWorldHost, StringComparison.Ordinal);
        Assert.Contains("exactLegacySpymasterCheckpoint", passiveWorldHost, StringComparison.Ordinal);

        string liveServer = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(TestOptions.FindWorkspace(), "ReignServer"),
            "LiveInteractionTest.cs", "/src/"));
        Assert.Contains("\"spymaster_organic\"", liveServer, StringComparison.Ordinal);
    }

    [Fact]
    public void UiClosureRestoresSettlementContinuationAndEffectiveAffinityIsQueueable()
    {
        string root = TestOptions.FindWorkspace();
        string ui = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignBeta"),
            "ReignLiveInteractionUiCalibrationHost.cs", "/src/"));
        Assert.Contains("RestoreSettlementMenuAfterCalibrationAsync", ui, StringComparison.Ordinal);
        Assert.Contains("GameMenu.ActivateGameMenu(menuId)", ui, StringComparison.Ordinal);
        Assert.Contains("settlementMenuReady", ui, StringComparison.Ordinal);

        string liveServer = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignServer"), "LiveInteractionTest.cs", "/src/"));
        Assert.Contains("\"social_set_underlying_affinity\"", liveServer, StringComparison.Ordinal);
        string socialFixture = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignServer"), "SocialBalanceTest.cs", "/src/"));
        Assert.Contains("valueMode != \"underlying\" && valueMode != \"effective\"",
            socialFixture, StringComparison.Ordinal);
        Assert.Contains("SocialBalanceUnderlyingAffinityForEffectiveValue",
            socialFixture, StringComparison.Ordinal);

        string mcp = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignMcp"), "TestingTools.Spymaster.cs"));
        Assert.Contains("reign_set_spymaster_agent_activation_fixture", mcp,
            StringComparison.Ordinal);
        Assert.Contains("set Reign Spymaster agent threshold on disposable save", mcp,
            StringComparison.Ordinal);
        Assert.Contains("valueMode = \"underlying\"", mcp, StringComparison.Ordinal);
        Assert.Contains("InputGuard.Range(relationValue, nameof(relationValue), -100, -29)",
            mcp, StringComparison.Ordinal);
        Assert.Contains("CampaignTestService campaignTests", mcp, StringComparison.Ordinal);
        Assert.Contains("campaignTestRunId", mcp, StringComparison.Ordinal);
        Assert.Contains("RequireArmedOwnedSaveAsync", mcp, StringComparison.Ordinal);
        Assert.Contains("savePrefix = authorization.SavePrefix", mcp, StringComparison.Ordinal);
        Assert.Contains("disposableSaveName = authorization.CurrentSaveName", mcp,
            StringComparison.Ordinal);
        Assert.Contains("protectedBaselineSaveName = authorization.BaselineSaveName", mcp,
            StringComparison.Ordinal);
        Assert.DoesNotContain("savePrefix must begin with Reign_SocialBalance_", mcp,
            StringComparison.Ordinal);
        int nativeRelation = mcp.IndexOf("operation = \"social_set_relation\"",
            StringComparison.Ordinal);
        int directionalAffinity = mcp.IndexOf("EffectiveAffinity(foreignRulerHeroId, mainHeroId)",
            StringComparison.Ordinal);
        Assert.True(nativeRelation >= 0 && directionalAffinity > nativeRelation,
            "The native relation must be set before the exact directional affinities so projection cannot drift the requested sponsor outlook.");
    }

    [Fact]
    public void AppointmentProfileUsesProductionOfficePathAndObservableUiEvidence()
    {
        string root = TestOptions.FindWorkspace();
        string mcp = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignMcp"), "TestingTools.Spymaster.cs"));
        Assert.Contains("\"appointment\", \"smoke\", \"feature\"", mcp,
            StringComparison.Ordinal);
        Assert.Contains("operation = \"ui_action\", targetSearch = \"spymaster\", text = \"appoint\"",
            mcp, StringComparison.Ordinal);

        string uiHost = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignBeta"), "ReignLiveInteractionUiCalibrationHost.cs", "/src/"));
        Assert.Contains("TryExecuteAutomationAppointmentAsync", uiHost, StringComparison.Ordinal);
        Assert.Contains("spymasterHeroId", uiHost, StringComparison.Ordinal);
        Assert.Contains("spymasterName", uiHost, StringComparison.Ordinal);

        string viewModel = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignBeta"), "ReignSpymasterScreenVM.cs", "/src/"));
        Assert.Contains("DateTime.UtcNow.AddSeconds(60)", viewModel, StringComparison.Ordinal);
        Assert.Contains("_court.ServerAvailable && _court.ServerSessionOpened", viewModel,
            StringComparison.Ordinal);
        Assert.Contains("!_court.ServerRequestInFlight", viewModel, StringComparison.Ordinal);
        Assert.Contains("ReignMainThread.InvokeAsync(_court.EnsureServerSessionAligned)", viewModel,
            StringComparison.Ordinal);
        Assert.Contains("_court.AssignOfficeAsync(ReignCourtOffice.Spymaster, selected)",
            viewModel, StringComparison.Ordinal);
        Assert.Contains("ReignCourtOfficeService.IsEligible", viewModel, StringComparison.Ordinal);

        string courtBehavior = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignBeta"), "ReignCourtCampaignBehavior.cs", "/src/"));
        Assert.Contains("response.Raw?[\"currentRevision\"] != null", courtBehavior,
            StringComparison.Ordinal);
        Assert.Contains("expectedRevision = response.Revision", courtBehavior,
            StringComparison.Ordinal);
    }

    [Fact]
    public void OrganicIntelligenceExcludesRetiringNpcStatDiscovery()
    {
        string root = Path.Combine(TestOptions.FindWorkspace(),
            "ReignServer", "tests", "ReignLiveTest", "scenarios");
        string intelligence = File.ReadAllText(Path.Combine(root,
            "spymaster-organic-intelligence.json"));
        Assert.Contains("\"person_relationships\"", intelligence, StringComparison.Ordinal);
        Assert.Contains("\"person_rumors\"", intelligence, StringComparison.Ordinal);
        Assert.DoesNotContain("\"person_skills\"", intelligence, StringComparison.Ordinal);

        string manifest = File.ReadAllText(Path.Combine(root,
            "spymaster-organic-manifest.json"));
        Assert.Contains("person_skills NPC-stat discovery is intentionally excluded",
            manifest, StringComparison.Ordinal);
    }

    [Fact]
    public void SocialItemAutomationAwaitsTheProductionStatusRefresh()
    {
        string root = TestOptions.FindWorkspace();
        string uiHost = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignBeta"), "ReignLiveInteractionUiCalibrationHost.cs", "/src/"));
        Assert.Contains("TryExecuteAutomationSocialSelectionAsync", uiHost, StringComparison.Ordinal);
        Assert.Contains("social records refreshed and selected through the production UI path", uiHost,
            StringComparison.OrdinalIgnoreCase);

        string manager = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignBeta"), "ReignSpymasterScreenManager.cs", "/src/"));
        Assert.Contains("TryExecuteAutomationSocialSelectionAsync", manager, StringComparison.Ordinal);

        string viewModel = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignBeta"), "ReignSpymasterScreenVM.cs", "/src/"));
        Assert.Contains("await RefreshSocialItemsAsync().ConfigureAwait(false)", viewModel,
            StringComparison.Ordinal);
        Assert.Contains("TryExecuteAutomationSelection(\"social\", value, out string error)", viewModel,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SpymasterHeroTargetsMatchProductionMissionEligibility()
    {
        string viewModel = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(TestOptions.FindWorkspace(), "ReignBeta"),
            "ReignSpymasterScreenVM.cs", "/src/"));
        Assert.Contains("x != null && x.IsAlive && x.IsActive", viewModel,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SocialMitigationSelectsIndependentTargetsWithLiveProductionRecords()
    {
        string root = TestOptions.FindWorkspace();
        string scenario = File.ReadAllText(Path.Combine(root, "ReignServer", "tests", "ReignLiveTest",
            "scenarios", "spymaster-organic-social-mitigation.json"));
        Assert.Contains("foreign-noble-with-rumor", scenario, StringComparison.Ordinal);
        Assert.Contains("foreign-noble-with-reputation", scenario, StringComparison.Ordinal);

        string viewModel = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignBeta"), "ReignSpymasterScreenVM.cs", "/src/"));
        Assert.Contains("TryExecuteAutomationSocialTargetSelectionAsync", viewModel, StringComparison.Ordinal);
        Assert.Contains("ReignSpymasterMissionState.Succeeded", viewModel, StringComparison.Ordinal);
        Assert.Contains("ReignSpymasterServerClient.GetSocialStatusAsync(candidateId)", viewModel,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SocialFabricationRotatesAcrossFreshEligibleForeignTargets()
    {
        string root = TestOptions.FindWorkspace();
        string scenario = File.ReadAllText(Path.Combine(root, "ReignServer", "tests", "ReignLiveTest",
            "scenarios", "spymaster-organic-social-fabrication.json"));
        Assert.Contains("fresh-foreign-noble", scenario, StringComparison.Ordinal);

        string viewModel = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignBeta"), "ReignSpymasterScreenVM.cs", "/src/"));
        Assert.Contains("fresh-foreign-noble", viewModel, StringComparison.Ordinal);
        Assert.Contains("DefaultIfEmpty(-1f).Max()", viewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void ControlledSocialSuccessIsBoundToExactCapturedMissionIds()
    {
        string root = TestOptions.FindWorkspace();
        string scenario = File.ReadAllText(Path.Combine(root, "ReignServer", "tests", "ReignLiveTest",
            "scenarios", "spymaster-controlled-social-success.json"));
        Assert.Contains("stable-fresh-foreign-noble", scenario, StringComparison.Ordinal);
        Assert.Contains("control_outcomes", scenario, StringComparison.Ordinal);
        Assert.Contains("force Reign Spymaster outcomes on disposable save", scenario, StringComparison.Ordinal);
        Assert.Contains("verify_controlled_outcomes", scenario, StringComparison.Ordinal);

        string viewModel = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignBeta"), "ReignSpymasterScreenVM.cs", "/src/"));
        Assert.Contains("stable-fresh-foreign-noble", viewModel, StringComparison.Ordinal);
        Assert.Contains("!candidate.Hero.IsPrisoner", viewModel, StringComparison.Ordinal);
        Assert.Contains("candidate.Hero.PartyBelongedTo == null", viewModel, StringComparison.Ordinal);

        string runtime = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignBeta"), "ReignSpymasterTestRuntime.cs", "/src/"));
        Assert.Contains("ForceMissionOutcome", runtime, StringComparison.Ordinal);
        Assert.Contains("TryTakeMissionOutcome", runtime, StringComparison.Ordinal);
        Assert.Contains("ForcedMissionOutcomes.Remove", runtime, StringComparison.Ordinal);
        Assert.Contains("ControlledMissionIdsByRun", runtime, StringComparison.Ordinal);
        Assert.Contains("ControlledMissionIds", runtime, StringComparison.Ordinal);

        string organicTests = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignBeta"), "ReignSpymasterOrganicInGameTests.cs", "/src/"));
        Assert.Contains("controlledMissionIds.Contains(x.MissionId)", organicTests, StringComparison.Ordinal);

        string behavior = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignBeta"), "ReignSpymasterCampaignBehavior.cs", "/src/"));
        Assert.Contains("TryTakeMissionOutcome(mission.MissionId", behavior, StringComparison.Ordinal);
        Assert.Contains("DiscardMissionOutcome(mission.MissionId)", behavior, StringComparison.Ordinal);
    }

    [Fact]
    public void ControlledRumorMitigationUsesOneExactMissionAndACompressedLiveWindow()
    {
        string root = TestOptions.FindWorkspace();
        string scenario = File.ReadAllText(Path.Combine(root, "ReignServer", "tests", "ReignLiveTest",
            "scenarios", "spymaster-controlled-rumor-mitigation.json"));
        Assert.Contains("foreign-noble-with-rumor", scenario, StringComparison.Ordinal);
        Assert.Contains("mitigate_target_rumor", scenario, StringComparison.Ordinal);
        Assert.Contains("\"phase\": \"control_outcomes\"", scenario, StringComparison.Ordinal);
        Assert.Contains("\"minimum\": 1", scenario, StringComparison.Ordinal);
        Assert.Contains("\"days\": 12", scenario, StringComparison.Ordinal);
        Assert.Contains("\"phase\": \"verify_controlled_outcomes\"", scenario, StringComparison.Ordinal);
    }

    [Fact]
    public void ControlledRemainingRareBranchesStayExactAndOneShot()
    {
        string root = Path.Combine(TestOptions.FindWorkspace(), "ReignServer", "tests", "ReignLiveTest", "scenarios");
        string self = File.ReadAllText(Path.Combine(root, "spymaster-controlled-self-mitigation.json"));
        Assert.Contains("mitigate_own_rumor", self, StringComparison.Ordinal);
        Assert.Contains("mitigate_own_reputation", self, StringComparison.Ordinal);
        Assert.Contains("\"minimum\": 2", self, StringComparison.Ordinal);
        Assert.Contains("force Reign Spymaster outcomes on disposable save", self, StringComparison.Ordinal);

        string counter = File.ReadAllText(Path.Combine(root, "spymaster-controlled-counterintelligence.json"));
        Assert.Contains("\"missionTypes\": [\"counterintelligence\"]", counter, StringComparison.Ordinal);
        Assert.Contains("verify_controlled_outcomes", counter, StringComparison.Ordinal);

        string agent = File.ReadAllText(Path.Combine(root, "spymaster-controlled-agent-action.json"));
        Assert.Contains("control_agent_action", agent, StringComparison.Ordinal);
        Assert.Contains("force one Reign Spymaster foreign-agent action on disposable save", agent, StringComparison.Ordinal);
        Assert.Contains("verify_controlled_agent_action", agent, StringComparison.Ordinal);

        string runtime = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(TestOptions.FindWorkspace(), "ReignBeta"), "ReignSpymasterTestRuntime.cs", "/src/"));
        Assert.Contains("ForceForeignAgentAction", runtime, StringComparison.Ordinal);
        Assert.Contains("_forcedForeignAgentAction = null", runtime, StringComparison.Ordinal);
        string server = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(TestOptions.FindWorkspace(), "ReignServer"), "SpymasterSystem.cs", "/src/"));
        Assert.Contains("productionActionChance", server, StringComparison.Ordinal);
        Assert.Contains("productionStableRoll", server, StringComparison.Ordinal);
    }

    [Fact]
    public void ControlledDestructiveBranchesUseExactMissionAndBreakoutOneShots()
    {
        string root = TestOptions.FindWorkspace();
        string scenarios = Path.Combine(root, "ReignServer", "tests", "ReignLiveTest", "scenarios");
        string assassination = File.ReadAllText(Path.Combine(scenarios,
            "spymaster-controlled-assassination-success.json"));
        Assert.Contains("\"missionTypes\": [\"assassinate_person\"]", assassination,
            StringComparison.Ordinal);
        Assert.Contains("verify_controlled_assassination", assassination, StringComparison.Ordinal);
        string breakout = File.ReadAllText(Path.Combine(scenarios,
            "spymaster-controlled-capture-breakout-success.json"));
        Assert.Contains("foreign-governed", breakout, StringComparison.Ordinal);
        Assert.Contains("\"missionTypes\": [\"assassinate_governor\"]", breakout,
            StringComparison.Ordinal);
        Assert.Contains("\"consequenceUnitRoll\": 0.5", breakout, StringComparison.Ordinal);
        Assert.Contains("verify_controlled_capture", breakout, StringComparison.Ordinal);
        Assert.Contains("\"phase\": \"attempt_breakout\"", breakout, StringComparison.Ordinal);

        string runtime = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignBeta"), "ReignSpymasterTestRuntime.cs", "/src/"));
        Assert.Contains("ForcedMissionConsequences", runtime, StringComparison.Ordinal);
        Assert.Contains("ForceBreakoutOutcome", runtime, StringComparison.Ordinal);
        Assert.Contains("TryTakeBreakoutOutcome", runtime, StringComparison.Ordinal);
        string behavior = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignBeta"), "ReignSpymasterCampaignBehavior.cs", "/src/"));
        Assert.Contains("TryTakeMissionConsequence(mission.MissionId", behavior,
            StringComparison.Ordinal);
        Assert.Contains("TryTakeBreakoutOutcome(mission.MissionId", behavior,
            StringComparison.Ordinal);
        Assert.Contains("ResolveSpymasterCaptivityHolder", behavior, StringComparison.Ordinal);
        Assert.Contains("The Spymaster evaded capture without revealing the sponsor", behavior,
            StringComparison.Ordinal);
    }
}
