using System.Diagnostics;
using Reign.Mcp.Server;

namespace Reign.Mcp.Tests;

public sealed class DeploymentAuthorityPromotionTests
{
    [Fact]
    public async Task PromotionReDerivesDeltaAndPinsReportsReviewAndHelperInventory()
    {
        string root = Path.Combine(Path.GetTempPath(), "reign-promotion-contract-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string script = Path.Combine(root, "promotion.ps1");
            File.WriteAllText(script, """
                param($ModulePath,$Root)
                $ErrorActionPreference='Stop'
                Import-Module $ModulePath -Force
                $checks=0
                function Reject([scriptblock]$Action,[string]$Message){
                    $caught=$false
                    try{&$Action}catch{$caught=$true;if($_.Exception.Message -notlike ('*'+$Message+'*')){throw}}
                    if(-not $caught){throw 'Negative promotion control incorrectly passed.'}
                }
                function Content($Before,$After){
                    $diff=@(Get-ReignInventoryDiff $Before $After)
                    @{Schema='reign-deployment-authority-content-v1';PriorAuthoritySha256='prior';SourceFiles=$After;SourceInventorySha256=Get-ReignJsonSha $After;Diff=$diff;DiffSha256=Get-ReignJsonSha $diff}
                }
                $before=@(@{key='client/A.cs';sha256='old'})
                $after=@(@{key='client/A.cs';sha256='new'},@{key='server/New.cs';sha256='added'})
                $prior=@{SourceFiles=$before}
                $content=Content $before $after
                Assert-ReignCheckpointDelta $prior $content;$checks++
                $content.Diff=@($content.Diff[0]);$content.DiffSha256=Get-ReignJsonSha $content.Diff
                Reject {Assert-ReignCheckpointDelta $prior $content} 'independently derived';$checks++
                $initial=Content @() $after
                Assert-ReignCheckpointDelta $null $initial;$checks++
                $initial.Diff=@();$initial.DiffSha256=Get-ReignJsonSha $initial.Diff
                Reject {Assert-ReignCheckpointDelta $null $initial} 'independently derived';$checks++
                $content=Content $before @(@{key='client/a.cs';sha256='old'})
                $candidate=Join-Path $Root 'candidate.json';$content|ConvertTo-Json -Depth 16|Set-Content $candidate
                $review=@{CandidateSha256=Get-ReignFileSha $candidate;PriorAuthoritySha256='prior';DiffSha256=$content.DiffSha256;Reason='Reviewed both sides of the case-only rename';ReviewedPaths=@('client/A.cs','client/a.cs')}
                Assert-ReignCheckpointReview $content $review $candidate;$checks++
                $review.ReviewedPaths=@('client/A.cs')
                Reject {Assert-ReignCheckpointReview $content $review $candidate} 'Every added';$checks++
                $report=Join-Path $Root 'report.json';[IO.File]::WriteAllText($report,'qualified report')
                $selection=@{ValidationReportSelection=$report;ClientReport=@{path=$report;sha256=Get-ReignFileSha $report};LinuxBuildReportSelection='';LinuxReport=$null}
                Assert-ReignSelectedReportHashes $selection;$checks++
                [IO.File]::AppendAllText($report,' ')
                Reject {Assert-ReignSelectedReportHashes $selection} 'report changed';$checks++
                $stage=Join-Path $Root 'stage';$helper=Join-Path $Root 'helper'
                [IO.Directory]::CreateDirectory((Join-Path $stage 'bin/Win64_Shipping_Client'))|Out-Null;[IO.Directory]::CreateDirectory($helper)|Out-Null
                $dll=Join-Path $stage 'bin/Win64_Shipping_Client/ReignBeta.dll';[IO.File]::WriteAllText($dll,'qualified DLL')
                $exe=Join-Path $helper 'helper.exe';[IO.File]::WriteAllText($exe,'qualified helper')
                $authority=@{ClientArtifacts=@(@{path='ReignBeta.dll';sha256=Get-ReignFileSha $dll},@{path='native-portrait-generator\helper.exe';sha256=Get-ReignFileSha $exe})}
                Assert-ReignStagedClient $authority $stage $helper;$checks++
                [IO.File]::WriteAllText((Join-Path $helper 'unreviewed.json'),'extra')
                Reject {Assert-ReignStagedClient $authority $stage $helper} 'portrait-helper inventory';$checks++
                Remove-Item -LiteralPath (Join-Path $helper 'unreviewed.json')
                $content=Content @() $after
                $content.PriorAuthoritySha256=$null
                $content|ConvertTo-Json -Depth 16|Set-Content $candidate
                $review=@{CandidateSha256=Get-ReignFileSha $candidate;PriorAuthoritySha256=$null;DiffSha256=$content.DiffSha256;Reason='Reviewed complete initial fixture inventory';ReviewedPaths=@($content.Diff|ForEach-Object path)}
                $reviewPath=Join-Path $Root 'review.json';$review|ConvertTo-Json -Depth 16|Set-Content $reviewPath
                $pointer=@{Schema='reign-deployment-authority-v1';CandidatePath=$candidate;CandidateSha256=Get-ReignFileSha $candidate;ReviewPath=$reviewPath;ReviewSha256=Get-ReignFileSha $reviewPath}
                $pointerPath=Join-Path $Root 'deployment-source.json';$pointer|ConvertTo-Json|Set-Content $pointerPath
                [void](Read-ReignDeploymentAuthority $pointerPath);$checks++
                # Pin the altered review too: read-time coverage must still reject it.
                $review.ReviewedPaths=@();$review|ConvertTo-Json -Depth 16|Set-Content $reviewPath
                $pointer.ReviewSha256=Get-ReignFileSha $reviewPath;$pointer|ConvertTo-Json|Set-Content $pointerPath
                Reject {Read-ReignDeploymentAuthority $pointerPath} 'Every added';$checks++
                if($checks -ne 12){throw "Unexpected contract count $checks"}
                Write-Output 'PROMOTION_CONTRACTS_12_PASS'
                """);
            var start = new ProcessStartInfo("pwsh") { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string arg in new[] { "-NoProfile", "-NonInteractive", "-File", script,
                Path.Combine(TestOptions.FindWorkspace(), "ReignServer/scripts/ReignDeploymentAuthority.psm1"), root }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start)!;
            process.StandardInput.Close();
            var output = process.StandardOutput.ReadToEndAsync();var error = process.StandardError.ReadToEndAsync();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            try { await process.WaitForExitAsync(deadline.Token); }
            catch (OperationCanceledException) { process.Kill(true); throw new TimeoutException("Promotion contract fixture timed out."); }
            string stdout = await output;string stderr = await error;
            Assert.True(process.ExitCode == 0, stderr);
            Assert.Contains("PROMOTION_CONTRACTS_12_PASS", stdout);
        }
        finally
        {
            Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()), Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith("reign-promotion-contract-", Path.GetFileName(root));
            Directory.Delete(root, true);
        }
    }
}
