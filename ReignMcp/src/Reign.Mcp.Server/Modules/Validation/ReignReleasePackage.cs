using System.ComponentModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;

namespace Reign.Mcp.Server;

public sealed partial class ReignValidationService
{
    public async Task<ReleasePackageReport> BuildReleasePackageAsync(string validationRunId,
        string pythonExecutable, string buildSpecification, string? requestingTaskId = null,
        CancellationToken cancellationToken = default)
    {
        if (!options.AllowBuild || !options.AllowOfflineVerification)
            throw new InvalidOperationException("Release packaging requires the trusted build and offline-verification gates.");
        if (!Regex.IsMatch(validationRunId, @"^\d{8}-\d{6}-[a-f0-9]{8}$")
            || !Path.IsPathFullyQualified(pythonExecutable) || !File.Exists(pythonExecutable)
            || !Path.IsPathFullyQualified(buildSpecification) || !File.Exists(buildSpecification))
            throw new ArgumentException("Supply an exact validation run ID and absolute existing Python/specification files.");
        var plan = GetPlan("all", "Release", false);
        if (!plan.CoverageComplete || plan.Status != "ready")
            throw new InvalidOperationException("Resolve paired-source ownership before packaging.");
        using var lease = ReignValidationLease.Acquire(options.BuildRoot, plan, requestingTaskId);
        var hygiene = await hygieneAudit.AuditAsync(cancellationToken).ConfigureAwait(false);
        if (hygiene.BlocksValidation) throw new InvalidOperationException("Enforced repository hygiene blocks release packaging.");
        string fingerprint = ComputeSourceFingerprint(plan);
        string validationPath = Path.Combine(options.BuildRoot, "validation", validationRunId, "validation-report.json");
        var validation = JsonSerializer.Deserialize<ValidationReport>(await File.ReadAllTextAsync(validationPath, cancellationToken));
        if (validation is null || !validation.Ok || validation.Plan.Profile != "all"
            || validation.Plan.Configuration != "Release" || validation.SourceFingerprintSha256 != fingerprint)
            throw new InvalidOperationException("A successful current-fingerprint all/Release report is required. Package only those exact validated artifacts.");
        string runId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];
        string root = Path.Combine(options.BuildRoot, "release-package", runId);
        Directory.CreateDirectory(root);
        string reportPath = Path.Combine(root, "package-report.json");
        lease.Update("packaging", "release-package", runId: runId, reportPath: reportPath, sourceFingerprint: fingerprint);
        var process = await runner.RunAsync(pythonExecutable,
            [Path.Combine(options.WorkspaceRoot, "ReignRelease", "Build-ReignPackage.py"),
             "--spec", buildSpecification, "--validation-report", validationPath, "--output", root],
            options.WorkspaceRoot, timeout: TimeSpan.FromMinutes(45), artifactDirectory: root,
            environment: new Dictionary<string, string> { ["REIGN_CANONICAL_RELEASE_BUILD"] = "1" },
            cancellationToken: cancellationToken).ConfigureAwait(false);
        string proofPath = Path.Combine(root, "assembly-proof.json");
        bool proofOk = false;
        if (process.Ok && File.Exists(proofPath))
        {
            using var proof = JsonDocument.Parse(await File.ReadAllTextAsync(proofPath, cancellationToken));
            proofOk = proof.RootElement.GetProperty("ok").GetBoolean()
                && proof.RootElement.GetProperty("sourceFingerprint").GetString() == fingerprint
                && !proof.RootElement.GetProperty("cleanComputerAccepted").GetBoolean()
                && !proof.RootElement.GetProperty("providerCallsMade").GetBoolean();
        }
        var report = new ReleasePackageReport("reign-release-package-report-v1",
            process.Ok && proofOk && fingerprint == ComputeSourceFingerprint(plan), runId,
            fingerprint, root, reportPath, proofPath, validationRunId, hygiene, process);
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, JsonOptions), cancellationToken);
        lease.CompleteOperation(report.Ok, runId, reportPath, fingerprint);
        return report;
    }
}

public sealed record ReleasePackageReport(string Schema, bool Ok, string RunId,
    string SourceFingerprintSha256, string ArtifactRoot, string ReportPath, string ProofPath,
    string ValidationRunId, RepositoryHygieneReport RepositoryHygiene, ProcessResult Process);

[McpServerToolType]
public static class ReleasePackageTools
{
    [McpServerTool(Name = "reign_build_release_package", ReadOnly = false, Destructive = false, Idempotent = false, UseStructuredContent = true)]
    [Description("Assembles a Windows Reign setup executable and five independently hashed offline payloads from an exact current successful all/Release report, locked external downloads, verified vector component and allowlisted shared portraits. Uses the canonical build lease and enforced paired-source hygiene. Requires trusted build/offline gates and absolute build-Python/specification paths. Never installs, launches a listener, uploads, accesses campaigns or calls providers. Assembly proof does not mark clean-computer or native acceptance complete.")]
    public static Task<ReleasePackageReport> Build(ReignValidationService validation, string validationRunId,
        string pythonExecutable, string buildSpecification, string requestingTaskId = "",
        CancellationToken cancellationToken = default) =>
        validation.BuildReleasePackageAsync(validationRunId, pythonExecutable, buildSpecification, requestingTaskId, cancellationToken);
}
