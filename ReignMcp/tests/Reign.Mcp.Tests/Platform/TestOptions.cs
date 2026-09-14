using Reign.Mcp.Server;

namespace Reign.Mcp.Tests;

internal static class TestOptions
{
    public static ReignMcpOptions Create(
        string? root = null,
        int maxApiBytes = 1024 * 1024)
    {
        root ??= FindWorkspace();
        return new ReignMcpOptions
        {
            WorkspaceRoot = root,
            ServerBaseUri = new Uri("http://127.0.0.1:5101"),
            BuildRoot = Path.Combine(root, ".codex-build", "reign-mcp-tests"),
            MaxApiResponseBytes = maxApiBytes,
            MaxToolTextBytes = 128 * 1024,
            ApiTimeout = TimeSpan.FromSeconds(5),
            ProcessTimeout = TimeSpan.FromSeconds(30)
        };
    }

    public static string FindWorkspace()
    {
        var cursor = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; cursor is not null && depth < 12; depth++, cursor = cursor.Parent)
        {
            if (File.Exists(Path.Combine(cursor.FullName, "REIGN_ROADMAP.md"))
                && Directory.Exists(Path.Combine(cursor.FullName, "ReignBetaServer")))
            {
                return cursor.FullName;
            }
        }
        throw new DirectoryNotFoundException("Test workspace not found.");
    }
}

