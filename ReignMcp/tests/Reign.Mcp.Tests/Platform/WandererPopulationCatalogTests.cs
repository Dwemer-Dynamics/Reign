using System.Text.Json;
using Reign.Mcp.Server;

namespace Reign.Mcp.Tests;

public sealed class WandererPopulationCatalogTests
{
    [Fact]
    public void CatalogDeclaresPopulationEvidenceAndSeparateNativeAcceptance()
    {
        string root = TestOptions.FindWorkspace();
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reign.testing.json")));
        var entry = document.RootElement.GetProperty("wandererPopulation");
        Assert.Equal("reign-wanderer-population-v1", entry.GetProperty("schema").GetString());
        Assert.Equal("_reign_wanderer_population_v1", entry.GetProperty("saveNamespace").GetString());
        Assert.Contains("wanderer_population", entry.GetProperty("verification").GetString());
        Assert.Contains("observational", entry.GetProperty("contactHistory").GetString());
        Assert.Contains("current-town menu", entry.GetProperty("population").GetString());
        Assert.Contains("active missions", entry.GetProperty("population").GetString());
        Assert.Contains("disposable", entry.GetProperty("nativeAcceptance").GetString());
        Assert.Contains("Offline", entry.GetProperty("nativeAcceptance").GetString());
        foreach (string path in new[] { "docs/agent/TESTING_TOOL_GUIDE.md", "ReignMcp/docs/tool-catalog.md", "ReignMcp/docs/security-model.md" })
            Assert.Contains("wandererPopulation", TestingDocumentation.Read(Path.Combine(root, path)));
    }
}
