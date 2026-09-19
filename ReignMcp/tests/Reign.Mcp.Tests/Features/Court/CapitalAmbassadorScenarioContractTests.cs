using System.Text.Json;

namespace Reign.Mcp.Tests;

public sealed class CapitalAmbassadorScenarioContractTests
{
    [Fact]
    public void ManifestAndMcpExposeEveryPreparedBranch()
    {
        string root = TestOptions.FindWorkspace();
        string manifestPath = Path.Combine(root, "ReignServer", "tests", "ReignLiveTest", "scenarios", "capital-ambassador-manifest.json");
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        Assert.Equal(2, manifest.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.True(manifest.RootElement.GetProperty("preparedOnly").GetBoolean());
        Assert.Equal("reign_start_capital_ambassador_test", manifest.RootElement.GetProperty("entryPoint").GetString());
        string[] profiles = manifest.RootElement.GetProperty("profiles").EnumerateArray()
            .Select(item => item.GetString() ?? string.Empty).ToArray();
        Assert.Equal(new[]
        {
            "preflight", "prepare_ownership", "deterministic", "server_matrix", "prepare_travel", "prepare_resident", "observe",
            "lifecycle_a", "move_capital", "simulate_loss", "simulate_war", "lifecycle_b_transition", "save_prepare", "save_verify", "ui",
            "official_dialogue", "official_dialogue_corpus", "cleanup_marker"
        }, profiles);
        Assert.Equal(6, manifest.RootElement.GetProperty("baselineBranches").GetArrayLength());
        Assert.Equal(2, manifest.RootElement.GetProperty("passOnce").GetArrayLength());

        string mcp = File.ReadAllText(TestSourceLocator.Unique(Path.Combine(root, "ReignMcp"), "TestingTools.Court.cs"));
        Assert.Contains("reign_get_capital_ambassador_test_manifest", mcp, StringComparison.Ordinal);
        Assert.Contains("reign_start_capital_ambassador_test", mcp, StringComparison.Ordinal);
        Assert.Contains("start Reign capital ambassador test on disposable save", mcp, StringComparison.Ordinal);
        Assert.Contains("prepare Reign capital ambassador disposable fixture", mcp, StringComparison.Ordinal);
        Assert.Contains("grant Reign capital fixture town on disposable save", mcp, StringComparison.Ordinal);
        Assert.Contains("official_dialogue_corpus", mcp, StringComparison.Ordinal);
        Assert.Contains("bounded ninth natural exchange", mcp, StringComparison.Ordinal);
        Assert.Contains("bounded ninth grant follow-up", manifest.RootElement.GetProperty("releaseGate").GetString(), StringComparison.Ordinal);
        Assert.Contains("lifecycle_a", mcp, StringComparison.Ordinal);
        Assert.Contains("lifecycle_b_transition", mcp, StringComparison.Ordinal);
        Assert.DoesNotContain("Reign_CapitalAmbassador_Roundtrip_A", mcp, StringComparison.Ordinal);
        Assert.DoesNotContain("operation = \"save_checkpoint\"", mcp, StringComparison.Ordinal);
    }

    [Fact]
    public void ServerAndGameAdaptersUseExplicitProductionBoundaries()
    {
        string root = TestOptions.FindWorkspace();
        string liveServer = File.ReadAllText(TestSourceLocator.Unique(Path.Combine(root, "ReignServer"), "LiveInteractionTest.cs", "/src/"));
        Assert.Contains("\"capital_ambassador_test\"", liveServer, StringComparison.Ordinal);
        Assert.Contains("\"ambassador_official\"", liveServer, StringComparison.Ordinal);

        string program = File.ReadAllText(TestSourceLocator.Unique(Path.Combine(root, "ReignServer"), "Program.cs", "/src/Modules/Platform/"));
        Assert.Contains("/court/foreign-ambassadors/test", program, StringComparison.Ordinal);
        string serverMatrix = File.ReadAllText(TestSourceLocator.Unique(Path.Combine(root, "ReignServer"), "CapitalAmbassadorTestSystem.cs", "/src/"));
        Assert.Contains("run Reign capital ambassador server matrix", serverMatrix, StringComparison.Ordinal);
        Assert.Contains("cleanup", serverMatrix, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("server_provider_failure_class_once", serverMatrix, StringComparison.Ordinal);
        Assert.Contains("server_transport_failure_class_once", serverMatrix, StringComparison.Ordinal);
        Assert.Contains("server_retry_exhaustion_class_once", serverMatrix, StringComparison.Ordinal);

        string fixture = File.ReadAllText(TestSourceLocator.Unique(Path.Combine(root, "ReignBeta"), "ReignCapitalAmbassadorInGameTests.cs", "/src/"));
        Assert.Contains("EstablishForeignAmbassadorAsync", fixture, StringComparison.Ordinal);
        Assert.Contains("ProcessForeignAmbassadorTick", fixture, StringComparison.Ordinal);
        Assert.Contains("HandleCapitalLoss", fixture, StringComparison.Ordinal);
        Assert.Contains("OnAmbassadorWarDeclared", fixture, StringComparison.Ordinal);
        Assert.Contains("RunCapitalLifecycleAFixture", fixture, StringComparison.Ordinal);
        Assert.Contains("RunAmbassadorLifecycleBTransitionFixture", fixture, StringComparison.Ordinal);
        Assert.Contains("fixture_preexisting_posting_replaced", fixture, StringComparison.Ordinal);
        Assert.Contains("EndForeignAmbassadorAsync(blockingPosting, \"test_fixture_replaced\")", fixture, StringComparison.Ordinal);
        Assert.Contains("ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, ruler", fixture, StringComparison.Ordinal);
        Assert.Contains("-30 - beforeRelation", fixture, StringComparison.Ordinal);
        Assert.Contains("fixture_origin_relation_boundary", fixture, StringComparison.Ordinal);
        Assert.Contains("occupiedOriginDiagnostics", fixture, StringComparison.Ordinal);
        Assert.Contains("Foreign ambassador posting was not found.", fixture, StringComparison.Ordinal);
        Assert.Contains("serverPostingAbsent", fixture, StringComparison.Ordinal);
        Assert.Contains("EndForeignAmbassador(blockingPosting, \"ended\"", fixture, StringComparison.Ordinal);
        Assert.Contains("blockingPosting.EndReason = \"test_fixture_replaced\"", fixture, StringComparison.Ordinal);
        Assert.Contains("CapitalAmbassadorOwnershipFixtureConfirmation", fixture, StringComparison.Ordinal);
        Assert.Contains("ChangeOwnerOfSettlementAction.ApplyByGift", fixture, StringComparison.Ordinal);
        Assert.Contains("Reload the exact verified disposable baseline", fixture, StringComparison.Ordinal);
        Assert.DoesNotContain("DeclareWarAction.Apply", fixture, StringComparison.Ordinal);

        string host = File.ReadAllText(TestSourceLocator.Unique(Path.Combine(root, "ReignBeta"), "ReignLiveInteractionTestHost.cs", "/src/"));
        Assert.Contains("TryBuildOfficialAmbassadorContext", host, StringComparison.Ordinal);
        Assert.Contains("ambassador_official", host, StringComparison.Ordinal);
        string ui = File.ReadAllText(TestSourceLocator.Unique(Path.Combine(root, "ReignBeta"), "ReignLiveInteractionUiCalibrationHost.cs", "/src/"));
        Assert.Contains("ReignAmbassadorScreenManager.TryExecuteAutomationAction", ui, StringComparison.Ordinal);
        string liveTest = File.ReadAllText(Path.Combine(root, "ReignServer", "tests", "ReignLiveTest", "Program.cs"));
        Assert.Contains("[\"value\"] = Value(args, \"--value\", \"\")", liveTest, StringComparison.Ordinal);
        Assert.Contains("ambassador|economic-report|spymaster|war-council", liveTest, StringComparison.Ordinal);
    }

    [Fact]
    public void FixtureLedgerIsSaveRegisteredAndServerTestHookIsIsolated()
    {
        string root = TestOptions.FindWorkspace();
        string saveDefiner = File.ReadAllText(TestSourceLocator.Unique(Path.Combine(root, "ReignBeta"), "ReignBetaSaveDefiner.cs", "/src/"));
        Assert.Contains("CapitalAmbassadorTestLedger", saveDefiner, StringComparison.Ordinal);
        string behavior = File.ReadAllText(TestSourceLocator.Unique(Path.Combine(root, "ReignBeta"), "ReignCourtCampaignBehavior.cs", "/src/"));
        Assert.Contains("_reignCourt_capitalAmbassadorTestLedgers", behavior, StringComparison.Ordinal);
        Assert.Contains("CompactCapitalAmbassadorTestLedgers();", behavior, StringComparison.Ordinal);
        string fixture = File.ReadAllText(TestSourceLocator.Unique(Path.Combine(root, "ReignBeta"), "ReignCapitalAmbassadorInGameTests.cs", "/src/"));
        Assert.Contains("capital_harness_save_payload_bounded", fixture, StringComparison.Ordinal);
        Assert.Contains("CapitalAmbassadorLedgerMaxStoredJsonChars", fixture, StringComparison.Ordinal);

        string foreign = File.ReadAllText(TestSourceLocator.Unique(Path.Combine(root, "ReignServer"), "ForeignAmbassadorSystem.cs", "/src/"));
        Assert.Contains("forcedTestClassification", foreign, StringComparison.Ordinal);
        Assert.Contains("string forcedTestClassification = \"\"", foreign, StringComparison.Ordinal);
        Assert.Contains("forced_test_hook", foreign, StringComparison.Ordinal);
        string docs = File.ReadAllText(Path.Combine(root, "ReignServer", "docs", "server", "CapitalAmbassadorTesting.md"));
        Assert.Contains("two compact end-to-end native lifecycles", docs, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sixteen player/NPC turns", docs, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("never creates a fixed", docs, StringComparison.OrdinalIgnoreCase);
    }

}
