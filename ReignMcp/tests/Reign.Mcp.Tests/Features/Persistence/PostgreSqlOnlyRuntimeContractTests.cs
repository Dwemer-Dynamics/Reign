namespace Reign.Mcp.Tests;

public sealed class PostgreSqlOnlyRuntimeContractTests
{
    [Fact]
    public void ProductionServerHasNoSqliteRuntimeDependency()
    {
        var root = TestOptions.FindWorkspace();
        var project = File.ReadAllText(Path.Combine(
            root, "ReignBetaServer", "ReignBetaServer.csproj"));

        Assert.DoesNotContain("Microsoft.Data.Sqlite", project,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SQLitePCLRaw", project,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("e_sqlite3", project,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Npgsql", project,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SqliteDependencyIsIsolatedToExplicitImporter()
    {
        var root = TestOptions.FindWorkspace();
        var importer = File.ReadAllText(Path.Combine(
            root,
            "ReignTools",
            "Reign.LegacySqliteImporter",
            "Reign.LegacySqliteImporter.csproj"));

        Assert.Contains("Microsoft.Data.Sqlite", importer,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Npgsql", importer,
            StringComparison.OrdinalIgnoreCase);
    }
}
