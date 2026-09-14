using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Reign.Mcp.Server;

public sealed partial class WorkspaceAccess
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".csproj", ".props", ".targets", ".json", ".md", ".txt", ".xml",
        ".ps1", ".cmd", ".toml", ".yml", ".yaml", ".sln", ".slnx", ".spec", ".py", ".iss"
    };

    private static readonly HashSet<string> ExcludedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".tmp", ".codex-build", ".codex-live-artifacts", ".codex-live-backups",
        "bin", "obj", "staging", "artifacts",
        "PortraitCache", "logs", "decompiled", "AIInfluence_decompiled"
    };

    private readonly ReignMcpOptions _options;
    private readonly SensitiveDataRedactor _redactor;

    public WorkspaceAccess(ReignMcpOptions options, SensitiveDataRedactor redactor)
    {
        _options = options;
        _redactor = redactor;
    }

    public string ReadRoadmap()
    {
        return File.ReadAllText(Path.Combine(_options.WorkspaceRoot, "REIGN_ROADMAP.md"));
    }

    public WorkspaceStatus GetStatus()
    {
        var components = new[]
        {
            Component("Bannerlord client", Path.Combine("ReignBeta", "ReignBeta.csproj")),
            Component("Reign server", Path.Combine("ReignBetaServer", "ReignBetaServer.csproj")),
            Component("Verification runner", Path.Combine("ReignBetaServer", "ReignVerification", "ReignVerification.csproj")),
            Component("Live-test controller", Path.Combine("ReignBetaServer", "ReignLiveTest", "ReignLiveTest.csproj")),
            Component("Reign MCP", Path.Combine("ReignMcp", "src", "Reign.Mcp.Server", "Reign.Mcp.Server.csproj")),
            Component("Bannerlord Editor MCP", Path.Combine("BannerlordEditorMcp", "src", "Bannerlord.EditorMcp.Server", "Bannerlord.EditorMcp.Server.csproj"))
        };

        return new WorkspaceStatus
        {
            WorkspaceRoot = _options.WorkspaceRoot,
            RoadmapPath = Path.Combine(_options.WorkspaceRoot, "REIGN_ROADMAP.md"),
            TestingGuidePath = Path.Combine(_options.WorkspaceRoot, "docs", "agent", "TESTING_TOOL_GUIDE.md"),
            TestingCatalogPath = Path.Combine(_options.WorkspaceRoot, "reign.testing.json"),
            Components = components,
            TestScenarios = EnumerateRelativeFiles(
                Path.Combine(_options.WorkspaceRoot, "ReignBetaServer", "ReignLiveTest", "scenarios"),
                "*.json",
                500),
            VerificationDocuments = new[]
            {
                Path.Combine("docs", "agent", "TESTING_TOOL_GUIDE.md"),
                Path.Combine("ReignBetaServer", "docs", "VerificationLab.md"),
                Path.Combine("ReignBetaServer", "README.md"),
                Path.Combine("ReignBeta", "README.md")
            }
        };
    }

    public SourceSearchResult Search(
        string query,
        string? scope,
        int maxResults,
        bool caseSensitive)
    {
        query = InputGuard.BoundedText(query, nameof(query), 300);
        if (query.Length == 0)
        {
            throw new ArgumentException("query is required.", nameof(query));
        }
        maxResults = InputGuard.Range(maxResults, nameof(maxResults), 1, 200);
        var root = ResolveScope(scope);
        var comparison = caseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;
        var matches = new List<SourceMatch>();
        var truncated = false;

        foreach (var path in EnumerateAllowedFiles(root))
        {
            if (new FileInfo(path).Length > 2 * 1024 * 1024)
            {
                continue;
            }

            var lineNumber = 0;
            foreach (var line in File.ReadLines(path))
            {
                lineNumber++;
                if (line.IndexOf(query, comparison) < 0)
                {
                    continue;
                }
                if (matches.Count >= maxResults)
                {
                    truncated = true;
                    break;
                }
                matches.Add(new SourceMatch(
                    Path.GetRelativePath(_options.WorkspaceRoot, path),
                    lineNumber,
                    _redactor.RedactText(line.Length > 500 ? line[..500] + "…" : line)));
            }
            if (truncated)
            {
                break;
            }
        }

        return new SourceSearchResult
        {
            Query = query,
            Count = matches.Count,
            Truncated = truncated,
            Matches = matches
        };
    }

    public SourceDocument ReadSource(string relativePath, int startLine, int lineCount)
    {
        startLine = InputGuard.Range(startLine, nameof(startLine), 1, 1_000_000);
        lineCount = InputGuard.Range(lineCount, nameof(lineCount), 1, 1000);
        var path = ResolveReadablePath(relativePath);
        var builder = new StringBuilder();
        var current = 0;
        var included = 0;
        var truncated = false;

        foreach (var line in File.ReadLines(path))
        {
            current++;
            if (current < startLine)
            {
                continue;
            }
            if (included >= lineCount)
            {
                truncated = true;
                break;
            }
            var rendered = $"{current,6}: {_redactor.RedactText(line)}{Environment.NewLine}";
            if (Encoding.UTF8.GetByteCount(builder.ToString()) + Encoding.UTF8.GetByteCount(rendered)
                > _options.MaxToolTextBytes)
            {
                truncated = true;
                break;
            }
            builder.Append(rendered);
            included++;
        }

        return new SourceDocument
        {
            Path = Path.GetRelativePath(_options.WorkspaceRoot, path),
            StartLine = startLine,
            EndLine = included == 0 ? startLine : startLine + included - 1,
            Truncated = truncated,
            Text = builder.ToString()
        };
    }

    public string ReadDocumentation(string relativePath)
    {
        var document = ReadSource(relativePath, 1, 1000);
        return document.Text;
    }

    internal string ResolveReadablePath(string relativePath)
    {
        relativePath = InputGuard.BoundedText(relativePath, nameof(relativePath), 500);
        if (relativePath.Length == 0
            || Path.IsPathRooted(relativePath)
            || relativePath.Contains(':', StringComparison.Ordinal))
        {
            throw new ArgumentException("A workspace-relative path is required.", nameof(relativePath));
        }

        var path = Path.GetFullPath(Path.Combine(
            _options.WorkspaceRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        ReignMcpOptions.EnsureWithin(_options.WorkspaceRoot, path, nameof(relativePath));
        new ReignSourceLayout(_options.WorkspaceRoot).ValidateAccessPath(path);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The workspace file was not found.", relativePath);
        }
        if (!AllowedExtensions.Contains(Path.GetExtension(path)) || IsExcluded(path))
        {
            throw new InvalidOperationException("That file is outside the MCP server's readable source/document boundary.");
        }
        return path;
    }

    private string ResolveScope(string? scope)
    {
        new ReignSourceLayout(_options.WorkspaceRoot).ValidateProjections();
        if (string.IsNullOrWhiteSpace(scope))
        {
            return _options.WorkspaceRoot;
        }
        var path = Path.GetFullPath(Path.Combine(
            _options.WorkspaceRoot,
            scope.Replace('/', Path.DirectorySeparatorChar)));
        ReignMcpOptions.EnsureWithin(_options.WorkspaceRoot, path, nameof(scope));
        new ReignSourceLayout(_options.WorkspaceRoot).ValidateAccessPath(path);
        if (!Directory.Exists(path) || IsExcluded(path))
        {
            throw new DirectoryNotFoundException("The requested source scope is unavailable.");
        }
        return path;
    }

    private IEnumerable<string> EnumerateAllowedFiles(string root)
    {
        var layout = new ReignSourceLayout(_options.WorkspaceRoot);
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new DirectoryInfo(root));
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            if (!layout.CanTraverse(directory)
                || ExcludedDirectoryNames.Contains(directory.Name)
                || IsSensitiveRuntimeData(directory.FullName))
            {
                continue;
            }
            foreach (var file in directory.EnumerateFiles())
            {
                if (AllowedExtensions.Contains(file.Extension) && !file.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    yield return file.FullName;
                }
            }
            foreach (var child in directory.EnumerateDirectories())
            {
                pending.Push(child);
            }
        }
    }

    private bool IsExcluded(string path)
    {
        var relative = Path.GetRelativePath(_options.WorkspaceRoot, path);
        return relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(ExcludedDirectoryNames.Contains)
            || IsSensitiveRuntimeData(path);
    }

    private bool IsSensitiveRuntimeData(string path)
    {
        var runtimeData = Path.GetFullPath(Path.Combine(
            _options.WorkspaceRoot, "ReignBeta", "server", "app", "data"));
        var candidate = Path.GetFullPath(path);
        return candidate.Equals(runtimeData, StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith(runtimeData + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private WorkspaceComponent Component(string name, string relativeProject)
    {
        var path = Path.Combine(_options.WorkspaceRoot, relativeProject);
        var exists = File.Exists(path);
        return new WorkspaceComponent
        {
            Name = name,
            ProjectPath = relativeProject,
            Exists = exists,
            TargetFramework = exists ? ReadTargetFramework(path) : null,
            LastWriteUtc = exists ? File.GetLastWriteTimeUtc(path).ToString("O") : null
        };
    }

    private static string? ReadTargetFramework(string projectPath)
    {
        try
        {
            var document = XDocument.Load(projectPath);
            return document.Descendants()
                .FirstOrDefault(element =>
                    element.Name.LocalName is "TargetFramework" or "TargetFrameworks")
                ?.Value.Trim();
        }
        catch
        {
            return null;
        }
    }

    private IReadOnlyList<string> EnumerateRelativeFiles(string root, string pattern, int limit)
    {
        if (!Directory.Exists(root))
        {
            return [];
        }
        return Directory.EnumerateFiles(root, pattern, SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .Select(path => Path.GetRelativePath(_options.WorkspaceRoot, path))
            .ToArray();
    }
}
