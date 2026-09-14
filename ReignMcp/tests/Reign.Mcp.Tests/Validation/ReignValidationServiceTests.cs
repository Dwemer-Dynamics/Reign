using Reign.Mcp.Server;

namespace Reign.Mcp.Tests;

public sealed class ReignValidationServiceTests
{
    [Fact]
    public async Task SuccessfulMatchingReportIsReusedWithoutAnotherRun()
    {
        string buildRoot = Path.Combine(TestOptions.FindWorkspace(), ".codex-build",
            "reign-mcp-tests", Guid.NewGuid().ToString("N"));
        try
        {
            ReignValidationService service = Service(buildRoot);

            ValidationReport first = await service.ValidateAsync(
                "changed", "Release", false, "AGENTS.md", true, CancellationToken.None);
            ValidationReport second = await service.ValidateAsync(
                "changed", "Release", false, "AGENTS.md", true, CancellationToken.None);

            Assert.True(first.Ok);
            Assert.False(first.Reused);
            Assert.True(second.Reused);
            Assert.Equal(first.RunId, second.ReusedFromRunId);
            Assert.Equal(first.ReportPath, second.ReportPath);
            var status = service.GetStatus();
            Assert.True(status.CanStart);
            Assert.Null(status.Owner);
            Assert.Equal("reused", status.LastRun!.Phase);
            Assert.Equal(first.ReportPath, status.LastRun.ReportPath);
            Assert.Equal(first.SourceFingerprintSha256, status.LastRun.SourceFingerprintSha256);
        }
        finally
        {
            if (Directory.Exists(buildRoot)) Directory.Delete(buildRoot, recursive: true);
        }
    }

    [Fact]
    public async Task CompetingValidationFailsBeforeStartingBuildWork()
    {
        string buildRoot = Path.Combine(TestOptions.FindWorkspace(), ".codex-build",
            "reign-mcp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(buildRoot);
        try
        {
            using var held = new FileStream(Path.Combine(buildRoot, "validation.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            ReignValidationService service = Service(buildRoot);

            InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.ValidateAsync("changed", "Release", false, "AGENTS.md", true,
                    CancellationToken.None));

            Assert.Contains("workspace build lane", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(buildRoot)) Directory.Delete(buildRoot, recursive: true);
        }
    }

    [Fact]
    public async Task EnforcedRepositoryHygieneFailureBlocksValidationBeforeBuildWork()
    {
        string buildRoot = Path.Combine(TestOptions.FindWorkspace(), ".codex-build",
            "reign-mcp-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var hygiene = RepositoryHygiene(
                enforcement: "enforce", ok: false, blocksValidation: true);
            ReignValidationService service = Service(buildRoot, new FixedHygieneAudit(hygiene));

            ValidationReport report = await service.ValidateAsync(
                "changed", "Release", false, "AGENTS.md", true, CancellationToken.None);

            Assert.False(report.Ok);
            Assert.Empty(report.Results);
            Assert.Equal(hygiene.Enforcement, report.RepositoryHygiene.Enforcement);
            Assert.Equal(hygiene.BlocksValidation, report.RepositoryHygiene.BlocksValidation);
            Assert.Equal(hygiene.Issues, report.RepositoryHygiene.Issues);
            Assert.True(File.Exists(report.RepositoryHygiene.EvidencePath));
        }
        finally
        {
            if (Directory.Exists(buildRoot)) Directory.Delete(buildRoot, recursive: true);
        }
    }

    [Fact]
    public async Task AuditOnlyRepositoryHygieneIssuesRemainVisibleWithoutBlockingValidation()
    {
        string buildRoot = Path.Combine(TestOptions.FindWorkspace(), ".codex-build",
            "reign-mcp-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var hygiene = RepositoryHygiene(
                enforcement: "audit", ok: false, blocksValidation: false);
            ReignValidationService service = Service(buildRoot, new FixedHygieneAudit(hygiene));

            ValidationReport report = await service.ValidateAsync(
                "changed", "Release", false, "AGENTS.md", true, CancellationToken.None);

            Assert.True(report.Ok);
            Assert.False(report.RepositoryHygiene.Ok);
            Assert.False(report.RepositoryHygiene.BlocksValidation);
        }
        finally
        {
            if (Directory.Exists(buildRoot)) Directory.Delete(buildRoot, recursive: true);
        }
    }

    [Fact]
    public void VerificationPayloadSucceeded_AcceptsCleanSuccessfulPayload()
    {
        Assert.True(ReignValidationService.VerificationPayloadSucceeded(
            "{\"ok\":true,\"passed\":true,\"failedCount\":0}"));
    }

    [Fact]
    public void VerificationPayloadSucceeded_AcceptsDiagnosticPrefixBeforeFinalPayload()
    {
        var output = "Ingested event daily_world_snapshot campaign=_architecture_test tags=\r\n"
            + "{\"ok\":true,\"checks\":[{\"passed\":true}],\"passed\":true}";

        Assert.True(ReignValidationService.VerificationPayloadSucceeded(output));
    }

    [Theory]
    [InlineData("diagnostic only")]
    [InlineData("diagnostic\n{\"ok\":false,\"passed\":false}")]
    [InlineData("{\"ok\":true}\ntrailing non-json output")]
    public void VerificationPayloadSucceeded_RejectsMissingFailedOrNonFinalPayload(string output)
    {
        Assert.False(ReignValidationService.VerificationPayloadSucceeded(output));
    }

    private static RepositoryHygieneReport RepositoryHygiene(
        string enforcement, bool ok, bool blocksValidation) => new()
    {
        Schema = "reign-repository-hygiene-report-v1",
        Enforcement = enforcement,
        Ok = ok,
        BlocksValidation = blocksValidation,
        PolicyPath = Path.Combine(TestOptions.FindWorkspace(), "reign.repository.json"),
        EvidencePath = null,
        GitRoot = TestOptions.FindWorkspace(),
        TrackedFileCount = 1,
        UntrackedFileCount = 0,
        Issues = ok
            ? []
            : [new RepositoryHygieneIssue
            {
                Category = "untracked-authored-source",
                Path = "src/NewFeature.cs",
                Classifier = "authored-extension",
                Message = "Human-authored source is not tracked."
            }]
    };

    private static ReignValidationService Service(
        string buildRoot,
        IReignRepositoryHygieneAudit? hygieneAudit = null)
    {
        ReignMcpOptions options = TestOptions.Create() with
        {
            AllowBuild = true,
            BuildRoot = buildRoot
        };
        return new ReignValidationService(
            options,
            new ReignProjectCatalog(options),
            new ReignProcessRunner(options, new SensitiveDataRedactor()),
            hygieneAudit ?? new FixedHygieneAudit(RepositoryHygiene(
                enforcement: "audit", ok: true, blocksValidation: false)));
    }

    private sealed class FixedHygieneAudit(RepositoryHygieneReport report)
        : IReignRepositoryHygieneAudit
    {
        public Task<RepositoryHygieneReport> AuditAsync(CancellationToken cancellationToken) =>
            Task.FromResult(report);
    }
}
