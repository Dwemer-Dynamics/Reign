using System.Text.Json;
using System.Text.Json.Nodes;
using Reign.Mcp.Server;

namespace Reign.Mcp.Tests;

public sealed class ReignValidationServiceTests
{
    [Theory]
    [InlineData("App.xaml")]
    [InlineData("MainWindow.xaml")]
    public async Task ChangingOnlyCompiledPortraitMarkupRequiresNewArtifacts(string file)
    {
        using var fixture = new PortraitFixture();
        var first = await fixture.Validate();
        Assert.True(first.Ok);
        Assert.Equal(3, first.Plan.CoverageAudit.OwnedPaths); // The project file and both XAML files.
        File.AppendAllText(Path.Combine(fixture.SourceDirectory, file), "\n<!-- edited -->");
        var next = await fixture.Validate();
        Assert.True(next.Ok);
        Assert.False(next.Reused);
        Assert.NotEqual(first.RunId, next.RunId);
        Assert.NotEqual(first.SourceFingerprintSha256, next.SourceFingerprintSha256);
        Assert.Equal(next.SourceFingerprintSha256, next.FinalSourceFingerprintSha256);
        Assert.Equal(next.RunId, (await fixture.Validate()).ReusedFromRunId);
    }

    [Fact]
    public async Task ReportWithoutFinalFingerprintRemainsHistorical()
    {
        using var fixture = new PortraitFixture();
        var first = await fixture.Validate();
        var historical = JsonNode.Parse(File.ReadAllText(first.ReportPath))!.AsObject();
        historical.Remove(nameof(ValidationReport.FinalSourceFingerprintSha256));
        var original = historical.ToJsonString();
        File.WriteAllText(first.ReportPath, original);

        var next = await fixture.Validate();

        Assert.True(next.Ok);
        Assert.False(next.Reused);
        Assert.NotEqual(first.RunId, next.RunId);
        Assert.Equal(original, File.ReadAllText(first.ReportPath));
    }

    [Fact]
    public async Task SourceDriftDuringReusePreservesOriginalReceiptAndFailsTheAttempt()
    {
        Action? edit = null;
        using var fixture = new PortraitFixture(onAudit: () => edit?.Invoke());
        var original = await fixture.Validate();
        var receipt = File.ReadAllBytes(original.ReportPath);
        edit = () => File.AppendAllText(Path.Combine(fixture.SourceDirectory, "App.xaml"), "\n<!-- edited -->");

        var attempt = await fixture.Validate();

        Assert.False(attempt.Ok);
        Assert.False(attempt.Reused);
        Assert.Empty(attempt.Results);
        Assert.NotEqual(original.RunId, attempt.RunId);
        Assert.Equal(original.SourceFingerprintSha256, attempt.SourceFingerprintSha256);
        Assert.NotEqual(attempt.SourceFingerprintSha256, attempt.FinalSourceFingerprintSha256);
        Assert.Equal(receipt, File.ReadAllBytes(original.ReportPath));
    }

    [Fact]
    public async Task UnreadableFinalSourceRetainsCompletedProcessEvidence()
    {
        string file = "";
        FileStream? held = null;
        using var fixture = new PortraitFixture(_ =>
        {
            held = File.Open(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return Task.CompletedTask;
        });
        file = Path.Combine(fixture.SourceDirectory, "App.xaml");
        try
        {
            var report = await fixture.Validate();
            Assert.False(report.Ok);
            Assert.Null(report.FinalSourceFingerprintSha256);
            Assert.Contains("Could not read", report.SourceFingerprintError);
            Assert.True(Assert.Single(report.Results).Process.Ok);
            Assert.True(File.Exists(report.ReportPath));
        }
        finally
        {
            held?.Dispose();
        }
    }

    [Theory]
    [InlineData("mcp", "changed")]
    [InlineData("client-core", "core")]
    [InlineData("nativecharacterimagegenerator-src-nativecharacterimagegenerator-app-nativecharacterimagegenerator-app-csproj", "tooling")]
    public void LegacyProjectPlanIncludesTheRequestedManagedProject(string projectId, string profile)
    {
        var options = TestOptions.Create();
        var redactor = new SensitiveDataRedactor();
        using var http = new HttpClient();
        var service = new ReignBuildService(options, new ReignProcessRunner(options, redactor),
            new ReignApiClient(http, options, redactor), new ReignProjectCatalog(options));
        var plan = service.GetBuildPlan(projectId, "Release", false);
        Assert.True(plan.CoverageComplete);
        Assert.Equal(profile, plan.Profile);
        var project = Assert.Single(plan.Projects, candidate => candidate.Id == projectId);
        Assert.Equal(project.ProjectPath, Assert.Single(plan.ChangedPaths));
    }

    [Fact]
    public async Task LegacyBuildOwnsCanonicalLeaseAndRetainsDiagnosticsAfterSourceDrift()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var fixture = new PortraitFixture(async token =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(token);
        });
        string taskId = Guid.NewGuid().ToString();
        string file = Path.Combine(fixture.SourceDirectory, "MainWindow.xaml");
        var initial = File.ReadAllBytes(file);
        var build = TestingTools.Build(fixture.BuildService, " PORTRAIT ", requestingTaskId: taskId);
        BuildBatchResult result;
        try
        {
            await Task.WhenAny(entered.Task, build).WaitAsync(TimeSpan.FromSeconds(15));
            if (build.IsCompleted) await build;
            Assert.True(entered.Task.IsCompletedSuccessfully);
            Assert.Equal(taskId, fixture.Validation.GetStatus().Owner!.RequestingTaskId);
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Validate());
            Assert.Contains("owns the validation lane", error.Message);
            error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                fixture.BuildService.BuildAsync("tooling", "Release", false, CancellationToken.None));
            Assert.Contains("owns the validation lane", error.Message);
            File.AppendAllText(file, "\n<!-- changed while building -->");
        }
        finally
        {
            release.TrySetResult();
            result = await build;
        }
        var report = JsonSerializer.Deserialize<ValidationReport>(File.ReadAllText(result.ValidationReportPath!))!;
        Assert.False(result.Ok);
        Assert.True(Assert.Single(result.Results).Ok);
        Assert.Equal(result.ValidationRunId, report.RunId);
        Assert.Equal(result.ArtifactRoot, report.ArtifactRoot);
        Assert.False(report.Ok);
        Assert.NotEqual(report.SourceFingerprintSha256, report.FinalSourceFingerprintSha256);
        Assert.Contains("changed during validation", report.SourceFingerprintError);
        Assert.Equal("changed", report.Plan.Profile);
        Assert.Equal(PortraitFixture.ProjectPath, Assert.Single(report.Plan.ChangedPaths));
        Assert.True(File.Exists(report.RepositoryHygiene.EvidencePath));
        Assert.True(File.Exists(Path.Combine(report.ArtifactRoot, "portrait", "out", "portrait.txt")));
        Assert.Equal("failed", fixture.Validation.GetStatus().LastRun!.Phase);
        File.WriteAllBytes(file, initial);
        var recovered = await fixture.Validate();
        Assert.True(recovered.Ok);
        Assert.False(recovered.Reused);
        Assert.Equal(report.SourceFingerprintSha256, recovered.SourceFingerprintSha256);
        var reused = await TestingTools.Build(fixture.BuildService, "portrait");
        Assert.True(reused.Ok);
        Assert.True(reused.Reused);
        Assert.Equal(recovered.ReportPath, reused.ValidationReportPath);
    }

    [Theory]
    [InlineData("core")]
    [InlineData("product")]
    [InlineData("tooling")]
    [InlineData("all")]
    public async Task LegacyProfilesUseCanonicalHygieneAndRestoreGates(string component)
    {
        using var fixture = new PortraitFixture(hygiene: RepositoryHygiene("enforce", false, true));
        var result = await TestingTools.Build(fixture.BuildService, component);
        Assert.False(result.Ok);
        Assert.Empty(result.Results);
        var report = JsonSerializer.Deserialize<ValidationReport>(File.ReadAllText(result.ValidationReportPath!))!;
        Assert.Equal(component, report.Plan.Profile);
        Assert.True(report.RepositoryHygiene.BlocksValidation);
        await Assert.ThrowsAsync<InvalidOperationException>(() => TestingTools.Build(fixture.BuildService, component, restore: true));
        await Assert.ThrowsAsync<ArgumentException>(() => TestingTools.Build(fixture.BuildService, "behavioral"));
    }

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

            Assert.Contains("owns the validation lane", error.Message, StringComparison.OrdinalIgnoreCase);
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

    private sealed class PortraitFixture : IDisposable
    {
        private const string AppDirectory = "ReignServer/tools/portrait-generator/src/NativeCharacterImageGenerator.App";
        public const string ProjectPath = AppDirectory + "/NativeCharacterImageGenerator.App.csproj";
        private readonly string root = Directory.CreateTempSubdirectory("reign-validation-portrait-").FullName;
        private readonly HttpClient http = new();
        public string SourceDirectory => Path.Combine(root, AppDirectory);
        public ReignValidationService Validation { get; }
        public ReignBuildService BuildService { get; }

        public PortraitFixture(Func<CancellationToken, Task>? onBuild = null,
            RepositoryHygieneReport? hygiene = null, Action? onAudit = null)
        {
            Directory.CreateDirectory(SourceDirectory);
            foreach (string name in new[] { Path.GetFileName(ProjectPath), "App.xaml", "MainWindow.xaml" })
                File.Copy(Path.Combine(TestOptions.FindWorkspace(), AppDirectory, name), Path.Combine(SourceDirectory, name));
            File.WriteAllText(Path.Combine(root, "reign-projects.json"), JsonSerializer.Serialize(new
            {
                schema = "reign-project-catalog-v1",
                managedRoots = new[] { new { path = "ReignServer", group = "tooling" } },
                excludedDirectoryNames = new[] { ".codex-build" },
                projectOverrides = new Dictionary<string, object> { [ProjectPath] = new { id = "portrait", name = "Portrait app" } }
            }));
            File.WriteAllText(Path.Combine(root, "reign.modules.json"), """
                {"schema":"reign-module-manifest-v2","enforcement":"enforce","modules":[
                  {"id":"portraits","facets":[{"id":"tooling","ownership":["ReignServer/**"],"projects":["portrait"]}]}
                ]}
                """);
            var options = TestOptions.Create(root) with { AllowBuild = true };
            var catalog = new ReignProjectCatalog(options);
            var runner = new FixtureRunner(options, onBuild);
            Validation = new ReignValidationService(options, catalog, runner,
                new FixedHygieneAudit(hygiene ?? RepositoryHygiene("enforce", true, false), onAudit));
            BuildService = new ReignBuildService(options, runner,
                new ReignApiClient(http, options, new SensitiveDataRedactor()), catalog, Validation);
        }

        public Task<ValidationReport> Validate() =>
            Validation.ValidateAsync("changed", "Release", false, ProjectPath, true, CancellationToken.None);

        public void Dispose()
        {
            http.Dispose();
            Directory.Delete(root, true);
        }
    }

    private sealed class FixtureRunner(ReignMcpOptions options, Func<CancellationToken, Task>? onBuild)
        : ReignProcessRunner(options, new SensitiveDataRedactor())
    {
        public override async Task<ProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments,
            string workingDirectory, TimeSpan? timeout = null, string? artifactDirectory = null,
            IReadOnlyDictionary<string, string>? environment = null, CancellationToken cancellationToken = default)
        {
            Assert.Equal("build", arguments[0]);
            File.WriteAllText(Path.Combine(artifactDirectory!, "out", "portrait.txt"), "fixture bytes");
            if (onBuild is not null) await onBuild(cancellationToken);
            return new ProcessResult { Ok = true, ExitCode = 0, Command = executable,
                WorkingDirectory = workingDirectory, DurationMs = 0, TimedOut = false,
                OutputTruncated = false, StandardOutput = "", StandardError = "", ArtifactDirectory = artifactDirectory };
        }
    }

    private sealed class FixedHygieneAudit(RepositoryHygieneReport report, Action? onAudit = null)
        : IReignRepositoryHygieneAudit
    {
        public Task<RepositoryHygieneReport> AuditAsync(CancellationToken cancellationToken)
        {
            onAudit?.Invoke();
            return Task.FromResult(report);
        }
    }
}
