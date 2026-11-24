using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Reign.Mcp.Server;

public sealed record PartyAgencyApprovedModule(string AssemblyName, Guid Mvid, string Sha256);

public sealed record PartyAgencyApprovedArtifactWitness(
    string Schema, string RunId, string CampaignId, string CampaignTestRunId,
    string CapturedUtc, string AuthoritySha256, string CandidateSha256, string ReviewSha256,
    string SourceInventorySha256, string ClientReportSha256, string ClientReportRunId,
    string ClientSourceFingerprintSha256, string LinuxReportSha256, string LinuxReportRunId,
    string LinuxSourceFingerprintSha256, string LinuxManifestSha256,
    PartyAgencyApprovedModule Client, PartyAgencyApprovedModule Server)
{
    internal const string WitnessSchema = "reign-party-agency-approved-artifacts-v1";

    internal bool BelongsTo(string runId, string campaignId, string campaignTestRunId)
    {
        if (Schema != WitnessSchema || RunId != runId || CampaignId != campaignId
            || CampaignTestRunId != campaignTestRunId
            || Client is not { AssemblyName: "ReignBeta" } || Client.Mvid == Guid.Empty
            || Server is not { AssemblyName: "ReignServer" } || Server.Mvid == Guid.Empty)
            return false;

        string[] digests = [AuthoritySha256, CandidateSha256, ReviewSha256, SourceInventorySha256,
            ClientReportSha256, ClientSourceFingerprintSha256, LinuxReportSha256,
            LinuxSourceFingerprintSha256, LinuxManifestSha256, Client.Sha256, Server.Sha256];
        return digests.All(PartyAgencyApprovedArtifacts.IsSha256);
    }
}

internal static class PartyAgencyApprovedArtifacts
{
    private const string ClientArtifact = "client/out/ReignBeta.dll";
    private const string ServerArtifact = "ReignServer.dll";

    internal static PartyAgencyApprovedArtifactWitness Capture(ReignMcpOptions options,
        ReignValidationService validation, string runId, string campaignId, string campaignTestRunId)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Approved artifact capture requires the Windows reviewed deployment authority.");
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(profile) || !Path.IsPathFullyQualified(profile))
            throw new InvalidDataException("No local reviewed deployment authority profile is available.");

        // The local promoted deployment pointer is the only approval selector.
        // No report, artifact, licensed-installation path, or digest comes from tool arguments.
        using var pointer = new BoundJson(Path.Combine(profile, ".reign", "deployment-source.json"));
        JsonElement selection = pointer.Root;
        if (Text(selection, "Schema") != "reign-deployment-authority-v1")
            throw new InvalidDataException("Unsupported reviewed deployment authority.");
        using var candidate = new BoundJson(Text(selection, "CandidatePath"), Digest(selection, "CandidateSha256"));
        using var review = new BoundJson(Text(selection, "ReviewPath"), Digest(selection, "ReviewSha256"));
        JsonElement approved = candidate.Root;
        ValidateReviewBinding(approved, review.Root, candidate.Sha256);

        JsonElement clientApproval = Property(approved, "ClientReport");
        JsonElement linuxApproval = Property(approved, "LinuxReport");
        string clientPath = OriginalReportPath(options.BuildRoot, clientApproval, "validation", "validation-report.json");
        string linuxPath = OriginalReportPath(options.BuildRoot, linuxApproval, "linux-server", "linux-build-report.json");
        using var clientReport = new BoundJson(clientPath, Digest(clientApproval, "sha256"));
        using var linuxReport = new BoundJson(linuxPath, Digest(linuxApproval, "sha256"));
        // Retain the same read handles while the existing verifier checks canonical roots,
        // current source, reports and their complete artifact inventories.
        DeploymentSourceCheck check = validation.CheckDeploymentSource(clientPath, linuxPath);
        if (!check.Ok || check.ServerRoot is null
            || !ReignValidationService.SamePath(check.WorkspaceRoot, Text(approved, "WorkspaceRoot"))
            || !ReignValidationService.SamePath(check.ServerRoot, Text(approved, "ServerRoot")))
            throw new InvalidDataException("Both approved reports must match the current, reviewed primary source pair.");

        string clientHash = Digest(Property(clientReport.Root, "DeploymentArtifactHashes"), ClientArtifact);
        var approvedClientRows = Property(approved, "ClientArtifacts").EnumerateArray()
            .Where(row => Text(row, "path") == "ReignBeta.dll").ToArray();
        if (approvedClientRows.Length != 1 || Digest(approvedClientRows[0], "sha256") != clientHash)
            throw new InvalidDataException("The approved client inventory does not bind the exact ReignBeta assembly.");

        string linuxManifestHash = Digest(approved, "LinuxManifestSha256");
        if (Digest(linuxReport.Root, "ArtifactManifestSha256") != linuxManifestHash)
            throw new InvalidDataException("The approved Linux manifest does not match its canonical report.");
        string linuxArtifactRoot = Text(linuxReport.Root, "ArtifactRoot");
        using var manifest = new BoundJson(Path.Combine(linuxArtifactRoot, "reign-linux-artifact.json"), linuxManifestHash);
        string linuxFingerprint = Digest(linuxReport.Root, "SourceFingerprintSha256");
        if (Text(manifest.Root, "schema") != "reign-linux-artifact-v1"
            || Digest(manifest.Root, "sourceFingerprint") != linuxFingerprint)
            throw new InvalidDataException("The approved Linux manifest does not belong to the qualified source.");
        string serverHash = Digest(Property(manifest.Root, "files"), ServerArtifact);

        PartyAgencyApprovedModule client = ReadManagedArtifact(
            Path.Combine(Text(clientReport.Root, "ArtifactRoot"), ClientArtifact), "ReignBeta", clientHash);
        PartyAgencyApprovedModule server = ReadManagedArtifact(
            Path.Combine(linuxArtifactRoot, ServerArtifact), "ReignServer", serverHash);
        return new(PartyAgencyApprovedArtifactWitness.WitnessSchema, runId, campaignId, campaignTestRunId,
            DateTimeOffset.UtcNow.ToString("O"), pointer.Sha256, candidate.Sha256, review.Sha256,
            Digest(approved, "SourceInventorySha256"), clientReport.Sha256, Text(clientReport.Root, "RunId"),
            Digest(clientReport.Root, "SourceFingerprintSha256"), linuxReport.Sha256,
            Text(linuxReport.Root, "RunId"), linuxFingerprint, manifest.Sha256, client, server);
    }

    internal static void ValidateReviewBinding(JsonElement candidate, JsonElement review, string candidateHash)
    {
        if (Text(candidate, "Schema") != "reign-deployment-authority-content-v1"
            || Digest(review, "CandidateSha256") != candidateHash
            || Digest(review, "DiffSha256") != Digest(candidate, "DiffSha256")
            || Property(review, "PriorAuthoritySha256").GetString()
                != Property(candidate, "PriorAuthoritySha256").GetString()
            || Text(review, "Reason").Trim().Length < 15)
            throw new InvalidDataException("The retained deployment review does not bind this exact candidate and source delta.");

        var changed = Property(candidate, "Diff").EnumerateArray().Select(row => Text(row, "path")).ToArray();
        var reviewed = Property(review, "ReviewedPaths").EnumerateArray()
            .Select(path => path.GetString() ?? "").ToArray();
        if (changed.Distinct(StringComparer.Ordinal).Count() != changed.Length
            || reviewed.Distinct(StringComparer.Ordinal).Count() != reviewed.Length
            || !changed.ToHashSet(StringComparer.Ordinal).SetEquals(reviewed))
            throw new InvalidDataException("The retained deployment review does not cover the exact candidate paths.");
    }

    internal static PartyAgencyApprovedModule ReadManagedArtifact(string path, string assemblyName, string approvedSha256)
    {
        using var stream = OpenPhysicalRead(path);
        using var pe = new PEReader(stream, PEStreamOptions.PrefetchEntireImage | PEStreamOptions.LeaveOpen);
        // The PE reader owns one buffered image for both the SHA and managed metadata.
        string sha256 = Convert.ToHexStringLower(SHA256.HashData(pe.GetEntireImage().GetContent().AsSpan()));
        if (sha256 != approvedSha256)
            throw new InvalidDataException("The approved managed assembly bytes changed.");
        if (!pe.HasMetadata)
            throw new InvalidDataException("The approved artifact is not a managed assembly.");
        MetadataReader metadata = pe.GetMetadataReader();
        string name = metadata.IsAssembly ? metadata.GetString(metadata.GetAssemblyDefinition().Name) : "";
        Guid mvid = metadata.GetGuid(metadata.GetModuleDefinition().Mvid);
        if (name != assemblyName || mvid == Guid.Empty)
            throw new InvalidDataException("The approved artifact lacks the expected managed assembly identity.");
        return new(name, mvid, sha256);
    }

    private static string OriginalReportPath(string buildRoot, JsonElement approval, string category, string filename)
    {
        string path = Text(approval, "path");
        string? runId = Path.GetFileName(Path.GetDirectoryName(path));
        if (!Path.IsPathFullyQualified(path) || runId is null || !Regex.IsMatch(runId, @"^\d{8}-\d{6}-[0-9a-f]{8}$")
            || !ReignValidationService.SamePath(path, Path.Combine(buildRoot, category, runId, filename)))
            throw new InvalidDataException("The reviewed deployment report is outside the original canonical report location.");
        return path;
    }

    private static FileStream OpenPhysicalRead(string path)
    {
        if (!Path.IsPathFullyQualified(path))
            throw new InvalidDataException("Approved artifact evidence requires a local absolute file path.");
        for (string? entry = Path.GetFullPath(path); entry != null; entry = Path.GetDirectoryName(entry))
            if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Approved artifact evidence cannot read a redirected file.");
        return File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    }


    private static JsonElement Property(JsonElement source, string key)
    {
        if (source.ValueKind != JsonValueKind.Object || !source.TryGetProperty(key, out JsonElement value))
            throw new InvalidDataException("Approved artifact evidence is missing " + key + ".");
        return value;
    }

    private static string Text(JsonElement source, string key) =>
        Property(source, key).GetString() is { Length: > 0 } value
            ? value : throw new InvalidDataException("Approved artifact evidence is missing " + key + ".");

    private static string Digest(JsonElement source, string key)
    {
        string digest = Text(source, key);
        if (!IsSha256(digest))
            throw new InvalidDataException("Approved artifact evidence has an invalid digest for " + key + ".");
        return digest;
    }

    internal static bool IsSha256(string? value) => value is { Length: 64 }
        && value.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f'));

    private sealed class BoundJson : IDisposable
    {
        private readonly FileStream _stream;
        private readonly JsonDocument _document;
        public JsonElement Root => _document.RootElement;
        public string Sha256 { get; }

        public BoundJson(string path, string? expectedSha256 = null)
        {
            _stream = OpenPhysicalRead(path);
            try
            {
                if (_stream.Length > 64L * 1024 * 1024)
                    throw new InvalidDataException("Approved JSON evidence exceeds its read bound.");
                byte[] bytes = new byte[(int)_stream.Length];
                _stream.ReadExactly(bytes);
                Sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));
                if (expectedSha256 is not null && Sha256 != expectedSha256)
                    throw new InvalidDataException("The durable reviewed deployment evidence changed.");
                _document = JsonDocument.Parse(bytes, new JsonDocumentOptions { AllowDuplicateProperties = false });
            }
            catch
            {
                _stream.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            _document.Dispose();
            _stream.Dispose();
        }
    }
}
