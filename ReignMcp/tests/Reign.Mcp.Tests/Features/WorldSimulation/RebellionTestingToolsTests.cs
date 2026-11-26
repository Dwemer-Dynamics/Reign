using Reign.Mcp.Server;
using System.Text.Json;

namespace Reign.Mcp.Tests;

public sealed class RebellionTestingToolsTests
{
    [Fact]
    public void OrdinarySurrenderAndRulerReceiptRequireNativeClanReintegration()
    {
        string root = TestOptions.FindWorkspace();
        string path = Path.Combine(root,
            "ReignBeta/src/Modules/Rebellion/Campaign/ReignRebellionCampaignBehavior.CivilWar.cs");
        string source = File.ReadAllText(path);
        string surrender = source[source.IndexOf("public bool TrySurrender(", StringComparison.Ordinal)
            ..source.IndexOf("public bool SetPlayerClanSide(", StringComparison.Ordinal)];
        int check = surrender.IndexOf("if (!ResolveCivilWar(", StringComparison.Ordinal);
        int winner = surrender.IndexOf("won the throne", StringComparison.Ordinal);
        Assert.True(check >= 0 && winner > check);
        Assert.Contains("result = OrdinaryReintegrationUnverified;", surrender[check..winner]);
        Assert.Contains("return false;", surrender[check..winner]);

        string resolver = source[source.IndexOf("private bool ResolveCivilWar(", StringComparison.Ordinal)
            ..source.IndexOf("private void ApplyNpcMercyJudgment(", StringComparison.Ordinal)];
        int phase = resolver.IndexOf("movement.ExecutionPhase = \"resolving_reunification\"", StringComparison.Ordinal);
        int restore = resolver.IndexOf("if (!RestoreCommittedClans(movement, parent, rebels)) return false;", StringComparison.Ordinal);
        int judgment = resolver.IndexOf("movement.Stage = \"judgment\"", StringComparison.Ordinal);
        Assert.True(phase >= 0 && restore > phase && judgment > restore);
        Assert.Contains("OrdinaryResolutionInProgress(movement)) return false;", resolver[..phase]);
        Assert.Contains("if (clan.Kingdom != destination)", resolver);
        Assert.Contains("clan_reintegration_unverified:", resolver);

        string ruler = source[source.IndexOf("internal void CompleteRulerJudgment(", StringComparison.Ordinal)
            ..source.IndexOf("private void OnHeroPrisonerTaken(", StringComparison.Ordinal)];
        Assert.Contains("if (OrdinaryResolutionInProgress(movement)) throw", ruler);
        Assert.Contains("if (!ResolveCivilWar(movement, false, \"ruler_judgment\"))", ruler);
    }

    [Fact]
    public void ForeignClanNativeFixtureChecksHeldAndCompletedSurrenderReplays()
    {
        string root = TestOptions.FindWorkspace();
        string source = File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/Rebellion/Campaign/ReignRebellionCampaignBehavior.PreparationHarness.cs"));
        Assert.Contains("movement.ExecutionPhase = \"resolving_reunification\";", source);
        Assert.Contains("heldSurrenderRejected = !TrySurrender(challengedRuler, movement.MovementId, out _)", source);
        Assert.Contains("movement.ExecutionPhase = originalPhase;", source);
        Assert.Contains("bool completedSurrenderReplayRejected = resolved", source);
        Assert.Contains("check(\"reunification_exactly_once\", heldSurrenderRejected && completedSurrenderReplayRejected", source);
    }

    [Fact]
    public void OutbreakReloadRequiresNativeResultForEachDistinctStage()
    {
        static JsonElement Report(string profile, object result, string status = "completed") =>
            JsonSerializer.SerializeToElement(new
            {
                status = "completed",
                commands = new[] { new { operation = "rebellion_test", profile, caseId = "RB-NATIVE-004", status, result } }
            });
        var prepared = new { passed = true, nativeBoundary = new { movementId = "real-movement", rebelRealmId = "observed-realm" } };
        var restarted = new
        {
            passed = true, nativeBoundary = new { movementId = "real-movement", rebelRealmId = "observed-realm" },
            separateNativeProcess = true, retainedHold = true, sameRealmFounderAndAllegiance = true,
            replayPublishedNothing = true
        };
        Assert.Equal("real-movement", TestingTools.ReadRebellionOutbreakReportMovement(Report("outbreak_reload_prepare", prepared), "prepare"));
        Assert.Equal("real-movement", TestingTools.ReadRebellionOutbreakReportMovement(Report("outbreak_reload_verify", restarted), "verify"));
        Assert.Empty(TestingTools.ReadRebellionOutbreakReportMovement(Report("outbreak_reload_prepare", prepared), "verify"));
        Assert.Empty(TestingTools.ReadRebellionOutbreakReportMovement(Report("outbreak_reload_verify", prepared), "verify"));
        Assert.Empty(TestingTools.ReadRebellionOutbreakReportMovement(Report("outbreak_reload_verify", restarted, "failed"), "verify"));
        Assert.Empty(TestingTools.ReadRebellionOutbreakReportMovement(JsonSerializer.SerializeToElement(new { result = restarted }), "verify"));
    }

    [Fact]
    public void OutbreakReloadFixtureStopsOnlyAfterObservedNativeFounder()
    {
        string workspace = TestOptions.FindWorkspace();
        string source = File.ReadAllText(Path.Combine(workspace,
            "ReignBeta/src/Modules/Rebellion/Campaign/ReignRebellionCampaignBehavior.CivilWar.cs"));
        int create = source.IndexOf("rebels = CreateRebelKingdom(", StringComparison.Ordinal);
        Assert.True(create >= 0);
        int verify = source.IndexOf("if (!HasEstablishedOutbreakAuthority(", create, StringComparison.Ordinal);
        int callback = source.IndexOf("verifiedFounderFixture(movement, rebels);", create, StringComparison.Ordinal);
        int hold = source.IndexOf("HoldOutbreak(movement, \"fixture_verified_founder_interruption\");", create, StringComparison.Ordinal);
        int transfer = source.IndexOf("var rebelMembers", create, StringComparison.Ordinal);
        Assert.True(verify > create && callback > verify && hold > callback && transfer > hold);
        string host = File.ReadAllText(Path.Combine(workspace,
            "ReignBeta/src/Modules/Rebellion/Testing/ReignRebellionPreparationTestHost.cs"));
        Assert.Contains("ReignSaveSyncCoordinator.CurrentVerifiedLoad", host, StringComparison.Ordinal);
        Assert.Contains("NativeCheckpointHash(save) != hash", host, StringComparison.Ordinal);
        Assert.Contains("if (!verifying)", host, StringComparison.Ordinal);
        Assert.Contains("Outbreak preparation is held pending independent operator authority.", host, StringComparison.Ordinal);
        string fixture = File.ReadAllText(Path.Combine(workspace,
            "ReignBeta/src/Modules/Rebellion/Campaign/ReignRebellionCampaignBehavior.PreparationHarness.cs"));
        Assert.Contains("retained.Value<string>(\"fromProcess\") == process", fixture, StringComparison.Ordinal);
        Assert.Contains("retained.Value<string>(\"fromPoint\") == point", fixture, StringComparison.Ordinal);
    }

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
        var outbreakReload = Assert.Single(document.GetProperty("nativeCases").EnumerateArray(),
            item => item.GetProperty("id").GetString() == "RB-NATIVE-004");
        Assert.Equal(new[] { "prepare", "verify" }, outbreakReload.GetProperty("stages")
            .EnumerateArray().Select(stage => stage.GetString()).ToArray());
        Assert.False(outbreakReload.GetProperty("required").GetBoolean());
        Assert.Equal("independent_operator_authority_required", outbreakReload.GetProperty("prepareAdmission").GetString());
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
    public void Native_outbreak_revalidates_authority_and_allegiance_at_callback_boundaries()
    {
        string source = File.ReadAllText(TestSourceLocator.Unique(Path.Combine(TestOptions.FindWorkspace(),
            "ReignBeta"), "ReignRebellionCampaignBehavior.CivilWar.cs", "/src/Modules/Rebellion/Campaign/"));
        int move = source.IndexOf("if (clan.Kingdom == parent) MoveClanToKingdom(clan, rebels);",
            StringComparison.Ordinal);
        Assert.True(move >= 0);
        int stamp = source.IndexOf("member.LastSwitchDay = CurrentDay();", move, StringComparison.Ordinal);
        Assert.True(stamp > move);
        string postTransfer = source[move..stamp];
        Assert.Contains("if (!HasEstablishedOutbreakAuthority(parent, challengedRuler, leaderClan, founder, rebels))",
            postTransfer, StringComparison.Ordinal);
        Assert.Contains("HoldOutbreak(movement, \"outbreak_authority_changed\");", postTransfer, StringComparison.Ordinal);
        Assert.Contains("verifiedClans.Add(clan);", postTransfer, StringComparison.Ordinal);
        Assert.Contains("if (!HasOutbreakAllegiance(verifiedClans, rebels))", postTransfer, StringComparison.Ordinal);
        Assert.Contains("HoldOutbreak(movement, step);", postTransfer, StringComparison.Ordinal);
        Assert.Contains("rebels.RulingClan == leaderClan", source, StringComparison.Ordinal);
        Assert.Contains("FindClan(clan.StringId) == clan", source, StringComparison.Ordinal);

        int creationStart = source.IndexOf("private Kingdom CreateRebelKingdom(", StringComparison.Ordinal);
        Assert.True(creationStart >= 0);
        int creationEnd = source.IndexOf("private void ReconcileCivilWar(", creationStart, StringComparison.Ordinal);
        Assert.True(creationEnd > creationStart);
        string creation = source[creationStart..creationEnd];
        Assert.DoesNotContain(".RulingClan =", creation, StringComparison.Ordinal);

        const string invokeNative = "ChangeKingdomAction.ApplyByCreateKingdom(leaderClan, rebels, true);";
        const string verifyFounder = "if (!HasEstablishedOutbreakAuthority(parent, challengedRuler, leaderClan, founder, rebels)) return null;";
        int founderInstall = creation.IndexOf(invokeNative, StringComparison.Ordinal);
        Assert.True(founderInstall >= 0);
        int inheritPolicies = creation.IndexOf("foreach (PolicyObject policy", founderInstall, StringComparison.Ordinal);
        Assert.True(inheritPolicies > founderInstall);
        Assert.Equal(verifyFounder, creation[(founderInstall + invokeNative.Length)..inheritPolicies].Trim());
        int policyWrite = creation.IndexOf("rebels.AddPolicy(policy);", inheritPolicies, StringComparison.Ordinal);
        Assert.True(policyWrite > inheritPolicies);
        Assert.Contains(verifyFounder, creation[inheritPolicies..policyWrite], StringComparison.Ordinal);
        Assert.Contains("return rebels;", creation[policyWrite..], StringComparison.Ordinal);
    }

    [Fact]
    public void Native_host_runs_distinct_profile_fixtures_and_blocks_post_report_recruitment()
    {
        string workspace = TestOptions.FindWorkspace();
        string host = File.ReadAllText(TestSourceLocator.Unique(Path.Combine(workspace, "ReignBeta"),
            "ReignRebellionPreparationTestHost.cs", "/src/Modules/Rebellion/Testing/"));
        string behavior = TestSourceLocator.PartialTypeText(TestSourceLocator.Unique(Path.Combine(workspace, "ReignBeta"),
            "ReignRebellionCampaignBehavior.cs", "/src/Modules/Rebellion/Campaign/"));
        string correspondence = File.ReadAllText(TestSourceLocator.Unique(Path.Combine(workspace, "ReignBeta"),
            "ReignLiveInteractionCorrespondenceHost.cs", "/src/Modules/Dialogue/Campaign/"));
        string liveServer = TestSourceLocator.ServerFileGroupText(TestSourceLocator.Unique(Path.Combine(workspace, "ReignServer"),
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
        string server = TestSourceLocator.ServerFileGroupText(TestSourceLocator.Unique(
            Path.Combine(workspace, "ReignServer"), "RebellionDirector.cs",
            "/src/Modules/Rebellion/"));
        string prompt = TestSourceLocator.ServerFileGroupText(TestSourceLocator.Unique(
            Path.Combine(workspace, "ReignServer"), "PromptCaching.cs",
            "/src/Modules/Dialogue/"));
        string client = TestSourceLocator.PartialTypeText(TestSourceLocator.Unique(
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
