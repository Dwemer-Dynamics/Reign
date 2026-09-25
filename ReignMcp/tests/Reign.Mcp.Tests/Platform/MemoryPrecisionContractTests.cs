using System.Diagnostics;
using System.Text.Json;
using Reign.Mcp.Server;

namespace Reign.Mcp.Tests;

public sealed class MemoryPrecisionContractTests
{
    [Theory]
    [InlineData("Stage", false, false)]
    [InlineData("Preview", false, false)]
    [InlineData("Stage", true, false)]
    [InlineData("Apply", false, false)]
    [InlineData("Apply", false, true)]
    public async Task MaintenanceWrapperBindsManagedIdentityAndPreservesGates(string mode, bool wrongHash, bool gameRunning)
    {
        string root=Path.Combine(Path.GetTempPath(),"reign memory wrapper-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string manifest=Path.Combine(root,"request.json"), log=Path.Combine(root,"invocation.json");
            File.WriteAllText(manifest,JsonSerializer.Serialize(new {schema=mode=="Stage" ? "reign-memory-stage-request-v1" : "reign-continuity-repair-v1",campaignId="wrapper_fixture"}));
            string script="""
                $ErrorActionPreference='Stop'
                function global:Get-Process {
                    param([string[]]$Name,[string]$ErrorAction)
                    if ($env:WRAPPER_GAME_RUNNING -eq '1' -and $Name -contains 'Bannerlord.BLSE.Standalone') {
                        return [PSCustomObject]@{ProcessName='Bannerlord.BLSE.Standalone'}
                    }
                }
                function global:wsl.exe {
                    $a=@($args); $global:LASTEXITCODE=0
                    if ($a -contains 'sha256sum') { return (('a'*64)+'  /validated/ReignServer') }
                    if ($a -contains 'wslpath') {
                        if ($a[-1].Contains('\') -or !$a[-1].Contains('reign memory wrapper-')) { throw 'Windows paths must preserve spaces and use forward slashes at the WSL boundary.' }
                        return ('/mnt/reign-fixture/'+[IO.Path]::GetFileName($a[-1]))
                    }
                    if ($a -contains 'systemctl') { return }
                    if ($a -contains '--continuity-repair') {
                        [IO.File]::WriteAllText($env:WRAPPER_LOG,(ConvertTo-Json -InputObject $a -Compress))
                        return '{"ok":true}'
                    }
                    throw 'Unexpected WSL operation in maintenance fixture.'
                }
                $expected=if ($env:WRAPPER_WRONG_HASH -eq '1') {'Server artifact hash differs from the reviewed validation artifact.'} elseif ($env:WRAPPER_GAME_RUNNING -eq '1') {'Close Bannerlord before applying the reviewed repair.'} elseif ($env:WRAPPER_MODE -eq 'Apply') {'Stop ReignServer through its launcher before applying the repair.'} else {''}
                $hash=if ($env:WRAPPER_WRONG_HASH -eq '1') {'b'*64} else {'a'*64}
                $manifestHash=(Get-FileHash -LiteralPath $env:WRAPPER_MANIFEST -Algorithm SHA256).Hash.ToLowerInvariant()
                try {
                    & $env:WRAPPER_SCRIPT -Manifest $env:WRAPPER_MANIFEST -Mode $env:WRAPPER_MODE -Executable '/validated/ReignServer' -ExecutableSha256 $hash -ConfirmedManifestSha256 $manifestHash -EvidenceDirectory $env:WRAPPER_EVIDENCE
                    if ($expected) { throw 'Maintenance gate unexpectedly allowed execution.' }
                } catch { if (!$expected -or $_.Exception.Message -ne $expected) { throw } }
                exit 0
                """;
            var start=new ProcessStartInfo("powershell.exe") {UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true};
            foreach(string arg in new[]{"-NoProfile","-NonInteractive","-EncodedCommand",Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script))}) start.ArgumentList.Add(arg);
            start.Environment["WRAPPER_SCRIPT"]=Path.Combine(TestOptions.FindWorkspace(),"ReignServer/scripts/Repair-ConversationContinuity.ps1");
            start.Environment["WRAPPER_MANIFEST"]=manifest;
            start.Environment["WRAPPER_LOG"]=log;
            start.Environment["WRAPPER_EVIDENCE"]=Path.Combine(root,"evidence");
            start.Environment["WRAPPER_MODE"]=mode;
            start.Environment["WRAPPER_WRONG_HASH"]=wrongHash ? "1" : "0";
            start.Environment["WRAPPER_GAME_RUNNING"]=gameRunning ? "1" : "0";
            using var process=Process.Start(start)!;
            var output=process.StandardOutput.ReadToEndAsync(); var error=process.StandardError.ReadToEndAsync();
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch(OperationCanceledException) {process.Kill(true);throw new TimeoutException("Maintenance wrapper fixture exceeded 30 seconds.");}
            Assert.True(process.ExitCode==0,(await output)+(await error));
            if(wrongHash || mode=="Apply") { Assert.False(File.Exists(log)); return; }
            var arguments=JsonSerializer.Deserialize<string[]>(File.ReadAllText(log))!;
            // PowerShell consumes the literal -- before invoking a mocked function;
            // the sudo separator comes from the argument array and remains intact.
            Assert.Equal(new[]{"-d","ReignServer","sudo","-n","-u","reign","--","env"},arguments.Take(8));
            foreach(string value in new[]{"REIGN_VALIDATION_MODE=0","REIGN_DB_NAME=reign","REIGN_DB_HOST=/var/run/postgresql","REIGN_DB_USER=reign","REIGN_DATA_ROOT=/mnt/reign-fixture/evidence","/validated/ReignServer","--continuity-repair"}) Assert.Contains(value,arguments);
            Assert.Equal(mode=="Stage",arguments.Contains("--stage"));
            Assert.DoesNotContain("--apply",arguments);
        }
        finally { Directory.Delete(root,recursive:true); }
    }

    [Fact]
    public async Task LinuxTokenizerAndQdrantEligibilityUseIsolatedState()
    {
        string root=TestOptions.FindWorkspace();
        string test=Path.Combine(root,"ReignServer/services/vector-worker/test_precision.py").Replace('\\','/');
        var start=new ProcessStartInfo("wsl.exe") {UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true};
        foreach(string arg in new[]{"-d","ReignServer","--","python3","-c",
            "import pathlib,subprocess,os,sys,hashlib; r=pathlib.Path('/var/www/html/ReignServer/runtime'); req=next(p for p in [r/'current/services/vector-worker/requirements-linux.txt',r/'current/VectorWorker/requirements-linux.txt'] if p.is_file()); py=r/'venvs'/hashlib.sha256(req.read_bytes()).hexdigest()/'bin/python'; tok=r/'model-versions'/hashlib.sha256((r/'current/embedding-model.lock.json').read_bytes()).hexdigest()/'tokenizer.json'; assert py.is_file() and tok.is_file(), 'Managed worker dependencies are required'; script=subprocess.check_output(['wslpath','-u',sys.argv[1]],text=True).strip(); env=dict(os.environ,REIGN_PRECISION_TEST_TOKENIZER=str(tok),PYTHONDONTWRITEBYTECODE='1'); sys.exit(subprocess.call([str(py),'-B',script,'--runtime-contracts'],env=env))",test}) start.ArgumentList.Add(arg);
        using var process=Process.Start(start)!;
        var output=process.StandardOutput.ReadToEndAsync(); var error=process.StandardError.ReadToEndAsync();
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch(OperationCanceledException) {process.Kill(true);throw new TimeoutException("Isolated worker runtime contracts exceeded 60 seconds.");}
        Assert.True(process.ExitCode==0,(await output)+(await error));
    }

    [Fact]
    public async Task WorkerPrecisionContractsExecuteWithoutProvidersOrListeners()
    {
        string workspace = TestOptions.FindWorkspace();
        var start = new ProcessStartInfo("wsl.exe") { UseShellExecute=false, RedirectStandardOutput=true, RedirectStandardError=true,CreateNoWindow=true };
        string test=Path.Combine(workspace,"ReignServer/services/vector-worker/test_precision.py").Replace('\\','/');
        foreach(string arg in new[]{"-d","ReignServer","--","python3","-c",
            "import subprocess,sys; p=subprocess.check_output(['wslpath','-u',sys.argv[1]],text=True).strip(); sys.exit(subprocess.call(['python3','-B',p]))",test}) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var output=process.StandardOutput.ReadToEndAsync(); var error=process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(true); throw new TimeoutException("Worker contracts exceeded 30 seconds."); }
        string stdout=await output, stderr=await error;
        Assert.True(process.ExitCode==0,stdout+stderr);
        Assert.Contains("reign-memory-worker-contracts-v1",stdout);
    }

    [Fact]
    public void MaintenanceVocabularyAndTestingCatalogRemainExplicit()
    {
        string root=TestOptions.FindWorkspace();
        using var catalog=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"reign.testing.json")));
        Assert.Equal("memory_precision",catalog.RootElement.GetProperty("memoryPrecision").GetProperty("suite").GetString());
        string wrapper=File.ReadAllText(Path.Combine(root,"ReignServer/scripts/Repair-ConversationContinuity.ps1"));
        Assert.Contains("'Stage','Preview','Apply'",wrapper);
        Assert.Contains("ConfirmedManifestSha256",wrapper);
        Assert.Contains("pg_dump",wrapper);
        Assert.Contains("systemctl is-active",wrapper);
        foreach(string file in new[]{"ReignMcp/docs/security-model.md","ReignMcp/docs/tool-catalog.md","docs/agent/testing/DIALOGUE_AND_PROVIDERS.md"})
            Assert.Contains("memory_precision",File.ReadAllText(Path.Combine(root,file)));
    }
}
