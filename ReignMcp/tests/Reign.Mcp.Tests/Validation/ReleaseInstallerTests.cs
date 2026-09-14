using System.Diagnostics;

namespace Reign.Mcp.Tests;

public sealed class ReleaseInstallerTests
{
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
