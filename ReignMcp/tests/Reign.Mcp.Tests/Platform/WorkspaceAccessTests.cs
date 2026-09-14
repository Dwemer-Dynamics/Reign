using Reign.Mcp.Server;

namespace Reign.Mcp.Tests;

public sealed class WorkspaceAccessTests
{
    [Fact]
    public void ReadsAndSearchesAllowlistedWorkspaceSource()
    {
        var service = NewService();

        var document = service.ReadSource(
            Path.Combine("ReignBetaServer", "src", "Modules", "WorldSimulation", "WorldTest.cs"),
            1,
            20);
        var search = service.Search(
            "WorldTestHeartbeatApi",
            "ReignBetaServer",
            10,
            caseSensitive: true);

        Assert.Contains("WorldTest.cs", document.Path, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(search.Matches);
        Assert.All(search.Matches, match =>
            Assert.DoesNotContain("staging", match.Path, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("../REIGN_ROADMAP.md")]
    [InlineData("ReignBeta/server/app/data/settings.json")]
    [InlineData("ReignBeta/staging/anything.txt")]
    [InlineData("logs/verification-offline.json")]
    public void RefusesTraversalAndSensitiveGeneratedData(string path)
    {
        var service = NewService();
        Assert.ThrowsAny<Exception>(() => service.ReadSource(path, 1, 20));
    }

    [Fact]
    public void WorkspaceStatusIncludesCoreComponents()
    {
        var status = NewService().GetStatus();
        Assert.Contains(status.Components, component =>
            component.Name == "Reign server" && component.Exists);
        Assert.Contains(status.Components, component =>
            component.Name == "Reign MCP" && component.Exists);
    }

    private static WorkspaceAccess NewService()
    {
        return new WorkspaceAccess(TestOptions.Create(), new SensitiveDataRedactor());
    }
}
