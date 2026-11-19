using System.Text.RegularExpressions;

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

    // The server code that was the Program partial class before the module services and providers took it over
    // (ReignServer P3-3): files declaring Program or one of the Platform area services, the domain handler files
    // that were Program.<Area>.cs, and the module route files. Mirrors the server's
    // VerificationSourceLocator.IsProgramAreaSource.
    private static readonly Regex ProgramAreaTypePattern = new(
        @"\bpartial\s+class\s+(Program|LlmProviderService|SettingsService|PromptService|ActionPipelineService|"
        + @"ConversationResponseService|CampaignFileService|OperationsService|VerificationService)\b");
    private static readonly HashSet<string> FormerProgramHandlerFiles = new(StringComparer.Ordinal)
    {
        "CharacterConstruction.cs", "CharacterProfiles.cs", "ContextPulls.cs", "DialogueTurns.cs",
        "EventConversations.cs", "MemoryPacket.cs", "MemoryRetrieval.cs", "MemoryStore.cs", "PromptContext.cs",
        "PortraitRequests.cs"
    };

    private static IEnumerable<(string Path, string Text)> ServerProgramAreaFiles(string workspace) =>
        Enumerate(Path.Combine(workspace, "ReignServer", "src"), "*.cs")
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(path => (Path: path, Text: File.ReadAllText(path)))
            .Where(file => FormerProgramHandlerFiles.Contains(Path.GetFileName(file.Path))
                || Path.GetFileName(file.Path).StartsWith("Program", StringComparison.Ordinal)
                || Path.GetFileName(file.Path).EndsWith("Routes.cs", StringComparison.Ordinal)
                || ProgramAreaTypePattern.IsMatch(file.Text));

    // Server router text: the route table and every module's route registrations, within the former Program area.
    public static string ServerRouterText(string workspace) => ServerProgramText(workspace);

    // Server Program source: the former Program area (Platform/Program.cs hub, the Platform area services and
    // providers, the domain handler files and the route files), wherever it lives under ReignServer/src.
    public static string ServerProgramText(string workspace) => string.Join(Environment.NewLine,
        ServerProgramAreaFiles(workspace).Select(file => file.Text));

    // The first file of the former Program area (path order) whose text contains anchor, for checks that slice or
    // order text inside one member.
    public static string ServerProgramFileText(string workspace, string anchor) =>
        ServerProgramAreaFiles(workspace)
            .Select(file => file.Text)
            .FirstOrDefault(text => text.Contains(anchor, StringComparison.Ordinal))
        ?? throw new FileNotFoundException($"No server Program-area file contains {anchor}.");

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
