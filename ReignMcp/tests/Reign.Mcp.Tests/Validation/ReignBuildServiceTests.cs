using System.Security.Cryptography;
using System.Text.Json;
using Reign.Mcp.Server;

namespace Reign.Mcp.Tests;

public sealed class ReignBuildServiceTests
{
    [Theory]
    [InlineData("reign")]
    [InlineData("Reign")]
    public async Task LinuxServerRunnerRejectsProductionDatabaseBeforeLaunching(string database)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = ValidatedArtifactFixture.Create();
        var runner = new ReignProcessRunner(fixture.Options, new SensitiveDataRedactor());
        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync(
            fixture.ExecutablePath, [], fixture.Root,
            environment: new Dictionary<string, string> { ["REIGN_VALIDATION_MODE"] = "1", ["REIGN_DB_NAME"] = database }));
    }

    [Fact]
    public void ResolvesExactSuccessfulReleaseValidationArtifact()
    {
        using var fixture = ValidatedArtifactFixture.Create();

        var artifact = ReignBuildService.ResolveValidatedServerArtifact(
            fixture.Options, fixture.RunId);

        Assert.Equal(fixture.RunId, artifact.ValidationRunId);
        Assert.Equal(fixture.ReportPath, artifact.ValidationReportPath);
        Assert.Equal(fixture.ExecutablePath, artifact.ExecutablePath);
        Assert.Equal(fixture.SourceFingerprint, artifact.SourceFingerprintSha256);
        Assert.Equal(fixture.ExecutableSha256, artifact.ExecutableSha256);
        Assert.DoesNotContain(Path.Combine(fixture.Root, "ReignBeta", "server", "app"),
            artifact.ExecutablePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsFailedOrDebugValidationReports()
    {
        using var failed = ValidatedArtifactFixture.Create(ok: false);
        Assert.Throws<InvalidDataException>(() =>
            ReignBuildService.ResolveValidatedServerArtifact(failed.Options, failed.RunId));

        using var debug = ValidatedArtifactFixture.Create(configuration: "Debug");
        Assert.Throws<InvalidDataException>(() =>
            ReignBuildService.ResolveValidatedServerArtifact(debug.Options, debug.RunId));
    }

    [Fact]
    public void RejectsTraversalAndMismatchedArtifactRoots()
    {
        using var fixture = ValidatedArtifactFixture.Create(artifactRootMatches: false);
        Assert.Throws<ArgumentException>(() =>
            ReignBuildService.ResolveValidatedServerArtifact(fixture.Options, ".."));
        Assert.Throws<InvalidDataException>(() =>
            ReignBuildService.ResolveValidatedServerArtifact(fixture.Options, fixture.RunId));
    }

    private sealed class ValidatedArtifactFixture : IDisposable
    {
        private ValidatedArtifactFixture(
            string root,
            ReignMcpOptions options,
            string runId,
            string reportPath,
            string executablePath,
            string sourceFingerprint,
            string executableSha256)
        {
            Root = root;
            Options = options;
            RunId = runId;
            ReportPath = reportPath;
            ExecutablePath = executablePath;
            SourceFingerprint = sourceFingerprint;
            ExecutableSha256 = executableSha256;
        }

        public string Root { get; }
        public ReignMcpOptions Options { get; }
        public string RunId { get; }
        public string ReportPath { get; }
        public string ExecutablePath { get; }
        public string SourceFingerprint { get; }
        public string ExecutableSha256 { get; }

        public static ValidatedArtifactFixture Create(
            bool ok = true,
            string configuration = "Release",
            bool artifactRootMatches = true)
        {
            var root = Path.Combine(Path.GetTempPath(),
                "reign-build-service-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var options = TestOptions.Create(root);
            const string runId = "20260905-043544-696f877f";
            const string sourceFingerprint = "7c96de718783cc81e185c7712211a073fa314b3a9d0b9fda860e6baaaa4b3951";
            var runRoot = Path.Combine(options.BuildRoot, "validation", runId);
            var executablePath = Path.Combine(runRoot, "server", "out", "ReignBetaServer.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(executablePath)!);
            File.WriteAllBytes(executablePath, [0x52, 0x45, 0x49, 0x47, 0x4e]);
            var executableSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(executablePath)))
                .ToLowerInvariant();
            var reportPath = Path.Combine(runRoot, "validation-report.json");
            var reportedRoot = artifactRootMatches
                ? runRoot
                : Path.Combine(options.BuildRoot, "validation", "different-run");
            var report = new
            {
                Schema = "reign-validation-report-v4",
                RunId = runId,
                SourceFingerprintSha256 = sourceFingerprint,
                Ok = ok,
                ArtifactRoot = reportedRoot,
                ReportPath = reportPath,
                Plan = new
                {
                    Configuration = configuration,
                    CoverageComplete = true,
                    Projects = new[] { new { Id = "server" } }
                },
                Results = new[] { new { Process = new { Ok = true } } }
            };
            File.WriteAllText(reportPath, JsonSerializer.Serialize(report));
            return new ValidatedArtifactFixture(root, options, runId, reportPath,
                executablePath, sourceFingerprint, executableSha256);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
