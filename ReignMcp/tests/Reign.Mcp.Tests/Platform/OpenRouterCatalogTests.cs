using System.Text.Json;

namespace Reign.Mcp.Tests;

public sealed class OpenRouterCatalogTests
{
    [Fact]
    public void OpenRouterUsesExistingIsolatedSuitesAndExactReferenceImageCatalog()
    {
        string root = TestOptions.FindWorkspace();
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reign.testing.json")));
        var entry = doc.RootElement.GetProperty("openRouterProviders");
        Assert.Equal("reign-openrouter-providers-v1", entry.GetProperty("schema").GetString());
        Assert.Equal("https://openrouter.ai/api/v1/images", entry.GetProperty("imageEndpoint").GetString());
        Assert.Equal("openRouterApiKey", entry.GetProperty("sharedKey").GetString());
        Assert.Equal(5, entry.GetProperty("imageModels").GetArrayLength());
        string source = File.ReadAllText(Path.Combine(root, "ReignBetaServer/src/Modules/Portraits/OpenRouterImageProvider.cs"));
        foreach (var model in entry.GetProperty("imageModels").EnumerateArray()) Assert.Contains(model.GetString()!, source);
        Assert.Contains("input_references", source);
        foreach (var contract in entry.GetProperty("browserContracts").EnumerateArray()) Assert.True(File.Exists(Path.Combine(root, contract.GetString()!)));
        foreach (string file in new[] { "docs/agent/TESTING_TOOL_GUIDE.md", "ReignMcp/docs/tool-catalog.md", "ReignMcp/docs/security-model.md", "ReignBetaServer/docs/VerificationLab.md" })
            Assert.Contains("reign-openrouter-providers-v1", TestingDocumentation.Read(Path.Combine(root, file)));
    }
}
