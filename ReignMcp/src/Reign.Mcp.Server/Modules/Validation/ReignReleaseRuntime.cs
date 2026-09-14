using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Reign.Mcp.Server;

public sealed partial class ReignValidationService
{
    public async Task<ReleaseRuntimeReport> BuildReleaseRuntimeAsync(string pythonExecutable,
        string modelDirectory, string? requestingTaskId = null, CancellationToken cancellationToken = default)
    {
        if (!options.AllowBuild || !options.AllowOfflineVerification)
            throw new InvalidOperationException("Release runtime packaging requires the trusted build and offline-verification gates.");
        if (!Path.IsPathFullyQualified(pythonExecutable) || !File.Exists(pythonExecutable)
            || !Path.IsPathFullyQualified(modelDirectory) || !Directory.Exists(modelDirectory))
            throw new ArgumentException("Supply the absolute Python build executable and acquired model directory.");
        var plan = GetPlan("all", "Release", false);
        if (!plan.CoverageComplete || plan.Status != "ready")
            throw new InvalidOperationException("Resolve every paired-source ownership gap before packaging.");
        using var lease = ReignValidationLease.Acquire(options.BuildRoot, plan, requestingTaskId);
        var hygiene = await hygieneAudit.AuditAsync(cancellationToken).ConfigureAwait(false);
        if (hygiene.BlocksValidation) throw new InvalidOperationException("Enforced repository hygiene blocks release runtime packaging.");
        string fingerprint = ComputeSourceFingerprint(plan);
        string runId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];
        string root = Path.Combine(options.BuildRoot, "release-runtime", runId);
        Directory.CreateDirectory(root);
        string reportPath = Path.Combine(root, "runtime-report.json");
        lease.Update("packaging", "vector-runtime", runId: runId, reportPath: reportPath, sourceFingerprint: fingerprint);
        var process = await runner.RunAsync(pythonExecutable,
            [Path.Combine(options.WorkspaceRoot, "ReignRelease", "Build-VectorRuntime.py"),
             "--output", root, "--model", Path.GetFullPath(modelDirectory)], options.WorkspaceRoot,
            timeout: TimeSpan.FromMinutes(15), artifactDirectory: root,
            environment: new Dictionary<string, string> { ["REIGN_CANONICAL_RELEASE_BUILD"] = "1" },
            cancellationToken: cancellationToken).ConfigureAwait(false);
        string proofPath = Path.Combine(root, "isolated-proof", "release-proof.json");
        bool proofOk = false;
        if (process.Ok && File.Exists(proofPath))
        {
            using var proof = JsonDocument.Parse(File.ReadAllText(proofPath));
            proofOk = proof.RootElement.GetProperty("ok").GetBoolean()
                && proof.RootElement.GetProperty("bundledExecutable").GetBoolean()
                && proof.RootElement.GetProperty("networkBlocked").GetBoolean()
                && !proof.RootElement.GetProperty("listenerStarted").GetBoolean();
        }
        var report = new ReleaseRuntimeReport("reign-release-runtime-report-v1",
            process.Ok && proofOk && fingerprint == ComputeSourceFingerprint(plan), runId,
            fingerprint, root, reportPath, proofPath, hygiene, process);
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, JsonOptions), cancellationToken);
        lease.CompleteOperation(report.Ok, runId, reportPath, fingerprint);
        return report;
    }
}

public sealed record ReleaseRuntimeReport(string Schema, bool Ok, string RunId,
    string SourceFingerprintSha256, string ArtifactRoot, string ReportPath, string ProofPath,
    RepositoryHygieneReport RepositoryHygiene, ProcessResult Process);

[McpServerToolType]
public static class ReleaseRuntimeTools
{
    [McpServerTool(Name = "reign_build_release_runtime", ReadOnly = false, Destructive = false, Idempotent = false, UseStructuredContent = true)]
    [Description("Packages the pinned Windows vector runtime and proves offline model inference plus persistent search in fresh isolated storage under the canonical build lease. Requires absolute local build-Python and acquired model paths. Does not start a listener, access campaigns, call providers, install or upload. This component report does not replace full release validation or clean-machine acceptance.")]
    public static Task<ReleaseRuntimeReport> Build(ReignValidationService validation,
        string pythonExecutable, string modelDirectory, string requestingTaskId = "",
        CancellationToken cancellationToken = default) =>
        validation.BuildReleaseRuntimeAsync(pythonExecutable, modelDirectory, requestingTaskId, cancellationToken);
}
