using System.Diagnostics;
using System.Text.Json;

namespace Reign.Mcp.Tests;

public sealed class TavernHouseVisitNativeTests
{
    [ClientArtifactFact]
    public async Task CompiledClientRequiresOwnedScopeAndExactPreparedTermsBeforePayment()
    {
        string client = MapBarInputSafetyTests.CurrentClientArtifact()!;
        string evidence = Path.Combine(new DirectoryInfo(AppContext.BaseDirectory).Parent!.FullName, "tavern-visit-probe", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(evidence);
        string shellRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0");
        var start = new ProcessStartInfo(Path.Combine(shellRoot, "powershell.exe")) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = evidence,
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true };
        start.Environment["PSModulePath"] = Path.Combine(shellRoot, "Modules"); start.Environment["REIGN_VALIDATION_MODE"] = "1";
        foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File",
            Path.Combine(TestOptions.FindWorkspace(), "ReignMcp/tests/Reign.Mcp.Tests/Platform/TavernHouseVisitNativeProbe.ps1"),
            "-ClientAssembly", client, "-EvidenceDirectory", evidence, "-BannerlordPath", Environment.GetEnvironmentVariable("REIGN_BANNERLORD_PATH") ?? "" }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start isolated visit binding probe.");
        process.StandardInput.Close(); Task<string> output = process.StandardOutput.ReadToEndAsync(), error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(true); await process.WaitForExitAsync(); throw new TimeoutException("Visit binding probe exceeded 60 seconds: " + evidence); }
        Assert.True(process.ExitCode == 0, (await output) + (await error) + " Evidence: " + evidence);
        using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(evidence, "report.json")));
        Assert.True(report.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(12, report.RootElement.GetProperty("passed").GetInt32());
        Assert.Equal(client, report.RootElement.GetProperty("clientAssembly").GetString());
    }
    private sealed class ClientArtifactFactAttribute : FactAttribute
    {
        public ClientArtifactFactAttribute() { if (MapBarInputSafetyTests.CurrentClientArtifact() == null) Skip = "Requires same-run canonical client artifact."; }
    }
}
