using System.Text.Json;

namespace Reign.Mcp.Server;

public sealed record ApiEnvelope
{
    public required bool Ok { get; init; }
    public required string Endpoint { get; init; }
    public required string RetrievedUtc { get; init; }
    public int? StatusCode { get; init; }
    public JsonElement? Data { get; init; }
    public string? Error { get; init; }
}

public sealed record CapabilityManifest
{
    public required string ServerName { get; init; }
    public required string Version { get; init; }
    public required string WorkspaceRoot { get; init; }
    public required string ReignServer { get; init; }
    public required string Transport { get; init; }
    public required bool BuildEnabled { get; init; }
    public required bool RestoreEnabled { get; init; }
    public required bool VerificationControlEnabled { get; init; }
    public required bool OfflineVerificationEnabled { get; init; }
    public required string[] EnforcedProhibitions { get; init; }
    public required string[] DataBoundaries { get; init; }
}

public sealed record SourceMatch(string Path, int Line, string Text);

public sealed record SourceSearchResult
{
    public required string Query { get; init; }
    public required int Count { get; init; }
    public required bool Truncated { get; init; }
    public required IReadOnlyList<SourceMatch> Matches { get; init; }
}

public sealed record SourceDocument
{
    public required string Path { get; init; }
    public required int StartLine { get; init; }
    public required int EndLine { get; init; }
    public required bool Truncated { get; init; }
    public required string Text { get; init; }
}

public sealed record WorkspaceStatus
{
    public required string WorkspaceRoot { get; init; }
    public required string RoadmapPath { get; init; }
    public required string TestingGuidePath { get; init; }
    public required string TestingCatalogPath { get; init; }
    public required IReadOnlyList<WorkspaceComponent> Components { get; init; }
    public required IReadOnlyList<string> TestScenarios { get; init; }
    public required IReadOnlyList<string> VerificationDocuments { get; init; }
}

public sealed record WorkspaceComponent
{
    public required string Name { get; init; }
    public required string ProjectPath { get; init; }
    public required bool Exists { get; init; }
    public string? TargetFramework { get; init; }
    public string? LastWriteUtc { get; init; }
}

public sealed record ProcessResult
{
    public required bool Ok { get; init; }
    public required int ExitCode { get; init; }
    public required string Command { get; init; }
    public required string WorkingDirectory { get; init; }
    public required long DurationMs { get; init; }
    public required bool TimedOut { get; init; }
    public required bool OutputTruncated { get; init; }
    public required string StandardOutput { get; init; }
    public required string StandardError { get; init; }
    public string? ArtifactDirectory { get; init; }
}

public sealed record BuildBatchResult
{
    public required bool Ok { get; init; }
    public required string Configuration { get; init; }
    public required string ArtifactRoot { get; init; }
    public required IReadOnlyList<ProcessResult> Results { get; init; }
}

public sealed record ReignProject
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Group { get; init; }
    public required string ProjectPath { get; init; }
    public required string TargetFramework { get; init; }
    public required bool IsTestProject { get; init; }
}

public sealed record UnmanagedProject
{
    public required string ProjectPath { get; init; }
    public required string Reason { get; init; }
}

public sealed record ValidationPlan
{
    public required string Schema { get; init; }
    public required string Status { get; init; }
    public required string Profile { get; init; }
    public required string Scope { get; init; }
    public required string VerificationTier { get; init; }
    public required string Configuration { get; init; }
    public required bool Restore { get; init; }
    public required bool CoverageComplete { get; init; }
    public required IReadOnlyList<ReignProject> Projects { get; init; }
    public required IReadOnlyList<UnmanagedProject> UnmanagedProjects { get; init; }
    public required IReadOnlyList<string> ChangedPaths { get; init; }
    public required IReadOnlyList<string> Diagnostics { get; init; }
    public required int? SelectedTier { get; init; }
    public required bool NonCodeOnly { get; init; }
    public required IReadOnlyList<string> SelectionReasons { get; init; }
    public required bool ModuleCoverageComplete { get; init; }
    public required IReadOnlyList<ReignModuleImpact> AffectedModules { get; init; }
    public required IReadOnlyList<ModuleValidationOperation> VerificationOperations { get; init; }
    public required IReadOnlyList<ModulePathResolution> PathResolutions { get; init; }
    public required ModuleCoverageAudit CoverageAudit { get; init; }
}

public sealed record ReignModuleImpact
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required IReadOnlyList<string> ChangedPaths { get; init; }
    public required IReadOnlyList<string> Dependencies { get; init; }
    public required IReadOnlyList<string> Facets { get; init; }
}

public sealed record ModulePathResolution
{
    public required string Path { get; init; }
    public required string Status { get; init; }
    public string? ModuleId { get; init; }
    public string? FacetId { get; init; }
    public string? MatchedRule { get; init; }
    public int? Tier { get; init; }
    public required IReadOnlyList<string> Projects { get; init; }
    public required string Reason { get; init; }
}

public sealed record ModuleCoverageAudit
{
    public required int InspectedPaths { get; init; }
    public required int OwnedPaths { get; init; }
    public required int NonCodePaths { get; init; }
    public required IReadOnlyList<string> UnmappedPaths { get; init; }
    public required IReadOnlyList<string> AmbiguousPaths { get; init; }
    public bool Complete => UnmappedPaths.Count == 0 && AmbiguousPaths.Count == 0;
}

public sealed record ModuleValidationOperation
{
    public required string ModuleId { get; init; }
    public required string ProjectId { get; init; }
    public required string Kind { get; init; }
    public required IReadOnlyList<string> Arguments { get; init; }
    public string? TestFilter { get; init; }
}

public sealed record ProjectValidationResult
{
    public required ReignProject Project { get; init; }
    public required string Operation { get; init; }
    public required ProcessResult Process { get; init; }
}

public sealed record RepositoryHygieneIssue
{
    public required string Category { get; init; }
    public required string Path { get; init; }
    public required string Classifier { get; init; }
    public required string Message { get; init; }
}

public sealed record RepositoryHygieneReport
{
    public required string Schema { get; init; }
    public required string Enforcement { get; init; }
    public required bool Ok { get; init; }
    public required bool BlocksValidation { get; init; }
    public required string PolicyPath { get; init; }
    public string? EvidencePath { get; init; }
    public required string GitRoot { get; init; }
    public IReadOnlyList<string> RepositoryRoots { get; init; } = [];
    public required int TrackedFileCount { get; init; }
    public required int UntrackedFileCount { get; init; }
    public required IReadOnlyList<RepositoryHygieneIssue> Issues { get; init; }
}

public sealed record ValidationReport
{
    public required string Schema { get; init; }
    public required string ValidatorVersion { get; init; }
    public required string RunId { get; init; }
    public required string SourceFingerprintSha256 { get; init; }
    public required string StartedUtc { get; init; }
    public required string CompletedUtc { get; init; }
    public required bool Ok { get; init; }
    public required bool Reused { get; init; }
    public string? ReusedFromRunId { get; init; }
    public required ValidationPlan Plan { get; init; }
    public required string ArtifactRoot { get; init; }
    public required string ReportPath { get; init; }
    public required RepositoryHygieneReport RepositoryHygiene { get; init; }
    public required IReadOnlyList<ProjectValidationResult> Results { get; init; }
}
