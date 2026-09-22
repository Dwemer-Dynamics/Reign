using System.Text.Json;

namespace Reign.Mcp.Tests;

public sealed class CodexImageCatalogTests
{
    [Fact]
    public void ImageCompatibilityStaysExplicitBoundedAndNormalOnly()
    {
        string root = TestOptions.FindWorkspace();
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reign.testing.json")));
        var entry = document.RootElement.GetProperty("codexImages");
        Assert.Equal("reign-codex-image-compatibility-v1", entry.GetProperty("schema").GetString());
        Assert.Equal("codex_images", entry.GetProperty("liveSuite").GetString());
        Assert.Equal("live-llm", entry.GetProperty("liveTier").GetString());
        Assert.Equal(3, entry.GetProperty("maximumLiveCases").GetInt32());
        Assert.Equal(new[] { "portrait", "scenery" }, entry.GetProperty("normalProfiles").EnumerateArray().Select(x => x.GetString()).ToArray());
        Assert.Equal(new[] { "adultPortrait", "adultScenery" }, entry.GetProperty("blockedProfiles").EnumerateArray().Select(x => x.GetString()).ToArray());
        Assert.Contains("explicit subscription-usage authorization", entry.GetProperty("liveRoute").GetString());
        Assert.Contains("never stop the shared text provider", entry.GetProperty("isolation").GetString());
        Assert.True(File.Exists(Path.Combine(root, entry.GetProperty("browserContract").GetString()!)));
        foreach (string path in new[] { "docs/agent/TESTING_TOOL_GUIDE.md", "ReignMcp/docs/tool-catalog.md", "ReignMcp/docs/security-model.md", "ReignServer/docs/server/VerificationLab.md" })
            Assert.Contains("reign-codex-image-compatibility-v1", TestingDocumentation.Read(Path.Combine(root, path)));
    }
}
