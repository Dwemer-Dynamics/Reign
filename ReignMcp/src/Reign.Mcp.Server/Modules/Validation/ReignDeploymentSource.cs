using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Reign.Mcp.Server;

public sealed record DeploymentSourceCheck(string Schema, bool Ok, string WorkspaceRoot,
    string? ServerRoot, IReadOnlyList<string> Diagnostics);

public sealed partial class ReignValidationService
{
    // Uses the validator's existing algorithm. Deployment never maintains a second
    // approximation of the canonical source fingerprint.
    public DeploymentSourceCheck CheckDeploymentSource(string validationReport = "",
        string linuxBuildReport = "")
    {
        var errors = new List<string>();
        string? serverRoot = null;
        try
        {
            var layout = new ReignSourceLayout(options.WorkspaceRoot);
            layout.ValidateProjections();
            serverRoot = layout.ServerRoot is null ? null : PhysicalDirectory(layout.ServerRoot);
            if (string.IsNullOrWhiteSpace(validationReport) && string.IsNullOrWhiteSpace(linuxBuildReport))
                throw new InvalidDataException("Select a qualified client or Linux report.");
            if (!string.IsNullOrWhiteSpace(validationReport))
            {
                AssertSuppliedReportLocation(validationReport, "validation", "validation-report.json");
                var report = JsonSerializer.Deserialize<ValidationReport>(File.ReadAllText(validationReport), JsonOptions)
                    ?? throw new InvalidDataException("Missing client report.");
                AssertCanonicalReport(validationReport, report.ReportPath, report.ArtifactRoot,
                    "validation", report.RunId, "validation-report.json");
                if (report.Schema != "reign-validation-report-v4" || report.Plan is null
                    || report.RepositoryHygiene is null || report.Results is null || report.DeploymentArtifactHashes is null
                    || !report.Ok || report.Results.Any(x => !x.Process.Ok) || report.Plan.Profile is not ("product" or "all")
                    || report.Plan.Configuration != "Release" || !report.Plan.CoverageComplete
                    || report.RepositoryHygiene.BlocksValidation || !report.RepositoryHygiene.Ok)
                    throw new InvalidDataException("Client deployment requires a successful clean Release product/all report.");
                var plan = GetPlan(report.Plan.Profile, "Release", report.Plan.Restore,
                    string.Join(';', report.Plan.ChangedPaths));
                if (plan.Status != "ready" || !plan.CoverageComplete
                    || ComputeSourceFingerprint(plan) != report.SourceFingerprintSha256)
                    throw new InvalidDataException("Client report does not match the current source. Reconcile and validate the intended source.");
                if (report.DeploymentArtifactHashes.Count == 0)
                    throw new InvalidDataException("Client report predates artifact binding; qualify with the current validator.");
                VerifyArtifactHashes(report.ArtifactRoot, report.DeploymentArtifactHashes);
                if (!CaptureDeploymentArtifactHashes(report.ArtifactRoot).Keys.ToHashSet(StringComparer.Ordinal)
                    .SetEquals(report.DeploymentArtifactHashes.Keys))
                    throw new InvalidDataException("Client/verifier artifact inventory has extra or missing files.");
            }
            if (!string.IsNullOrWhiteSpace(linuxBuildReport))
            {
                AssertSuppliedReportLocation(linuxBuildReport, "linux-server", "linux-build-report.json");
                var report = JsonSerializer.Deserialize<LinuxServerBuildReport>(File.ReadAllText(linuxBuildReport), JsonOptions)
                    ?? throw new InvalidDataException("Missing Linux report.");
                AssertCanonicalReport(linuxBuildReport, report.ReportPath, report.ArtifactRoot,
                    "linux-server", report.RunId, "linux-build-report.json", "publish");
                var plan = GetPlan("all", "Release", false);
                if (report.Schema != "reign-linux-build-v1" || report.RepositoryHygiene is null || report.Process is null
                    || !report.Ok || !report.Process.Ok || !report.RepositoryHygiene.Ok || report.RepositoryHygiene.BlocksValidation
                    || plan.Status != "ready" || !plan.CoverageComplete
                    || ComputeSourceFingerprint(plan) != report.SourceFingerprintSha256)
                    throw new InvalidDataException("Linux report does not match the current clean qualified source.");
                string manifestPath = Path.Combine(report.ArtifactRoot, "reign-linux-artifact.json");
                if (string.IsNullOrEmpty(report.ArtifactManifestSha256)
                    || FileSha256(manifestPath) != report.ArtifactManifestSha256)
                    throw new InvalidDataException("Linux artifact manifest is missing, changed, or predates artifact binding.");
                using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
                if (manifest.RootElement.GetProperty("sourceFingerprint").GetString() != report.SourceFingerprintSha256)
                    throw new InvalidDataException("Linux artifact belongs to a different source fingerprint.");
                var hashes = manifest.RootElement.GetProperty("files").EnumerateObject()
                    .ToDictionary(x => x.Name, x => x.Value.GetString() ?? "", StringComparer.Ordinal);
                VerifyArtifactHashes(report.ArtifactRoot, hashes);
                if (!Directory.EnumerateFiles(report.ArtifactRoot, "*", SearchOption.AllDirectories)
                    .Select(x => Path.GetRelativePath(report.ArtifactRoot, x).Replace('\\', '/'))
                    .Where(x => x != "reign-linux-artifact.json").ToHashSet(StringComparer.Ordinal).SetEquals(hashes.Keys))
                    throw new InvalidDataException("Linux artifact inventory has extra or missing files.");
            }
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException or JsonException
            or ArgumentException or InvalidOperationException or KeyNotFoundException)
        {
            errors.Add(e.Message);
        }
        return new("reign-deployment-source-check-v1", errors.Count == 0,
            PhysicalDirectory(options.WorkspaceRoot), serverRoot, errors);
    }

    private void AssertCanonicalReport(string supplied, string recorded, string artifact,
        string kind, string runId, string name, string artifactChild = "")
    {
        if (!Regex.IsMatch(runId, @"^\d{8}-\d{6}-[0-9a-f]{8}$"))
            throw new InvalidDataException("Invalid canonical run ID.");
        string root = Path.Combine(options.BuildRoot, kind, runId);
        if (!SamePath(supplied, Path.Combine(root, name)) || !SamePath(recorded, supplied)
            || !SamePath(artifact, Path.Combine(root, artifactChild)))
            throw new InvalidDataException("Foreign/copied report or artifact root: use this canonical workspace's original report.");
        RejectRedirectedPath(root, options.BuildRoot);
    }

    private void AssertSuppliedReportLocation(string supplied, string kind, string name)
    {
        string path = Path.GetFullPath(supplied);
        string runId = Path.GetFileName(Path.GetDirectoryName(path)) ?? "";
        if (!Regex.IsMatch(runId, @"^\d{8}-\d{6}-[0-9a-f]{8}$")
            || !SamePath(path, Path.Combine(options.BuildRoot, kind, runId, name)))
            throw new InvalidDataException("Only original reports beneath this workspace's canonical build root are readable.");
        RejectRedirectedPath(path, options.BuildRoot);
        if (new FileInfo(path).Length > 64 * 1024 * 1024)
            throw new InvalidDataException("Canonical deployment report exceeds its read bound.");
    }

    internal static bool SamePath(string left, string right) => Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar)
        .Equals(Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    internal static string PhysicalDirectory(string path)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(path));
        return (directory.ResolveLinkTarget(true)?.FullName ?? directory.FullName).TrimEnd(Path.DirectorySeparatorChar);
    }

    internal static string FileSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static IReadOnlyDictionary<string, string> CaptureDeploymentArtifactHashes(string root)
    {
        var hashes = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (string component in new[] { "client", "mcp" })
        {
            string folder = Path.Combine(root, component, "out");
            if (!Directory.Exists(folder)) continue;
            foreach (string file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            {
                RejectRedirectedPath(file, root);
                hashes.Add(Path.GetRelativePath(root, file).Replace('\\', '/'), FileSha256(file));
            }
        }
        return hashes;
    }

    internal static void VerifyArtifactHashes(string root, IReadOnlyDictionary<string, string> hashes)
    {
        if (hashes.Count == 0) throw new InvalidDataException("Empty artifact inventory.");
        foreach (var (relative, hash) in hashes)
        {
            if (Path.IsPathRooted(relative) || relative.Split('/', '\\').Contains(".."))
                throw new InvalidDataException("Artifact path escapes its qualified root.");
            string file = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
            RejectRedirectedPath(file, root);
            if (!Regex.IsMatch(hash, "^[0-9a-f]{64}$") || !File.Exists(file) || FileSha256(file) != hash)
                throw new InvalidDataException("Qualified artifact is missing or changed: " + relative);
        }
    }

    private static void RejectRedirectedPath(string path, string root)
    {
        if (Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar).Contains(".."))
            throw new InvalidDataException("Artifact escaped its qualified root.");
        for (string? current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
        {
            if ((File.Exists(current) || Directory.Exists(current))
                && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Redirected qualified artifact path.");
            if (SamePath(current, root)) break;
        }
    }
}
