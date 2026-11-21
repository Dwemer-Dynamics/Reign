using Reign.Mcp.Server;

namespace Reign.Mcp.Tests;

public sealed class WorkspaceAccessTests
{
    [Fact]
    public void ReadsAndSearchesAllowlistedWorkspaceSource()
    {
        var service = NewService();

        var document = service.ReadSource(
            Path.Combine("ReignServer", "src", "Modules", "WorldSimulation", "WorldTest.cs"),
            1,
            20);
        var search = service.Search(
            "WorldTestHeartbeatApi",
            "ReignServer",
            10,
            caseSensitive: true);

        Assert.Contains("WorldTest.cs", document.Path, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(search.Matches);
        Assert.All(search.Matches, match =>
            Assert.DoesNotContain("staging", match.Path, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SourceToolsPreserveCodeAndMarkCredentialsAtTheirOriginalLineNumbers()
    {
        using var fixture = new SourceFixture();
        const string assignment = "    JToken token = obj[key];";
        const string comparison = "    return token == null ? fallback : token.Value<float>();";
        const string redacted = "[REDACTED: possible credential]";
        fixture.Write("Fixture.cs", [
            "{",
            assignment,
            comparison,
            """    string token = "fixture";""",
            "}"
        ]);

        var document = WorkspaceTools.ReadSource(fixture.Service, "Fixture.cs", startLine: 2, lineCount: 3);
        var search = WorkspaceTools.SearchSource(fixture.Service, "token", caseSensitive: true);

        Assert.Equal(2, document.StartLine);
        Assert.Equal(4, document.EndLine);
        Assert.True(document.Truncated);
        Assert.Equal($"     2: {assignment}{Environment.NewLine}"
            + $"     3: {comparison}{Environment.NewLine}"
            + $"     4: {redacted}{Environment.NewLine}", document.Text);
        Assert.Collection(search.Matches,
            match => Assert.Equal(new SourceMatch("Fixture.cs", 2, assignment), match),
            match => Assert.Equal(new SourceMatch("Fixture.cs", 3, comparison), match),
            match => Assert.Equal(new SourceMatch("Fixture.cs", 4, redacted), match));
    }

    [Fact]
    public void SourceSearchChecksTheWholeLineBeforeTruncatingItsSnippet()
    {
        using var fixture = new SourceFixture();
        var prefix = "// fixture " + new string('x', 479);
        var harmless = "// fixture " + string.Concat(Enumerable.Repeat("xy-", 32_768));
        fixture.Write("Fixture.cs", [
            prefix + " Bearer syntheticFixtureToken",
            harmless
        ]);

        var search = WorkspaceTools.SearchSource(fixture.Service, "fixture ");

        Assert.Collection(search.Matches,
            match => Assert.Equal("[REDACTED: possible credential]", match.Text),
            match => Assert.Equal(harmless[..500] + "…", match.Text));
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

    private sealed class SourceFixture : IDisposable
    {
        private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("reign-source-tests-");

        public WorkspaceAccess Service => new(TestOptions.Create(_root.FullName), new SensitiveDataRedactor());

        public void Write(string file, string[] lines) => File.WriteAllLines(Path.Combine(_root.FullName, file), lines);

        public void Dispose() => _root.Delete(recursive: true);
    }
}
