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

                function New-Pair($Name){
                    $parent=Join-Path $Root $Name
                    foreach($leaf in @('Reign','ReignServer')){
                        $repo=Join-Path $parent $leaf
                        git init --quiet $repo
                        if($LASTEXITCODE){throw 'Could not initialize fixture Git checkout.'}
                        git -C $repo remote add origin "https://github.com/Dwemer-Dynamics/$leaf.git"
                        if($LASTEXITCODE){throw 'Could not configure fixture origin.'}
                    }
                    $pair=@{WorkspaceRoot=Join-Path $parent Reign;ServerRoot=Join-Path $parent ReignServer}
                    New-Item -ItemType Junction -Path (Join-Path $pair.WorkspaceRoot ReignServer) -Target $pair.ServerRoot|Out-Null
                    $pair
                }
                $old=New-Pair 'from'
                $new=New-Pair 'to'
                function Migration { Get-ReignRootMigration $old $new.WorkspaceRoot $new.ServerRoot $report $report }
                $scope=Migration
                if($scope.FromWorkspaceRoot -cne $old.WorkspaceRoot -or $scope.FromServerRoot -cne $old.ServerRoot -or
                    $scope.ToWorkspaceRoot -cne $new.WorkspaceRoot -or $scope.ToServerRoot -cne $new.ServerRoot){throw 'Incorrect transfer endpoints.'}
                Reject {Get-ReignRootMigration $null $new.WorkspaceRoot $new.ServerRoot $report $report} 'existing reviewed prior'
                Reject {Get-ReignRootMigration $old $new.WorkspaceRoot $new.ServerRoot '' $report} 'both original'
                Reject {Get-ReignRootMigration $old $new.WorkspaceRoot $new.ServerRoot $report ''} 'both original'
                Reject {Get-ReignRootMigration $old $old.WorkspaceRoot $old.ServerRoot $report $report} 'distinct primary pairs'
                Reject {Assert-ReignPrimaryPair $new.WorkspaceRoot $old.ServerRoot} 'physical Reign and ReignServer siblings'
                $alias=Join-Path $Root 'alias'
                New-Item -ItemType Junction -Path $alias -Target (Split-Path $new.WorkspaceRoot)|Out-Null
                Reject {Assert-ReignPrimaryPair (Join-Path $alias Reign) (Join-Path $alias ReignServer)} 'redirected paths'
                $projection=Join-Path $new.WorkspaceRoot ReignServer
                [IO.Directory]::Delete($projection)
                New-Item -ItemType Junction -Path $projection -Target $old.ServerRoot|Out-Null
                Reject {Migration} 'direct ReignServer sibling junction'
                [IO.Directory]::Delete($projection)
                New-Item -ItemType Junction -Path $projection -Target $new.ServerRoot|Out-Null
                git -C $new.ServerRoot remote set-url --push origin 'https://example.invalid/ReignServer.git'
                Reject {Migration} 'canonical fetch and push origins'
                git -C $new.ServerRoot config --unset-all remote.origin.pushurl
                $linked=Join-Path $Root 'linked/ReignServer'
                [IO.Directory]::CreateDirectory($linked)|Out-Null
                [IO.File]::WriteAllText((Join-Path $linked '.git'),"gitdir: $($new.ServerRoot)/.git/worktrees/linked")
                $linkedClient=Join-Path (Split-Path $linked) Reign
                git init --quiet $linkedClient
                git -C $linkedClient remote add origin 'https://github.com/Dwemer-Dynamics/Reign.git'
                Reject {Assert-ReignPrimaryPair $linkedClient $linked} 'primary Git checkouts'

                $content.RootMigration=$scope
                $content|ConvertTo-Json -Depth 16|Set-Content $candidate
                $review.CandidateSha256=Get-ReignFileSha $candidate
                $review.ReviewedPaths=@($content.Diff|ForEach-Object path)
                function Read-Review {Get-Content $reviewPath -Raw|ConvertFrom-Json}
                $review|ConvertTo-Json -Depth 16|Set-Content $reviewPath
                $parsed=Get-Content $candidate -Raw|ConvertFrom-Json
                Reject {Assert-ReignCheckpointReview $parsed (Read-Review) $candidate} 'root migration scope'
                $review.RootMigrationSha256=Get-ReignJsonSha $scope
                $review|ConvertTo-Json -Depth 16|Set-Content $reviewPath
                Assert-ReignCheckpointReview $parsed (Read-Review) $candidate
                $promote=Join-Path (Split-Path $ModulePath) 'Promote-DeploymentCheckpoint.ps1'
                $argsForPromotion=@{Candidate=$candidate;ReviewReceipt=$reviewPath;AuthorityPath=$pointerPath}
                $fixtureHash=Get-ReignFileSha $pointerPath
                Reject {& $promote @argsForPromotion} 'marked candidate'
                Reject {& $promote @argsForPromotion -Initialize -MigrateRoots} 'marked candidate'
                if((Get-ReignFileSha $pointerPath) -ne $fixtureHash){throw 'Rejected promotion changed the pointer.'}

                $content.WorkspaceRoot=$new.WorkspaceRoot
                $content.ServerRoot=$new.ServerRoot
                $content.ValidationReportSelection=$report
                $content.LinuxBuildReportSelection=$report
                $content.PriorAuthoritySha256=$fixtureHash
                [IO.File]::Copy($pointerPath,($candidate+'.previous-authority.json'))
                Assert-ReignRootMigration $old $content $candidate
                $content.RootMigration.FromServerRoot=$new.ServerRoot
                Reject {Assert-ReignRootMigration $old $content $candidate} 'scope differs'
                $content.RootMigration=Migration
                [IO.File]::AppendAllText(($candidate+'.previous-authority.json'),' ')
                Reject {Assert-ReignRootMigration $old $content $candidate} 'exact retained prior'

                $out=Join-Path $Root 'mcp/out'
                [IO.Directory]::CreateDirectory($out)|Out-Null
                $dll=Join-Path $out 'Reign.Mcp.Server.dll'
                [IO.File]::WriteAllText($dll,'verifier fixture only')
                $clientReport=[pscustomobject]@{ArtifactRoot=$Root;DeploymentArtifactHashes=[pscustomobject]@{
                    'mcp/out/Reign.Mcp.Server.dll'=Get-ReignFileSha $dll;'client/out/ReignBeta.dll'='fixture'}}
                $qualified=Get-ReignReportVerifier $clientReport -MigrateRoots
                if($qualified.files[0].sha256 -ne (Get-ReignFileSha $dll)){throw 'Incorrect verifier artifact binding.'}
                [IO.File]::WriteAllText((Join-Path $out 'extra.json'),'extra')
                Reject {Get-ReignReportVerifier $clientReport -MigrateRoots} 'exact qualified verifier file set'
                Write-Output 'ROOT_MIGRATION_CONTRACTS_PASS'
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
            Assert.Contains("ROOT_MIGRATION_CONTRACTS_PASS", stdout);
        }
        finally
        {
            Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()), Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith("reign-promotion-contract-", Path.GetFileName(root));
            Directory.Delete(root, true);
        }
    }
}
