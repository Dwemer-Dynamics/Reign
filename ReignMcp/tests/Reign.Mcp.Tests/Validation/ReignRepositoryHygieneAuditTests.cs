using System.Diagnostics;
using System.Text.Json;
using Reign.Mcp.Server;

namespace Reign.Mcp.Tests;

public sealed class ReignRepositoryHygieneAuditTests
{
    [Fact]
    public void GitClientNeverInheritsTheMcpStdioInputHandle()
    {
        string workspace = TestOptions.FindWorkspace();
        string source = File.ReadAllText(Path.Combine(workspace, "ReignMcp", "src",
            "Reign.Mcp.Server", "Modules", "Validation", "ReignRepositoryHygieneAudit.cs"));

        Assert.Contains("RedirectStandardInput = true", source, StringComparison.Ordinal);
        Assert.Contains("process.StandardInput.Close();", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RedirectStandardInput = standardInput is not null", source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CompiledGlobSetHandlesRepositoryScaleWithinBoundedTime()
    {
        string[] patterns = Enumerable.Range(0, 54)
            .Select(index => $"root-{index}/**")
            .ToArray();
        string[] paths = Enumerable.Range(0, 1900)
            .Select(index => $"root-{index % patterns.Length}/src/File{index}.cs")
            .ToArray();
        var matcher = new RepositoryGlobSet(patterns);

        var stopwatch = Stopwatch.StartNew();
        int matchCount = paths.Count(matcher.Matches);
        stopwatch.Stop();

        Assert.Equal(paths.Length, matchCount);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2),
            $"Repository-scale glob matching took {stopwatch.Elapsed}.");
    }

    [Fact]
    public async Task EnforceModeRejectsUntrackedHumanAuthoredSource()
    {
        using var fixture = Fixture.Create("enforce");
        fixture.Write("ReignMcp/src/NewFeature.cs", "namespace Example;");
        fixture.Git.Status = "?? ReignMcp/src/NewFeature.cs\0";

        RepositoryHygieneReport report = await fixture.Audit();

        Assert.False(report.Ok);
        Assert.True(report.BlocksValidation);
        Assert.Contains(report.Issues, issue =>
            issue.Category == "untracked-human-authored" && issue.Path == "ReignMcp/src/NewFeature.cs");
    }

    [Fact]
    public async Task EnforceModeRejectsTrackedRuntimeOrDecompilerPath()
    {
        using var fixture = Fixture.Create("enforce");
        fixture.Write("Bannerlord_CampaignSystem_decompiled/Secret.cs", "namespace Reference;");
        fixture.Track("Bannerlord_CampaignSystem_decompiled/Secret.cs");

        RepositoryHygieneReport report = await fixture.Audit();

        Assert.Contains(report.Issues, issue => issue.Category == "prohibited-tracked-path");
    }

    [Fact]
    public async Task EnforceModeRejectsMissingRequiredTrackedPath()
    {
        using var fixture = Fixture.Create("enforce", required: ["AGENTS.md", "docs/agent/PROJECT_MEMORY.md"]);

        RepositoryHygieneReport report = await fixture.Audit();

        Assert.Contains(report.Issues, issue =>
            issue.Category == "required-path-not-tracked" && issue.Path == "docs/agent/PROJECT_MEMORY.md");
    }

    [Fact]
    public async Task EnforceModeRejectsOversizedFileWithoutAllowlist()
    {
        using var fixture = Fixture.Create("enforce", maxBytes: 8);
        fixture.Write("ReignMcp/README.md", "longer than eight bytes");
        fixture.Track("ReignMcp/README.md");

        RepositoryHygieneReport report = await fixture.Audit();

        Assert.Contains(report.Issues, issue => issue.Category == "oversized-tracked-file");
    }

    [Fact]
    public async Task EnforceModeReportsSecretClassifierWithoutSecretValue()
    {
        const string sentinel = "ghp_abcdefghijklmnopqrstuvwxyz123456";
        using var fixture = Fixture.Create("enforce");
        fixture.Write("ReignMcp/config.json", "{\"token\":\"" + sentinel + "\"}");
        fixture.Track("ReignMcp/config.json");

        RepositoryHygieneReport report = await fixture.Audit();
        string json = JsonSerializer.Serialize(report);

        Assert.Contains(report.Issues, issue => issue.Classifier == "github-token");
        Assert.DoesNotContain(sentinel, json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EnforceModeAllowsExplicitClassifierFixturePath()
    {
        const string sentinel = "ghp_abcdefghijklmnopqrstuvwxyz123456";
        using var fixture = Fixture.Create("enforce",
            secretContentAllowlist: ["github-token:ReignMcp/config.json"]);
        fixture.Write("ReignMcp/config.json", "{\"token\":\"" + sentinel + "\"}");
        fixture.Track("ReignMcp/config.json");

        RepositoryHygieneReport report = await fixture.Audit();

        Assert.True(report.Ok);
        Assert.Empty(report.Issues);
    }

    [Fact]
    public async Task AuditModeReportsIssuesWithoutFailingValidation()
    {
        using var fixture = Fixture.Create("audit");
        fixture.Write("ReignMcp/src/NewFeature.cs", "namespace Example;");
        fixture.Git.Status = "?? ReignMcp/src/NewFeature.cs\0";

        RepositoryHygieneReport report = await fixture.Audit();

        Assert.False(report.Ok);
        Assert.False(report.BlocksValidation);
        Assert.NotEmpty(report.Issues);
    }

    [Fact]
    public async Task CleanClassifiedRepositoryPasses()
    {
        using var fixture = Fixture.Create("enforce");

        RepositoryHygieneReport report = await fixture.Audit();

        Assert.True(report.Ok);
        Assert.False(report.BlocksValidation);
        Assert.Empty(report.Issues);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string root;
        public FakeGitClient Git { get; }
        private readonly ReignRepositoryHygieneAudit audit;

        private Fixture(string root, FakeGitClient git, ReignRepositoryHygieneAudit audit)
        {
            this.root = root;
            Git = git;
            this.audit = audit;
        }

        public static Fixture Create(string enforcement, string[]? required = null,
            long maxBytes = 1024, string[]? secretContentAllowlist = null)
        {
            string root = Path.Combine(Path.GetTempPath(), "reign-hygiene-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "AGENTS.md"), "# Test instructions");
            required ??= ["AGENTS.md"];
            File.WriteAllText(Path.Combine(root, "reign.repository.json"), JsonSerializer.Serialize(new
            {
                schema = "reign-repository-policy-v1",
                enforcement,
                canonicalRemote = "https://github.com/speedaemonc4/Reign-Workspace.git",
                requiredTrackedPaths = required,
                managedSourceRoots = new[] { "ReignMcp" },
                prohibitedTrackedPatterns = new[] { "Bannerlord_CampaignSystem_decompiled/**", "**/data/**" },
                humanAuthoredExtensions = new[] { ".cs", ".json", ".md" },
                secretNamePatterns = new[] { "**/.env", "**/*.pem" },
                secretContentClassifiers = new[]
                {
                    new { id = "github-token", pattern = "\\bgh[pousr]_[A-Za-z0-9]{20,}\\b" }
                },
                secretContentAllowlist = secretContentAllowlist ?? [],
                maxOrdinaryFileBytes = maxBytes,
                largeFileAllowlist = Array.Empty<string>()
            }));
            var git = new FakeGitClient(root)
            {
                Tracked = "AGENTS.md\0reign.repository.json\0"
            };
            ReignMcpOptions options = TestOptions.Create(root);
            return new Fixture(root, git, new ReignRepositoryHygieneAudit(options, git));
        }

        public void Write(string relativePath, string content)
        {
            string path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        public void Track(string relativePath)
        {
            Git.Tracked += relativePath + "\0";
        }

        public Task<RepositoryHygieneReport> Audit() => audit.AuditAsync(CancellationToken.None);

        public void Dispose()
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private sealed class FakeGitClient(string root) : IReignGitClient
    {
        public string Tracked { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;

        public Task<GitCommandResult> RunAsync(IReadOnlyList<string> arguments, string? standardInput,
            CancellationToken cancellationToken)
        {
            string output = arguments[0] switch
            {
                "rev-parse" => root,
                "ls-files" => Tracked,
                "status" => Status,
                "check-ignore" => string.Empty,
                _ => throw new InvalidOperationException("Unexpected Git command: " + arguments[0])
            };
            return Task.FromResult(new GitCommandResult(true, 0, output, string.Empty, false));
        }
    }
}
