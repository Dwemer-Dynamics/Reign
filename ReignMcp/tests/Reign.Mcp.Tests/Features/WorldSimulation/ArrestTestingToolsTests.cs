using Reign.Mcp.Server;

namespace Reign.Mcp.Tests;

public sealed class ArrestTestingToolsTests
{
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
            Path.Combine(workspace, "ReignBetaServer"),
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

        string liveClient = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(workspace, "ReignBeta"),
            "ReignLiveTestClient.cs",
            "/src/Modules/Platform/Integration/"));
        Assert.Contains("internal static string ActiveNativeSaveName()", liveClient,
            StringComparison.Ordinal);
        Assert.Contains("ActiveSaveSlotName", liveClient,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Conversation_router_contains_negative_and_hypothetical_guards()
    {
        var workspace = TestOptions.FindWorkspace();
        string source = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(workspace, "ReignBetaServer"),
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
