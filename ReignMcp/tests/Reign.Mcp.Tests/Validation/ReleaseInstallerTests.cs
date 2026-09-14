using System.Diagnostics;

namespace Reign.Mcp.Tests;

public sealed class ReleaseInstallerTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SetupRejectsUnsupportedDatabasePathsBeforeCreatingInstallationState(bool unicodeProgram)
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = TestOptions.FindWorkspace();
        string proofRoot = Path.Combine(Path.GetTempPath(), "Reign path preflight", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(proofRoot);
        string program = Path.Combine(proofRoot, unicodeProgram ? "Program \u6f22\u5b57" : "Program");
        string data = Path.Combine(proofRoot, unicodeProgram ? "Data" : "Data \u6f22\u5b57");
        string record = Path.Combine(proofRoot, "installation.json");
        var start = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe"),
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = root
        };
        // No manifest, payloads or game exist. A meaningful path error must win
        // before any of those inputs are read or installation state is created.
        foreach (var argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File",
            Path.Combine(root, "ReignRelease", "Install-Reign.ps1"),
            "-Manifest", Path.Combine(proofRoot, "missing-package.json"),
            "-PayloadDirectory", Path.Combine(proofRoot, "Payloads"),
            "-BannerlordRoot", Path.Combine(proofRoot, "Game"),
            "-ProgramRoot", program, "-DataRoot", data, "-InstallationFile", record }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(deadline.Token); }
        catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
        string output = await stdout + await stderr;
        Assert.NotEqual(0, process.ExitCode);
        Assert.Contains("must use ASCII characters", output);
        Assert.False(Directory.Exists(program));
        Assert.False(Directory.Exists(data));
        Assert.False(File.Exists(record));
    }

    [Fact]
    public async Task WindowsPowerShellInstallerRejectsUnsafePayloadsAndPreservesPlayerPortraits()
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = TestOptions.FindWorkspace();
        string proofRoot = Path.Combine(Path.GetTempPath(), "Reign installer contracts");
        Directory.CreateDirectory(proofRoot);
        var start = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe"),
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = root
        };
        foreach (var argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File",
            Path.Combine(root, "ReignRelease", "tests", "Test-Installer.ps1"), "-EvidenceRoot", proofRoot }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        try { await process.WaitForExitAsync(deadline.Token); }
        catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
        string output = await stdout + await stderr;
        Assert.True(process.ExitCode == 0, output);
        Assert.Contains("installer contracts passed", output);
    }
}
