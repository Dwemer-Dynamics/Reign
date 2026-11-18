namespace Reign.Mcp.Tests;

internal static class TestSourceLocator
{
    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".codex-build", "artifacts", "bin", "obj", "staging",
        "deployment-backups", "verification_contracts", "publish"
    };

    public static string Unique(string root, string fileName, params string[] preferredFragments)
    {
        var candidates = Enumerate(root, fileName).ToArray();
        foreach (var fragment in preferredFragments)
        {
            var preferred = candidates.Where(path => path.Replace('\\', '/')
                .Contains(fragment.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase)).ToArray();
            if (preferred.Length == 1) return preferred[0];
            if (preferred.Length > 1) candidates = preferred;
        }
        return candidates.Length switch
        {
            1 => candidates[0],
            0 => throw new FileNotFoundException($"Could not locate {fileName} beneath {root}."),
            _ => throw new InvalidDataException($"Source {fileName} is ambiguous: {string.Join(", ", candidates)}")
        };
    }

    // All ReignServer source text, for route declarations that may live either in the
    // legacy HandleClient chain or in a module's route-table registrations.
    public static string ServerRouterText(string workspace) => string.Join(Environment.NewLine,
        Enumerate(Path.Combine(workspace, "ReignServer", "src"), "*.cs")
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(File.ReadAllText));

    // True when the server declares this exact route: as a legacy chain comparison or a
    // route-table registration (routes.Get/Post). Mentions elsewhere do not count.
    public static bool ServerDeclaresRoute(string routerText, string route) =>
        routerText.Contains($"request.Path == \"{route}\"", StringComparison.Ordinal)
        || routerText.Contains($"case \"{route}\":", StringComparison.Ordinal)
        || routerText.Contains($".Get(\"{route}\",", StringComparison.Ordinal)
        || routerText.Contains($".Post(\"{route}\",", StringComparison.Ordinal);

    // True when the server declares this route prefix: a legacy StartsWith comparison or a
    // route-table prefix registration (routes.GetPrefix/PostPrefix).
    public static bool ServerDeclaresRoutePrefix(string routerText, string prefix) =>
        routerText.Contains($"request.Path.StartsWith(\"{prefix}\"", StringComparison.Ordinal)
        || routerText.Contains($".GetPrefix(\"{prefix}\",", StringComparison.Ordinal)
        || routerText.Contains($".PostPrefix(\"{prefix}\",", StringComparison.Ordinal);

    private static IEnumerable<string> Enumerate(string root, string fileName)
    {
        var layout = new Reign.Mcp.Server.ReignSourceLayout(TestOptions.FindWorkspace());
        layout.ValidateProjections();
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new DirectoryInfo(root));
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            if (!directory.Exists || !layout.CanTraverse(directory)
                || Excluded.Contains(directory.Name)) continue;
            foreach (var file in directory.EnumerateFiles(fileName)) yield return file.FullName;
            foreach (var child in directory.EnumerateDirectories()) pending.Push(child);
        }
    }
}
