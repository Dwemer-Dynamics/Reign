using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Reign.Mcp.Server;

namespace Reign.Mcp.Tests;

public sealed class ReignDeploymentSourceTests
{
    [Theory]
    [InlineData("unchanged", true)]
    [InlineData("source-fingerprint", false)]
    [InlineData("artifact-swap", false)]
    [InlineData("extra-artifact", false)]
    [InlineData("foreign-report", false)]
    [InlineData("artifact-escape", false)]
    public void OnlyCurrentOriginalReportsAndExactArtifactsPass(string change, bool expected)
    {
        using var fixture = new ReportFixture();
        var plan = fixture.Service.GetPlan("product", "Release", false);
        var report = fixture.BaseReport with
        {
            Plan = plan,
            SourceFingerprintSha256 = fixture.Fingerprint(plan),
            DeploymentArtifactHashes = new Dictionary<string, string>
            {
                ["client/out/example.dll"] = ReignValidationService.FileSha256(fixture.ArtifactFile)
            }
        };
        string supplied = report.ReportPath;
        if (change == "source-fingerprint") report = report with { SourceFingerprintSha256 = new string('0', 64) };
        if (change == "artifact-swap") File.WriteAllText(fixture.ArtifactFile, "older DLL bytes");
        if (change == "extra-artifact") File.WriteAllText(Path.Combine(Path.GetDirectoryName(fixture.ArtifactFile)!, "extra.dll"), "unexpected");
        if (change == "artifact-escape") report = report with { DeploymentArtifactHashes = new Dictionary<string, string> { ["../outside"] = new string('0', 64) } };
        if (change == "foreign-report")
        {
            supplied = Path.Combine(fixture.BuildRoot, "validation", "20261113-230001-abcdef12", "validation-report.json");
            Directory.CreateDirectory(Path.GetDirectoryName(supplied)!);
        }
        File.WriteAllText(supplied, JsonSerializer.Serialize(report));
        string bytesBefore = File.ReadAllText(fixture.ArtifactFile);
        var result = fixture.Service.CheckDeploymentSource(supplied);
        Assert.Equal(expected, result.Ok);
        if (!expected)
        {
            string reason = change switch
            {
                "source-fingerprint" => "current source",
                "artifact-swap" => "missing or changed",
                "extra-artifact" => "extra or missing",
                "foreign-report" => "Foreign/copied",
                "artifact-escape" => "escapes",
                _ => throw new InvalidOperationException("Unknown negative fixture")
            };
            Assert.Contains(reason, Assert.Single(result.Diagnostics));
        }
        Assert.Equal(bytesBefore, File.ReadAllText(fixture.ArtifactFile));
    }

    [Fact]
    public void OutsideReportIsRejectedBeforeReadingIt()
    {
        using var fixture = new ReportFixture();
        var result = fixture.Service.CheckDeploymentSource(Path.Combine(fixture.BuildRoot, "private-settings.json"));
        Assert.False(result.Ok);
        Assert.Contains("Only original reports", Assert.Single(result.Diagnostics));
        Assert.Empty(Directory.GetFiles(fixture.BuildRoot, "private-settings.json"));
    }

    [Fact]
    public void LinuxManifestAndItsEntireArtifactSetAreBound()
    {
        using var fixture = new ReportFixture();
        string runId = "20261113-230002-abcdef12";
        string root = Path.Combine(fixture.BuildRoot, "linux-server", runId);
        string publish = Path.Combine(root, "publish");
        Directory.CreateDirectory(publish);
        File.WriteAllText(Path.Combine(publish, "ReignServer"), "qualified server bytes");
        string fingerprint = fixture.Fingerprint(fixture.Service.GetPlan("all", "Release", false));
        string manifestPath = Path.Combine(publish, "reign-linux-artifact.json");
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(new
        {
            schema = "reign-linux-artifact-v1", sourceFingerprint = fingerprint,
            files = new Dictionary<string, string> { ["ReignServer"] = ReignValidationService.FileSha256(Path.Combine(publish, "ReignServer")) }
        }));
        var process = new ProcessResult { Ok = true, ExitCode = 0, Command = "fixture", WorkingDirectory = publish,
            DurationMs = 0, TimedOut = false, OutputTruncated = false, StandardOutput = "", StandardError = "" };
        string path = Path.Combine(root, "linux-build-report.json");
        var report = new LinuxServerBuildReport("reign-linux-build-v1", true, runId, fingerprint, publish,
            path, fixture.BaseReport.RepositoryHygiene, process) { ArtifactManifestSha256 = ReignValidationService.FileSha256(manifestPath) };
        File.WriteAllText(path, JsonSerializer.Serialize(report));
        Assert.True(fixture.Service.CheckDeploymentSource(linuxBuildReport: path).Ok);
        File.WriteAllText(Path.Combine(publish, "extra.dll"), "unreported");
        Assert.False(fixture.Service.CheckDeploymentSource(linuxBuildReport: path).Ok);
        File.Delete(Path.Combine(publish, "extra.dll"));
        File.AppendAllText(manifestPath, " ");
        Assert.False(fixture.Service.CheckDeploymentSource(linuxBuildReport: path).Ok);
    }

    [Fact]
    public async Task AuthorityContractsRejectUnreviewedChangesAndStaleReceiptsWithoutLiveWrites()
    {
        string root = Path.Combine(Path.GetTempPath(), "reign-deployment-authority-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string script = Path.Combine(root, "contracts.ps1");
            File.WriteAllText(script, """
                param($ModulePath,$Root)
                $ErrorActionPreference='Stop'
                Import-Module $ModulePath -Force
                $checks=0
                function Reject([scriptblock]$Action){$rejected=$false;try{&$Action}catch{$rejected=$true};if(-not $rejected){throw 'Negative control incorrectly passed.'}}
                $a=@(@{key='client/a.cs';sha256='a'},@{key='server/b.cs';sha256='b'})
                Assert-ReignInventoryEqual $a $a; $checks++
                Reject {Assert-ReignInventoryEqual $a @(@{key='client/a.cs';sha256='old'},@{key='server/b.cs';sha256='b'})};$checks++
                Reject {Assert-ReignInventoryEqual $a @(@{key='client/a.cs';sha256='a'})};$checks++
                Reject {Assert-ReignInventoryEqual $a @($a+@{key='client/New.cs';sha256='new'})};$checks++
                Reject {Assert-ReignInventoryEqual $a @(@{key='client/A.cs';sha256='a'},@{key='server/b.cs';sha256='b'})};$checks++
                Reject {Assert-ReignDeploymentAuthority -Workspace $Root -ServerRoot $Root -AuthorityPath (Join-Path $Root 'absent.json')};$checks++
                $diff=@(@{path='client/a.cs';before='a';after='b'})
                $content=@{Schema='reign-deployment-authority-content-v1';PriorAuthoritySha256='prior';Diff=$diff;DiffSha256=Get-ReignJsonSha $diff}
                $candidate=Join-Path $Root 'candidate.json';$content|ConvertTo-Json -Depth 8|Set-Content $candidate
                $review=@{CandidateSha256=Get-ReignFileSha $candidate;PriorAuthoritySha256='prior';DiffSha256=$content.DiffSha256;Reason='Reviewed intentional feature update';ReviewedPaths=@('client/a.cs')}
                Assert-ReignCheckpointReview $content $review $candidate;$checks++
                $review.PriorAuthoritySha256='stale';Reject {Assert-ReignCheckpointReview $content $review $candidate};$checks++;$review.PriorAuthoritySha256='prior'
                $review.ReviewedPaths=@();Reject {Assert-ReignCheckpointReview $content $review $candidate};$checks++;$review.ReviewedPaths=@('client/a.cs')
                Add-Content $candidate ' ';Reject {Assert-ReignCheckpointReview $content $review $candidate};$checks++
                $stage=Join-Path $Root 'stage';$helper=Join-Path $Root 'helper';[IO.Directory]::CreateDirectory((Join-Path $stage 'bin/Win64_Shipping_Client'))|Out-Null;[IO.Directory]::CreateDirectory($helper)|Out-Null
                $dll=Join-Path $stage 'bin/Win64_Shipping_Client/ReignBeta.dll';[IO.File]::WriteAllText($dll,'qualified')
                $authority=@{ClientArtifacts=@(@{path='ReignBeta.dll';sha256=Get-ReignFileSha $dll})}
                Assert-ReignStagedClient $authority $stage $helper;$checks++
                [IO.File]::WriteAllText($dll,'stale');Reject {Assert-ReignStagedClient $authority $stage $helper};$checks++
                [IO.File]::WriteAllText($dll,'qualified');[IO.File]::WriteAllText((Join-Path (Split-Path $dll) 'Unexpected.dll'),'extra');Reject {Assert-ReignStagedClient $authority $stage $helper};$checks++
                $client=Join-Path $Root 'client';$server=Join-Path $Root 'server';$oldServer=Join-Path $Root 'old-server'
                foreach($repo in @($client,$server)){
                    [IO.Directory]::CreateDirectory((Join-Path $repo '.git/objects'))|Out-Null;[IO.Directory]::CreateDirectory((Join-Path $repo '.git/refs'))|Out-Null
                    [IO.File]::WriteAllText((Join-Path $repo '.git/HEAD'),"ref: refs/heads/test`n")
                    [IO.File]::WriteAllText((Join-Path $repo '.git/config'),"[core]`nrepositoryformatversion = 0`nbare = false`n")
                }
                [IO.Directory]::CreateDirectory($oldServer)|Out-Null
                [IO.File]::WriteAllText((Join-Path $client '.gitignore'),"/ReignServer/`n")
                [IO.File]::WriteAllText((Join-Path $client 'Existing.cs'),'accepted')
                [IO.File]::WriteAllText((Join-Path $server 'Server.cs'),'accepted')
                New-Item -ItemType Junction -Path (Join-Path $client 'ReignServer') -Target $server|Out-Null
                $roots=@{WorkspaceRoot=$client;ServerRoot=$server};Assert-ReignAuthorityRoots $roots $client $server;$checks++
                $inventory=@(Get-ReignSourceInventory $client $server)
                [IO.File]::WriteAllText((Join-Path $client 'New.cs'),'untracked')
                $changed=@(Get-ReignSourceInventory $client $server)
                if(-not @($changed|Where-Object key -eq 'client/New.cs').Count){throw 'Untracked source was omitted.'}
                Reject {Assert-ReignInventoryEqual $inventory $changed};$checks++
                $link=Get-Item -LiteralPath (Join-Path $client 'ReignServer');if(-not ($link.Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Fixture projection is not a junction.'}
                [IO.Directory]::Delete($link.FullName)
                New-Item -ItemType Junction -Path (Join-Path $client 'ReignServer') -Target $oldServer|Out-Null
                Reject {Assert-ReignAuthorityRoots $roots $client $server};$checks++
                [IO.Directory]::Delete((Join-Path $client 'ReignServer'))
                @{ok=$true;checks=$checks}|ConvertTo-Json -Compress
                """);
            var info = new ProcessStartInfo("pwsh") { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string arg in new[] { "-NoProfile", "-NonInteractive", "-File", script,
                Path.Combine(TestOptions.FindWorkspace(), "ReignServer/scripts/ReignDeploymentAuthority.psm1"), root }) info.ArgumentList.Add(arg);
            using var process = Process.Start(info)!;
            process.StandardInput.Close();
            var output = process.StandardOutput.ReadToEndAsync();var error = process.StandardError.ReadToEndAsync();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            try { await process.WaitForExitAsync(deadline.Token); }
            catch (OperationCanceledException) { process.Kill(true); throw new TimeoutException("Authority fixture timed out."); }
            string stdout = await output;
            string stderr = await error;
            Assert.True(process.ExitCode == 0, stderr);
            using var json = JsonDocument.Parse(stdout);
            Assert.True(json.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal(16, json.RootElement.GetProperty("checks").GetInt32());
        }
        finally
        {
            Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()), Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith("reign-deployment-authority-", Path.GetFileName(root));
            Directory.Delete(root, true);
        }
    }

    private sealed class ReportFixture : IDisposable
    {
        public string BuildRoot { get; } = Path.Combine(TestOptions.FindWorkspace(), ".codex-build", "deployment-source-tests", Guid.NewGuid().ToString("N"));
        public ReignValidationService Service { get; }
        public ValidationReport BaseReport { get; }
        public string ArtifactFile { get; }
        public ReportFixture()
        {
            var options = TestOptions.Create() with { AllowBuild = true, BuildRoot = BuildRoot };
            Service = new(options, new ReignProjectCatalog(options), new ReignProcessRunner(options, new SensitiveDataRedactor()), new CleanAudit(options.WorkspaceRoot));
            string runId = "20261113-230000-abcdef12";
            string root = Path.Combine(BuildRoot, "validation", runId);
            Directory.CreateDirectory(Path.Combine(root, "client/out"));
            BaseReport = new ValidationReport
            {
                Schema = "reign-validation-report-v4", ValidatorVersion = "fixture", RunId = runId,
                SourceFingerprintSha256 = "", StartedUtc = DateTimeOffset.UtcNow.ToString("O"),
                CompletedUtc = DateTimeOffset.UtcNow.ToString("O"), Ok = true, Reused = false,
                Plan = Service.GetPlan("product", "Release", false), ArtifactRoot = root,
                ReportPath = Path.Combine(root, "validation-report.json"),
                RepositoryHygiene = CleanAudit.Report(options.WorkspaceRoot), Results = []
            };
            ArtifactFile = Path.Combine(root, "client/out/example.dll");
            File.WriteAllText(ArtifactFile, "qualified bytes");
        }
        public string Fingerprint(ValidationPlan plan) => (string)typeof(ReignValidationService)
            .GetMethod("ComputeSourceFingerprint", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Service, [plan])!;
        public void Dispose()
        {
            string expected = Path.Combine(TestOptions.FindWorkspace(), ".codex-build", "deployment-source-tests") + Path.DirectorySeparatorChar;
            Assert.StartsWith(expected, Path.GetFullPath(BuildRoot), StringComparison.OrdinalIgnoreCase);
            if (Directory.Exists(BuildRoot)) Directory.Delete(BuildRoot, true);
        }
    }

    private sealed class CleanAudit(string workspace) : IReignRepositoryHygieneAudit
    {
        public Task<RepositoryHygieneReport> AuditAsync(CancellationToken cancellationToken) => Task.FromResult(Report(workspace));
        public static RepositoryHygieneReport Report(string workspace) => new()
        {
            Schema = "reign-repository-hygiene-report-v1", Enforcement = "enforce", Ok = true, BlocksValidation = false,
            PolicyPath = Path.Combine(workspace, "reign.repository.json"), GitRoot = workspace, TrackedFileCount = 0,
            UntrackedFileCount = 0, Issues = []
        };
    }
}
