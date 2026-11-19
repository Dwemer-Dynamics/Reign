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

    // Text of a client class split into partial files: the primary <Type>.cs file followed by its
    // sibling <Type>.<Area>.cs partial files in the same folder, in name order.
    public static string PartialTypeText(string primaryPath)
    {
        var directory = Path.GetDirectoryName(primaryPath)!;
        var stem = Path.GetFileNameWithoutExtension(primaryPath);
        var parts = Directory.EnumerateFiles(directory, stem + ".*.cs")
            .Order(StringComparer.OrdinalIgnoreCase);
        return string.Join(Environment.NewLine, new[] { primaryPath }.Concat(parts).Select(File.ReadAllText));
    }

    // Server router text: the legacy HandleClient chain and every module's route-table
    // registrations (Program*.cs, which includes Program.<Module>Routes.cs).
    public static string ServerRouterText(string workspace) => string.Join(Environment.NewLine,
        Enumerate(Path.Combine(workspace, "ReignServer", "src"), "Program*.cs")
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(File.ReadAllText));

    // Server Program source: the Platform/Program.cs hub and every Program.<Area>.cs partial (route files included),
    // wherever it lives under ReignServer/src. Accepts the former single-file layout and the split layout.
    public static string ServerProgramText(string workspace) => ServerRouterText(workspace);

    // The first server Program*.cs file (path order) whose text contains anchor, for checks that slice or order text
    // inside one member. Accepts the former single-file layout and the split layout.
    public static string ServerProgramFileText(string workspace, string anchor) =>
        Enumerate(Path.Combine(workspace, "ReignServer", "src"), "Program*.cs")
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(File.ReadAllText)
            .FirstOrDefault(text => text.Contains(anchor, StringComparison.Ordinal))
        ?? throw new FileNotFoundException($"No server Program*.cs file contains {anchor}.");

    // A server module's SQL text: every Modules/<module>/Data/*.cs file (stores and schema), ordinal name order.
    // Server SQL lives in these stores, so a check that looked for a statement in a feature file also reads them.
    // Empty when the module has no Data folder.
    public static string ServerModuleDataText(string workspace, string module)
    {
        string data = Path.Combine(workspace, "ReignServer", "src", "Modules", module, "Data");
        return Directory.Exists(data)
            ? string.Join(Environment.NewLine, Directory.EnumerateFiles(data, "*.cs").Order(StringComparer.Ordinal).Select(File.ReadAllText))
            : string.Empty;
    }

    // A server source file plus its Name.*.cs partial-class siblings in the same folder (ordinal file-name order), so
    // a check that reads one logical file keeps working when the file is split into partial files. Accepts the
    // former single-file layout and the split layout.
    public static string ServerFileGroupText(string primaryPath)
    {
        string fullPath = Path.GetFullPath(primaryPath);
        string stem = Path.GetFileNameWithoutExtension(fullPath);
        var siblings = Directory.EnumerateFiles(Path.GetDirectoryName(fullPath)!, stem + ".*.cs")
            .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal);
        return string.Join("\n\n", new[] { fullPath }.Concat(siblings).Select(File.ReadAllText));
    }

    // True when the server declares this exact route: as a legacy chain comparison or a
    // route-table registration (routes.Get/Post). Other mentions, such as HTTP client calls, do not count.
    public static bool ServerDeclaresRoute(string routerText, string route) =>
        routerText.Contains($"request.Path == \"{route}\"", StringComparison.Ordinal)
        || routerText.Contains($"case \"{route}\":", StringComparison.Ordinal)
        || routerText.Contains($"routes.Get(\"{route}\",", StringComparison.Ordinal)
        || routerText.Contains($"routes.Post(\"{route}\",", StringComparison.Ordinal);

    // True when the server declares this route prefix: a legacy StartsWith comparison or a
    // route-table prefix registration (routes.GetPrefix/PostPrefix).
    public static bool ServerDeclaresRoutePrefix(string routerText, string prefix) =>
        routerText.Contains($"request.Path.StartsWith(\"{prefix}\"", StringComparison.Ordinal)
        || routerText.Contains($"routes.GetPrefix(\"{prefix}\",", StringComparison.Ordinal)
        || routerText.Contains($"routes.PostPrefix(\"{prefix}\",", StringComparison.Ordinal);

    // Text of a client class split into partial files: the primary <File>.cs file followed by its
    // sibling <File>.<Area>.cs partial files in the same folder, in name order.
    public static string PartialTypeSourceText(string primaryPath)
    {
        var directory = Path.GetDirectoryName(primaryPath)!;
        var stem = Path.GetFileNameWithoutExtension(primaryPath);
        var parts = Directory.EnumerateFiles(directory, stem + ".*.cs")
            .Order(StringComparer.OrdinalIgnoreCase);
        return string.Join(Environment.NewLine, new[] { primaryPath }.Concat(parts).Select(File.ReadAllText));
    }

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
