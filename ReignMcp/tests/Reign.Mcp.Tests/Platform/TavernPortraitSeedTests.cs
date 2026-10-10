using System.Diagnostics;
using System.Text.Json;

namespace Reign.Mcp.Tests;

public sealed class TavernPortraitSeedTests
{
    [ClientArtifactFact]
    public async Task EveryTavernSeedsAndRepairsFromTheActualClientDespitePackageTimestampChanges()
    {
        string client = MapBarInputSafetyTests.CurrentClientArtifact()!;
        string evidence = Path.Combine(new DirectoryInfo(AppContext.BaseDirectory).Parent!.FullName,
            "tavern-portrait-probe", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(evidence);
        string shellRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0");
        var start = new ProcessStartInfo(Path.Combine(shellRoot, "powershell.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = evidence,
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true
        };
        start.Environment["PSModulePath"] = Path.Combine(shellRoot, "Modules");
        start.Environment["REIGN_VALIDATION_MODE"] = "1";
        foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File",
            Path.Combine(TestOptions.FindWorkspace(), "ReignMcp/tests/Reign.Mcp.Tests/Platform/TavernPortraitSeedProbe.ps1"),
            "-ClientAssembly", client, "-Workspace", TestOptions.FindWorkspace(), "-EvidenceDirectory", evidence,
            "-BannerlordPath", Environment.GetEnvironmentVariable("REIGN_BANNERLORD_PATH") ?? "" })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start tavern portrait probe.");
        process.StandardInput.Close();
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            process.Kill(true);
            await process.WaitForExitAsync();
            string progress = Path.Combine(evidence, "progress.json");
            throw new TimeoutException("Tavern portrait probe timed out: "
                + (File.Exists(progress) ? File.ReadAllText(progress) : "no progress") + ". Evidence: " + evidence);
        }
        Assert.True(process.ExitCode == 0, (await output) + (await error) + " Evidence: " + evidence);
        using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(evidence, "report.json")));
        var result = report.RootElement;
        Assert.True(result.GetProperty("ok").GetBoolean());
        Assert.Equal(result.GetProperty("catalogCastCount").GetInt32(), result.GetProperty("firstVisitPassed").GetInt32());
        Assert.Equal(result.GetProperty("catalogCastCount").GetInt32(), result.GetProperty("repairPassed").GetInt32());
        Assert.Equal(result.GetProperty("catalogTownCount").GetInt32(), result.GetProperty("towns").GetArrayLength());
        Assert.True(result.GetProperty("relocatedTimestampCasePassed").GetBoolean());
        Assert.Equal(3, result.GetProperty("corruptionCasesRejectedWithoutCacheChanges").GetInt32());
        Assert.True(result.GetProperty("sourcePackUnchanged").GetBoolean());
    }

    private sealed class ClientArtifactFactAttribute : FactAttribute
    {
        public ClientArtifactFactAttribute()
        {
            if (MapBarInputSafetyTests.CurrentClientArtifact() == null)
                Skip = "Requires same-run canonical client artifact; include portrait client paths or use product validation.";
        }
    }
}
