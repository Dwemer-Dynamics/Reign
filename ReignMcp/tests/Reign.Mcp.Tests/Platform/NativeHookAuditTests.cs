using System.Diagnostics;
using System.Text.Json;

namespace Reign.Mcp.Tests;

public sealed class NativeHookAuditTests
{
    [CurrentNativeClientFact]
    public async Task ResidentRegistrationAndStaticNativeHooksBindTo157()
    {
        string evidence = Path.Combine(new DirectoryInfo(AppContext.BaseDirectory).Parent!.FullName,
            "native-hook-audit", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(evidence);
        string shell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0");
        var start = new ProcessStartInfo(Path.Combine(shell, "powershell.exe")) {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true
        };
        start.Environment["PSModulePath"] = Path.Combine(shell, "Modules");
        foreach (string arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File",
            Path.Combine(TestOptions.FindWorkspace(), "ReignMcp/tests/Reign.Mcp.Tests/Platform/NativeHookAuditProbe.ps1"),
            "-ClientAssembly", MapBarInputSafetyTests.CurrentClientArtifact()!,
            "-BannerlordPath", Environment.GetEnvironmentVariable("REIGN_BANNERLORD_PATH")!,
            "-EvidenceDirectory", evidence, "-SourceRoot", TestOptions.FindWorkspace() }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        process.StandardInput.Close();
        Task<string> output = process.StandardOutput.ReadToEndAsync(), error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(180));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) {
            process.Kill(true); await process.WaitForExitAsync();
            throw new TimeoutException("Native hook audit timed out: " + evidence);
        }
        Assert.True(process.ExitCode == 0, (await output) + (await error) + " Evidence: " + evidence);
        using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(evidence, "report.json")));
        Assert.True(report.RootElement.GetProperty("ok").GetBoolean());
        Assert.True(report.RootElement.GetProperty("staticLookups").GetArrayLength() >= 100);
        Assert.Equal(9, report.RootElement.GetProperty("residentBindings").GetInt32());
        Assert.Equal(6, report.RootElement.GetProperty("garrisonInvocationChecks").GetInt32());
        Assert.True(report.RootElement.GetProperty("legacyGarrisonArityRejected").GetBoolean());
    }

    private sealed class CurrentNativeClientFactAttribute : FactAttribute
    {
        public CurrentNativeClientFactAttribute()
        {
            if (!OperatingSystem.IsWindows() || MapBarInputSafetyTests.CurrentClientArtifact() == null
                || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("REIGN_BANNERLORD_PATH")))
                Skip = "Requires same-run canonical client artifact and licensed Bannerlord dependencies.";
        }
    }
}
