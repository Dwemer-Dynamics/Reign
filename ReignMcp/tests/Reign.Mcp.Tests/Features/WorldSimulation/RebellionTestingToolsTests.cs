using Reign.Mcp.Server;
using System.Text.Json;

namespace Reign.Mcp.Tests;

public sealed class RebellionTestingToolsTests
{
    [Fact]
    public void Manifest_exposes_durable_cli_and_complete_preparation_profiles()
    {
        var manifest = TestingTools.GetRebellionTestManifest(TestOptions.Create());
        Assert.Equal("reign-rebellion-certification-manifest-v1", manifest["schema"]);
        Assert.Equal("ReignLiveTest.exe rebellion --profile <profile>", manifest["cliEntryPoint"]);
        string[] profiles = Assert.IsType<string[]>(manifest["profiles"]);
        Assert.Contains("pledges", profiles);
        Assert.Contains("reporting", profiles);
        Assert.Contains("summons", profiles);
        Assert.Contains("verdicts", profiles);
        Assert.Contains("save_verify", profiles);
        Assert.Contains("cleanup", profiles);
        JsonElement document = JsonSerializer.SerializeToElement(manifest["manifest"]);
        Assert.Equal(11, document.GetProperty("languageCases").GetArrayLength());
        JsonElement[] organic = document.GetProperty("languageCases").EnumerateArray()
            .Where(item => item.GetProperty("profile").GetString() == "language").ToArray();
        Assert.Equal(6, organic.Length);
        Assert.All(organic, item => Assert.Equal(2,
            item.GetProperty("turns").GetArrayLength()));
        Assert.All(organic.Where(item => item.GetProperty("channel").GetString()
                == "correspondence"), item =>
            Assert.Contains("final answer", item.GetProperty("turns")[0].GetString(),
                StringComparison.OrdinalIgnoreCase));
        JsonElement pledge = Assert.Single(organic, item =>
            item.GetProperty("id").GetString() == "RB-LANG-001");
        string pledgeFollowUp = pledge.GetProperty("turns")[1].GetString()!;
        Assert.Contains("Gwyin", pledgeFollowUp, StringComparison.Ordinal);
        Assert.Contains("Rhydan", pledgeFollowUp, StringComparison.Ordinal);
        Assert.Contains("Llanoc Hen Castle", pledgeFollowUp, StringComparison.Ordinal);
        Assert.Contains("rebellion against our ruler", pledgeFollowUp, StringComparison.Ordinal);
        Assert.Contains("without further condition", pledgeFollowUp, StringComparison.Ordinal);
        JsonElement report = Assert.Single(organic, item =>
            item.GetProperty("id").GetString() == "RB-LANG-003");
        string reportTurns = string.Join("\n", report.GetProperty("turns")
            .EnumerateArray().Select(turn => turn.GetString()));
        Assert.Contains("Before you give me a final answer", reportTurns,
            StringComparison.Ordinal);
        Assert.Contains("will your clan support my planned rebellion", reportTurns,
            StringComparison.Ordinal);
        Assert.DoesNotContain("report", reportTurns, StringComparison.OrdinalIgnoreCase);
        JsonElement mailReport = Assert.Single(organic, item =>
            item.GetProperty("id").GetString() == "RB-LANG-006");
        string mailReportTurns = string.Join("\n", mailReport.GetProperty("turns")
            .EnumerateArray().Select(turn => turn.GetString()));
        Assert.Contains("Before sending a final answer", mailReportTurns,
            StringComparison.Ordinal);
        Assert.Contains("will your clan support my planned rebellion", mailReportTurns,
            StringComparison.Ordinal);
        Assert.DoesNotContain("report", mailReportTurns, StringComparison.OrdinalIgnoreCase);
        string naturalLanguage = document.GetProperty("releaseGate")
            .GetProperty("naturalLanguage").GetString()!;
        Assert.Contains("final_private_report", naturalLanguage, StringComparison.Ordinal);
        Assert.Contains("relation at least +10", naturalLanguage, StringComparison.Ordinal);
        Assert.Contains("Loyalty at least 61", naturalLanguage, StringComparison.Ordinal);
        Assert.Contains("delayed ruler summons", naturalLanguage, StringComparison.Ordinal);
        Assert.Contains("advance six native days", naturalLanguage, StringComparison.Ordinal);
        Assert.Contains("advance another six native days", naturalLanguage,
            StringComparison.Ordinal);
        Assert.Contains("dismisses Reign's standard letter-arrival inquiry as Not Now",
            naturalLanguage, StringComparison.Ordinal);
        Assert.Contains("dispatch as reply evidence", naturalLanguage, StringComparison.Ordinal);
        Assert.Contains(document.GetProperty("nativeCases").EnumerateArray(), item =>
            item.GetProperty("id").GetString() == "RB-NATIVE-002");
        Assert.DoesNotContain(document.GetProperty("nativeCases").EnumerateArray(), item =>
            item.GetProperty("profile").GetString() == "organic_ui");
    }

    [Fact]
    public void Destructive_profiles_require_a_namespaced_save_and_explicit_confirmation()
    {
        string workspace = TestOptions.FindWorkspace();
        string source = File.ReadAllText(TestSourceLocator.Unique(Path.Combine(workspace, "ReignMcp"),
            "TestingTools.Rebellion.cs", "/src/Reign.Mcp.Server/Modules/Platform/"));
        Assert.Contains("start Reign rebellion test", source, StringComparison.Ordinal);
        Assert.Contains("StartsWith(\"Reign_\"", source, StringComparison.Ordinal);
        Assert.Contains("StartsWith(\"ReignTest_\"", source, StringComparison.Ordinal);
        Assert.Contains("profile is not (\"smoke\" or \"feature\")", source, StringComparison.Ordinal);
        Assert.Contains("profile is \"save_prepare\" or \"cleanup\"", source,
            StringComparison.Ordinal);
        Assert.Contains("operation = \"rebellion_test\"", source, StringComparison.Ordinal);
        Assert.Contains("reign_prepare_rebellion_certification", source, StringComparison.Ordinal);
        Assert.Contains("reign_carry_forward_rebellion_passes", source, StringComparison.Ordinal);
        Assert.Contains("reign_evaluate_rebellion_release_readiness", source, StringComparison.Ordinal);
        Assert.Contains("RequireArmedOwnedSaveAsync", source, StringComparison.Ordinal);
        Assert.Contains("naturalLanguageRequired = language", source, StringComparison.Ordinal);
        Assert.Contains("targetHeroId = targetSearch", source, StringComparison.Ordinal);
        Assert.Contains("operation = \"world_advance\"", source, StringComparison.Ordinal);
        Assert.Contains("days = 6", source, StringComparison.Ordinal);
        Assert.Contains("operation = \"wait_for_correspondence\"", source, StringComparison.Ordinal);
        Assert.Contains("stableMilliseconds = 25000", source, StringComparison.Ordinal);
        Assert.Contains("for (int turnIndex = 0; turnIndex < turns.Length; turnIndex++)",
            source, StringComparison.Ordinal);
        Assert.Contains("will your clan support my planned rebellion against our ruler, or refuse me?",
            source, StringComparison.Ordinal);
        Assert.DoesNotContain("or report this conspiracy", source,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("item.GetProperty(\"profile\").GetString() == \"language_contract\"",
            source, StringComparison.Ordinal);
        Assert.Contains("RebellionReportMatchesPreparedEvidence", source, StringComparison.Ordinal);
        Assert.DoesNotContain("mode = language ? \"rebellion\"", source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("operation = \"save_checkpoint\"", source[..source.IndexOf(
            "reign_start_rebellion_test", StringComparison.Ordinal)], StringComparison.Ordinal);
    }

    [Fact]
    public void Native_host_runs_distinct_profile_fixtures_and_blocks_post_report_recruitment()
    {
        string workspace = TestOptions.FindWorkspace();
        string host = File.ReadAllText(TestSourceLocator.Unique(Path.Combine(workspace, "ReignBeta"),
            "ReignRebellionPreparationTestHost.cs", "/src/Modules/Rebellion/Testing/"));
        string behavior = File.ReadAllText(TestSourceLocator.Unique(Path.Combine(workspace, "ReignBeta"),
            "ReignRebellionCampaignBehavior.cs", "/src/Modules/Rebellion/Campaign/"));
        string correspondence = File.ReadAllText(TestSourceLocator.Unique(Path.Combine(workspace, "ReignBeta"),
            "ReignLiveInteractionCorrespondenceHost.cs", "/src/Modules/Dialogue/Campaign/"));
        string liveServer = File.ReadAllText(TestSourceLocator.Unique(Path.Combine(workspace, "ReignServer"),
            "LiveInteractionTest.cs", "/src/Modules/WorldSimulation/"));
        string relationships = File.ReadAllText(TestSourceLocator.Unique(Path.Combine(workspace, "ReignBeta"),
            "ReignRelationshipCampaignBehavior.cs", "/src/Modules/Relationships/Campaign/"));
        string passiveWorld = File.ReadAllText(TestSourceLocator.Unique(Path.Combine(workspace, "ReignBeta"),
            "ReignLiveInteractionPassiveWorldHost.cs", "/src/Modules/WorldSimulation/Campaign/"));

        Assert.Contains("RunPreparationHarnessProfile", host, StringComparison.Ordinal);
        foreach (string profile in new[] { "pledges", "reporting", "summons", "verdicts",
            "declaration", "save_prepare", "save_verify", "resolution", "native_setup",
            "language_observe", "travel_prepare", "travel_verify", "mixed_transfer",
            "foreign_reintegration", "cleanup" })
            Assert.Contains("case \"" + profile + "\"", behavior, StringComparison.Ordinal);
        Assert.Contains("IsPreparationRecruitmentOpen(plot)", behavior, StringComparison.Ordinal);
        Assert.Contains("persistence_marker", behavior, StringComparison.Ordinal);
        Assert.Contains("RemoveHarnessFixtures", behavior, StringComparison.Ordinal);
        Assert.Contains("RunMixedHoldingTransferHarness", behavior, StringComparison.Ordinal);
        Assert.Contains("RunForeignClanReintegrationHarness", behavior, StringComparison.Ordinal);
        Assert.Contains("ChangeOwnerOfSettlementAction.ApplyByGift", behavior, StringComparison.Ordinal);
        Assert.Contains("living_family_reintegrated", behavior, StringComparison.Ordinal);
        Assert.Contains("duplicate_retry_idempotent", behavior, StringComparison.Ordinal);
        Assert.Contains("out_of_order_delivery", behavior, StringComparison.Ordinal);
        Assert.Contains("deadline_minus_epsilon_open", behavior, StringComparison.Ordinal);
        Assert.Contains("flight_or_release_clears_capture", behavior, StringComparison.Ordinal);
        Assert.Contains("invalid_and_competing_rejected", behavior, StringComparison.Ordinal);
        Assert.Contains("report_target_relation_fixture", behavior, StringComparison.Ordinal);
        Assert.Contains("report_target_clan_leader_fixture", behavior, StringComparison.Ordinal);
        Assert.Contains("report_target_honor_fixture", behavior, StringComparison.Ordinal);
        Assert.Contains("Same(setupCaseId, \"RB-NATIVE-001\")", behavior,
            StringComparison.Ordinal);
        Assert.Contains("correspondence_target_is_exact_enrolled_clan_leader", behavior,
            StringComparison.Ordinal);
        Assert.Contains("MoveClanToKingdom(correspondenceTarget.Clan, setupRealm)", behavior,
            StringComparison.Ordinal);
        Assert.Contains("correspondenceTarget.SetHasMet()", behavior, StringComparison.Ordinal);
        Assert.Contains("correspondenceTargetKnownAfterFixture", behavior, StringComparison.Ordinal);
        Assert.Contains("requestedTargets.Count > 0", correspondence, StringComparison.Ordinal);
        Assert.Contains("No known living correspondence contact matched the requested target.",
            correspondence, StringComparison.Ordinal);
        Assert.Contains("\"wait_for_correspondence\"", liveServer, StringComparison.Ordinal);
        Assert.Contains("correspondence_quiescence_command", liveServer, StringComparison.Ordinal);
        Assert.Contains("-10 - priorPlayerRelation", behavior, StringComparison.Ordinal);
        Assert.Contains("35 - priorRulerRelation", behavior, StringComparison.Ordinal);
        Assert.Contains("Select(x => x.Leader)", behavior, StringComparison.Ordinal);
        Assert.Contains("x.Clan.Leader == x", behavior, StringComparison.Ordinal);
        Assert.Contains("SetTraitLevel(DefaultTraits.Honor, Math.Max(1, priorHonor))", behavior,
            StringComparison.Ordinal);
        Assert.Contains("playerRelation\") <= -10", behavior, StringComparison.Ordinal);
        Assert.Contains("rulerRelation\") >= 35", behavior, StringComparison.Ordinal);
        Assert.Contains("honor\") >= 1", behavior, StringComparison.Ordinal);
        Assert.Contains("TryAcknowledgeMailInquiryForLiveHarness", relationships,
            StringComparison.Ordinal);
        Assert.Contains("_standardMailInquiryOpen", relationships, StringComparison.Ordinal);
        Assert.Contains("InformationManager.HideInquiry()", relationships,
            StringComparison.Ordinal);
        Assert.Contains("TryAcknowledgeMailInquiryForLiveHarness", passiveWorld,
            StringComparison.Ordinal);
        Assert.Contains("mailInquiriesAcknowledged", passiveWorld, StringComparison.Ordinal);
    }

    [Fact]
    public void Server_language_contract_accepts_direct_questions_and_scopes_negation()
    {
        string workspace = TestOptions.FindWorkspace();
        string server = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(workspace, "ReignServer"), "RebellionDirector.cs",
            "/src/Modules/Rebellion/"));
        string prompt = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(workspace, "ReignServer"), "PromptCaching.cs",
            "/src/Modules/Dialogue/"));
        string client = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(workspace, "ReignBeta"), "ReignServerClient.cs",
            "/src/Modules/Platform/Integration/"));

        Assert.DoesNotContain("normalized.Contains(\"?\")", server, StringComparison.Ordinal);
        Assert.Contains("bool negatesPledge", server, StringComparison.Ordinal);
        Assert.Contains("\"will not report\"", server, StringComparison.Ordinal);
        Assert.Contains("\"will not support\"", server, StringComparison.Ordinal);
        Assert.Contains("normalized.StartsWith(\"no \", StringComparison.Ordinal)", server,
            StringComparison.Ordinal);
        Assert.Contains("No. I will not throw my children's future", server,
            StringComparison.Ordinal);
        Assert.Contains("\"will not throw my\"", server, StringComparison.Ordinal);
        Assert.Contains("\"stands aside\"", server, StringComparison.Ordinal);
        Assert.Contains("commitment == \"refused\"", server, StringComparison.Ordinal);
        Assert.Contains("actionNeeded && commitment == \"refused\"", server,
            StringComparison.Ordinal);
        Assert.Contains("ActionGateShouldPlan(actionGate)", server, StringComparison.Ordinal);
        Assert.Contains("I won't pledge today, but I'll listen.", server,
            StringComparison.Ordinal);
        Assert.Contains("My answer is no, and it stays no.", server, StringComparison.Ordinal);
        Assert.Contains("When I rise against our ruler, will you pledge", server,
            StringComparison.Ordinal);
        Assert.Contains("This private letter asks your clan to support my rebellion", server,
            StringComparison.Ordinal);
        Assert.Contains("BuildRebellionPrivateReportEvidence", server, StringComparison.Ordinal);
        Assert.Contains("privateReport", server, StringComparison.Ordinal);
        Assert.Contains("reportEligibility", server, StringComparison.Ordinal);
        Assert.Contains("sovereignRelation >= 10", server, StringComparison.Ordinal);
        Assert.Contains("loyalty >= 61 || honor >= 1", server, StringComparison.Ordinal);
        Assert.Contains("SECRET REBELLION PREPARATION DECISION", prompt, StringComparison.Ordinal);
        Assert.Contains("final_private_report", prompt, StringComparison.Ordinal);
        Assert.Contains("unconditional final refusal must set actionGate.needed=true", prompt,
            StringComparison.Ordinal);
        Assert.Contains("actionGate.commitment=conditional", prompt, StringComparison.Ordinal);
        Assert.Contains("relationToSovereign", client, StringComparison.Ordinal);
        Assert.Contains("sharesPlayerSovereign", client, StringComparison.Ordinal);
    }
}
