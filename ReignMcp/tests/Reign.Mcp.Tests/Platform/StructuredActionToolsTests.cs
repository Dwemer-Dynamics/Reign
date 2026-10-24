using System.Text.Json;
using Reign.Mcp.Server;

namespace Reign.Mcp.Tests;

public sealed class StructuredActionToolsTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(80)]
    public void AcceptedGuideStepKeepsExactTermsAndRequiresRetainedRecovery(int length)
    {
        string agreement = new('a', length), clause = "guide_clause-1";
        string json = GuideStepCase(JsonSerializer.Serialize(new { agreementId = agreement, clauseId = clause }));
        var parsed = Assert.Single(StructuredActionTools.ParseCases(json));
        Assert.Equal(agreement, parsed.GetProperty("arguments").GetProperty("agreementId").GetString());
        Assert.Equal(clause, parsed.GetProperty("arguments").GetProperty("clauseId").GetString());
        Assert.True(StructuredActionTools.RequiresRecoveryCheckpoint(parsed));
        var status = Assert.Single(StructuredActionTools.ParseCases(json.Replace("\"phase\":\"fixture\"", "\"phase\":\"status\"")));
        Assert.False(StructuredActionTools.RequiresRecoveryCheckpoint(status));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("coordinates")]
    [InlineData("destination")]
    [InlineData("nonstring")]
    [InlineData("null")]
    [InlineData("blank")]
    [InlineData("leading_space")]
    [InlineData("trailing_space")]
    [InlineData("oversized")]
    [InlineData("unicode")]
    [InlineData("slash")]
    [InlineData("duplicate")]
    public void AcceptedGuideStepRejectsAmbiguousTermsAndArbitraryMovement(string mutation)
    {
        var args = new Dictionary<string, object?> { ["agreementId"] = "accepted_guide", ["clauseId"] = "clause_1" };
        if (mutation == "missing") args.Remove("agreementId");
        if (mutation == "coordinates") args["x"] = "123";
        if (mutation == "destination") args["destinationId"] = "lordshall";
        if (mutation == "nonstring") args["agreementId"] = 1;
        if (mutation == "null") args["agreementId"] = null;
        if (mutation == "blank") args["agreementId"] = "";
        if (mutation == "leading_space") args["agreementId"] = " accepted_guide";
        if (mutation == "trailing_space") args["agreementId"] = "accepted_guide ";
        if (mutation == "oversized") args["agreementId"] = new string('a', 81);
        if (mutation == "unicode") args["agreementId"] = "guide_é";
        if (mutation == "slash") args["agreementId"] = "guide/1";
        string json = JsonSerializer.Serialize(args);
        if (mutation == "duplicate") json = json.Replace("\"clauseId\":", "\"agreementId\":");
        Assert.Throws<ArgumentException>(() => StructuredActionTools.ParseCases(GuideStepCase(json)));
    }

    private static string GuideStepCase(string args) => "[{\"caseId\":\"guide_step\",\"phase\":\"fixture\",\"kind\":\"fixture\",\"operationId\":\"guide_step_1\",\"actionId\":\"fixture.follow_accepted_guide_step\",\"arguments\":" + args + "}]";

    [Theory]
    [InlineData(1)]
    [InlineData(80)]
    [InlineData(128)]
    public void Pregnancy_discovery_preserves_one_bounded_exact_counterpart(int length)
    {
        string counterpart = new('h', length);
        string json = JsonSerializer.Serialize(new[] { new { caseId = "pregnancy", phase = "entities",
            entityKind = "pregnancy", ownerHeroId = "main_hero", counterpartHeroId = counterpart } });
        var parsed = Assert.Single(StructuredActionTools.ParseCases(json));
        Assert.Equal(counterpart, parsed.GetProperty("counterpartHeroId").GetString());
        Assert.False(StructuredActionTools.RequiresRecoveryCheckpoint(parsed));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("blank")]
    [InlineData("leading_space")]
    [InlineData("trailing_space")]
    [InlineData("oversized")]
    [InlineData("nonstring")]
    [InlineData("null")]
    [InlineData("invalid_identifier")]
    public void Pregnancy_discovery_refuses_ambiguous_counterparts(string mutation)
    {
        var request = new Dictionary<string, object?> { ["caseId"] = "pregnancy", ["phase"] = "entities",
            ["entityKind"] = "pregnancy", ["ownerHeroId"] = "main_hero", ["counterpartHeroId"] = "mother_1" };
        if (mutation == "missing") request.Remove("counterpartHeroId");
        if (mutation == "blank") request["counterpartHeroId"] = " ";
        if (mutation == "leading_space") request["counterpartHeroId"] = " mother_1";
        if (mutation == "trailing_space") request["counterpartHeroId"] = "mother_1 ";
        if (mutation == "oversized") request["counterpartHeroId"] = new string('h', 129);
        if (mutation == "nonstring") request["counterpartHeroId"] = 1;
        if (mutation == "null") request["counterpartHeroId"] = null;
        if (mutation == "invalid_identifier") request["counterpartHeroId"] = "mother/1";
        Assert.Throws<ArgumentException>(() => StructuredActionTools.ParseCases(JsonSerializer.Serialize(new[] { request })));
    }

    [Theory]
    [InlineData("entities", "hero")]
    [InlineData("catalog", "pregnancy")]
    [InlineData("status", "pregnancy")]
    public void Counterparts_cannot_change_another_discovery_or_action_phase(string phase, string entityKind)
    {
        string json = JsonSerializer.Serialize(new[] { new { caseId = "scope", phase, entityKind,
            ownerHeroId = "main_hero", counterpartHeroId = "mother_1", kind = "agreement", operationId = "saved_agreement" } });
        var error = Assert.Throws<ArgumentException>(() => StructuredActionTools.ParseCases(json));
        Assert.Contains("only for pregnancy entity discovery", error.Message);
    }

    [Theory]
    [InlineData("fixture.government_capital_visit")]
    [InlineData("fixture.government_action_composer")]
    public void GovernmentPlayerFixturesKeepExactTargetsAndRequireRecovery(string actionId)
    {
        var args = GovernmentArguments(actionId);
        var parsed = Assert.Single(StructuredActionTools.ParseCases(GovernmentCase(actionId, JsonSerializer.Serialize(args))));
        Assert.True(StructuredActionTools.RequiresRecoveryCheckpoint(parsed));
        foreach (var pair in args) Assert.Equal(pair.Value, parsed.GetProperty("arguments").GetProperty(pair.Key).GetString());
        string status = GovernmentCase(actionId, JsonSerializer.Serialize(args)).Replace("\"phase\":\"fixture\"", "\"phase\":\"status\"");
        Assert.False(StructuredActionTools.RequiresRecoveryCheckpoint(Assert.Single(StructuredActionTools.ParseCases(status))));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("extra")]
    [InlineData("nonstring")]
    [InlineData("blank")]
    [InlineData("oversized")]
    [InlineData("duplicate")]
    public void GovernmentPlayerFixturesRejectAmbiguousOrUnboundedInput(string mutation)
    {
        foreach (string actionId in new[] { "fixture.government_capital_visit", "fixture.government_action_composer" })
        {
            var args = GovernmentArguments(actionId);
            string key = args.Keys.First(), second = args.Keys.Skip(1).First();
            if (mutation == "missing") args.Remove(key);
            if (mutation == "extra") args["method"] = "Apply";
            if (mutation == "nonstring") args[key] = 1;
            if (mutation == "blank") args[key] = " ";
            if (mutation == "oversized") args[key] = new string('x', 81);
            string json = JsonSerializer.Serialize(args);
            if (mutation == "duplicate") json = json.Replace("\"" + key + "\":", "\"" + second + "\":");
            Assert.Throws<ArgumentException>(() => StructuredActionTools.ParseCases(GovernmentCase(actionId, json)));
        }
    }

    private static Dictionary<string, object> GovernmentArguments(string actionId) => actionId == "fixture.government_capital_visit"
        ? new() { ["expectedKingdomId"] = "empire", ["expectedCapitalSettlementId"] = "town_EN2", ["expectedOwnerClanId"] = "clan_1" }
        : new() { ["expectedHeroId"] = "member_1", ["businessId"] = "government_business_1" };
    private static string GovernmentCase(string actionId, string args) => "[{\"caseId\":\"government\",\"phase\":\"fixture\",\"kind\":\"fixture\",\"operationId\":\"government_1\",\"actionId\":\"" + actionId + "\",\"arguments\":" + args + "}]";

    [Theory]
    [InlineData("0", "other", true)] [InlineData("7", "other", true)]
    [InlineData("8", "other", false)] [InlineData("-1", "other", false)]
    [InlineData("3", "", false)] [InlineData("3", "actor", false)]
    public void News_fixture_rejects_unbounded_samples_and_ambiguous_principals(string amount, string otherHeroId, bool allowed)
    {
        var args = new { profile = "news", heroId = "actor", settlementId = "capital", otherHeroId, amount };
        string json = JsonSerializer.Serialize(new[] { new { caseId = "news", phase = "fixture", kind = "fixture",
            actionId = "fixture.ruler_setup", operationId = "news_1", arguments = args } });
        if (allowed) Assert.Single(StructuredActionTools.ParseCases(json));
        else Assert.Throws<ArgumentException>(() => StructuredActionTools.ParseCases(json));
    }

    [Theory]
    [InlineData("main_hero", "", "0", true)]
    [InlineData("main_hero", "", "1", true)]
    [InlineData("main_hero", "", "2", false)]
    [InlineData("main_hero", "", "-1", false)]
    [InlineData("main_hero", "other", "1", false)]
    [InlineData("main_hero", " ", "1", false)]
    [InlineData("other", "", "1", false)]
    [InlineData("", "", "1", false)]
    public void Ruler_mortality_probe_requires_exact_main_hero_and_boolean_amount(string heroId, string otherHeroId, string amount, bool allowed)
    {
        var args = new { profile = "mortality", heroId, settlementId = "capital", otherHeroId, amount };
        string json = JsonSerializer.Serialize(new[] { new { caseId = "mortality", phase = "fixture", kind = "fixture",
            actionId = "fixture.ruler_setup", operationId = "mortality_1", arguments = args } });
        if (allowed) Assert.Single(StructuredActionTools.ParseCases(json));
        else Assert.Throws<ArgumentException>(() => StructuredActionTools.ParseCases(json));
    }

    [Theory]
    [InlineData("prison_guard", "1", "captive", true)]
    [InlineData("prison_guard", "0", "captive", false)]
    [InlineData("prison_guard", "2", "captive", false)]
    [InlineData("prison_guard", "1", "", false)]
    [InlineData("prison_guard", "1", "actor", false)]
    [InlineData("ransom_offer", "1", "payer", true)]
    [InlineData("ransom_offer", "4000", "payer", true)]
    [InlineData("ransom_offer", "0", "payer", false)]
    [InlineData("barter_release", "1000", "payer", true)]
    [InlineData("barter_transfer", "1000", "payer", true)]
    [InlineData("barter_transfer", "4001", "payer", false)]
    [InlineData("barter_transfer", "-1", "payer", false)]
    [InlineData("barter_release", "1000", "actor", false)]
    [InlineData("barter_release", "1000", " ", false)]
    public void Ruler_prisoner_fixtures_reject_unsafe_roles_and_amounts(string profile, string amount, string otherHeroId, bool allowed)
    {
        var args = new { profile, heroId = "actor", settlementId = "capital", otherHeroId, amount };
        string json = JsonSerializer.Serialize(new[] { new { caseId = "prisoner", phase = "fixture", kind = "fixture",
            actionId = "fixture.ruler_setup", operationId = "prisoner_1", arguments = args } });
        if (allowed) Assert.Single(StructuredActionTools.ParseCases(json));
        else Assert.Throws<ArgumentException>(() => StructuredActionTools.ParseCases(json));
    }

    [Theory]
    [InlineData("party", "0", true)]
    [InlineData("party", "750", true)]
    [InlineData("party", "4000", true)]
    [InlineData("party", "4001", false)]
    [InlineData("party", "-1", false)]
    [InlineData("snapshot", "0", true)]
    [InlineData("snapshot", "1", false)]
    [InlineData("arbitrary", "0", false)]
    public void Ruler_roster_and_observation_fixtures_keep_their_exact_bounds(string profile, string amount, bool allowed)
    {
        var args = new { profile, heroId = "ruler", settlementId = "capital", otherHeroId = "", amount };
        var json = JsonSerializer.Serialize(new[] { new { caseId = "bounded", phase = "fixture", kind = "fixture",
            actionId = "fixture.ruler_setup", operationId = "bounded_1", arguments = args } });
        if (allowed) Assert.Single(StructuredActionTools.ParseCases(json));
        else Assert.Throws<ArgumentException>(() => StructuredActionTools.ParseCases(json));
    }
    private static (CampaignTestState State, ControllerCall Snapshot) PausedActionSnapshot(
        bool safeSettlement, string mutation = "")
    {
        var state = new CampaignTestState { CampaignId = "campaign", TimelineId = "timeline",
            BaselineSaveName = "BaseThree", CurrentSaveName = "ReignTest_Actions_Current", Armed = true,
            Checkpoints = ["ReignTest_Actions_Current", "ReignTest_Actions_Milestone"] };
        var data = new Dictionary<string, object?> { ["ok"] = true, ["campaignId"] = state.CampaignId,
            ["timelineId"] = state.TimelineId, ["activeSaveName"] = state.CurrentSaveName,
            ["saveSyncReady"] = true, ["saveSyncAlignmentPending"] = false,
            ["safeSettlement"] = safeSettlement, ["timeMode"] = "Stop" };
        switch (mutation)
        {
            case "baseline": data["activeSaveName"] = state.BaselineSaveName; break;
            case "foreign_save": data["activeSaveName"] = "ReignTest_Foreign_Current"; break;
            case "milestone": data["activeSaveName"] = state.Checkpoints[1]; break;
            case "campaign": data["campaignId"] = "foreign"; break;
            case "timeline": data["timelineId"] = "foreign"; break;
            case "unaligned": data["saveSyncAlignmentPending"] = true; break;
            case "sync_not_ready": data["saveSyncReady"] = false; break;
            case "missing_time": data.Remove("timeMode"); break;
            case "advancing": data["timeMode"] = "StoppableFastForward"; break;
            case "unknown_time": data["timeMode"] = "stop"; break;
            case "failed_snapshot": data["ok"] = false; break;
        }
        var snapshot = ControllerCall.Create(new ProcessResult { Ok = true, ExitCode = 0, Command = "fixture",
            WorkingDirectory = "fixture", DurationMs = 0, TimedOut = false, OutputTruncated = false,
            StandardOutput = JsonSerializer.Serialize(data), StandardError = "" });
        return (state, snapshot);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PausedActionsAllowFieldFixturesWithoutRelaxingAutonomousAdvancement(bool safeSettlement)
    {
        var (state, snapshot) = PausedActionSnapshot(safeSettlement);
        CampaignTestService.ValidatePausedActionSnapshot(state, snapshot);
        if (safeSettlement) CampaignTestService.ValidateSnapshot(state, snapshot, allowBaseline: false);
        else Assert.Throws<InvalidOperationException>(() =>
            CampaignTestService.ValidateSnapshot(state, snapshot, allowBaseline: false));
    }

    [Theory]
    [InlineData("baseline")]
    [InlineData("foreign_save")]
    [InlineData("milestone")]
    [InlineData("campaign")]
    [InlineData("timeline")]
    [InlineData("unaligned")]
    [InlineData("sync_not_ready")]
    [InlineData("missing_time")]
    [InlineData("advancing")]
    [InlineData("unknown_time")]
    [InlineData("failed_snapshot")]
    public void PausedActionsRejectUnownedUnalignedOrMovingSnapshots(string mutation)
    {
        var (state, snapshot) = PausedActionSnapshot(false, mutation);
        Assert.Throws<InvalidOperationException>(() => CampaignTestService.ValidatePausedActionSnapshot(state, snapshot));
    }

    private static string PlayerEncounterCase(string args) => "[{\"caseId\":\"entry\",\"phase\":\"fixture\",\"kind\":\"fixture\",\"operationId\":\"entry_1\",\"actionId\":\"fixture.player_settlement_encounter\",\"arguments\":" + args + "}]";
    private static Dictionary<string, object> PlayerEncounterArguments() => new()
    {
        ["settlementId"] = "town_1", ["expectedOwnerClanId"] = "clan_1", ["expectedBesiegerPartyId"] = "enemy_party"
    };
    [Fact] public void PlayerEncounterFixturePreservesExactTargetOwnerAndBesieger()
    {
        var parsed = Assert.Single(StructuredActionTools.ParseCases(PlayerEncounterCase(JsonSerializer.Serialize(PlayerEncounterArguments()))));
        Assert.Equal("town_1", parsed.GetProperty("arguments").GetProperty("settlementId").GetString());
        Assert.Equal("clan_1", parsed.GetProperty("arguments").GetProperty("expectedOwnerClanId").GetString());
        Assert.Equal("enemy_party", parsed.GetProperty("arguments").GetProperty("expectedBesiegerPartyId").GetString());
    }
    [Theory]
    [InlineData("missing")]
    [InlineData("extra")]
    [InlineData("nonstring")]
    [InlineData("blank")]
    [InlineData("oversized")]
    [InlineData("player")]
    [InlineData("duplicate")]
    public void PlayerEncounterFixtureRejectsUnsafeIdentifiers(string mutation)
    {
        var args = PlayerEncounterArguments();
        if (mutation == "missing") args.Remove("settlementId");
        if (mutation == "extra") args["method"] = "Finish";
        if (mutation == "nonstring") args["settlementId"] = 1;
        if (mutation == "blank") args["settlementId"] = " ";
        if (mutation == "oversized") args["settlementId"] = new string('x', 81);
        if (mutation == "player") args["expectedBesiegerPartyId"] = "player_party";
        string json = JsonSerializer.Serialize(args);
        if (mutation == "duplicate") json = json.Replace("\"settlementId\":\"town_1\"", "\"expectedOwnerClanId\":\"other\"");
        Assert.Throws<ArgumentException>(() => StructuredActionTools.ParseCases(PlayerEncounterCase(json)));
    }
    private static string SiegeCase(string args) => "[{\"caseId\":\"siege\",\"phase\":\"fixture\",\"kind\":\"fixture\",\"operationId\":\"siege_1\",\"actionId\":\"fixture.siege_setup\",\"arguments\":" + args + "}]";
    private static Dictionary<string, object> SiegeArguments() => new()
    {
        ["attackerPartyId"] = "npc_party", ["settlementId"] = "town_1",
        ["expectedOwnerClanId"] = "clan_1", ["expectedAttackerFactionId"] = "kingdom_2"
    };

    [Fact] public void SiegeFixturePreservesExactIdentifiers()
    {
        var parsed = Assert.Single(StructuredActionTools.ParseCases(SiegeCase(JsonSerializer.Serialize(SiegeArguments()))));
        Assert.Equal("kingdom_2", parsed.GetProperty("arguments").GetProperty("expectedAttackerFactionId").GetString());
    }
    [Theory]
    [InlineData("missing")]
    [InlineData("extra")]
    [InlineData("nonstring")]
    [InlineData("blank")]
    [InlineData("oversized")]
    [InlineData("player")]
    [InlineData("duplicate")]
    public void SiegeFixtureRejectsUnsafeIdentifiers(string mutation)
    {
        var args = SiegeArguments();
        if (mutation == "missing") args.Remove("settlementId");
        if (mutation == "extra") args["property"] = "Position";
        if (mutation == "nonstring") args["settlementId"] = 1;
        if (mutation == "blank") args["settlementId"] = " ";
        if (mutation == "oversized") args["settlementId"] = new string('x', 81);
        if (mutation == "player") args["attackerPartyId"] = "player_party";
        string json = JsonSerializer.Serialize(args);
        if (mutation == "duplicate") json = json.Replace("\"settlementId\":\"town_1\"", "\"attackerPartyId\":\"other\"");
        Assert.Throws<ArgumentException>(() => StructuredActionTools.ParseCases(SiegeCase(json)));
    }
    [Theory]
    [InlineData("valid")]
    [InlineData("unarmed")]
    [InlineData("wrongRun")]
    [InlineData("baseline")]
    [InlineData("outsideNamespace")]
    [InlineData("unretained")]
    [InlineData("unsaved")]
    [InlineData("failedSave")]
    [InlineData("otherSave")]
    public void SiegeRecoveryGateRequiresSuccessfulExactOwnedCheckpoint(string mutation)
    {
        var state = new CampaignTestState { RunId = "run", CampaignId = "campaign", TimelineId = "timeline",
            Armed = true, SavePrefix = "ReignTest_task", CurrentSaveName = "ReignTest_task_Current", BaselineSaveName = "BaseThree" };
        state.Checkpoints.Add(state.CurrentSaveName);
        state.Events.Add(new CampaignTestEvent { Operation = "checkpoint", Evidence = new
        {
            ok = mutation != "failedSave", activeSaveName = mutation == "otherSave" ? "other" : state.CurrentSaveName
        } });
        var auth = CampaignTestAuthorization.From(state);
        if (mutation == "unarmed") state.Armed = false;
        if (mutation == "wrongRun") auth = auth with { CampaignTestRunId = "other" };
        if (mutation == "baseline") state.BaselineSaveName = state.CurrentSaveName;
        if (mutation == "outsideNamespace") state.CurrentSaveName = "Other_Current";
        if (mutation == "unretained") state.Checkpoints.Clear();
        if (mutation == "unsaved") state.Events.Clear();
        if (mutation == "valid") CampaignTestService.RequireRetainedRecoveryCheckpoint(state, auth);
        else Assert.Throws<InvalidOperationException>(() => CampaignTestService.RequireRetainedRecoveryCheckpoint(state, auth));
    }
    [Theory]
    [InlineData("0.05")]
    [InlineData("0.75")]
    public void ShipFixturePreservesExactInputsAndBoundaries(string ratio)
    {
        var row = new { caseId = "damage", phase = "fixture", kind = "fixture", operationId = "damage_1",
            actionId = "fixture.ship_damage", arguments = new { shipId = "ship:42:hull", ownerPartyId = "party_1",
                expectedHitPoints = "123.5", remainingRatio = ratio } };
        var parsed = Assert.Single(StructuredActionTools.ParseCases(JsonSerializer.Serialize(new[] { row })));
        Assert.Equal("123.5", parsed.GetProperty("arguments").GetProperty("expectedHitPoints").GetString());
        Assert.Equal(ratio, parsed.GetProperty("arguments").GetProperty("remainingRatio").GetString());
    }
    [Theory]
    [InlineData("NaN", "0.5", "fixture", "fixture.ship_damage")]
    [InlineData("Infinity", "0.5", "fixture", "fixture.ship_damage")]
    [InlineData("0", "0.5", "fixture", "fixture.ship_damage")]
    [InlineData("100", "0.049", "fixture", "fixture.ship_damage")]
    [InlineData("100", "0.751", "fixture", "fixture.ship_damage")]
    [InlineData("100", "NaN", "fixture", "fixture.ship_damage")]
    [InlineData("100", "0.5", "run", "fixture.ship_damage")]
    [InlineData("100", "0.5", "fixture", "fixture.any_property")]
    public void InvalidShipFixturesCannotReachCampaignControl(string expected, string ratio, string phase, string actionId)
    {
        var row = new { caseId = "damage", phase, kind = "fixture", operationId = "damage_1", actionId,
            arguments = new { shipId = "ship:42:hull", ownerPartyId = "party_1", expectedHitPoints = expected, remainingRatio = ratio } };
        Assert.Throws<ArgumentException>(() => StructuredActionTools.ParseCases(JsonSerializer.Serialize(new[] { row })));
    }
    [Theory]
    [InlineData("{\"shipId\":\"ship\",\"ownerPartyId\":\"party\",\"expectedHitPoints\":\"100\"}")]
    [InlineData("{\"shipId\":\"ship\",\"ownerPartyId\":\"party\",\"expectedHitPoints\":\"100\",\"remainingRatio\":\"0.5\",\"property\":\"Gold\"}")]
    [InlineData("{\"shipId\":\"ship\",\"ownerPartyId\":\"party\",\"expectedHitPoints\":100,\"remainingRatio\":\"0.5\"}")]
    [InlineData("{\"shipId\":\"ship\",\"ownerPartyId\":\"party\",\"expectedHitPoints\":\"100\",\"expectedHitPoints\":\"100\"}")]
    public void ShipFixtureRefusesMissingDuplicateExtraOrUntypedArguments(string args)
        => Assert.Throws<ArgumentException>(() => StructuredActionTools.ParseCases(
            "[{\"caseId\":\"damage\",\"phase\":\"fixture\",\"kind\":\"fixture\",\"operationId\":\"op\",\"actionId\":\"fixture.ship_damage\",\"arguments\":" + args + "}]"));
    [Fact] public void SiegeAndCombatCatalogRetainsBoundsRecoveryAndOperatorAuthority()
    {
        string root = TestOptions.FindWorkspace();
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reign.testing.json")));
        var fixture = doc.RootElement.GetProperty("structuredActionAgreements").GetProperty("siegeSetupFixture");
        Assert.Equal(4, fixture.GetProperty("arguments").GetArrayLength());
        Assert.Contains("drained Current checkpoint", fixture.GetProperty("isolation").GetString());
        Assert.Contains("restore", fixture.GetProperty("cleanup").GetString());
        var playerFixture = doc.RootElement.GetProperty("structuredActionAgreements").GetProperty("playerSettlementEncounterFixture");
        Assert.Equal(3, playerFixture.GetProperty("arguments").GetArrayLength());
        Assert.Contains("retained recovery checkpoint", playerFixture.GetProperty("isolation").GetString());
        Assert.Contains("restore", playerFixture.GetProperty("cleanup").GetString());
        var combat = doc.RootElement.GetProperty("nativeActionConsole").GetProperty("combatObservations");
        Assert.Equal(64, combat.GetProperty("participantLimit").GetInt32());
        Assert.Contains("No mutation", combat.GetProperty("authority").GetString());
        foreach (string path in new[] { "docs/agent/testing/FEATURE_HARNESSES.md", "ReignMcp/docs/tool-catalog.md", "ReignMcp/docs/security-model.md" })
        {
            string text = File.ReadAllText(Path.Combine(root, path));
            Assert.Contains(fixture.GetProperty("schema").GetString()!, text);
            Assert.Contains(playerFixture.GetProperty("schema").GetString()!, text);
            Assert.Contains(combat.GetProperty("schema").GetString()!, text);
        }
    }
    [Fact] public void NativeObservationContractNamesDistinctGoldAndClanHolders()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(TestOptions.FindWorkspace(), "reign.testing.json")));
        string observations = doc.RootElement.GetProperty("nativeActionConsole").GetProperty("observations").GetString()!;
        Assert.Contains("PartyTradeGold", observations);
        Assert.Contains("settlement-component Gold", observations);
        Assert.Contains("Influence and Renown", observations);
        foreach (string state in new[] { "IsFugitive", "IsTraveling", "IsReleased", "DeathMark", "IsPregnant", "HeroState", "IsInRaftState", "MaxHitPoints", "IsWaitingForDisband" })
        {
            Assert.Contains(state, observations);
            Assert.Contains("\"" + state + "\"", File.ReadAllText(Path.Combine(TestOptions.FindWorkspace(),
                "ReignBeta/src/Modules/WorldSimulation/Actions/ReignNativeActionConsole.cs")));
        }
    }
    [Fact] public void NativeRomanceAndTributeObservationsRetainScopeBoundsAndAuthority()
    {
        string root = TestOptions.FindWorkspace();
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reign.testing.json")));
        var row = doc.RootElement.GetProperty("nativeActionConsole").GetProperty("relationshipAndTributeObservations");
        const string schema = "reign-native-relationship-tribute-observations-v1";
        Assert.Equal(schema, row.GetProperty("schema").GetString());
        Assert.Equal(32, row.GetProperty("romanceRowLimit").GetInt32());
        Assert.Contains("both identified heroes", row.GetProperty("scope").GetString());
        Assert.Contains("Duplicate native records", row.GetProperty("scope").GetString());
        Assert.Contains("never inferred zero", row.GetProperty("tribute").GetString());
        Assert.Contains("no command", row.GetProperty("authority").GetString());
        foreach (string path in new[] { "docs/agent/testing/FEATURE_HARNESSES.md", "ReignMcp/docs/tool-catalog.md", "ReignMcp/docs/security-model.md" })
            Assert.Contains(schema, File.ReadAllText(Path.Combine(root, path)));
    }
    [Fact] public async Task VerificationGatePrecedesAnyApiOrCampaignAccess()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => StructuredActionTools.TestStructuredActions(null!, TestOptions.Create(), null!,
            "campaign", "run", "batch", "[]", "test structured actions on disposable save"));
    }
    [Fact] public async Task MissingConfirmationPrecedesAnyApiOrCampaignAccess()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => StructuredActionTools.TestStructuredActions(null!, TestOptions.Create() with { AllowVerificationControl = true }, null!,
            "campaign", "run", "batch", "[]"));
    }
    [Theory]
    [InlineData("[]")]
    [InlineData("[{\"caseId\":\"a\",\"phase\":\"send\"}]")]
    [InlineData("[{\"caseId\":\"a\",\"phase\":\"catalog\",\"script\":\"no\"}]")]
    [InlineData("[{\"caseId\":\"a\",\"phase\":\"catalog\"},{\"caseId\":\"a\",\"phase\":\"catalog\"}]")]
    [InlineData("[{\"caseId\":\"a\",\"phase\":\"catalog\",\"phase\":\"run\"}]")]
    [InlineData("[{\"caseId\":\"a\",\"phase\":\"run\",\"kind\":\"native\",\"operationId\":\"op\",\"actionId\":\"unregistered\",\"arguments\":{}}]")]
    [InlineData("[{\"caseId\":\"a\",\"phase\":\"run\",\"kind\":\"native\",\"operationId\":\"op\",\"actionId\":\"native.test\",\"arguments\":{\"operation\":\"override\"}}]")]
    public void InvalidCasesCannotReachCampaignControl(string json) => Assert.Throws<ArgumentException>(() => StructuredActionTools.ParseCases(json));

    [Fact] public void RetainsExactAgreementAndNativeValuesWithoutTextInference()
    {
        var cases = StructuredActionTools.ParseCases("[{\"caseId\":\"a\",\"phase\":\"preview\",\"kind\":\"native\",\"operationId\":\"op\",\"actionId\":\"native.test\",\"arguments\":{\"value\":\"100\"}},{\"caseId\":\"b\",\"phase\":\"run\",\"kind\":\"agreement\",\"operationId\":\"b\",\"agreement\":{\"Clauses\":[]}}]");
        Assert.Equal("100", cases[0].GetProperty("arguments").GetProperty("value").GetString());
        Assert.Equal(0, cases[1].GetProperty("agreement").GetProperty("Clauses").GetArrayLength());
    }
    [Fact] public void RefusesOversizedObservationAndBatch()
    {
        string json = JsonSerializer.Serialize(new[] { new { caseId = "a", phase = "catalog", heroIds = Enumerable.Range(0, 17).Select(i => "hero" + i) } });
        Assert.Throws<ArgumentException>(() => StructuredActionTools.ParseCases(json));
        Assert.Throws<ArgumentException>(() => StructuredActionTools.ParseCases(JsonSerializer.Serialize(Enumerable.Range(0, 9).Select(i => new { caseId = "a" + i, phase = "catalog" }))));
    }
    [Fact] public void CatalogDeclaresNoAutomaticSemanticPassOrImplicitConsent()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(TestOptions.FindWorkspace(), "reign.testing.json")));
        var row = doc.RootElement.GetProperty("structuredActionAgreements");
        Assert.Equal("reign_test_structured_actions", row.GetProperty("entry").GetString());
        Assert.Contains("independent native effect", row.GetProperty("evidence").GetString());
        Assert.Contains("operator", row.GetProperty("isolation").GetString());
        var paused = row.GetProperty("pausedActionScenes");
        Assert.Equal("reign-paused-action-scene-authorization-v1", paused.GetProperty("schema").GetString());
        Assert.Equal(new[] { "reign_test_structured_actions", "reign_test_action_conversations" },
            paused.GetProperty("entries").EnumerateArray().Select(x => x.GetString()));
        Assert.Contains("timeMode Stop", paused.GetProperty("scope").GetString());
        Assert.Contains("Autonomous advancement", paused.GetProperty("isolation").GetString());
    }
}
