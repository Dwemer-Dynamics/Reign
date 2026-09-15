using System.Text.Json;

namespace Reign.Mcp.Server;

public sealed partial class ReignValidationService
{
    // Cross-publish only the Linux runtime, retaining the canonical ownership audit and OS lease.
    public async Task<LinuxServerBuildReport> BuildLinuxServerAsync(bool restore,
        string? requestingTaskId = null, CancellationToken cancellationToken = default)
    {
        if (!options.AllowBuild || (restore && !options.AllowRestore))
            throw new InvalidOperationException("Linux publishing requires the trusted build/restore gates.");
        var plan = GetPlan("all", "Release", restore);
        if (!plan.CoverageComplete || plan.Status != "ready")
            throw new InvalidOperationException("Resolve paired-source ownership before Linux publishing.");
        using var lease = ReignValidationLease.Acquire(options.BuildRoot, plan, requestingTaskId);
        var hygiene = await hygieneAudit.AuditAsync(cancellationToken).ConfigureAwait(false);
        if (hygiene.BlocksValidation)
        {
            string hygienePath = Path.Combine(options.BuildRoot, "linux-hygiene-report.json");
            await File.WriteAllTextAsync(hygienePath, JsonSerializer.Serialize(hygiene, JsonOptions), cancellationToken);
            throw new InvalidOperationException("Repository hygiene blocks Linux publishing: " + hygienePath);
        }
        string fingerprint = ComputeSourceFingerprint(plan);
        string runId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];
        string root = Path.Combine(options.BuildRoot, "linux-server", runId);
        Directory.CreateDirectory(root);
        string reportPath = Path.Combine(root, "linux-build-report.json");
        string publish = Path.Combine(root, "publish");
        lease.Update("building", "linux-server", runId: runId, reportPath: reportPath, sourceFingerprint: fingerprint);
        var arguments = new List<string>
        {
            "publish", Path.Combine(options.WorkspaceRoot, "ReignBetaServer", "ReignBetaServer.csproj"),
            "-c", "Release", "-p:ReignLinux=true", "--runtime", "linux-x64",
            "--self-contained", "true", "--output", publish
        };
        if (!restore) arguments.Add("--no-restore");
        var process = await runner.RunAsync("dotnet", arguments, options.WorkspaceRoot,
            artifactDirectory: root, timeout: TimeSpan.FromMinutes(15),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (process.Ok)
        {
            using var release = JsonDocument.Parse(File.ReadAllText(Path.Combine(options.WorkspaceRoot, "ReignRelease", "release.json")));
            string version = release.RootElement.GetProperty("version").GetString()
                ?? throw new InvalidDataException("Release version is missing.");
            int protocolVersion = release.RootElement.GetProperty("protocolVersion").GetInt32();
            var files = Directory.EnumerateFiles(publish, "*", SearchOption.AllDirectories)
                .ToDictionary(path => Path.GetRelativePath(publish, path).Replace('\\', '/'),
                    path => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant());
            await File.WriteAllTextAsync(Path.Combine(publish, "reign-linux-artifact.json"),
                JsonSerializer.Serialize(new { schema = "reign-linux-artifact-v1", version,
                    protocolVersion, sourceFingerprint = fingerprint, files }, JsonOptions), cancellationToken);
        }
        var report = new LinuxServerBuildReport("reign-linux-build-v1",
            process.Ok && File.Exists(Path.Combine(publish, "ReignBetaServer"))
                && fingerprint == ComputeSourceFingerprint(plan),
            runId, fingerprint, publish, reportPath, hygiene, process);
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, JsonOptions), cancellationToken);
        lease.CompleteOperation(report.Ok, runId, reportPath, fingerprint);
        return report;
    }
}

// A build report intentionally does not claim Linux runtime, full-product, or in-game validation.
public sealed record LinuxServerBuildReport(string Schema, bool Ok, string RunId,
    string SourceFingerprintSha256, string ArtifactRoot, string ReportPath,
    RepositoryHygieneReport RepositoryHygiene, ProcessResult Process);
