using Newtonsoft.Json.Linq;
using ReignBeta.Court.Campaign;
using ReignBeta.WorldSimulation.Arrests;
using Reign.Mcp.Server;

namespace Reign.Mcp.Tests;

public sealed class ArrestTestingToolsTests
{
    [Theory]
    [InlineData("exact", true)]
    [InlineData("deferred", true)]
    [InlineData("unrevealed", false)]
    [InlineData("unreliable", false)]
    [InlineData("incomplete", false)]
    [InlineData("duplicate-chain", false)]
    [InlineData("reserved", false)]
    [InlineData("wrong-campaign", false)]
    [InlineData("wrong-timeline", false)]
    [InlineData("actor-case-variant", false)]
    [InlineData("victim", false)]
    [InlineData("formally-accused-only", false)]
    [InlineData("acquitted", false)]
    [InlineData("convicted", false)]
    [InlineData("reversed", false)]
    public void MurderRequiresAPublicCompleteChainForTheExactImplicatedHero(string changed, bool accepted)
    {
        var chain = new ReignNobleEvidenceItem
        {
            EvidenceId = "complete_chain", ImplicatesHeroId = "implicated",
            Revealed = true, Reliable = true, CompleteChain = true
        };
        var matter = new ReignNobleDocketMatter
        {
            MatterId = "matter", CampaignId = "campaign", TimelineId = "timeline", TemplateId = "exceptional-murder",
            ActivationCommitted = true, ActivationReceiptId = "activation", State = ReignNobleMatterState.Active,
            VictimHeroId = "victim", ActualCulpritHeroId = "private-only",
            Participants = [new() { HeroId = "implicated" }, new() { HeroId = "named", Role = "formally_accused" }],
            Evidence = [chain]
        };
        string campaign = "campaign", timeline = "timeline", accused = "implicated";
        switch (changed)
        {
            case "deferred": matter.State = ReignNobleMatterState.Deferred; break;
            case "unrevealed": chain.Revealed = false; matter.ActualCulpritHeroId = accused; break;
            case "unreliable": chain.Reliable = false; break;
            case "incomplete": chain.CompleteChain = false; break;
            case "duplicate-chain": matter.Evidence.Add(new() { EvidenceId = "Complete_Chain" }); break;
            case "reserved": matter.ActivationCommitted = false; break;
            case "wrong-campaign": campaign = "other"; break;
            case "wrong-timeline": timeline = "other"; break;
            case "actor-case-variant": accused = "Implicated"; break;
            case "victim": accused = "victim"; chain.ImplicatesHeroId = accused; break;
            case "formally-accused-only": accused = "named"; break;
            case "acquitted": matter.State = ReignNobleMatterState.Acquitted; break;
            case "convicted": matter.State = ReignNobleMatterState.Convicted; matter.ConvictedHeroId = accused; break;
            case "reversed": matter.State = ReignNobleMatterState.Reversed; break;
        }
        Assert.Equal(accepted ? "noble_murder:matter:complete_chain" : "",
            matter.PublicMurderArrestEvidenceId(campaign, timeline, accused));
    }

    [Theory]
    [InlineData("exact", true)]
    [InlineData("old-server", false)]
    [InlineData("campaignId", false)]
    [InlineData("timelineId", false)]
    [InlineData("chargeCategory", false)]
    [InlineData("accusedHeroId", false)]
    [InlineData("evidenceStatus", false)]
    [InlineData("wrong-role", false)]
    [InlineData("wrong-subject", false)]
    [InlineData("unknown-kind", false)]
    [InlineData("legacy-id-mismatch", false)]
    [InlineData("empty", false)]
    [InlineData("hidden", false)]
    [InlineData("coerced-boolean", false)]
    public void EvidenceResponseBindsChargeOwnerAndTypedActor(string changed, bool accepted)
    {
        var request = JObject.Parse("""
            {"schema":"reign-arrest-evidence-request-v2","campaignId":"campaign","timelineId":"timeline",
            "chargeCategory":"espionage","accusedHeroId":"npc"}
            """);
        var response = JObject.Parse("""
            {"schema":"reign-arrest-evidence-response-v2","campaignId":"campaign","timelineId":"timeline",
            "chargeCategory":"espionage","accusedHeroId":"npc","evidenceStatus":"established",
            "ok":true,"causeEstablished":true,"hiddenAgentInformationRevealed":false,
            "evidenceIds":["foreign_agent:npc:sponsor"],
            "evidence":[{"kind":"foreign_agent_exposure","subjectRole":"agent","subjectHeroId":"npc",
            "evidenceId":"foreign_agent:npc:sponsor"}]}
            """);
        var row = (JObject)response["evidence"]![0]!;
        switch (changed)
        {
            case "exact": break;
            case "old-server": response.Remove("schema"); response.Remove("evidence"); break;
            case "wrong-role": row["subjectRole"] = "witness"; break;
            case "wrong-subject": row["subjectHeroId"] = "other"; break;
            case "unknown-kind": row["kind"] = "generic_world_event"; break;
            case "legacy-id-mismatch": response["evidenceIds"] = new JArray("foreign_agent:npc:other"); break;
            case "empty": response["evidence"] = new JArray(); break;
            case "hidden": response["hiddenAgentInformationRevealed"] = true; break;
            case "coerced-boolean": response["causeEstablished"] = "true"; break;
            default: response[changed] = "other"; break;
        }
        Assert.Equal(accepted, ReignArrestEvidenceResponse.TryRead(request, response, out string evidenceId));
        Assert.Equal(accepted ? "foreign_agent:npc:sponsor" : "", evidenceId);
    }

    [Fact]
    public void Manifest_exposes_one_durable_cli_and_both_test_surfaces()
    {
        IReadOnlyDictionary<string, object?> manifest =
            TestingTools.GetArrestTestManifest(TestOptions.Create());

        Assert.Equal(2, manifest["schemaVersion"]);
        Assert.Equal("ReignLiveTest.exe arrest --profile <profile>",
            manifest["cliEntryPoint"]);
        string[] deterministic = Assert.IsType<string[]>(manifest["deterministicProfiles"]);
        Assert.Contains("save_prepare", deterministic);
        Assert.Contains("save_verify", deterministic);
        Assert.Contains("relationship", deterministic);
        Assert.Contains("duel", deterministic);
        Assert.Contains("battle", deterministic);
        Assert.Contains("town", deterministic);
        Assert.Contains("castle", deterministic);
        Assert.Contains("sovereignty", deterministic);
        string[] organic = Assert.IsType<string[]>(manifest["organicProfiles"]);
        Assert.Contains("town_guard", organic);
        Assert.Contains("surrender_duel", organic);
        Assert.Contains("party_battle", organic);
        Assert.Contains("evidence_agent", organic);

        string manifestPath = Assert.IsType<string>(manifest["manifestPath"]);
        using var document = System.Text.Json.JsonDocument.Parse(
            File.ReadAllText(manifestPath));
        Assert.Equal("reign-arrest-equivalence-manifest-v2",
            document.RootElement.GetProperty("schema").GetString());
        Assert.Equal(36,
            document.RootElement.GetProperty("languageCases").GetArrayLength());
        Assert.Equal(5,
            document.RootElement.GetProperty("deterministicCases").GetArrayLength());
        Assert.Equal(9,
            document.RootElement.GetProperty("nativeCases").GetArrayLength());
    }

    [Fact]
    public void Certification_is_fingerprint_bound_and_derives_the_guarded_current_save()
    {
        var workspace = TestOptions.FindWorkspace();
        string source = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(workspace, "ReignMcp"),
            "TestingTools.Arrest.cs",
            "/src/Reign.Mcp.Server/Modules/Platform/"));

        Assert.Contains("Name = \"reign_prepare_arrest_certification\"", source,
            StringComparison.Ordinal);
        Assert.Contains("RequireArmedOwnedSaveAsync", source,
            StringComparison.Ordinal);
        Assert.Contains("Name = \"reign_carry_forward_arrest_passes\"", source,
            StringComparison.Ordinal);
        Assert.Contains("ArrestFingerprintsMatch", source,
            StringComparison.Ordinal);
        Assert.Contains("campaign_checkpoint_restore_harness_only", source,
            StringComparison.Ordinal);
        Assert.Contains("arrest_sovereignty_fixture_only", source,
            StringComparison.Ordinal);
        Assert.Contains("ArrestSovereigntyMigrationCarryInputsMatch", source,
            StringComparison.Ordinal);
        Assert.Contains("actualMapBattleProven", source,
            StringComparison.Ordinal);
        Assert.Contains("Name = \"reign_evaluate_arrest_release_readiness\"",
            source, StringComparison.Ordinal);
    }

    [Fact]
    public void Contract_tool_is_read_only_and_routes_to_server_authority()
    {
        var workspace = TestOptions.FindWorkspace();
        string source = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(workspace, "ReignMcp"),
            "TestingTools.Arrest.cs",
            "/src/Reign.Mcp.Server/Modules/Platform/"));
        string server = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(workspace, "ReignServer"),
            "ArrestSystem.cs",
            "/src/Modules/WorldSimulation/"));

        Assert.Contains("Name = \"reign_run_arrest_contract_tests\", ReadOnly = true",
            source, StringComparison.Ordinal);
        Assert.Contains("/arrests/test/contracts", source, StringComparison.Ordinal);
        Assert.Contains("reign-arrest-contract-report-v2", server,
            StringComparison.Ordinal);
        Assert.Contains("AR-LANG-036", server, StringComparison.Ordinal);
        Assert.Contains("ArrestRelationshipCorrection(\"rescind\"", server,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Destructive_organic_entry_point_requires_explicit_disposable_save()
    {
        var workspace = TestOptions.FindWorkspace();
        string source = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(workspace, "ReignMcp"),
            "TestingTools.Arrest.cs",
            "/src/Reign.Mcp.Server/Modules/Platform/"));

        Assert.Contains("start Reign organic arrest test on disposable save", source,
            StringComparison.Ordinal);
        Assert.Contains("requireDisposableSave = true", source,
            StringComparison.Ordinal);
        Assert.Contains("StartsWith(\"Reign_\"", source,
            StringComparison.Ordinal);
        Assert.Contains("StartsWith(\"ReignTest_\"", source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Client_host_keeps_test_fixtures_namespaced_and_exactly_cleanable()
    {
        var workspace = TestOptions.FindWorkspace();
        string behavior = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(workspace, "ReignBeta"),
            "ReignArrestCampaignBehavior.cs",
            "/src/Modules/WorldSimulation/Arrests/"));
        string host = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(workspace, "ReignBeta"),
            "ReignArrestTestHost.cs",
            "/src/Modules/WorldSimulation/Arrests/"));

        Assert.Contains("arrest_test_", behavior, StringComparison.Ordinal);
        Assert.Contains("PrepareSerializationFixtures", host,
            StringComparison.Ordinal);
        Assert.Contains("CleanupSerializationFixtures", host,
            StringComparison.Ordinal);
        Assert.Contains("explicit_disposable_save", host,
            StringComparison.Ordinal);
        Assert.Contains("ReignServerClient.ActiveNativeSaveName()", host,
            StringComparison.Ordinal);

        string liveClient = TestSourceLocator.PartialTypeText(TestSourceLocator.Unique(
            Path.Combine(workspace, "ReignBeta"),
            "ReignServerClient.cs",
            "/src/Modules/Platform/Integration/"));
        Assert.Contains("internal static string ActiveNativeSaveName()", liveClient,
            StringComparison.Ordinal);
        Assert.Contains("ActiveSaveSlotName", liveClient,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Explicit_arrest_case_is_bound_to_the_action_target_before_legacy_fallback()
    {
        var workspace = TestOptions.FindWorkspace();
        string behavior = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(workspace, "ReignBeta"),
            "ReignArrestCampaignBehavior.cs",
            "/src/Modules/WorldSimulation/Arrests/"));

        int resolverStart = behavior.IndexOf("private ReignArrestCase ResolveActionCase", StringComparison.Ordinal);
        int resolverEnd = behavior.IndexOf("private static void AddHistory", resolverStart, StringComparison.Ordinal);
        string resolver = behavior[resolverStart..resolverEnd];
        int exactCaseStart = resolver.IndexOf("if (!string.IsNullOrWhiteSpace(id))", StringComparison.Ordinal);
        int legacyStart = resolver.IndexOf("return ActiveCaseFor(accused)", StringComparison.Ordinal);
        string exactCase = resolver[exactCaseStart..legacyStart];
        Assert.Contains("ReignArrestCase item = FindCase(id);", exactCase, StringComparison.Ordinal);
        Assert.Contains("item.AccusedHeroStringId, accused.StringId", exactCase, StringComparison.Ordinal);
        Assert.Contains("? item : null;", exactCase, StringComparison.Ordinal);

        int releaseStart = behavior.IndexOf("public ReignActionResult ReleaseArrested", StringComparison.Ordinal);
        int releaseEnd = behavior.IndexOf("public ReignActionResult AttackTargetParty", releaseStart, StringComparison.Ordinal);
        string release = behavior[releaseStart..releaseEnd];
        Assert.Contains("ResolveActionCase(action, accused, includeClosedAccusation: true);", release, StringComparison.Ordinal);
        Assert.DoesNotContain("?? _cases", release, StringComparison.Ordinal);
    }

    [Fact]
    public void Conversation_router_contains_negative_and_hypothetical_guards()
    {
        var workspace = TestOptions.FindWorkspace();
        string source = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(workspace, "ReignServer"),
            "ArrestSystem.cs",
            "/src/Modules/WorldSimulation/"));

        Assert.Contains("do not arrest", source, StringComparison.Ordinal);
        Assert.Contains("if i arrest", source, StringComparison.Ordinal);
        Assert.Contains("would arrest", source, StringComparison.Ordinal);
        Assert.Contains("arrest_reason_required", File.ReadAllText(
            TestSourceLocator.Unique(Path.Combine(workspace, "ReignBeta"),
                "ReignArrestCampaignBehavior.cs",
                "/src/Modules/WorldSimulation/Arrests/")),
            StringComparison.Ordinal);
    }
}
