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
        foreach (string arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File",
            Path.Combine(root, "ReignServer/scripts/Test-LauncherStartup.ps1"), "-Launcher",
            Path.Combine(root, "ReignServer/scripts/Start-ReignServer-Wsl.ps1") })
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
        string installer = File.ReadAllText(Path.Combine(root, "ReignServer/scripts/install-reign-control.sh"));
        Assert.Contains("visudo -cf", installer);
        Assert.Contains("systemctl --no-block start reignserver.service", installer);
        Assert.Contains("systemctl --no-block stop reignserver.service", installer);
        Assert.DoesNotContain("NOPASSWD: ALL", installer);
        Assert.Contains("/usr/local/bin/reignctl configure-web", installer);
        Assert.Contains("/usr/local/lib/reign/prepare-systemd-sessions.sh", installer);
        Assert.True(installer.IndexOf("\n/usr/local/lib/reign/prepare-systemd-sessions.sh", StringComparison.Ordinal)
            < installer.IndexOf("/usr/local/bin/reignctl.next", StringComparison.Ordinal));
        string control = File.ReadAllText(Path.Combine(root, "ReignServer/scripts/reignctl"));
        Assert.Contains("/usr/local/lib/reign/prepare-systemd-sessions.sh", control);
        // First enabling mod_cgid needs a new daemon; graceful reload left live
        // requests returning AH01257 even though Apache configtest succeeded.
        Assert.Contains("service apache2 restart", control);
        Assert.DoesNotContain("service apache2 reload", control);
    }
}
