namespace Reign.Mcp.Tests;

public sealed class PostgreSqlOnlyRuntimeContractTests
{
    [Fact]
    public void ProductionServerHasNoSqliteRuntimeDependency()
    {
        var root = TestOptions.FindWorkspace();
        var project = File.ReadAllText(Path.Combine(
            root, "ReignServer", "ReignServer.csproj"));

        Assert.DoesNotContain("Microsoft.Data.Sqlite", project,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SQLitePCLRaw", project,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("e_sqlite3", project,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Npgsql", project,
            StringComparison.OrdinalIgnoreCase);
    }

}
