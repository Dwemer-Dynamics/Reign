using System.Text.Json;
using Reign.Mcp.Server;

namespace Reign.Mcp.Tests;

public sealed class StructuredActionToolsTests
{
    [Fact]
    public void NotableStartupFixtureRequiresOneBoundedTownAndRecoveryCheckpoint()
    {
        const string fixture = """
            {"caseId":"notable_seed","phase":"fixture","kind":"fixture","operationId":"stage_notable",
             "actionId":"fixture.notable_startup_reservation","arguments":{"settlementId":"town_EW2"}}
            """;
        var parsed = Assert.Single(StructuredActionTools.ParseCases("[" + fixture + "]"));
        Assert.True(StructuredActionTools.RequiresRecoveryCheckpoint(parsed));

        string foreignCase = fixture.Replace("notable_seed", "foreign_seed", StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => StructuredActionTools.ParseCases("[" + fixture + "," + foreignCase + "]"));
        Assert.Throws<ArgumentException>(() => StructuredActionTools.ParseCases("[" + fixture.Replace("town_EW2", " town_EW2", StringComparison.Ordinal) + "]"));
        Assert.Throws<ArgumentException>(() => StructuredActionTools.ParseCases("[" + fixture.Replace("\"settlementId\"", "\"heroId\"", StringComparison.Ordinal) + "]"));
        Assert.Throws<ArgumentException>(() => StructuredActionTools.ParseCases("[" + fixture.Replace("\"phase\":\"fixture\"", "\"phase\":\"status\"", StringComparison.Ordinal) + "]"));
    }

    [Fact]
    public void ArrestMurderEvidenceWitnessBindsExactSingleCheckpointObservation()
    {
        const string arguments = """{"accusedHeroId":"lord_1","matterId":"noble-murder_1"}""";
        string input = ArrestMurderEvidenceCase(arguments);
        var parsed = Assert.Single(StructuredActionTools.ParseCases(input));
        Assert.True(StructuredActionTools.RequiresRecoveryCheckpoint(parsed));
        Assert.Equal(arguments, parsed.GetProperty("arguments").GetRawText());
        Assert.Throws<ArgumentException>(() => StructuredActionTools.ParseCases(
            input[..^1] + """,{"caseId":"other","phase":"catalog"}]"""));
        Assert.Throws<ArgumentException>(() => StructuredActionTools.ParseCases(
            input.Replace("\"phase\":\"fixture\"", "\"phase\":\"status\"")));
    }

    [Theory]
    [InlineData("prepare")]
    [InlineData("advance")]
    [InlineData("observe")]
    public void ArrestLateEvidenceBindsOneExactActorAndRetainedCheckpoint(string stage)
    {
        string json = """
            [{"caseId":"late","phase":"fixture","kind":"fixture","operationId":"late_once",
              "actionId":"fixture.arrest_late_evidence","arguments":{"accusedHeroId":"lord_1","stage":"STAGE"}}]
            """.Replace("STAGE", stage, StringComparison.Ordinal);
        JsonElement test = Assert.Single(StructuredActionTools.ParseCases(json));
        Assert.True(StructuredActionTools.RequiresRecoveryCheckpoint(test));
        Assert.Throws<ArgumentException>(() => StructuredActionTools.ParseCases(json.Replace("lord_1", "../other", StringComparison.Ordinal)));
        Assert.Throws<ArgumentException>(() => StructuredActionTools.ParseCases(json.Replace("\"" + stage + "\"", "\"reset\"", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("""{"accusedHeroId":"lord_1"}""")]
    [InlineData("""{"accusedHeroId":" lord_1","matterId":"noble-murder_1"}""")]
    [InlineData("""{"accusedHeroId":"lord_1","matterId":1}""")]
    [InlineData("""{"accusedHeroId":"lord_1","accusedHeroId":"lord_2"}""")]
    [InlineData("""{"accusedHeroId":"lord_1","matterId":"noble-murder_1","evidenceId":"invented"}""")]
    public void ArrestMurderEvidenceWitnessRejectsUnscopedOrAuthoredEvidence(string arguments) =>
        Assert.Throws<ArgumentException>(() => StructuredActionTools.ParseCases(ArrestMurderEvidenceCase(arguments)));

    [Theory]
    [InlineData("valid")]
    [InlineData("ordinary_snapshot")]
    [InlineData("unmarked_result")]
    [InlineData("missing_idle_owner")]
    [InlineData("stale_instance")]
    [InlineData("wrong_point")]
    [InlineData("invalid_generation")]
    [InlineData("invalid_session")]
    [InlineData("invalid_restore")]
    public void ArrestWitnessDiagnosticBindsExactIdleNativeLoad(string changed)
    {
        var (state, _) = PausedActionSnapshot(safeSettlement: true);
        var checkpoint = state.CheckpointReceipts[0];
        const string instance = "game-current";
        const string mode = "post_arm_current_diagnostic";
        var observation = new
        {
            schema = "reign-native-verified-current-observation-v1",
            campaignId = state.CampaignId, timelineId = state.TimelineId, saveName = state.CurrentSaveName,
            gameInstanceId = changed == "stale_instance" ? "old-instance" : instance,
            savePointId = changed == "wrong_point" ? Guid.NewGuid().ToString("N") : checkpoint.SavePointId,
            nativeProcessId = 1200, clientProcessInstanceId = Guid.NewGuid().ToString("N"),
            origin = "http://127.0.0.1:8089", loadResult = "no_op",
            coordinatorGeneration = changed == "invalid_generation" ? 0 : 17,
            loadSessionSha256 = changed == "invalid_session" ? "" : new string('A', 64),
            restoreGenerationSha256 = changed == "invalid_restore" ? "" : new string('B', 64)
        };
        var snapshot = FixtureCall(new
        {
            ok = true, found = true, runId = "run", gameInstanceId = instance, campaignId = state.CampaignId,
            status = "completed", mode = "passive_world",
            commands = new[]
            {
                new
                {
                    runId = "run", commandId = "command", gameInstanceId = instance, campaignId = state.CampaignId,
                    status = "completed", operation = "world_snapshot", snapshotMode = changed == "ordinary_snapshot" ? "" : mode,
                    result = new
                    {
                        snapshotMode = changed == "unmarked_result" ? "" : mode,
                        campaignId = state.CampaignId, timelineId = state.TimelineId, activeSaveName = state.CurrentSaveName,
                        timeMode = "Stop", checkpointGameInstanceId = instance, checkpointSavePointId = checkpoint.SavePointId,
                        saveSyncReady = true, saveSyncAlignmentPending = false,
                        verifiedCurrentLoadObservation = changed == "missing_idle_owner" ? null : observation
                    }
                }
            }
        });
        if (changed != "valid")
        {
            Assert.Throws<InvalidOperationException>(() =>
                CampaignTestCheckpointOwnership.ReadPausedActionDiagnosticOwner(state, checkpoint, snapshot));
            return;
        }

        var owner = CampaignTestCheckpointOwnership.ReadPausedActionDiagnosticOwner(state, checkpoint, snapshot);
        Assert.True(JsonElement.DeepEquals(owner, JsonSerializer.SerializeToElement(observation)));
    }

    private static string ArrestMurderEvidenceCase(string arguments) =>
        """
        [{"caseId":"evidence","phase":"fixture","kind":"fixture","actionId":"fixture.arrest_murder_evidence_witness",
        "operationId":"case-1","arguments":ARGUMENTS}]
        """.Replace("ARGUMENTS", arguments);

    [Theory]
    [InlineData("espionage", "lord_1", true)]
    [InlineData("espionage", "lord_2", false)]
    [InlineData("rebellion", "reb_id", true)]
    [InlineData("sabotage", "agent_action_1", true)]
    [InlineData("sabotage", " agent_action_1", false)]
    [InlineData("treason", "reb_id", false)]
    [InlineData("murder", "noble_matter_1", false)]
    public void ScopedArrestWitnessUsesOneTypedExistingSource(string charge, string source, bool accepted)
    {
        string args = JsonSerializer.Serialize(new { accusedHeroId = "lord_1", chargeCategory = charge, sourceId = source });
        string fixture = ArrestMurderEvidenceCase(args).Replace("fixture.arrest_murder_evidence_witness", "fixture.arrest_scoped_evidence_witness");
        if (!accepted)
        {
            Assert.Throws<ArgumentException>(() => StructuredActionTools.ParseCases(fixture));
            return;
        }

        var parsed = Assert.Single(StructuredActionTools.ParseCases(fixture));
        Assert.True(StructuredActionTools.RequiresRecoveryCheckpoint(parsed));
        Assert.Equal(args, parsed.GetProperty("arguments").GetRawText());
        Assert.Throws<ArgumentException>(() => StructuredActionTools.ParseCases(fixture.Replace("\"phase\":\"fixture\"", "\"phase\":\"status\"")));
        string entry = fixture[1..^1];
        Assert.Throws<ArgumentException>(() => StructuredActionTools.ParseCases("[" + entry + "," + entry.Replace("\"evidence\"", "\"second-case\"") + "]"));
    }

    [Theory]
    [InlineData("food")]
    [InlineData("construction")]
    [InlineData("security")]
    [InlineData("loyalty")]
    public void SpymasterDisruptionObservationPreservesExactSelectorsAndRequiresRecovery(string effectType)
    {
        string args = JsonSerializer.Serialize(new { agentHeroId = "lord_1", assignmentId = "agent_1",
            actionId = "foreign-agent-lord_1-5-" + effectType + "-agent_1", settlementId = "town_1", effectType });
        var parsed = Assert.Single(StructuredActionTools.ParseCases(SpymasterDisruptionObservationCase(args)));
        Assert.True(StructuredActionTools.RequiresRecoveryCheckpoint(parsed));
        Assert.Equal(args, parsed.GetProperty("arguments").GetRawText());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"agentHeroId\":\"lord_1\",\"assignmentId\":\"agent_1\",\"actionId\":\"foreign_agent_1\",\"settlementId\":\"town_1\",\"effectType\":\"prosperity\"}")]
    [InlineData("{\"agentHeroId\":\"lord_1\",\"assignmentId\":\" agent_1\",\"actionId\":\"foreign_agent_1\",\"settlementId\":\"town_1\",\"effectType\":\"security\"}")]
    [InlineData("{\"agentHeroId\":\"lord_1\",\"assignmentId\":\"agent_1\",\"actionId\":\"foreign_agent_1\",\"effectType\":\"security\",\"settlementId\":4}")]
    [InlineData("{\"agentHeroId\":\"lord_1\",\"assignmentId\":\"agent_1\",\"actionId\":\"foreign_agent_1\",\"effectType\":\"security\",\"settlementId\":\"town_1\",\"settlementId\":\"town_foreign\"}")]
    [InlineData("{\"agentHeroId\":\"lord_1\",\"assignmentId\":\"agent_1\",\"actionId\":\"foreign_agent_1\",\"effectType\":\"security\",\"settlementId\":\"town_1\",\"dispatch\":true}")]
    public void SpymasterDisruptionObservationRejectsMalformedOrMutationSelectors(string args)
    {
        Assert.Throws<ArgumentException>(() => StructuredActionTools.ParseCases(SpymasterDisruptionObservationCase(args)));
    }

    private static string SpymasterDisruptionObservationCase(string args) =>
        "[{\"caseId\":\"disruption-read\",\"phase\":\"fixture\",\"kind\":\"fixture\"," +
        "\"operationId\":\"disruption_before\",\"actionId\":\"fixture.spymaster_disruption_observe\",\"arguments\":" + args + "}]";

    [Theory]
    [InlineData("arm")]
    [InlineData("observe")]
    public void SpymasterDisruptionDeliveryRequiresOneExactRecoveryCase(string control)
    {
        string args = JsonSerializer.Serialize(new { control, agentHeroId = "lord_1", assignmentId = "agent_1", settlementId = "town_1" });
        string fixture = SpymasterDisruptionDeliveryCase(args);
        var parsed = Assert.Single(StructuredActionTools.ParseCases(fixture));
        Assert.True(StructuredActionTools.RequiresRecoveryCheckpoint(parsed));
        Assert.Equal(args, parsed.GetProperty("arguments").GetRawText());
        Assert.Throws<ArgumentException>(() => StructuredActionTools.ParseCases(fixture.Replace("\"phase\":\"fixture\"", "\"phase\":\"status\"")));
        string entry = fixture[1..^1];
        string batch = "[" + entry + "," + entry.Replace("disruption-delivery", "other-case") + "]";
        Assert.Throws<ArgumentException>(() => StructuredActionTools.ParseCases(batch));
    }

    [Theory]
    [InlineData("{\"control\":\"release\",\"agentHeroId\":\"lord_1\",\"assignmentId\":\"agent_1\",\"settlementId\":\"town_1\"}")]
    [InlineData("{\"control\":\"arm\",\"agentHeroId\":\"lord_1\",\"assignmentId\":\"agent_1\",\"settlementId\":\"town_1\",\"actionId\":\"chosen\"}")]
    [InlineData("{\"control\":\"arm\",\"agentHeroId\":\"lord_1\",\"assignmentId\":\" agent_1\",\"settlementId\":\"town_1\"}")]
    [InlineData("{\"control\":\"arm\",\"agentHeroId\":\"lord_1\",\"assignmentId\":\"agent_1\",\"settlementId\":5}")]
    public void SpymasterDisruptionDeliveryRejectsOverridesAndInvalidSelectors(string args)
    {
        Assert.Throws<ArgumentException>(() => StructuredActionTools.ParseCases(SpymasterDisruptionDeliveryCase(args)));
    }

    private static string SpymasterDisruptionDeliveryCase(string args) =>
        "[{\"caseId\":\"disruption-delivery\",\"phase\":\"fixture\",\"kind\":\"fixture\"," +
        "\"operationId\":\"disruption_delivery\",\"actionId\":\"fixture.spymaster_disruption_delivery\",\"arguments\":" + args + "}]";

    [Theory]
    [InlineData("{\"motherId\":\"mother_1\"}")]
    [InlineData("{\"motherId\":\"mother_1\",\"conceptionId\":\"conception-1\"}")]
    public void ManagedBirthObservationRequiresRecoveryAndPreservesExactArguments(string args)
    {
        var parsed = Assert.Single(StructuredActionTools.ParseCases(ManagedBirthObservationCase(args)));
        Assert.True(StructuredActionTools.RequiresRecoveryCheckpoint(parsed));
        Assert.Equal(args, parsed.GetProperty("arguments").GetRawText());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"conceptionId\":\"only_child\"}")]
    [InlineData("{\"motherId\":\" mother_1\"}")]
    [InlineData("{\"motherId\":\"mother_1\",\"conceptionId\":\"\"}")]
    [InlineData("{\"motherId\":\"mother_1\",\"conceptionId\":5}")]
    [InlineData("{\"motherId\":\"mother_1\",\"motherId\":\"foreign\"}")]
    [InlineData("{\"motherId\":\"mother_1\",\"holdAcknowledgement\":true}")]
    public void ManagedBirthObservationRejectsMissingAmbiguousOrMutationArguments(string args)
    {
        Assert.Throws<ArgumentException>(() => StructuredActionTools.ParseCases(ManagedBirthObservationCase(args)));
    }

    private static string ManagedBirthObservationCase(string args) =>
        "[{\"caseId\":\"birth-read\",\"phase\":\"fixture\",\"kind\":\"fixture\"," +
        "\"operationId\":\"birth_before\",\"actionId\":\"fixture.managed_birth_observe\",\"arguments\":" + args + "}]";

    [Theory]
    [InlineData("""{"control":"arm","pairKey":"adultA|adultB","revision":3}""", true)]
    [InlineData("""{"control":"arm_pair","pairKey":"adultA|adultB"}""", true)]
    [InlineData("""{"control":"arm_pair","pairKey":"adultA|adultB","revision":3}""", false)]
    [InlineData("""{"control":"arm_pair","pairKey":"adultA|adultB","targetRelation":25}""", false)]
    [InlineData("""{"control":"arm_pair","pairKey":"adultA|adultB","pairKey":"adultA|adultC"}""", false)]
    [InlineData("""{"control":"arm_pair","pairKey":"adultA|adultB|adultC"}""", false)]
    [InlineData("""{"control":"arm_pair","pairKey":"|adultB"}""", false)]
    [InlineData("""{"control":"arm_pair"}""", false)]
    [InlineData("""{"control":"observe"}""", true)]
    [InlineData("""{"control":"release"}""", true)]
    [InlineData("""{"control":"arm","pairKey":"adultA|adultB","revision":0}""", false)]
    [InlineData("""{"control":"arm","pairKey":"adultA|adultB","revision":"3"}""", false)]
    [InlineData("""{"control":"arm","pairKey":"adultA|adultB","revision":2147483648}""", false)]
    [InlineData("""{"control":"arm","pairKey":"|adultB","revision":3}""", false)]
    [InlineData("""{"control":"arm","pairKey":"adultA|adultB ","revision":3}""", false)]
    [InlineData("""{"control":"arm","pairKey":"adultA|adultB","revision":3,"response":"invented"}""", false)]
    [InlineData("""{"control":"observe","control":"release"}""", false)]
    [InlineData("""{"control":"release","pairKey":"adultA|adultB"}""", false)]
    public void RelationshipPullControlRequiresOneExactCaseAndRecoveryCheckpoint(string arguments, bool accepted)
    {
        string single = """
            [{"caseId":"one","phase":"fixture","kind":"fixture","actionId":"fixture.relationship_native_pull",
            "operationId":"response-1","arguments":ARGUMENTS}]
            """.Replace("ARGUMENTS", arguments);
        if (!accepted)
        {
            Assert.Throws<ArgumentException>(() => StructuredActionTools.ParseCases(single));
            return;
        }
        var parsed = Assert.Single(StructuredActionTools.ParseCases(single));
        Assert.True(StructuredActionTools.RequiresRecoveryCheckpoint(parsed));
        Assert.Throws<ArgumentException>(() => StructuredActionTools.ParseCases(
            single[..^1] + """,{"caseId":"two","phase":"catalog"}]"""));
        Assert.Throws<ArgumentException>(() => StructuredActionTools.ParseCases(
            single.Replace("\"phase\":\"fixture\"", "\"phase\":\"status\"")));
    }

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
    [Theory]
    [InlineData("vacant")]
    [InlineData("retained")]
    [InlineData("foreign")]
    [InlineData("duplicate")]
    [InlineData("missing_point")]
    [InlineData("unexpected_point")]
    [InlineData("different_slot")]
    [InlineData("wrong_campaign")]
    [InlineData("wrong_slot")]
    [InlineData("disabled")]
    [InlineData("invalid_foreign")]
    [InlineData("missing_capacity")]
    public void CheckpointSlotResponseRequiresExactUnambiguousOccupancy(string scenario)
    {
        const string campaign = "campaign";
        const string slot = "ReignTest_Inventory_Current";
        var data = new Dictionary<string, object?>
        {
            ["enabled"] = true, ["campaignId"] = campaign, ["checkpointSlot"] = slot,
            ["foreignRegistration"] = false, ["registrationCount"] = 0,
            ["registrationUniqueStateCount"] = 2
        };
        switch (scenario)
        {
            case "retained":
                data["registrationCount"] = 1;
                data["point"] = new { nativeSaveName = " reigntest_inventory_current.SAV " };
                break;
            case "foreign": data["foreignRegistration"] = true; break;
            case "duplicate": data["registrationCount"] = 2; break;
            case "missing_point": data["registrationCount"] = 1; break;
            case "unexpected_point": data["point"] = new { nativeSaveName = slot }; break;
            case "different_slot":
                data["registrationCount"] = 1;
                data["point"] = new { nativeSaveName = "ReignTest_Other_Current" };
                break;
            case "wrong_campaign": data["campaignId"] = "other"; break;
            case "wrong_slot": data["checkpointSlot"] = "ReignTest_Other_Current"; break;
            case "disabled": data["enabled"] = false; break;
            case "invalid_foreign": data["foreignRegistration"] = "false"; break;
            case "missing_capacity": data.Remove("registrationUniqueStateCount"); break;
        }
        var envelope = new ApiEnvelope
        {
            Ok = true, Endpoint = "/api/save-sync/status", RetrievedUtc = "2026-11-20T12:00:00Z",
            Data = JsonSerializer.SerializeToElement(data)
        };
        if (scenario is "vacant" or "retained")
        {
            var (point, count) = CampaignTestService.ParseCheckpointSlot(envelope, campaign, slot);
            Assert.Equal(2, count);
            Assert.Equal(scenario == "retained", point.HasValue);
        }
        else
        {
            Assert.Throws<InvalidOperationException>(() => CampaignTestService.ParseCheckpointSlot(envelope, campaign, slot));
        }
    }

    private static CampaignCheckpointReceipt TestCheckpoint(CampaignTestState state, string name) =>
        new(new CampaignCheckpointAttempt(Guid.NewGuid().ToString("N"), state.RunId, state.CampaignId,
            state.TimelineId, state.BaselineSaveName, name, state.BaselineSaveName, "",
            "test-game", "", "", "2026-11-20T12:00:00Z"), Guid.NewGuid().ToString("N"),
            new string('a', 64), "2026-11-20T12:01:00Z");

    private static ControllerCall FixtureCall(object data) =>
        ControllerCall.Create(new ProcessResult { Ok = true, ExitCode = 0, Command = "fixture",
            WorkingDirectory = "fixture", DurationMs = 0, TimedOut = false, OutputTruncated = false,
            StandardOutput = JsonSerializer.Serialize(data), StandardError = "" });

    private static (CampaignTestState State, ControllerCall Snapshot) PausedActionSnapshot(
        bool safeSettlement, string mutation = "")
    {
        var state = new CampaignTestState { RunId = "run", CampaignId = "campaign", TimelineId = "timeline",
            BaselineSaveName = "BaseThree", SavePrefix = "ReignTest_Actions",
            CurrentSaveName = "ReignTest_Actions_Current", Armed = true,
            Checkpoints = ["ReignTest_Actions_Current", "ReignTest_Actions_Milestone"] };
        state.CheckpointReceipts.AddRange(state.Checkpoints.Select(name => TestCheckpoint(state, name)));
        var data = new Dictionary<string, object?> { ["ok"] = true, ["campaignId"] = state.CampaignId,
            ["timelineId"] = state.TimelineId, ["activeSaveName"] = state.CurrentSaveName,
            ["saveSyncReady"] = true, ["saveSyncAlignmentPending"] = false,
            ["safeSettlement"] = safeSettlement, ["timeMode"] = "Stop",
            ["checkpointSavePointId"] = state.CheckpointReceipts[0].SavePointId };
        switch (mutation)
        {
            case "baseline": data["activeSaveName"] = state.BaselineSaveName; break;
            case "foreign_save": data["activeSaveName"] = "ReignTest_Foreign_Current"; break;
            case "milestone": data["activeSaveName"] = state.Checkpoints[1];
                data["checkpointSavePointId"] = state.CheckpointReceipts[1].SavePointId; break;
            case "name_only": state.Checkpoints.Clear(); state.CheckpointReceipts.Clear(); break;
            case "legacy_name": state.CheckpointReceipts.Clear(); break;
            case "baseline_alias": state.BaselineSaveName = state.CurrentSaveName.ToLowerInvariant(); break;
            case "wrong_point": data["checkpointSavePointId"] = Guid.NewGuid().ToString("N"); break;
            case "foreign_receipt": state.RunId = "new-run"; break;
            case "campaign": data["campaignId"] = "foreign"; break;
            case "timeline": data["timelineId"] = "foreign"; break;
            case "unaligned": data["saveSyncAlignmentPending"] = true; break;
            case "missing_alignment": data.Remove("saveSyncAlignmentPending"); break;
            case "sync_not_ready": data["saveSyncReady"] = false; break;
            case "missing_time": data.Remove("timeMode"); break;
            case "advancing": data["timeMode"] = "StoppableFastForward"; break;
            case "unknown_time": data["timeMode"] = "stop"; break;
            case "failed_snapshot": data["ok"] = false; break;
        }
        return (state, FixtureCall(data));
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
    [InlineData("missing_alignment")]
    [InlineData("sync_not_ready")]
    [InlineData("missing_time")]
    [InlineData("advancing")]
    [InlineData("unknown_time")]
    [InlineData("failed_snapshot")]
    [InlineData("name_only")]
    [InlineData("legacy_name")]
    [InlineData("baseline_alias")]
    [InlineData("wrong_point")]
    [InlineData("foreign_receipt")]
    public void PausedActionsRejectUnownedUnalignedOrMovingSnapshots(string mutation)
    {
        var (state, snapshot) = PausedActionSnapshot(false, mutation);
        Assert.Throws<InvalidOperationException>(() => CampaignTestService.ValidatePausedActionSnapshot(state, snapshot));
    }

    [Fact]
    public void CampaignCheckpointProtectsBaselineAndForeignDestinationBytes()
    {
        var (state, _) = PausedActionSnapshot(safeSettlement: true);
        var receipt = state.CheckpointReceipts[0];
        string directory = Path.Combine(Path.GetTempPath(), "reign-checkpoint-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, state.CurrentSaveName + ".sav");
        try
        {
            Assert.Throws<InvalidOperationException>(() => CampaignTestCheckpointOwnership
                .RequireDistinctBaseline(state.CurrentSaveName.ToLowerInvariant(), state.CurrentSaveName));
            CampaignTestCheckpointOwnership.RequireVacant(state.CurrentSaveName,
                CampaignTestCheckpointOwnership.NativeHash(state.CurrentSaveName, directory), registered: false);
            Assert.Throws<InvalidOperationException>(() => CampaignTestCheckpointOwnership
                .RequireVacant(state.CurrentSaveName, null, registered: true));
            File.WriteAllText(file, "fixture initial bytes");
            string hash = CampaignTestCheckpointOwnership.NativeHash(state.CurrentSaveName, directory)!;
            Assert.Throws<InvalidOperationException>(() => CampaignTestCheckpointOwnership
                .RequireVacant(state.CurrentSaveName, hash, registered: false));
            receipt = receipt with { Sha256 = hash };
            CampaignTestCheckpointOwnership.VerifyBytes(receipt, hash);
            File.WriteAllText(file, "same name, different fixture bytes");
            Assert.Throws<InvalidOperationException>(() => CampaignTestCheckpointOwnership
                .VerifyBytes(receipt, CampaignTestCheckpointOwnership.NativeHash(state.CurrentSaveName, directory)));
        }
        finally
        {
            File.Delete(file);
            Directory.Delete(directory);
        }
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("missing")]
    [InlineData("wrongAttempt")]
    [InlineData("wrongInstance")]
    [InlineData("oldPoint")]
    [InlineData("nativeFailed")]
    [InlineData("saveSyncFailed")]
    [InlineData("notQuiescent")]
    public void CampaignCheckpointRequiresCorrelatedNativeAndSaveSyncCompletion(string mutation)
    {
        var (state, _) = PausedActionSnapshot(safeSettlement: true);
        var receipt = state.CheckpointReceipts[0];
        var attempt = receipt.Attempt with { SourcePointId = Guid.NewGuid().ToString("N") };
        state.Checkpoints.Clear();
        state.CheckpointReceipts.Clear();
        state.PendingCheckpoint = attempt;
        var reloaded = JsonSerializer.Deserialize<CampaignTestState>(JsonSerializer.Serialize(state))!;
        Assert.Equal(attempt, reloaded.PendingCheckpoint);
        Assert.Null(CampaignTestCheckpointOwnership.Find(reloaded, attempt.SaveName));
        Assert.Throws<InvalidOperationException>(() => CampaignTestCheckpointOwnership.RequireResolved(reloaded));

        var nativeReceipt = new
        {
            id = mutation == "wrongAttempt" ? Guid.NewGuid().ToString("N") : attempt.Id,
            runId = attempt.RunId, saveName = attempt.SaveName,
            gameInstanceId = mutation == "wrongInstance" ? "wrong-game" : attempt.GameInstanceId,
            savePointId = mutation == "oldPoint" ? attempt.SourcePointId : receipt.SavePointId,
            sha256 = receipt.Sha256
        };
        var controller = FixtureCall(new
        {
            ok = true, postSaveRuntimeQuiesced = mutation != "notQuiescent",
            save = new
            {
                ok = true, saveName = attempt.SaveName,
                nativeSave = new { succeeded = mutation != "nativeFailed", saveName = attempt.SaveName },
                saveFinalization = new { succeeded = mutation != "saveSyncFailed", completedSaveName = attempt.SaveName },
                checkpointOwnership = mutation == "missing" ? null : nativeReceipt
            }
        });
        if (mutation == "valid")
            Assert.Equal(receipt.SavePointId, CampaignTestCheckpointOwnership.ReadCompletion(attempt, controller).SavePointId);
        else
            Assert.Throws<InvalidOperationException>(() => CampaignTestCheckpointOwnership.ReadCompletion(attempt, controller));
    }

    [Theory]
    [InlineData(0L, "")]
    [InlineData(42L, "native-head")]
    public void CampaignCheckpointRetainsExactRegisteredPointHistoryObservation(long history, string head)
    {
        var (receipt, controller, point, ledgerCampaign) = RegisteredPointFixture("valid", history, head);
        var observation = CampaignTestCheckpointOwnership.ObserveRegisteredPoint(receipt, controller, point, ledgerCampaign);
        Assert.Equal(new CampaignRegisteredPointEvidence("reign-campaign-test-registered-point-evidence-v1",
            receipt.Attempt.CampaignId, receipt.Attempt.TimelineId, receipt.SavePointId, 14L, history, head), observation);
        var retained = receipt with { RegisteredPointEvidence = observation };
        string json = JsonSerializer.Serialize(retained, JsonSerializerOptions.Web);
        Assert.Contains("\"registeredPointEvidence\"", json, StringComparison.Ordinal);
        Assert.Equal(retained, JsonSerializer.Deserialize<CampaignCheckpointReceipt>(json, JsonSerializerOptions.Web));
    }

    [Theory]
    [InlineData("absent")]
    [InlineData("null")]
    [InlineData("wrong_native_metadata_type")]
    [InlineData("wrong_schema")]
    [InlineData("wrong_campaign")]
    [InlineData("wrong_timeline")]
    [InlineData("wrong_point")]
    [InlineData("old_native_sequence")]
    [InlineData("newer_save_started")]
    [InlineData("different_completed_sequence")]
    [InlineData("string_native_sequence")]
    [InlineData("string_history_sequence")]
    [InlineData("negative_history_sequence")]
    [InlineData("invalid_head_pair")]
    [InlineData("missing_head")]
    [InlineData("non_string_head")]
    [InlineData("foreign_ledger_campaign")]
    [InlineData("foreign_ledger_timeline")]
    [InlineData("wrong_ledger_point")]
    [InlineData("missing_ledger_sequence")]
    [InlineData("different_ledger_sequence")]
    public void CampaignCheckpointOmitsUnprovedRegisteredPointHistoryWithoutRejectingTheReceipt(string mutation)
    {
        var (receipt, controller, point, ledgerCampaign) = RegisteredPointFixture(mutation, 42L, "native-head");
        Assert.Null(CampaignTestCheckpointOwnership.ObserveRegisteredPoint(receipt, controller, point, ledgerCampaign));
        Assert.DoesNotContain("\"registeredPointEvidence\"",
            JsonSerializer.Serialize(receipt, JsonSerializerOptions.Web), StringComparison.Ordinal);
        Assert.Equal(receipt.SavePointId, CampaignTestCheckpointOwnership.ReadCompletion(receipt.Attempt, controller).SavePointId);
    }

    private static (CampaignCheckpointReceipt Receipt, ControllerCall Controller, JsonElement Point, string LedgerCampaign)
        RegisteredPointFixture(string mutation, long history, string head)
    {
        var (state, _) = PausedActionSnapshot(safeSettlement: true);
        var checkpoint = state.CheckpointReceipts[0];
        var attempt = checkpoint.Attempt with { SourcePointId = Guid.NewGuid().ToString("N") };
        var observed = new Dictionary<string, object?>
        {
            ["schema"] = mutation == "wrong_schema" ? "other" : "reign-campaign-test-native-registered-point-v1",
            ["campaignId"] = mutation == "wrong_campaign" ? attempt.CampaignId + "-other" : attempt.CampaignId,
            ["timelineId"] = mutation == "wrong_timeline" ? attempt.TimelineId + "-other" : attempt.TimelineId,
            ["savePointId"] = mutation == "wrong_point" ? attempt.SourcePointId : checkpoint.SavePointId,
            ["nativeSaveSequence"] = mutation switch { "old_native_sequence" => 13L, "string_native_sequence" => "14", _ => 14L },
            ["worldHistorySequence"] = mutation switch
            {
                "string_history_sequence" => "42", "negative_history_sequence" => -1L, _ => history
            },
            ["nativeHistoryHeadEventId"] = mutation switch { "invalid_head_pair" => "", "non_string_head" => 1, _ => head }
        };
        if (mutation == "missing_head") observed.Remove("nativeHistoryHeadEventId");
        var ownership = new Dictionary<string, object?>
        {
            ["id"] = attempt.Id, ["runId"] = attempt.RunId, ["gameInstanceId"] = attempt.GameInstanceId,
            ["saveName"] = attempt.SaveName, ["savePointId"] = checkpoint.SavePointId, ["sha256"] = checkpoint.Sha256
        };
        if (mutation != "absent")
            ownership["nativeRegisteredPoint"] = mutation switch
            {
                "null" => null, "wrong_native_metadata_type" => "unavailable", _ => observed
            };

        var controller = FixtureCall(new
        {
            ok = true,
            postSaveRuntimeQuiesced = true,
            save = new
            {
                ok = true, saveName = attempt.SaveName,
                nativeSave = new
                {
                    succeeded = true, saveName = attempt.SaveName,
                    sequence = mutation == "newer_save_started" ? 15L : 14L,
                    completedSequence = mutation == "different_completed_sequence" ? 13L : 14L
                },
                saveFinalization = new { succeeded = true, completedSaveName = attempt.SaveName },
                checkpointOwnership = ownership
            }
        });
        var registered = new Dictionary<string, object?>
        {
            ["savePointId"] = mutation == "wrong_ledger_point" ? attempt.SourcePointId : checkpoint.SavePointId,
            ["timelineId"] = mutation == "foreign_ledger_timeline" ? attempt.TimelineId + "-other" : attempt.TimelineId,
            ["worldHistorySequence"] = mutation == "different_ledger_sequence" ? history + 1L : history,
            ["nativeSaveName"] = attempt.SaveName,
            ["status"] = "ready",
            ["snapshotAvailable"] = true,
            ["saveFinalizedUtc"] = "2026-11-25T20:00:00Z"
        };
        if (mutation == "missing_ledger_sequence") registered.Remove("worldHistorySequence");
        return (CampaignTestCheckpointOwnership.ReadCompletion(attempt, controller), controller,
            JsonSerializer.SerializeToElement(registered),
            mutation == "foreign_ledger_campaign" ? attempt.CampaignId + "-other" : attempt.CampaignId);
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("legacy")]
    [InlineData("foreign_checkpoint")]
    [InlineData("foreign_save")]
    [InlineData("foreign_point")]
    [InlineData("wrong_sha")]
    [InlineData("wrong_original_load")]
    [InlineData("same_load")]
    [InlineData("missing_reload")]
    public void CampaignCheckpointRestartBindsBytesAndTheTwoNativeLoads(string mutation)
    {
        var (state, _) = PausedActionSnapshot(safeSettlement: true);
        var receipt = state.CheckpointReceipts[0];
        const string reloaded = "reloaded-game";
        var evidence = JsonSerializer.SerializeToElement(new
        {
            restartEvidenceSchema = mutation == "legacy" ? "" : "reign-campaign-checkpoint-restart-v1",
            saveName = mutation == "foreign_save" ? "foreign-save" : receipt.Attempt.SaveName,
            savePointId = mutation == "foreign_point" ? "foreign-point" : receipt.SavePointId,
            checkpointAttemptId = mutation == "foreign_checkpoint" ? "foreign-attempt" : receipt.Attempt.Id,
            checkpointSha256 = mutation == "wrong_sha" ? "wrong-sha" : receipt.Sha256,
            checkpointGameInstanceId = mutation == "wrong_original_load" ? "wrong-game" : receipt.Attempt.GameInstanceId,
            reloadedGameInstanceId = mutation == "same_load" ? receipt.Attempt.GameInstanceId
                : mutation == "missing_reload" ? "" : reloaded
        });

        if (mutation == "valid")
            Assert.Equal(reloaded, CampaignTestCheckpointOwnership.ReadRestartedGameInstance(receipt, evidence));
        else
            Assert.Throws<InvalidOperationException>(() =>
                CampaignTestCheckpointOwnership.ReadRestartedGameInstance(receipt, evidence));
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("legacy")]
    [InlineData("armed_baseline")]
    [InlineData("pending_checkpoint")]
    [InlineData("foreign_attempt")]
    [InlineData("same_instance")]
    [InlineData("foreign_run")]
    [InlineData("foreign_command")]
    [InlineData("duplicate_commands")]
    [InlineData("not_stopped")]
    [InlineData("alignment_pending")]
    [InlineData("stale_live_point")]
    [InlineData("foreign_observed_generation")]
    [InlineData("foreign_observed_point")]
    [InlineData("foreign_origin")]
    [InlineData("origin_with_path")]
    [InlineData("unverified_load")]
    [InlineData("malformed_session_digest")]
    [InlineData("ordinary_snapshot")]
    [InlineData("unmarked_snapshot_result")]
    [InlineData("already_running")]
    [InlineData("restart_missing_save")]
    [InlineData("restart_missing_runtime")]
    [InlineData("restart_not_running")]
    [InlineData("restart_not_ready")]
    [InlineData("restart_offline")]
    [InlineData("restart_other_instance")]
    [InlineData("restart_other_timeline")]
    [InlineData("restart_other_process_nonce")]
    [InlineData("restart_other_pid")]
    [InlineData("restart_duplicate_pid")]
    [InlineData("snapshot_invalid_pid")]
    public void PostArmCurrentObservationRequiresTheExactPausedReloadAndIsDiagnosticOnly(string mutation)
    {
        var (state, _) = PausedActionSnapshot(safeSettlement: true);
        var receipt = state.CheckpointReceipts[0];
        string original = receipt.Attempt.GameInstanceId;
        string processInstance = Guid.NewGuid().ToString("N");
        const int nativeProcessId = 38192;
        string reloaded = mutation == "same_instance" ? original : "fresh-native-instance";
        if (mutation == "pending_checkpoint") state.PendingCheckpoint = receipt.Attempt;
        if (mutation == "foreign_attempt")
            receipt = receipt with { Attempt = receipt.Attempt with { Id = Guid.NewGuid().ToString("N") } };
        string scope = mutation == "armed_baseline" ? state.BaselineSaveName : state.CurrentSaveName;
        string alternate = Guid.NewGuid().ToString("N");
        string origin = mutation == "foreign_origin" ? "http://example.com"
            : mutation == "origin_with_path" ? "http://127.0.0.1:8089/api" : "http://127.0.0.1:8089";
        var observation = new
        {
            schema = "reign-native-verified-current-observation-v1", campaignId = state.CampaignId,
            timelineId = state.TimelineId, saveName = scope, gameInstanceId = reloaded,
            savePointId = mutation == "foreign_observed_point" ? alternate : receipt.SavePointId,
            nativeProcessId = mutation == "snapshot_invalid_pid" ? 0 : nativeProcessId, clientProcessInstanceId = processInstance,
            origin, coordinatorGeneration = mutation == "foreign_observed_generation" ? 0 : 17,
            loadResult = mutation == "unverified_load" ? "degraded" : "no_op",
            loadSessionSha256 = mutation == "malformed_session_digest" ? alternate : new string('A', 64),
            restoreGenerationSha256 = new string('B', 64)
        };
        var command = new
        {
            runId = mutation == "foreign_command" ? "elsewhere" : "live-run", commandId = "snapshot-command",
            campaignId = state.CampaignId, gameInstanceId = reloaded, operation = "world_snapshot", status = "completed",
            snapshotMode = mutation == "ordinary_snapshot" ? "" : "post_arm_current_diagnostic",
            result = new
            {
                campaignId = state.CampaignId, timelineId = state.TimelineId, activeSaveName = scope,
                checkpointGameInstanceId = reloaded, checkpointSavePointId = mutation == "stale_live_point" ? alternate : receipt.SavePointId,
                timeMode = mutation == "not_stopped" ? "StoppablePlay" : "Stop", saveSyncReady = true,
                saveSyncAlignmentPending = mutation == "alignment_pending",
                snapshotMode = mutation == "unmarked_snapshot_result" ? "" : "post_arm_current_diagnostic",
                verifiedCurrentLoadObservation = mutation == "legacy" ? null : observation
            }
        };
        var native = FixtureCall(new
        {
            ok = true, found = true, runId = "live-run", campaignId = state.CampaignId, mode = "passive_world",
            status = "completed", gameInstanceId = mutation == "foreign_run" ? "old-instance" : reloaded,
            commands = mutation == "duplicate_commands" ? new[] { command, command } : new[] { command }
        });
        var restartedRuntime = new
        {
            ok = true, gameOnline = mutation != "restart_offline",
            runtime = new
            {
                campaignId = state.CampaignId, gameInstanceId = mutation == "restart_other_instance" ? "separate-instance" : reloaded,
                activeSaveName = scope,
                saveSync = new { ready = mutation != "restart_not_ready", alignmentPending = false,
                    timelineId = mutation == "restart_other_timeline" ? "other-timeline" : state.TimelineId },
                executionIdentity = new { clientProcessInstanceId = mutation == "restart_other_process_nonce" ? alternate : processInstance }
            }
        };
        var restart = FixtureCall(new
        {
            ok = true, status = mutation == "already_running" ? "already_running" : "restarted",
            running = mutation != "restart_not_running", saveName = mutation == "restart_missing_save" ? "" : state.CurrentSaveName,
            processIds = mutation == "restart_duplicate_pid" ? new[] { nativeProcessId, nativeProcessId }
                : new[] { mutation == "restart_other_pid" ? nativeProcessId + 1 : nativeProcessId },
            runtime = mutation == "restart_missing_runtime" ? null : restartedRuntime
        });
        var read = CampaignTestCheckpointOwnership.ReadPostArmCurrentObservation(state, receipt, restart, native, original, reloaded);
        if (mutation != "valid")
        {
            Assert.Null(read);
            return;
        }
        JsonElement evidence = JsonSerializer.SerializeToElement(read);
        Assert.True(evidence.GetProperty("diagnosticOnly").GetBoolean());
        Assert.Equal(receipt.Attempt.Id, evidence.GetProperty("checkpointAttemptId").GetString());
        Assert.Equal(receipt.Sha256, evidence.GetProperty("checkpointSha256").GetString());
        Assert.Equal(reloaded, evidence.GetProperty("reloadedGameInstanceId").GetString());
        Assert.Equal(17, evidence.GetProperty("coordinatorGeneration").GetInt32());
        Assert.Equal(nativeProcessId, evidence.GetProperty("nativeProcessId").GetInt32());
        Assert.False(evidence.TryGetProperty("clientProcessInstanceId", out _));
        Assert.False(evidence.TryGetProperty("loadSessionId", out _));
        Assert.False(evidence.TryGetProperty("restoreGeneration", out _));
    }

    [Fact]
    public void ExplicitCurrentDiagnosticBypassesHarnessActivationOnFreshCompletionAndReplay()
    {
        string root = TestOptions.FindWorkspace();
        string campaign = Path.Combine(root, "ReignBeta", "src", "Modules", "WorldSimulation", "Campaign");
        string host = File.ReadAllText(Path.Combine(campaign, "ReignLiveInteractionTestHost.cs"));
        string passive = File.ReadAllText(Path.Combine(campaign, "ReignLiveInteractionPassiveWorldHost.cs"));
        string controller = File.ReadAllText(Path.Combine(root, "ReignServer", "tests", "ReignLiveTest",
            "Features", "WorldSimulation", "PassiveWorldControl.cs"));

        int guard = host.IndexOf("private static bool IsPostArmCurrentDiagnosticSnapshot", StringComparison.Ordinal);
        int activate = host.IndexOf("private static void ActivateHarnessRun", StringComparison.Ordinal);
        string predicate = host[guard..activate];
        Assert.Contains("== \"world_snapshot\"", predicate);
        Assert.Contains("== \"passive_world\"", predicate);
        Assert.Contains("command[\"snapshotMode\"]?.Type == JTokenType.String", predicate);
        int earlyExit = host.IndexOf("if (IsPostArmCurrentDiagnosticSnapshot(command)) return;", activate, StringComparison.Ordinal);
        int ownership = host.IndexOf("_activeRunId =", activate, StringComparison.Ordinal);
        Assert.InRange(earlyExit, activate + 1, ownership - 1);
        Assert.Contains("if (completed.Status == \"completed\")", host);
        Assert.Contains("if (result.Status == \"completed\")", host);
        Assert.Contains("if (IsPostArmCurrentDiagnosticSnapshot(command))", passive);
        Assert.Contains("if (postArmCurrentDiagnostic) snapshotFields[\"snapshotMode\"]", controller);
    }

    [Theory]
    [InlineData("ordinary")]
    [InlineData("matching_stage")]
    [InlineData("missing_stage")]
    [InlineData("foreign_run")]
    [InlineData("foreign_command")]
    [InlineData("foreign_receipt")]
    [InlineData("foreign_lease")]
    [InlineData("wrong_generation")]
    [InlineData("different_agreement")]
    [InlineData("different_agreement_revision")]
    [InlineData("different_hero")]
    [InlineData("different_phase")]
    public void CampaignCheckpointStageReceiptMustEchoTheExactStageAndAgreement(string mutation)
    {
        var (state, _) = PausedActionSnapshot(safeSettlement: true);
        var receipt = state.CheckpointReceipts[0];
        var stage = FixturePartyAgencyStage(receipt.Attempt.GameInstanceId);
        var attempt = receipt.Attempt with { PartyAgencyStage = mutation == "ordinary" ? null : stage };
        var echoed = mutation switch
        {
            "ordinary" or "missing_stage" => null,
            "foreign_run" => stage with { LiveRunId = "foreign-run" },
            "foreign_command" => stage with { CommandId = "foreign-command" },
            "foreign_receipt" => stage with { ReceiptId = "foreign-receipt" },
            "foreign_lease" => stage with { ExecutionLeaseId = "foreign-lease" },
            "wrong_generation" => stage with { RetryGeneration = stage.RetryGeneration + 1 },
            "different_agreement" => stage with { Agreement = stage.Agreement with { AgreementId = "other-agreement" } },
            "different_agreement_revision" => stage with { Agreement = stage.Agreement with { AgreementRevision = 3 } },
            "different_hero" => stage with { Agreement = stage.Agreement with { HeroId = "other-hero" } },
            "different_phase" => stage with { Agreement = stage.Agreement with { Phase = "Returning" } },
            _ => stage
        };
        var controller = FixtureCall(new
        {
            ok = true,
            postSaveRuntimeQuiesced = true,
            save = new
            {
                ok = true,
                saveName = attempt.SaveName,
                nativeSave = new { succeeded = true, saveName = attempt.SaveName },
                saveFinalization = new { succeeded = true, completedSaveName = attempt.SaveName },
                checkpointOwnership = new
                {
                    id = attempt.Id, runId = attempt.RunId, gameInstanceId = attempt.GameInstanceId,
                    saveName = attempt.SaveName, savePointId = receipt.SavePointId, sha256 = receipt.Sha256,
                    partyAgencyStage = echoed is null ? (JsonElement?)null
                        : JsonSerializer.SerializeToElement(echoed, JsonSerializerOptions.Web)
                }
            }
        });

        if (mutation is "ordinary" or "matching_stage")
            Assert.Equal(attempt, CampaignTestCheckpointOwnership.ReadCompletion(attempt, controller).Attempt);
        else
            Assert.Throws<InvalidOperationException>(() => CampaignTestCheckpointOwnership.ReadCompletion(attempt, controller));
    }

    [Fact]
    public void CampaignCheckpointStageReservationSurvivesCurrentCheckpointReplacement()
    {
        var (state, _) = PausedActionSnapshot(safeSettlement: true);
        var current = state.CheckpointReceipts[0];
        var attempt = current.Attempt with
        {
            SourceSaveName = state.CurrentSaveName,
            PartyAgencyStage = FixturePartyAgencyStage(current.Attempt.GameInstanceId)
        };
        CampaignTestCheckpointOwnership.ReservePartyAgencyStageReceipt(state, attempt);
        state.PendingCheckpoint = attempt;
        var restored = JsonSerializer.Deserialize<CampaignTestState>(JsonSerializer.Serialize(state))!;
        Assert.Equal(attempt, restored.PendingCheckpoint);
        restored.PendingCheckpoint = null;
        // A later ordinary save replaces the only Current checkpoint receipt.
        restored.CheckpointReceipts[0] = current;
        Assert.Throws<InvalidOperationException>(() =>
            CampaignTestCheckpointOwnership.ReservePartyAgencyStageReceipt(restored, attempt));
        Assert.Single(restored.PartyAgencyCheckpointStageReceiptIds);
    }

    [Theory]
    [InlineData("different_source")]
    [InlineData("different_destination")]
    [InlineData("different_native_load")]
    public void CampaignCheckpointStageRequiresTheOwnedCurrentOnItsRecordedNativeLoad(string mutation)
    {
        var (state, _) = PausedActionSnapshot(safeSettlement: true);
        var current = state.CheckpointReceipts[0];
        var attempt = current.Attempt with
        {
            SourceSaveName = mutation == "different_source" ? state.BaselineSaveName : state.CurrentSaveName,
            SaveName = mutation == "different_destination" ? "some_milestone" : state.CurrentSaveName,
            PartyAgencyStage = FixturePartyAgencyStage(
                mutation == "different_native_load" ? "other-native-load" : current.Attempt.GameInstanceId)
        };
        Assert.Throws<InvalidOperationException>(() =>
            CampaignTestCheckpointOwnership.ReservePartyAgencyStageReceipt(state, attempt));
        Assert.Empty(state.PartyAgencyCheckpointStageReceiptIds);
    }

    private static PartyAgencyCheckpointStage FixturePartyAgencyStage(string gameInstanceId) =>
        new("certification-run", "PA-NATIVE-017", "Completed", "stage-live-run", "stage-command",
            "party_agency_save_stage_123", gameInstanceId, "issued-lease", 2,
            new PartyAgencyCheckpointAgreement("party_guest_456", 2, "hero", "clan", "kingdom", "source", "Completed"));

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
        state.CheckpointReceipts.Add(TestCheckpoint(state, state.CurrentSaveName));
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
