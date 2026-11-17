using System.Diagnostics;
using System.Text.Json;

namespace Reign.Mcp.Tests;

public sealed class ControlCenterRuntimeTests
{
    [Fact]
    public async Task LauncherReusesHealthyServerAndRecoversUnhealthyServer()
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = TestOptions.FindWorkspace();
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32/WindowsPowerShell/v1.0/powershell.exe"))
        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        start.Environment["REIGN_TEST_STARTUP_SCRIPT"] = Path.Combine(root, "ReignServer/scripts/Test-LauncherStartup.ps1");
        start.Environment["REIGN_TEST_LAUNCHER_SCRIPT"] = Path.Combine(root, "ReignServer/scripts/Start-ReignServer-Wsl.ps1");
        // The host need not register ReignServer: the child-only wsl.exe mock checks the
        // production wslpath invocation and answers both path conversions.
        const string command = """
            $ErrorActionPreference = 'Stop'
            $global:wslCalls = 0
            $global:wslExpected = @(
                @('C:/Users/speed/.reign/setup/reign-recover.py', '/mnt/c/Users/speed/.reign/setup/reign-recover.py'),
                @('C:/reign launcher fixture/helper.py', '/mnt/c/reign launcher fixture/helper.py'))
            function global:wsl.exe {
                $i = $global:wslCalls++
                if ($i -ge $global:wslExpected.Count -or $args.Count -ne 6 -or
                    ($args -join '|') -cne ('-d|ReignServer|--exec|/usr/bin/wslpath|-u|' + $global:wslExpected[$i][0])) {
                    throw "Unexpected wsl.exe call: $($args -join '|')"
                }
                $global:LASTEXITCODE = 0
                $global:wslExpected[$i][1]
            }
            try {
                & $env:REIGN_TEST_STARTUP_SCRIPT -Launcher $env:REIGN_TEST_LAUNCHER_SCRIPT
                if ($global:wslCalls -ne 2) { throw "Expected 2 wsl.exe path conversions, saw $global:wslCalls." }
            } catch { [Console]::Error.WriteLine($_); exit 1 }
            """;
        foreach (string arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-EncodedCommand",
            Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(command)) })
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(true); throw new TimeoutException("Isolated launcher contracts exceeded 20 seconds."); }
        string output = (await stdout) + (await stderr);
        Assert.True(process.ExitCode == 0, output);
        Assert.Contains("reign-launcher-startup-contract-v1", output);
    }

    [Fact]
    public async Task ManagedServerControlUsesIsolatedServiceAndSecurityContracts()
    {
        string script = Path.Combine(TestOptions.FindWorkspace(), "ReignServer/scripts/test-reign-control.py").Replace('\\', '/');
        var start = new ProcessStartInfo("wsl.exe") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (string arg in new[] { "-d", Environment.GetEnvironmentVariable("REIGN_WSL_DISTRO") ?? "ReignServer", "-u", "root", "--", "python3", "-c",
            "import subprocess,sys; p=subprocess.check_output(['wslpath','-u',sys.argv[1]],text=True).strip(); sys.exit(subprocess.call(['python3','-B',p]))", script })
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(true); throw new TimeoutException("Isolated server control contracts exceeded 30 seconds."); }
        string output = (await stdout) + (await stderr);
        Assert.True(process.ExitCode == 0, output);
        Assert.Contains("reign-control-contracts-v1", output);
    }

    [Fact]
    public void ControlCenterCatalogDocumentsBehaviorIsolationAndInstallBoundary()
    {
        string root = TestOptions.FindWorkspace();
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reign.testing.json")));
        var entry = catalog.RootElement.GetProperty("controlCenterSavePower");
        foreach (string field in new[] { "browserContract", "runtimeContract", "procedure", "securityDocumentation" })
            Assert.True(File.Exists(Path.Combine(root, entry.GetProperty(field).GetString()!)));
        string installer = File.ReadAllText(Path.Combine(root, "ReignServer/scripts/install-reign-control.sh")).Replace("\r\n", "\n");
        Assert.Contains("visudo -cf", installer);
        Assert.Contains("systemctl --no-block start reignserver.service", installer);
        Assert.Contains("systemctl --no-block stop reignserver.service", installer);
        Assert.DoesNotContain("NOPASSWD: ALL", installer);
        Assert.Contains("/usr/local/bin/reignctl configure-web", installer);
        Assert.Contains("/usr/local/lib/reign/prepare-systemd-sessions.sh", installer);
        int prerequisiteRun = installer.IndexOf("\nsh \"$prep\"\n", StringComparison.Ordinal);
        int controlReplacement = installer.IndexOf("\ninstall -m 0755 \"$1\" /usr/local/bin/reignctl.next\n", StringComparison.Ordinal);
        Assert.True(prerequisiteRun >= 0, "Installer must run the staged session prerequisite script.");
        Assert.True(controlReplacement >= 0, "Installer must stage the replacement reignctl.");
        Assert.True(prerequisiteRun < controlReplacement, "Session prerequisites must finish before reignctl is replaced.");
        string control = File.ReadAllText(Path.Combine(root, "ReignServer/scripts/reignctl"));
        Assert.Contains("/usr/local/lib/reign/prepare-systemd-sessions.sh", control);
        // First enabling mod_cgid needs a new daemon; graceful reload left live
        // requests returning AH01257 even though Apache configtest succeeded.
        Assert.Contains("service apache2 restart", control);
        Assert.DoesNotContain("service apache2 reload", control);
    }
}
