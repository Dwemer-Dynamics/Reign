using System.Text.Json;
using System.Xml.Linq;

namespace Reign.Mcp.Server;

public sealed class ReignProjectCatalog(ReignMcpOptions options)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ValidationPlan CreatePlan(
        string profile,
        string configuration,
        bool restore,
        string changedPaths = "")
    {
        new ReignSourceLayout(options.WorkspaceRoot).ValidateProjections();
        profile = profile.Trim().ToLowerInvariant();
        if (profile is not ("changed" or "core" or "product" or "tooling" or "all"))
        {
            throw new ArgumentException(
                "profile must be changed, core, product, tooling, or all.",
                nameof(profile));
        }
        configuration = configuration.Trim();
        if (configuration is not ("Debug" or "Release"))
        {
            throw new ArgumentException(
                "configuration must be Debug or Release.",
                nameof(configuration));
        }

        var manifestPath = Path.Combine(options.WorkspaceRoot, "reign-projects.json");
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException(
                "The canonical Reign project catalog is missing.",
                manifestPath);
        }

        var manifest = JsonSerializer.Deserialize<CatalogManifest>(
            File.ReadAllText(manifestPath),
            JsonOptions) ?? throw new InvalidOperationException(
                "The canonical Reign project catalog is invalid.");
        if (!string.Equals(
                manifest.Schema,
                "reign-project-catalog-v1",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The canonical Reign project catalog has an unsupported schema.");
        }

        var exclusions = manifest.ExcludedDirectoryNames
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ignoredRoots = manifest.IgnoredRoots
            .ToDictionary(
                item => Normalize(item.Path),
                item => item.Reason,
                StringComparer.OrdinalIgnoreCase);
        var overrides = manifest.ProjectOverrides
            .ToDictionary(
                pair => Normalize(pair.Key),
                pair => pair.Value,
                StringComparer.OrdinalIgnoreCase);
        var projects = new List<ReignProject>();
        var claimedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var diagnostics = new List<string>();

        foreach (var root in manifest.ManagedRoots)
        {
            var relativeRoot = Normalize(root.Path);
            var absoluteRoot = Path.GetFullPath(Path.Combine(
                options.WorkspaceRoot,
                relativeRoot.Replace('/', Path.DirectorySeparatorChar)));
            ReignMcpOptions.EnsureWithin(
                options.WorkspaceRoot,
                absoluteRoot,
                $"managed root {relativeRoot}");
            if (!Directory.Exists(absoluteRoot))
            {
                diagnostics.Add($"Managed root is missing: {relativeRoot}");
                continue;
            }

            foreach (var projectPath in EnumerateProjects(absoluteRoot, exclusions))
            {
                var relativePath = Normalize(Path.GetRelativePath(
                    options.WorkspaceRoot,
                    projectPath));
                claimedPaths.Add(relativePath);
                overrides.TryGetValue(relativePath, out var projectOverride);
                var project = ReadProject(
                    projectPath,
                    relativePath,
                    root.Group,
                    projectOverride);
                projects.Add(project);
            }
        }

        var unmanaged = EnumerateProjects(options.WorkspaceRoot, exclusions)
            .Select(path => Normalize(Path.GetRelativePath(options.WorkspaceRoot, path)))
            .Where(path => !claimedPaths.Contains(path))
            .Where(path => !ignoredRoots.Keys.Any(root =>
                path.Equals(root, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => new UnmanagedProject
            {
                ProjectPath = path,
                Reason = "Project is outside every managed or explicitly ignored Reign root."
            })
            .ToArray();

        var changed = ParseChangedPaths(changedPaths);
        ModuleResolution? moduleResolution = null;
        ModuleResolution? auditResolution = null;
        IReadOnlyList<ReignProject> selected;
        if (profile == "changed")
        {
            moduleResolution = new ReignModuleCatalog(options)
                .Resolve(changed, projects);
            selected = moduleResolution.SelectedProjects;
            diagnostics.AddRange(moduleResolution.Diagnostics);
        }
        else if (profile is "all" or "product")
        {
            moduleResolution = new ReignModuleCatalog(options)
                .ResolveAll(projects, profile == "all" ? "ecosystem" : "product");
            selected = moduleResolution.SelectedProjects;
            diagnostics.AddRange(moduleResolution.Diagnostics);
        }
        else
        {
            selected = SelectProjects(projects, profile, changed);
            auditResolution = new ReignModuleCatalog(options).ResolveAll(projects, "ecosystem");
            diagnostics.AddRange(auditResolution.Diagnostics);
        }
        if (projects.Count == 0)
        {
            diagnostics.Add("No managed projects were discovered.");
        }
        if (unmanaged.Length > 0)
        {
            diagnostics.Add(
                "Unclassified project coverage must be resolved before validation can pass.");
        }

        int? selectedTier = moduleResolution is not null
            ? moduleResolution.SelectedTier
            : profile == "core" ? 4 : 2;
        var reasons = moduleResolution?.Reasons
            ?? [$"Explicit {profile} profile selected."];
        var effectiveResolution = moduleResolution ?? auditResolution;
        var moduleCoverageComplete = effectiveResolution?.ModuleCoverageComplete ?? true;
        var nonCodeOnly = moduleResolution?.NonCodeOnly ?? false;
        var status = moduleResolution?.Status
            ?? (moduleCoverageComplete ? "ready" : "blocked");
        return new ValidationPlan
        {
            Schema = "reign-validation-plan-v3",
            Status = status,
            Profile = profile,
            Scope = moduleResolution?.Scope ?? profile,
            VerificationTier = moduleResolution?.VerificationTier
                ?? (profile == "core" ? "quick" : "none"),
            Configuration = configuration,
            Restore = restore,
            CoverageComplete = unmanaged.Length == 0
                && (projects.Count > 0 || nonCodeOnly)
                && moduleCoverageComplete,
            Projects = selected,
            UnmanagedProjects = unmanaged,
            ChangedPaths = changed,
            Diagnostics = diagnostics,
            SelectedTier = selectedTier,
            NonCodeOnly = nonCodeOnly,
            SelectionReasons = reasons,
            ModuleCoverageComplete = moduleCoverageComplete,
            AffectedModules = moduleResolution?.AffectedModules ?? [],
            VerificationOperations = moduleResolution?.VerificationOperations ?? [],
            PathResolutions = moduleResolution?.PathResolutions ?? [],
            CoverageAudit = effectiveResolution?.CoverageAudit ?? new ModuleCoverageAudit
            {
                InspectedPaths = 0, OwnedPaths = 0, NonCodePaths = 0,
                UnmappedPaths = [], AmbiguousPaths = []
            }
        };
    }

    private static IReadOnlyList<ReignProject> SelectProjects(
        IReadOnlyList<ReignProject> projects,
        string profile,
        IReadOnlyList<string> changed)
    {
        IEnumerable<ReignProject> selected = profile switch
        {
            "core" => projects.Where(project => project.Group == "core"),
            "product" => projects.Where(project => project.Group is "core" or "infrastructure"),
            "tooling" => projects.Where(project => project.Group != "core"),
            "changed" when changed.Count > 0 => ExpandAffectedProjects(
                projects,
                projects.Where(project =>
                    changed.Any(path => AffectsProject(path, project.ProjectPath)))
                    .ToArray()),
            _ => projects
        };

        var result = selected
            .OrderBy(project => project.IsTestProject)
            .ThenBy(project => project.Group, StringComparer.OrdinalIgnoreCase)
            .ThenBy(project => project.ProjectPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return profile == "changed" && result.Length == 0
            ? projects.OrderBy(project => project.IsTestProject)
                .ThenBy(project => project.ProjectPath, StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : result;
    }

    private static IEnumerable<ReignProject> ExpandAffectedProjects(
        IReadOnlyList<ReignProject> allProjects,
        IReadOnlyList<ReignProject> affected)
    {
        if (affected.Count == 0)
        {
            return [];
        }
        if (affected.Count == allProjects.Count)
        {
            return allProjects;
        }

        var affectedGroups = affected
            .Where(project => project.Group is "core" or "infrastructure")
            .Select(project => project.Group)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var affectedRoots = affected
            .Where(project => project.Group == "tooling")
            .Select(project => project.ProjectPath.Split('/')[0])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return allProjects.Where(project =>
            affectedGroups.Contains(project.Group)
            || (project.Group == "tooling"
                && affectedRoots.Contains(project.ProjectPath.Split('/')[0])));
    }

    private static bool AffectsProject(string changedPath, string projectPath)
    {
        var projectDirectory = Normalize(Path.GetDirectoryName(projectPath) ?? "");
        if (changedPath.Equals(projectPath, StringComparison.OrdinalIgnoreCase)
            || changedPath.StartsWith(
                projectDirectory + "/",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return changedPath is "reign-projects.json" or "AGENTS.md"
            || changedPath.StartsWith(".codex/", StringComparison.OrdinalIgnoreCase)
            || changedPath.StartsWith("ReignMcp/", StringComparison.OrdinalIgnoreCase);
    }

    private static ReignProject ReadProject(
        string absolutePath,
        string relativePath,
        string group,
        ProjectOverride? projectOverride)
    {
        var document = XDocument.Load(absolutePath);
        var properties = document.Descendants()
            .Where(element => !element.HasElements)
            .GroupBy(element => element.Name.LocalName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First().Value.Trim(),
                StringComparer.OrdinalIgnoreCase);
        var isTest = projectOverride?.Test
            ?? (properties.TryGetValue("IsTestProject", out var isTestValue)
                && bool.TryParse(isTestValue, out var parsed)
                && parsed)
            || document.Descendants().Any(element =>
                element.Name.LocalName == "PackageReference"
                && string.Equals(
                    element.Attribute("Include")?.Value,
                    "Microsoft.NET.Test.Sdk",
                    StringComparison.OrdinalIgnoreCase))
            || relativePath.Split('/').Contains("tests", StringComparer.OrdinalIgnoreCase);
        var fileName = Path.GetFileNameWithoutExtension(relativePath);
        return new ReignProject
        {
            Id = projectOverride?.Id ?? Slug(relativePath),
            Name = projectOverride?.Name ?? fileName,
            Group = (projectOverride?.Group ?? group).Trim().ToLowerInvariant(),
            ProjectPath = relativePath,
            TargetFramework = properties.TryGetValue("TargetFramework", out var framework)
                ? framework
                : properties.TryGetValue("TargetFrameworks", out var frameworks)
                    ? frameworks
                    : "unknown",
            IsTestProject = isTest
        };
    }

    private IEnumerable<string> EnumerateProjects(
        string root,
        IReadOnlySet<string> exclusions)
    {
        var layout = new ReignSourceLayout(options.WorkspaceRoot);
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new DirectoryInfo(root));
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            if (!layout.CanTraverse(directory)
                || exclusions.Contains(directory.Name))
            {
                continue;
            }
            foreach (var project in directory.EnumerateFiles("*.csproj"))
            {
                yield return project.FullName;
            }
            foreach (var child in directory.EnumerateDirectories())
            {
                pending.Push(child);
            }
        }
    }

    private static IReadOnlyList<string> ParseChangedPaths(string value)
    {
        return value.Split(
                ['\r', '\n', ';'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Normalize)
            .Where(path => path.Length > 0 && !Path.IsPathRooted(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string Normalize(string path)
    {
        return path.Replace('\\', '/').Trim().Trim('/');
    }

    private static string Slug(string value)
    {
        return string.Concat(value
                .ToLowerInvariant()
                .Select(character => char.IsLetterOrDigit(character) ? character : '-'))
            .Trim('-');
    }

    private sealed record CatalogManifest
    {
        public string Schema { get; init; } = "";
        public IReadOnlyList<ManagedRoot> ManagedRoots { get; init; } = [];
        public IReadOnlyList<IgnoredRoot> IgnoredRoots { get; init; } = [];
        public IReadOnlyList<string> ExcludedDirectoryNames { get; init; } = [];
        public IReadOnlyDictionary<string, ProjectOverride> ProjectOverrides { get; init; }
            = new Dictionary<string, ProjectOverride>();
    }

    private sealed record ManagedRoot
    {
        public string Path { get; init; } = "";
        public string Group { get; init; } = "";
    }

    private sealed record IgnoredRoot
    {
        public string Path { get; init; } = "";
        public string Reason { get; init; } = "";
    }

    private sealed record ProjectOverride
    {
        public string? Group { get; init; }
        public string? Id { get; init; }
        public string? Name { get; init; }
        public bool? Test { get; init; }
    }
}
