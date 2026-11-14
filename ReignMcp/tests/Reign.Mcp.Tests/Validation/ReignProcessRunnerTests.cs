using System.Diagnostics;
using System.Text.Json;
using Reign.Mcp.Server;

namespace Reign.Mcp.Tests;

public sealed class ReignProcessRunnerTests
{
    [Fact]
    public async Task ValidationLifetimeCanExceedTheGenericProcessLimit()
    {
        var options = TestOptions.Create(TestOptions.FindWorkspace()) with { ProcessTimeout = TimeSpan.FromMilliseconds(1) };
        var runner = new ReignProcessRunner(options, new SensitiveDataRedactor());
        var (file, arguments) = Shell(150);
        var result = await runner.RunAsync(file, arguments, options.WorkspaceRoot,
            timeout: Timeout.InfiniteTimeSpan).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(result.Ok, result.StandardError);
        Assert.False(result.TimedOut);
        Assert.Contains("completed", result.StandardOutput);
        Assert.True(result.DurationMs >= 150);
    }

    [Fact]
    public async Task UnrelatedProcessesRetainTheirConfiguredLimit()
    {
        var options = TestOptions.Create(TestOptions.FindWorkspace()) with { ProcessTimeout = TimeSpan.FromMilliseconds(20) };
        var runner = new ReignProcessRunner(options, new SensitiveDataRedactor());
        var (file, arguments) = Shell(10000);
        var result = await runner.RunAsync(file, arguments, options.WorkspaceRoot).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(result.Ok);
        Assert.True(result.TimedOut);
    }

    [Fact]
    public async Task UnlimitedValidationCancellationStopsItsStartedChild()
    {
        var options = TestOptions.Create(TestOptions.FindWorkspace());
        string directory = Path.Combine(options.BuildRoot, "runner-cancellation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string marker = Path.Combine(directory, "child.pid");
        var runner = new ReignProcessRunner(options, new SensitiveDataRedactor());
        var (file, arguments) = Shell(10000, writePid: true);
        using var cancellation = new CancellationTokenSource();
        Task<ProcessResult> execution = runner.RunAsync(file, arguments, options.WorkspaceRoot,
            timeout: Timeout.InfiniteTimeSpan, environment: new Dictionary<string, string> { ["REIGN_TEST_CHILD_PIDFILE"] = marker },
            cancellationToken: cancellation.Token);
        try
        {
            using var observation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            int pid = 0;
            while (pid == 0)
            {
                await Task.Delay(20, observation.Token);
                if (File.Exists(marker)) int.TryParse(await File.ReadAllTextAsync(marker, observation.Token), out pid);
            }
            using var child = Process.GetProcessById(pid);
            Assert.False(child.HasExited);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.True(child.HasExited);
        }
        finally
        {
            cancellation.Cancel();
            try { await execution.WaitAsync(TimeSpan.FromSeconds(10)); } catch (OperationCanceledException) { }
            if (File.Exists(marker)) File.Delete(marker);
            Directory.Delete(directory);
        }
    }

    [Fact]
    public void LinuxRelayPreservesUnlimitedLifetimeAsJsonNull()
    {
        Assert.Null(ReignProcessRunner.GetLinuxChildTimeout(Timeout.InfiniteTimeSpan));
        Assert.Equal("{\"timeout\":null}", JsonSerializer.Serialize(new { timeout = ReignProcessRunner.GetLinuxChildTimeout(Timeout.InfiniteTimeSpan) }));
        Assert.Equal(28d, ReignProcessRunner.GetLinuxChildTimeout(TimeSpan.FromSeconds(30)));
        Assert.Equal(1d, ReignProcessRunner.GetLinuxChildTimeout(TimeSpan.FromSeconds(1)));
    }

    private static (string, string[]) Shell(int milliseconds, bool writePid = false) => OperatingSystem.IsWindows()
        ? (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"),
            ["-NoProfile", "-NonInteractive", "-Command", (writePid ? "[IO.File]::WriteAllText($env:REIGN_TEST_CHILD_PIDFILE, $PID.ToString()); " : "")
                + $"[Threading.Thread]::Sleep({milliseconds}); [Console]::WriteLine('completed')"])
        : ("/bin/sh", ["-c", (writePid ? "printf '%s' $$ > \"$REIGN_TEST_CHILD_PIDFILE\"; " : "")
            + $"sleep {(milliseconds / 1000d).ToString(System.Globalization.CultureInfo.InvariantCulture)}; printf completed"]);
}
