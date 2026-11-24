using System.Globalization;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Reign.Core.Contracts.Platform;

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

public sealed record PartyAgencyInstalledArtifactWitness(
    string Schema, string RunId, string CampaignId, string CampaignTestRunId, string TimelineId,
    string DisposableSaveName, string CapturedUtc, string ApprovedWitnessSha256, string InstallationRecordSha256,
    string LinuxRuntimeVersion, PartyAgencyApprovedModule Client, PartyAgencyApprovedModule Server,
    PartyAgencyApprovedModule CampaignSystem, PartyAgencyApprovedModule Core)
{
    internal const string WitnessSchema = "reign-party-agency-installed-artifacts-v1";

    internal bool BelongsTo(PartyAgencyCertificationState state, PartyAgencyApprovedArtifactWitness approval) =>
        Schema == WitnessSchema && RunId == state.RunId && CampaignId == state.CampaignId
        && CampaignTestRunId == state.CampaignTestRunId
        && !string.IsNullOrWhiteSpace(TimelineId) && TimelineId == state.TimelineId
        && !string.IsNullOrWhiteSpace(DisposableSaveName) && DisposableSaveName == state.DisposableSaveName
        && approval.BelongsTo(RunId, CampaignId, CampaignTestRunId)
        && ReadTimestamp(CapturedUtc, out var captured) && ReadTimestamp(approval.CapturedUtc, out var approved)
        && approved <= captured
        && ApprovedWitnessSha256 == PartyAgencyApprovedArtifacts.ApprovalWitnessSha256(approval)
        && PartyAgencyApprovedArtifacts.IsSha256(InstallationRecordSha256)
        && PartyAgencyApprovedArtifacts.IsRuntimeVersion(LinuxRuntimeVersion)
        && Client == approval.Client && Server == approval.Server
        && IsLicensedModule(CampaignSystem, "TaleWorlds.CampaignSystem")
        && IsLicensedModule(Core, "TaleWorlds.Core");

    internal bool MatchesInstallation(PartyAgencyInstalledArtifactWitness current) =>
        (this with { CapturedUtc = current.CapturedUtc }) == current;

    internal bool CapturedBeforeIssuance(string originalObservedUtc) =>
        ReadTimestamp(CapturedUtc, out var captured)
        && ReadTimestamp(originalObservedUtc, out var issued) && captured <= issued;

    private static bool ReadTimestamp(string input, out DateTimeOffset timestamp) =>
        DateTimeOffset.TryParseExact(input, "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out timestamp);

    private static bool IsLicensedModule(PartyAgencyApprovedModule? module, string name) =>
        module is not null && module.AssemblyName == name && module.Mvid != Guid.Empty
        && PartyAgencyApprovedArtifacts.IsSha256(module.Sha256);
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

    // Measure installed files separately from the approved build. This receipt
    // does not claim those bytes were mapped into the original process.
    internal static PartyAgencyInstalledArtifactWitness CaptureInstalled(PartyAgencyCertificationState state)
    {
        var approval = state.ApprovedArtifactWitness;
        if (approval is null || !approval.BelongsTo(state.RunId, state.CampaignId, state.CampaignTestRunId)
            || string.IsNullOrWhiteSpace(state.TimelineId) || string.IsNullOrWhiteSpace(state.DisposableSaveName))
            throw new InvalidDataException("Installed artifact evidence requires an approved guarded enrollment.");
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Installed artifact capture requires the registered Windows Bannerlord installation.");

        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        using var record = new BoundJson(ReignInstallation.GetDefaultRecordPath(profile), maxBytes: 64 * 1024);
        if (Text(record.Root, "schema") != ReignInstallation.SchemaName)
            throw new InvalidDataException("No supported registered Reign installation is available.");
        var install = record.Root.Deserialize<ReignInstallation>(
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })
            ?? throw new InvalidDataException("The Reign installation record is empty.");
        install.Validate();
        if (install.ServerMode != "dwemerdistro-wsl")
            throw new InvalidDataException("Installed artifact evidence requires the registered dedicated WSL server.");

        const string manifestName = "reign-linux-artifact.json";
        string runtimeRoot = Path.GetDirectoryName(install.ServerRoot)
            ?? throw new InvalidDataException("The registered runtime root is missing.");
        string versionPath = Path.Combine(runtimeRoot, "current-version");
        string version = ReadRuntimeVersion(versionPath);
        using var activation = new BoundJson(Path.Combine(install.ServerRoot, manifestName), approval.LinuxManifestSha256);
        using var retained = new BoundJson(Path.Combine(runtimeRoot, "versions", version, manifestName), activation.Sha256);
        if (Text(activation.Root, "schema") != "reign-linux-artifact-v1"
            || Digest(activation.Root, "sourceFingerprint") != approval.LinuxSourceFingerprintSha256
            || Digest(Property(activation.Root, "files"), ServerArtifact) != approval.Server.Sha256)
            throw new InvalidDataException("The active Linux manifest does not bind the approved server assembly.");

        const string shipping = "Win64_Shipping_Client";
        string clientBin = Path.Combine(install.ModuleRoot, "bin", shipping);
        string gameBin = Path.Combine(install.BannerlordRoot, "bin", shipping);
        var client = ReadManagedArtifact(Path.Combine(clientBin, "ReignBeta.dll"), "ReignBeta", approval.Client.Sha256);
        var server = ReadManagedArtifact(Path.Combine(install.ServerRoot, ServerArtifact), "ReignServer", approval.Server.Sha256);
        var campaignSystem = ReadManagedArtifact(Path.Combine(gameBin, "TaleWorlds.CampaignSystem.dll"), "TaleWorlds.CampaignSystem");
        var core = ReadManagedArtifact(Path.Combine(gameBin, "TaleWorlds.Core.dll"), "TaleWorlds.Core");
        if (ReadRuntimeVersion(versionPath) != version)
            throw new InvalidDataException("The active Linux runtime changed during installed artifact capture.");

        var witness = new PartyAgencyInstalledArtifactWitness(PartyAgencyInstalledArtifactWitness.WitnessSchema,
            state.RunId, state.CampaignId, state.CampaignTestRunId, state.TimelineId, state.DisposableSaveName,
            DateTimeOffset.UtcNow.ToString("O"), ApprovalWitnessSha256(approval), record.Sha256,
            version, client, server, campaignSystem, core);
        if (!witness.BelongsTo(state, approval))
            throw new InvalidDataException("The installed managed identities do not match the approved enrollment.");
        return witness;
    }

    internal static PartyAgencyInstalledArtifactWitness? ReadInstalledWitness(
        string path, PartyAgencyCertificationState state)
    {
        var approval = state.ApprovedArtifactWitness;
        if (approval is null) return null;
        try
        {
            using var document = new BoundJson(path, maxBytes: 64 * 1024);
            var witness = document.Root.Deserialize<PartyAgencyInstalledArtifactWitness>(
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return witness is not null && witness.BelongsTo(state, approval)
                ? witness : throw new InvalidDataException("Installed artifact evidence belongs to a different certification.");
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    internal static string ApprovalWitnessSha256(PartyAgencyApprovedArtifactWitness approval) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(approval)));

    internal static bool IsRuntimeVersion(string? value) =>
        value is { Length: <= 40 } && Regex.IsMatch(value, @"\A[0-9]{14}-[0-9]+\z");

    private static string ReadRuntimeVersion(string path)
    {
        using var stream = OpenPhysicalRead(path);
        if (stream.Length is < 1 or > 128)
            throw new InvalidDataException("The registered runtime version marker exceeds its size bound.");
        byte[] bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        string version = Encoding.UTF8.GetString(bytes).Trim();
        if (!IsRuntimeVersion(version))
            throw new InvalidDataException("The registered runtime version marker is invalid.");
        return version;
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

    internal static PartyAgencyApprovedModule ReadManagedArtifact(string path, string assemblyName, string? approvedSha256 = null)
    {
        using var stream = OpenPhysicalRead(path);
        using var pe = new PEReader(stream, PEStreamOptions.PrefetchEntireImage | PEStreamOptions.LeaveOpen);
        // The PE reader owns one buffered image for both the SHA and managed metadata.
        string sha256 = Convert.ToHexStringLower(SHA256.HashData(pe.GetEntireImage().GetContent().AsSpan()));
        if (approvedSha256 is not null && sha256 != approvedSha256)
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

        public BoundJson(string path, string? expectedSha256 = null, int maxBytes = 64 * 1024 * 1024)
        {
            _stream = OpenPhysicalRead(path);
            try
            {
                if (_stream.Length > maxBytes)
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
