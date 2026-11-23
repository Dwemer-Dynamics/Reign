using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

namespace Reign.Mcp.Server;

public sealed partial class ReignValidationService(
    ReignMcpOptions options,
    ReignProjectCatalog catalog,
    ReignProcessRunner runner,
    IReignRepositoryHygieneAudit hygieneAudit)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public ValidationPlan GetPlan(
        string profile,
        string configuration,
        bool restore,
        string changedPaths = "")
    {
        return catalog.CreatePlan(profile, configuration, restore, changedPaths);
    }

    public Task<ValidationReport> ValidateAsync(
        string profile,
        string configuration,
        bool restore,
        string changedPaths,
        bool failFast,
        CancellationToken cancellationToken,
        string? requestingTaskId = null) =>
        ValidatePlanAsync(() => GetPlan(profile, configuration, restore, changedPaths),
            restore, failFast, cancellationToken, requestingTaskId);

    internal async Task<ValidationReport> ValidatePlanAsync(
        Func<ValidationPlan> selectPlan, bool restore, bool failFast,
        CancellationToken cancellationToken, string? requestingTaskId)
    {
        if (!options.AllowBuild)
        {
            throw new InvalidOperationException(
                "Validation is disabled. Set REIGN_MCP_ALLOW_BUILD=true in the trusted MCP configuration.");
        }
        if (restore && !options.AllowRestore)
        {
            throw new InvalidOperationException(
                "Package restore is disabled. Set REIGN_MCP_ALLOW_RESTORE=true only when dependency resolution is intended.");
        }

        var plan = selectPlan();
        var configuration = plan.Configuration;
        using var validationLease = ReignValidationLease.Acquire(options.EffectiveLeaseRoot, plan, requestingTaskId);
        validationLease.Update("fingerprinting");
        var sourceFingerprint = ComputeSourceFingerprint(plan);
        validationLease.Update("repository-hygiene", sourceFingerprint: sourceFingerprint);
        var repositoryHygiene = await hygieneAudit.AuditAsync(cancellationToken)
            .ConfigureAwait(false);
        var reusable = plan.CoverageComplete && !repositoryHygiene.BlocksValidation
            ? FindReusableSuccessfulReport(plan, sourceFingerprint, repositoryHygiene)
            : null;
        string? finalFingerprint = null;
        string? sourceError = null;
        if (reusable is not null)
        {
            // Verifying a retained artifact inventory can take time. Do not publish
            // a reused success if the requesting tree changed during that check.
            finalFingerprint = CheckFinalSource(plan, sourceFingerprint, out sourceError);
            if (sourceError is null)
            {
                var result = reusable with { Reused = true, ReusedFromRunId = reusable.RunId };
                validationLease.Complete(result);
                return result;
            }
        }
        var runId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")
            + "-"
            + Guid.NewGuid().ToString("N")[..8];
        var started = DateTimeOffset.UtcNow;
        var runRoot = Path.Combine(options.BuildRoot, "validation", runId);
        var reportPath = Path.Combine(runRoot, "validation-report.json");
        validationLease.Update("preparing", runId: runId, reportPath: reportPath,
            sourceFingerprint: sourceFingerprint);
        var hygienePath = Path.Combine(runRoot, "repository-hygiene-report.json");
        Directory.CreateDirectory(runRoot);
        repositoryHygiene = repositoryHygiene with { EvidencePath = hygienePath };
        await File.WriteAllTextAsync(hygienePath,
            JsonSerializer.Serialize(repositoryHygiene, JsonOptions), cancellationToken)
            .ConfigureAwait(false);
        var results = new List<ProjectValidationResult>();

        if (reusable is null && plan.CoverageComplete && !repositoryHygiene.BlocksValidation)
        {
            var filteredTestProjects = plan.VerificationOperations
                .Where(item => item.Kind.Equals("test-filter", StringComparison.OrdinalIgnoreCase))
                .Select(item => item.ProjectId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var project in plan.Projects)
            {
                var projectPath = Path.Combine(
                    options.WorkspaceRoot,
                    project.ProjectPath.Replace('/', Path.DirectorySeparatorChar));
                var output = Path.Combine(runRoot, project.Id, "out");
                Directory.CreateDirectory(output);
                var operation = project.IsTestProject
                    && !filteredTestProjects.Contains(project.Id)
                        ? "test"
                        : "build";
                var arguments = new List<string>
                {
                    operation,
                    projectPath,
                    "-c",
                    configuration,
                    restore ? "--restore" : "--no-restore",
                    $"-p:OutputPath={Path.TrimEndingDirectorySeparator(output)}{Path.DirectorySeparatorChar}",
                    "-p:AppendTargetFrameworkToOutputPath=false",
                    "-p:AppendRuntimeIdentifierToOutputPath=false"
                };
                arguments.Add($"-p:ReignRestoreBundledTools={restore.ToString().ToLowerInvariant()}");
                if (operation == "test")
                {
                    arguments.Add("--logger");
                    arguments.Add($"trx;LogFileName={project.Id}.trx");
                    arguments.Add("--results-directory");
                    arguments.Add(Path.Combine(runRoot, project.Id, "test-results"));
                }

                validationLease.Update("building", operation + ":" + project.Id, results.Count);
                var process = await runner.RunAsync(
                    "dotnet",
                    arguments,
                    options.WorkspaceRoot,
                    timeout: Timeout.InfiniteTimeSpan,
                    artifactDirectory: Path.Combine(runRoot, project.Id),
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                results.Add(new ProjectValidationResult
                {
                    Project = project,
                    Operation = operation,
                    Process = process
                });
                if (!process.Ok && failFast)
                {
                    break;
                }
            }

            var coreSelected = plan.Projects.Any(project =>
                project.Group.Equals("core", StringComparison.OrdinalIgnoreCase));
            if (results.All(result => result.Process.Ok)
                && plan.VerificationOperations.Count > 0)
            {
                if (!options.AllowOfflineVerification)
                {
                    throw new InvalidOperationException(
                        "Module validation requires isolated CLI verification. Set REIGN_MCP_ALLOW_OFFLINE_VERIFICATION=true.");
                }
                foreach (var operation in plan.VerificationOperations)
                {
                    validationLease.Update("verifying", operation.ModuleId + ":" + operation.Kind, results.Count);
                    var project = plan.Projects.FirstOrDefault(candidate =>
                        candidate.Id.Equals(operation.ProjectId,
                            StringComparison.OrdinalIgnoreCase));
                    if (project is null)
                    {
                        throw new InvalidOperationException(
                            $"Module {operation.ModuleId} requested verification from unselected project {operation.ProjectId}.");
                    }
                    if (operation.Kind.Equals("test-filter", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!project.IsTestProject || string.IsNullOrWhiteSpace(operation.TestFilter))
                        {
                            throw new InvalidOperationException(
                                $"Module {operation.ModuleId} declared an invalid filtered test operation.");
                        }
                        var testOutput = Path.Combine(runRoot, project.Id, "out");
                        var testResult = await runner.RunAsync(
                            "dotnet",
                            [
                                "test",
                                Path.Combine(options.WorkspaceRoot,
                                    project.ProjectPath.Replace('/', Path.DirectorySeparatorChar)),
                                "-c", configuration,
                                "--no-restore",
                                "--no-build",
                                $"-p:OutputPath={Path.TrimEndingDirectorySeparator(testOutput)}{Path.DirectorySeparatorChar}",
                                "-p:AppendTargetFrameworkToOutputPath=false",
                                "-p:AppendRuntimeIdentifierToOutputPath=false",
                                "--filter", operation.TestFilter,
                                "--logger", $"trx;LogFileName={project.Id}-filtered.trx",
                                "--results-directory", Path.Combine(runRoot, project.Id, "test-results")
                            ],
                            options.WorkspaceRoot,
                            timeout: Timeout.InfiniteTimeSpan,
                            artifactDirectory: Path.Combine(runRoot, project.Id),
                            cancellationToken: cancellationToken).ConfigureAwait(false);
                        results.Add(new ProjectValidationResult
                        {
                            Project = project,
                            Operation = $"module:{operation.ModuleId}:test-filter",
                            Process = testResult
                        });
                        if (!testResult.Ok && failFast) break;
                        continue;
                    }

                    var executable = Path.Combine(runRoot, project.Id, "out",
                        Path.GetFileNameWithoutExtension(project.ProjectPath) + (project.Id == "server" ? ".dll" : ".exe"));
                    if (!File.Exists(executable))
                    {
                        throw new FileNotFoundException(
                            $"Module verification executable was not built for {operation.ModuleId}.",
                            executable);
                    }
                    var verification = await runner.RunAsync(
                        executable,
                        operation.Arguments,
                        Path.GetDirectoryName(executable)!,
                        timeout: Timeout.InfiniteTimeSpan,
                        artifactDirectory: Path.Combine(runRoot, project.Id, "out", "data", "tests"),
                        environment: new Dictionary<string, string>
                        {
                            ["REIGN_VALIDATION_MODE"] = "1",
                            ["REIGN_VERIFICATION_CLIENT_ASSEMBLY"] = Path.Combine(runRoot, "client", "out", "ReignBeta.dll"),
                            ["REIGN_DB_NAME"] = "ReignValidation"
                        },
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                    verification = RequireSuccessfulVerificationPayload(
                        verification, operation.ModuleId);
                    results.Add(new ProjectValidationResult
                    {
                        Project = project,
                        Operation = $"module:{operation.ModuleId}:{operation.Kind}",
                        Process = verification
                    });
                    if (!verification.Ok && failFast) break;
                }
            }
            else if (coreSelected && results.All(result => result.Process.Ok))
            {
                if (!options.AllowOfflineVerification)
                {
                    throw new InvalidOperationException(
                        "Core validation requires isolated quick/offline verification. Set REIGN_MCP_ALLOW_OFFLINE_VERIFICATION=true.");
                }
                var serverProject = plan.Projects.FirstOrDefault(project =>
                    project.Id.Equals("server", StringComparison.OrdinalIgnoreCase));
                var serverExecutable = Path.Combine(
                    runRoot,
                    "server",
                    "out",
                    "ReignServer.dll");
                if (serverProject is not null && File.Exists(serverExecutable))
                {
                    var tier = plan.VerificationTier == "offline" ? "offline" : "quick";
                    validationLease.Update("verifying", "verification:" + tier, results.Count);
                    var verification = await runner.RunAsync(
                        serverExecutable,
                        [
                            "--run-verification",
                            "--tier",
                            tier,
                            "--seed",
                            "1337",
                            "--repeat",
                            "1",
                            "--fail-fast",
                            "--json"
                        ],
                        Path.GetDirectoryName(serverExecutable)!,
                        timeout: Timeout.InfiniteTimeSpan,
                        artifactDirectory: Path.Combine(runRoot, "server", "out", "data", "tests"),
                        environment: new Dictionary<string, string>
                        {
                            ["REIGN_VALIDATION_MODE"] = "1",
                            ["REIGN_VERIFICATION_CLIENT_ASSEMBLY"] = Path.Combine(runRoot, "client", "out", "ReignBeta.dll"),
                            ["REIGN_DB_NAME"] = "ReignValidation"
                        },
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                    verification = RequireSuccessfulVerificationPayload(
                        verification, tier);
                    results.Add(new ProjectValidationResult
                    {
                        Project = serverProject,
                        Operation = $"verification:{tier}",
                        Process = verification
                    });
                }
            }
        }

        var artifactHashes = CaptureDeploymentArtifactHashes(runRoot);
        if (reusable is null)
        {
            validationLease.Update("checking-final-source", completedOperations: results.Count);
            finalFingerprint = CheckFinalSource(plan, sourceFingerprint, out sourceError);
        }
        var report = new ValidationReport
        {
            Schema = "reign-validation-report-v4",
            ValidatorVersion = "0.7.3",
            RunId = runId,
            SourceFingerprintSha256 = sourceFingerprint,
            FinalSourceFingerprintSha256 = finalFingerprint,
            SourceFingerprintError = sourceError,
            StartedUtc = started.ToString("O"),
            CompletedUtc = DateTimeOffset.UtcNow.ToString("O"),
            Ok = sourceError is null && plan.CoverageComplete
                && !repositoryHygiene.BlocksValidation
                && (plan.NonCodeOnly || plan.Projects.All(project => results.Any(result =>
                    result.Project.ProjectPath.Equals(
                        project.ProjectPath,
                        StringComparison.OrdinalIgnoreCase))))
                && results.All(result => result.Process.Ok),
            Reused = false,
            ReusedFromRunId = null,
            Plan = plan,
            ArtifactRoot = runRoot,
            ReportPath = reportPath,
            RepositoryHygiene = repositoryHygiene,
            Results = results,
            DeploymentArtifactHashes = artifactHashes
        };
        await File.WriteAllTextAsync(
            reportPath,
            JsonSerializer.Serialize(report, JsonOptions),
            cancellationToken).ConfigureAwait(false);
        validationLease.Complete(report);
        return report;
    }

    private ValidationReport? FindReusableSuccessfulReport(
        ValidationPlan plan,
        string sourceFingerprint,
        RepositoryHygieneReport repositoryHygiene)
    {
        var root = Path.Combine(options.BuildRoot, "validation");
        if (!Directory.Exists(root)) return null;
        var plannedProjects = plan.Projects.Select(project => project.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in new DirectoryInfo(root).EnumerateDirectories()
                     .OrderByDescending(item => item.Name, StringComparer.OrdinalIgnoreCase)
                     .Take(50))
        {
            var path = Path.Combine(directory.FullName, "validation-report.json");
            if (!File.Exists(path)) continue;
            try
            {
                var candidate = JsonSerializer.Deserialize<ValidationReport>(
                    File.ReadAllText(path), JsonOptions);
                if (candidate is null
                    || !candidate.Ok
                    || !candidate.HasMatchingSourceFingerprints
                    || candidate.RepositoryHygiene.BlocksValidation
                    || !HygieneSnapshotsMatch(candidate.RepositoryHygiene, repositoryHygiene)
                    || !candidate.Plan.CoverageComplete
                    || !candidate.SourceFingerprintSha256.Equals(sourceFingerprint,
                        StringComparison.OrdinalIgnoreCase)
                    || !candidate.Plan.Configuration.Equals(plan.Configuration,
                        StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(candidate.Plan.Scope, plan.Scope,
                        StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(candidate.Plan.VerificationTier, plan.VerificationTier,
                        StringComparison.OrdinalIgnoreCase)
                    || !Directory.Exists(candidate.ArtifactRoot)
                    || (candidate.Plan.Profile is "product" or "all"
                        && candidate.DeploymentArtifactHashes.Count == 0)
                    || candidate.Results.Any(result => !result.Process.Ok))
                {
                    continue;
                }
                var candidateProjects = candidate.Plan.Projects
                    .Select(project => project.Id)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (plannedProjects.SetEquals(candidateProjects))
                {
                    if (candidate.DeploymentArtifactHashes.Count > 0)
                        VerifyArtifactHashes(candidate.ArtifactRoot, candidate.DeploymentArtifactHashes);
                    return candidate;
                }
            }
            catch (JsonException)
            {
                // Ignore incomplete or older incompatible reports and continue.
            }
            catch (IOException)
            {
                // A concurrently observed report may still be flushing; it is not reusable yet.
            }
            catch (InvalidDataException)
            {
                // An altered recorded artifact is not reusable qualification.
            }
        }
        return null;
    }

    private static bool HygieneSnapshotsMatch(
        RepositoryHygieneReport left,
        RepositoryHygieneReport right)
    {
        if (left.Enforcement != right.Enforcement
            || left.Ok != right.Ok
            || left.BlocksValidation != right.BlocksValidation
            || left.TrackedFileCount != right.TrackedFileCount
            || left.UntrackedFileCount != right.UntrackedFileCount
            || left.Issues.Count != right.Issues.Count)
        {
            return false;
        }
        return left.Issues.Select(IssueKey).Order(StringComparer.Ordinal)
            .SequenceEqual(right.Issues.Select(IssueKey).Order(StringComparer.Ordinal),
                StringComparer.Ordinal);
    }

    private static string IssueKey(RepositoryHygieneIssue issue) =>
        string.Join("\0", issue.Category, issue.Path, issue.Classifier);

    private string? CheckFinalSource(ValidationPlan plan, string initial, out string? error)
    {
        try
        {
            var final = ComputeSourceFingerprint(plan);
            error = final == initial ? null : "Managed source changed during validation.";
            return final;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
            or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            error = $"Could not read managed source at the end of validation ({ex.GetType().Name}).";
            return null;
        }
    }

    private string ComputeSourceFingerprint(ValidationPlan plan)
    {
        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".cs", ".csproj", ".props", ".targets", ".json", ".xml", ".xaml", ".md",
            ".ps1", ".psm1", ".cmd", ".toml", ".sln", ".slnx", ".yml", ".yaml", ".py", ".iss", ".js", ".mjs", ".txt", ".html", ".css", ".sh", ".service"
        };
        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".git", ".tmp", ".codex-build", "artifacts", "bin", "obj",
            "build", "dist", "logs", "node_modules", ".venv", ".pytest_cache",
            "staging", "deployment-backups", "verification_contracts", "publish",
            "TestResults", "decompiled", "third_party", "server", "PortraitCache"
        };
        var roots = plan.Projects
            .Select(project => project.ProjectPath.Split('/')[0])
            .Concat(["ReignRelease"])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var files = new SortedSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.Combine(options.WorkspaceRoot, "reign-projects.json"),
            Path.Combine(options.WorkspaceRoot, "reign.modules.json"),
            Path.Combine(options.WorkspaceRoot, "reign.testing.json"),
            Path.Combine(options.WorkspaceRoot, "reign.repository.json"),
            Path.Combine(options.WorkspaceRoot, "reign.repositories.json"),
            Path.Combine(options.WorkspaceRoot, "Directory.Build.props"),
            Path.Combine(options.WorkspaceRoot, ".gitignore"),
            Path.Combine(options.WorkspaceRoot, ".gitattributes"),
            Path.Combine(options.WorkspaceRoot, "AGENTS.md"),
            Path.Combine(options.WorkspaceRoot, ".codex", "hooks.json")
        };
        var layout = new ReignSourceLayout(options.WorkspaceRoot);
        if (layout.IsPaired)
        {
            files.Add(Path.Combine(layout.ServerRoot!, "reign.repository.json"));
            files.Add(Path.Combine(layout.ServerRoot!, ".gitignore"));
            files.Add(Path.Combine(layout.ServerRoot!, ".gitattributes"));
            files.Add(Path.Combine(layout.ServerRoot!, "AGENTS.md"));
        }
        foreach (var relativeRoot in roots)
        {
            var pending = new Stack<DirectoryInfo>();
            pending.Push(new DirectoryInfo(Path.Combine(options.WorkspaceRoot, relativeRoot)));
            while (pending.Count > 0)
            {
                var directory = pending.Pop();
                if (!directory.Exists
                    || !layout.CanTraverse(directory)
                    || excluded.Contains(directory.Name))
                {
                    continue;
                }
                foreach (var file in directory.EnumerateFiles())
                {
                    if (extensions.Contains(file.Extension) || file.Name.Equals("reignctl", StringComparison.OrdinalIgnoreCase))
                    {
                        files.Add(file.FullName);
                    }
                }
                foreach (var child in directory.EnumerateDirectories())
                {
                    pending.Push(child);
                }
            }
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[64 * 1024];
        foreach (var path in files)
        {
            if (!File.Exists(path))
            {
                continue;
            }
            var relative = Path.GetRelativePath(options.WorkspaceRoot, path)
                .Replace('\\', '/')
                .ToLowerInvariant();
            hash.AppendData(Encoding.UTF8.GetBytes(relative + "\n"));
            using var stream = File.OpenRead(path);
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                hash.AppendData(buffer, 0, read);
            }
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    public ValidationLaneStatus GetStatus() => ReignValidationLease.ReadStatus(options.EffectiveLeaseRoot);

    private static ProcessResult RequireSuccessfulVerificationPayload(
        ProcessResult process, string label)
    {
        if (!process.Ok) return process;
        var payloadSucceeded = VerificationPayloadSucceeded(process.StandardOutput);
        if (payloadSucceeded) return process;
        return process with
        {
            Ok = false,
            StandardError = string.Join(Environment.NewLine,
                new[]
                {
                    process.StandardError.Trim(),
                    $"{label} verification did not return a successful JSON payload."
                }.Where(value => value.Length > 0))
        };
    }

    internal static bool VerificationPayloadSucceeded(string standardOutput)
    {
        var output = (standardOutput ?? string.Empty).Trim();
        if (output.Length == 0) return false;
        if (TryReadSuccessfulPayload(output)) return true;

        // Verification is expected to emit one final JSON document. Preserve
        // fail-closed parsing, but tolerate bounded diagnostic lines written
        // before that document by an exercised runtime path.
        for (var index = output.LastIndexOf('{'); index >= 0;)
        {
            if (TryReadSuccessfulPayload(output[index..])) return true;
            index = index == 0 ? -1 : output.LastIndexOf('{', index - 1);
        }
        return false;
    }

    private static bool TryReadSuccessfulPayload(string candidate)
    {
        try
        {
            using var document = JsonDocument.Parse(candidate);
            var root = document.RootElement;
            return ReadTrue(root, "ok") || ReadTrue(root, "passed");
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool ReadTrue(JsonElement root, string property)
    {
        return root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty(property, out var value)
            && value.ValueKind is JsonValueKind.True;
    }
}
