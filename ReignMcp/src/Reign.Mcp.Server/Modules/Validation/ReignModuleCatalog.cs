using System.Text.Json;
using System.Text.RegularExpressions;

namespace Reign.Mcp.Server;

internal sealed class ReignModuleCatalog(ReignMcpOptions options)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly HashSet<string> AuditedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".csproj", ".props", ".targets", ".json", ".xml", ".md",
        ".ps1", ".cmd", ".toml", ".sln", ".slnx", ".yml", ".yaml", ".xsl", ".iss", ".html", ".css"
    };

    public ModuleResolution Resolve(
        IReadOnlyList<string> changedPaths,
        IReadOnlyList<ReignProject> projects)
    {
        if (changedPaths.Count == 0)
        {
            throw new ArgumentException(
                "The changed validation profile requires at least one exact workspace-relative changed path.",
                nameof(changedPaths));
        }

        var manifest = Load(projects);
        var audit = Audit(manifest);
        var reasons = new List<string>();
        var errors = new List<string>();
        var pathResolutions = new List<ModulePathResolution>();
        var affected = new Dictionary<string, ModuleSelection>(StringComparer.OrdinalIgnoreCase);
        var selectedTier = 1;
        var forceProduct = false;
        var forceEcosystem = false;

        foreach (var changedPath in changedPaths)
        {
            var path = Normalize(changedPath);
            var releaseRule = manifest.ReleaseTier4Paths.FirstOrDefault(pattern => Matches(pattern, path));
            if (releaseRule is not null)
            {
                selectedTier = 4;
                forceEcosystem = true;
                var reason = $"Tier 4 ecosystem/release path changed: {path}";
                reasons.Add(reason);
                pathResolutions.Add(new ModulePathResolution
                {
                    Path = path, Status = "owned", MatchedRule = releaseRule, Tier = 4,
                    Projects = projects.Select(project => project.Id).ToArray(), Reason = reason
                });
                continue;
            }

            var globalRule = manifest.Tier4Paths.FirstOrDefault(pattern => Matches(pattern, path));
            if (globalRule is not null)
            {
                selectedTier = 4;
                forceProduct = true;
                var productProjects = ProductProjects(projects);
                var reason = $"Tier 4 product-wide path changed: {path}";
                reasons.Add(reason);
                pathResolutions.Add(new ModulePathResolution
                {
                    Path = path, Status = "owned", MatchedRule = globalRule, Tier = 4,
                    Projects = productProjects.Select(project => project.Id).ToArray(), Reason = reason
                });
                continue;
            }

            var nonCodeRule = manifest.NonCodePaths.FirstOrDefault(rule => Matches(rule.Pattern, path));
            if (nonCodeRule is not null)
            {
                pathResolutions.Add(new ModulePathResolution
                {
                    Path = path, Status = "non-code", MatchedRule = nonCodeRule.Pattern, Tier = 1,
                    Projects = [], Reason = nonCodeRule.Reason
                });
                reasons.Add($"Non-code path requires no build: {path}");
                continue;
            }

            var matches = FindFacetMatches(manifest, path).ToArray();
            if (matches.Length != 1)
            {
                var status = matches.Length == 0 ? "unmapped" : "ambiguous";
                var reason = matches.Length == 0
                    ? $"No module facet owns changed path: {path}"
                    : $"Changed path has multiple module facet owners: {path} ({string.Join(", ", matches.Select(item => item.Module.Id + "/" + item.Facet.Id))})";
                errors.Add(reason);
                pathResolutions.Add(new ModulePathResolution
                {
                    Path = path, Status = status, Tier = null, Projects = [], Reason = reason
                });
                continue;
            }

            var match = matches[0];
            var pathTier = TierFor(match.Facet, path);
            selectedTier = Math.Max(selectedTier, pathTier);
            if (!affected.TryGetValue(match.Module.Id, out var selection))
            {
                selection = new ModuleSelection(match.Module);
                affected.Add(match.Module.Id, selection);
            }
            selection.Paths.Add(path);
            selection.Facets.Add(match.Facet.Id);
            selection.MatchedFacets.Add(match.Facet);
            var matchedRule = match.Facet.Ownership.First(pattern => Matches(pattern, path));
            var reasonText = $"{match.Module.Id}/{match.Facet.Id} selected Tier {pathTier} for {path}";
            reasons.Add(reasonText);
            pathResolutions.Add(new ModulePathResolution
            {
                Path = path, Status = "owned", ModuleId = match.Module.Id, FacetId = match.Facet.Id,
                MatchedRule = matchedRule, Tier = pathTier,
                Projects = match.Facet.Projects.Concat(match.Facet.TestProjects).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                Reason = reasonText
            });
        }

        if (errors.Count > 0)
        {
            return BlockedResolution(manifest, audit, pathResolutions, reasons, errors);
        }

        var codeSelections = affected.Values.ToArray();
        var changedFacetCount = codeSelections
            .SelectMany(selection => selection.MatchedFacets)
            .Distinct()
            .Count();
        if ((codeSelections.Length > 1 || changedFacetCount > 1) && selectedTier < 3)
        {
            selectedTier = 3;
            reasons.Add("Changes cross a module or project facet boundary; Tier 3 integration validation is required.");
        }

        if (selectedTier == 4 && !forceEcosystem) forceProduct = true;
        var nonCodeOnly = codeSelections.Length == 0 && !forceProduct && !forceEcosystem;
        if (nonCodeOnly)
        {
            return new ModuleResolution
            {
                Status = "ready", SelectedTier = 1, SelectedProjects = [], AffectedModules = [],
                VerificationOperations = [], Reasons = reasons, Diagnostics = [],
                ModuleCoverageComplete = true, NonCodeOnly = true,
                PathResolutions = pathResolutions, CoverageAudit = audit,
                Scope = "non-code", VerificationTier = "none"
            };
        }

        IReadOnlyList<ModuleDefinition> impactedModules;
        IReadOnlyList<ReignProject> selectedProjects;
        IReadOnlyList<ModuleValidationOperation> operations;
        if (forceEcosystem || forceProduct)
        {
            selectedProjects = forceEcosystem ? projects : ProductProjects(projects);
            var selectedIds = selectedProjects.Select(project => project.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            impactedModules = manifest.Modules.Where(module => module.Facets.Any(facet =>
                facet.Projects.Concat(facet.TestProjects).Any(selectedIds.Contains))).ToArray();
            operations = [];
        }
        else if (selectedTier >= 3)
        {
            impactedModules = ExpandModules(manifest, affected.Keys).ToArray();
            var projectIds = impactedModules.SelectMany(BoundaryProjectIds)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            selectedProjects = projects.Where(project => projectIds.Contains(project.Id)).ToArray();
            operations = BuildBoundaryOperations(impactedModules);
        }
        else
        {
            impactedModules = codeSelections.Select(selection => selection.Module).ToArray();
            var facets = codeSelections.SelectMany(selection => selection.MatchedFacets).Distinct().ToArray();
            var projectIds = facets.SelectMany(facet => facet.Projects.Concat(facet.TestProjects))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            selectedProjects = projects.Where(project => projectIds.Contains(project.Id)).ToArray();
            operations = BuildFacetOperations(facets, selectedTier);
        }

        if (selectedProjects.Count == 0)
        {
            return BlockedResolution(manifest, audit, pathResolutions, reasons,
                ["The affected module facets selected no discovered projects."]);
        }

        return new ModuleResolution
        {
            Status = "ready", SelectedTier = selectedTier, SelectedProjects = selectedProjects,
            AffectedModules = impactedModules.Select(module => Impact(module, affected)).ToArray(),
            VerificationOperations = operations, Reasons = reasons.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            Diagnostics = audit.Complete ? [] : ["The complete module coverage audit has unresolved paths."],
            ModuleCoverageComplete = audit.Complete, NonCodeOnly = false,
            PathResolutions = pathResolutions, CoverageAudit = audit,
            Scope = forceEcosystem ? "ecosystem" : forceProduct ? "product" : "module",
            VerificationTier = forceEcosystem ? "offline" : forceProduct ? "quick"
                : operations.Count > 0 ? "targeted"
                : selectedProjects.Any(project => project.Group == "core") ? "quick" : "none"
        };
    }

    public ModuleResolution ResolveAll(IReadOnlyList<ReignProject> projects, string scope)
    {
        if (scope is not ("product" or "ecosystem"))
            throw new ArgumentException("scope must be product or ecosystem.", nameof(scope));
        var manifest = Load(projects);
        var audit = Audit(manifest);
        var selectedProjects = scope == "ecosystem" ? projects : ProductProjects(projects);
        var selectedIds = selectedProjects.Select(project => project.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new ModuleResolution
        {
            Status = audit.Complete ? "ready" : "blocked",
            SelectedTier = audit.Complete ? 4 : null,
            SelectedProjects = audit.Complete ? selectedProjects : [],
            AffectedModules = manifest.Modules.Where(module => module.Facets.Any(facet =>
                    facet.Projects.Concat(facet.TestProjects).Any(selectedIds.Contains)))
                .Select(module => Impact(module, null)).ToArray(),
            VerificationOperations = [],
            Reasons = [$"Explicit {scope} profile selected."],
            Diagnostics = audit.Complete ? [] : ["The complete module coverage audit has unresolved paths."],
            ModuleCoverageComplete = audit.Complete,
            NonCodeOnly = false,
            PathResolutions = [],
            CoverageAudit = audit,
            Scope = scope,
            VerificationTier = scope == "ecosystem" ? "offline" : "quick"
        };
    }

    private static IReadOnlyList<ReignProject> ProductProjects(IReadOnlyList<ReignProject> projects) =>
        projects.Where(project => project.Group is "core" or "infrastructure").ToArray();

    private ModuleManifest Load(IReadOnlyList<ReignProject> projects)
    {
        var path = Path.Combine(options.WorkspaceRoot, "reign.modules.json");
        if (!File.Exists(path)) throw new FileNotFoundException("The canonical Reign module manifest is missing.", path);
        var manifest = JsonSerializer.Deserialize<ModuleManifest>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidOperationException("The canonical Reign module manifest is invalid.");
        if (!string.Equals(manifest.Schema, "reign-module-manifest-v2", StringComparison.Ordinal))
            throw new InvalidOperationException("The canonical Reign module manifest has an unsupported schema.");
        if (!string.Equals(manifest.Enforcement, "enforce", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("reign.modules.json must use enforce mode.");
        ValidateManifest(manifest, projects);
        return manifest;
    }

    private ModuleCoverageAudit Audit(ModuleManifest manifest)
    {
        var layout = new ReignSourceLayout(options.WorkspaceRoot);
        var catalogPath = Path.Combine(options.WorkspaceRoot, "reign-projects.json");
        var catalog = JsonSerializer.Deserialize<ProjectRootsManifest>(File.ReadAllText(catalogPath), JsonOptions)
            ?? throw new InvalidOperationException("The Reign project catalog is invalid.");
        var excluded = catalog.ExcludedDirectoryNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var inspected = 0;
        var owned = 0;
        var nonCode = 0;
        var unmapped = new List<string>();
        var ambiguous = new List<string>();
        foreach (var root in catalog.ManagedRoots)
        {
            var absolute = new DirectoryInfo(Path.Combine(options.WorkspaceRoot, root.Path));
            if (!absolute.Exists) continue;
            var pending = new Stack<DirectoryInfo>();
            pending.Push(absolute);
            while (pending.Count > 0)
            {
                var directory = pending.Pop();
                if (excluded.Contains(directory.Name) || !layout.CanTraverse(directory)) continue;
                foreach (var child in directory.EnumerateDirectories()) pending.Push(child);
                foreach (var file in directory.EnumerateFiles())
                {
                    if (!AuditedExtensions.Contains(file.Extension)) continue;
                    var relative = Normalize(Path.GetRelativePath(options.WorkspaceRoot, file.FullName));
                    inspected++;
                    if (manifest.Tier4Paths.Concat(manifest.ReleaseTier4Paths)
                        .Any(pattern => Matches(pattern, relative))) { owned++; continue; }
                    if (manifest.NonCodePaths.Any(rule => Matches(rule.Pattern, relative))) { nonCode++; continue; }
                    var count = FindFacetMatches(manifest, relative).Take(2).Count();
                    if (count == 1) owned++;
                    else if (count == 0) unmapped.Add(relative);
                    else ambiguous.Add(relative);
                }
            }
        }
        return new ModuleCoverageAudit
        {
            InspectedPaths = inspected, OwnedPaths = owned, NonCodePaths = nonCode,
            UnmappedPaths = unmapped.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
            AmbiguousPaths = ambiguous.Order(StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }

    private static IEnumerable<FacetMatch> FindFacetMatches(ModuleManifest manifest, string path) =>
        from module in manifest.Modules
        from facet in module.Facets
        where facet.Ownership.Any(pattern => Matches(pattern, path))
        select new FacetMatch(module, facet);

    private static int TierFor(ModuleFacet facet, string path)
    {
        if (facet.Tier4Paths.Any(pattern => Matches(pattern, path))) return 4;
        if (path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)) return 3;
        if (facet.ContractPaths.Any(pattern => Matches(pattern, path))) return 3;
        if (facet.IntegrationPaths.Any(pattern => Matches(pattern, path))) return 3;
        if (facet.FocusedPaths.Any(pattern => Matches(pattern, path))) return 1;
        return Math.Clamp(facet.DefaultTier, 1, 3);
    }

    private static IEnumerable<ModuleDefinition> ExpandModules(ModuleManifest manifest, IEnumerable<string> ids)
    {
        var directlyChanged = ids.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selected = new HashSet<string>(directlyChanged, StringComparer.OrdinalIgnoreCase);
        var byId = manifest.Modules.ToDictionary(module => module.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var adjacent in directlyChanged.SelectMany(id => byId[id].BoundaryModules))
            selected.Add(adjacent);
        return manifest.Modules.Where(module => selected.Contains(module.Id));
    }

    private static IEnumerable<string> BoundaryProjectIds(ModuleDefinition module) =>
        (module.BoundaryProjects.Count > 0 || module.BoundaryTestProjects.Count > 0)
            ? module.BoundaryProjects.Concat(module.BoundaryTestProjects)
            : module.Facets.SelectMany(facet => facet.Projects.Concat(facet.TestProjects));

    private static IReadOnlyList<ModuleValidationOperation> BuildFacetOperations(IEnumerable<ModuleFacet> facets, int tier) =>
        facets.SelectMany(facet => tier == 1 ? facet.Validation.Focused : facet.Validation.Subsystem)
            .Select(operation => Operation(operation, "facet"))
            .DistinctBy(OperationKey).ToArray();

    private static IReadOnlyList<ModuleValidationOperation> BuildBoundaryOperations(IEnumerable<ModuleDefinition> modules) =>
        modules.SelectMany(module => module.BoundaryValidation.Select(operation => Operation(operation, "boundary", module.Id)))
            .DistinctBy(OperationKey).ToArray();

    private static ModuleValidationOperation Operation(ValidationCommand command, string kind, string moduleId = "") => new()
    {
        ModuleId = moduleId.Length == 0 ? command.ModuleId : moduleId,
        ProjectId = command.ProjectId,
        Kind = command.Kind.Length == 0 ? kind : command.Kind,
        Arguments = command.Arguments,
        TestFilter = command.TestFilter
    };

    private static string OperationKey(ModuleValidationOperation operation) =>
        string.Join("\0", operation.ProjectId, operation.Kind, operation.TestFilter ?? "", string.Join("\0", operation.Arguments));

    private static ReignModuleImpact Impact(ModuleDefinition module, Dictionary<string, ModuleSelection>? affected)
    {
        ModuleSelection? selection = null;
        if (affected is not null) affected.TryGetValue(module.Id, out selection);
        return new ReignModuleImpact
        {
            Id = module.Id, Name = module.Name, Dependencies = module.DependsOn,
            ChangedPaths = selection?.Paths.Order(StringComparer.OrdinalIgnoreCase).ToArray() ?? [],
            Facets = selection?.Facets.Order(StringComparer.OrdinalIgnoreCase).ToArray() ?? []
        };
    }

    private static ModuleResolution BlockedResolution(
        ModuleManifest manifest, ModuleCoverageAudit audit,
        IReadOnlyList<ModulePathResolution> paths, IReadOnlyList<string> reasons,
        IReadOnlyList<string> errors) => new()
    {
        Status = "blocked", SelectedTier = null, SelectedProjects = [], AffectedModules = [],
        VerificationOperations = [], Reasons = reasons, Diagnostics = errors,
        ModuleCoverageComplete = false, NonCodeOnly = false,
        PathResolutions = paths, CoverageAudit = audit,
        Scope = "blocked", VerificationTier = "none"
    };

    private static void ValidateManifest(ModuleManifest manifest, IReadOnlyList<ReignProject> projects)
    {
        var moduleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var projectIds = projects.Select(project => project.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var module in manifest.Modules)
        {
            if (string.IsNullOrWhiteSpace(module.Id) || !moduleIds.Add(module.Id))
                throw new InvalidOperationException("Module IDs must be non-empty and unique.");
            if (module.Facets.Count == 0) throw new InvalidOperationException($"Module {module.Id} has no facets.");
            var facetIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var facet in module.Facets)
            {
                if (string.IsNullOrWhiteSpace(facet.Id) || !facetIds.Add(facet.Id))
                    throw new InvalidOperationException($"Module {module.Id} has an invalid or duplicate facet ID.");
                if (facet.Ownership.Count == 0) throw new InvalidOperationException($"Module {module.Id}/{facet.Id} has no ownership patterns.");
                foreach (var projectId in facet.Projects.Concat(facet.TestProjects)
                             .Concat(facet.Validation.Focused.Select(item => item.ProjectId))
                             .Concat(facet.Validation.Subsystem.Select(item => item.ProjectId)))
                    if (!projectIds.Contains(projectId)) throw new InvalidOperationException($"Module {module.Id}/{facet.Id} references unknown project {projectId}.");
            }
            foreach (var projectId in module.BoundaryProjects.Concat(module.BoundaryTestProjects)
                         .Concat(module.BoundaryValidation.Select(item => item.ProjectId)))
                if (!projectIds.Contains(projectId)) throw new InvalidOperationException($"Module {module.Id} references unknown boundary project {projectId}.");
        }
        foreach (var module in manifest.Modules)
            foreach (var dependency in module.DependsOn.Concat(module.BoundaryModules))
                if (!moduleIds.Contains(dependency)) throw new InvalidOperationException($"Module {module.Id} depends on unknown module {dependency}.");
        DetectCycles(manifest.Modules);
    }

    private static void DetectCycles(IReadOnlyList<ModuleDefinition> modules)
    {
        var byId = modules.ToDictionary(module => module.Id, StringComparer.OrdinalIgnoreCase);
        var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var complete = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Visit(string id)
        {
            if (complete.Contains(id)) return;
            if (!active.Add(id)) throw new InvalidOperationException($"The Reign module dependency graph contains a cycle at {id}.");
            foreach (var dependency in byId[id].DependsOn) Visit(dependency);
            active.Remove(id); complete.Add(id);
        }
        foreach (var module in modules) Visit(module.Id);
    }

    internal static bool Matches(string pattern, string path)
    {
        pattern = Normalize(pattern); path = Normalize(path);
        var expression = "^" + Regex.Escape(pattern)
            .Replace("\\*\\*/", "(?:.*/)?")
            .Replace("\\*\\*", ".*")
            .Replace("\\*", "[^/]*")
            .Replace("\\?", "[^/]") + "$";
        return Regex.IsMatch(path, expression, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string Normalize(string value) => value.Replace('\\', '/').Trim().Trim('/');

    private sealed record ModuleManifest
    {
        public string Schema { get; init; } = "";
        public string Enforcement { get; init; } = "";
        public IReadOnlyList<string> Tier4Paths { get; init; } = [];
        public IReadOnlyList<string> ReleaseTier4Paths { get; init; } = [];
        public IReadOnlyList<NonCodeRule> NonCodePaths { get; init; } = [];
        public IReadOnlyList<ModuleDefinition> Modules { get; init; } = [];
    }
    private sealed record NonCodeRule { public string Pattern { get; init; } = ""; public string Reason { get; init; } = ""; }
    private sealed record ModuleDefinition
    {
        public string Id { get; init; } = ""; public string Name { get; init; } = "";
        public IReadOnlyList<string> DependsOn { get; init; } = [];
        public IReadOnlyList<string> BoundaryModules { get; init; } = [];
        public IReadOnlyList<string> BoundaryProjects { get; init; } = [];
        public IReadOnlyList<string> BoundaryTestProjects { get; init; } = [];
        public IReadOnlyList<ValidationCommand> BoundaryValidation { get; init; } = [];
        public IReadOnlyList<ModuleFacet> Facets { get; init; } = [];
    }
    private sealed record ModuleFacet
    {
        public string Id { get; init; } = ""; public IReadOnlyList<string> Ownership { get; init; } = [];
        public IReadOnlyList<string> Projects { get; init; } = []; public IReadOnlyList<string> TestProjects { get; init; } = [];
        public int DefaultTier { get; init; } = 2; public IReadOnlyList<string> FocusedPaths { get; init; } = [];
        public IReadOnlyList<string> ContractPaths { get; init; } = []; public IReadOnlyList<string> IntegrationPaths { get; init; } = [];
        public IReadOnlyList<string> Tier4Paths { get; init; } = []; public ModuleValidation Validation { get; init; } = new();
    }
    private sealed record ModuleValidation { public IReadOnlyList<ValidationCommand> Focused { get; init; } = []; public IReadOnlyList<ValidationCommand> Subsystem { get; init; } = []; }
    private sealed record ValidationCommand
    {
        public string ModuleId { get; init; } = ""; public string ProjectId { get; init; } = ""; public string Kind { get; init; } = "";
        public IReadOnlyList<string> Arguments { get; init; } = []; public string? TestFilter { get; init; }
    }
    private sealed record ProjectRootsManifest { public IReadOnlyList<ManagedRoot> ManagedRoots { get; init; } = []; public IReadOnlyList<string> ExcludedDirectoryNames { get; init; } = []; }
    private sealed record ManagedRoot { public string Path { get; init; } = ""; }
    private sealed record FacetMatch(ModuleDefinition Module, ModuleFacet Facet);
    private sealed class ModuleSelection(ModuleDefinition module)
    {
        public ModuleDefinition Module { get; } = module; public List<string> Paths { get; } = [];
        public HashSet<string> Facets { get; } = new(StringComparer.OrdinalIgnoreCase); public HashSet<ModuleFacet> MatchedFacets { get; } = [];
    }
}

internal sealed record ModuleResolution
{
    public required string Status { get; init; }
    public required int? SelectedTier { get; init; }
    public required IReadOnlyList<ReignProject> SelectedProjects { get; init; }
    public required IReadOnlyList<ReignModuleImpact> AffectedModules { get; init; }
    public required IReadOnlyList<ModuleValidationOperation> VerificationOperations { get; init; }
    public required IReadOnlyList<string> Reasons { get; init; }
    public required IReadOnlyList<string> Diagnostics { get; init; }
    public required bool ModuleCoverageComplete { get; init; }
    public required bool NonCodeOnly { get; init; }
    public required IReadOnlyList<ModulePathResolution> PathResolutions { get; init; }
    public required ModuleCoverageAudit CoverageAudit { get; init; }
    public required string Scope { get; init; }
    public required string VerificationTier { get; init; }
}
