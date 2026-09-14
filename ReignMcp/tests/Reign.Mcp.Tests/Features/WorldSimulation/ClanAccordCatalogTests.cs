using System.Text.Json;

namespace Reign.Mcp.Tests;

public sealed class ClanAccordCatalogTests
{
    [Fact]
    public void CatalogKeepsNativeContractsAndDeploymentAcceptanceSeparate()
    {
        string root = TestOptions.FindWorkspace();
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reign.testing.json")));
        var accords = document.RootElement.GetProperty("clanAccords");
        Assert.Equal("_reign_clan_accords_v1", accords.GetProperty("saveNamespace").GetString());
        Assert.Contains("world_diplomacy", accords.GetProperty("verification").GetString());
        Assert.Contains("clan_accords_test", accords.GetProperty("nativeContracts").GetString());
        Assert.Contains("disposable", accords.GetProperty("nativeAcceptance").GetString());
        Assert.Contains("Offline evidence is not native acceptance", accords.GetProperty("nativeAcceptance").GetString());
        string host = File.ReadAllText(Path.Combine(root, "ReignBeta/src/Modules/WorldSimulation/Campaign/ReignLiveInteractionTestHost.cs"));
        Assert.Contains("case \"clan_accords_test\":", host);
        string gate = File.ReadAllText(Path.Combine(root, "ReignBetaServer/ReignLiveTest/Features/WorldSimulation/PassiveWorldControl.cs"));
        Assert.Contains("ClanAccordsRuntimeQuiescent(nativeRuntime)", gate);
        Assert.Contains("ClanAccordsRuntimeQuiescent(runtime)", gate);
        Assert.Contains("accords.ContainsKey(\"pending\")", gate);
        foreach (string path in new[] { "docs/agent/TESTING_TOOL_GUIDE.md", "ReignMcp/docs/tool-catalog.md", "ReignMcp/docs/security-model.md" })
        {
            string text = TestingDocumentation.Read(Path.Combine(root, path));
            Assert.Contains("clan_accords_test", text);
            Assert.Contains("runtime.clanAccords", text);
        }
    }
}
