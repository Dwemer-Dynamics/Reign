using System.Diagnostics;
using System.Text.Json;

namespace Reign.Mcp.Tests.Features.WorldSimulation;

public sealed class PlayerApproachNativeCompatibilityTests
{
    [ValidatedClientFact]
    public async Task ApproachesAndRescuesBindToTheInstalledNativeApi()
    {
        string client = MapBarInputSafetyTests.CurrentClientArtifact()!;
        string evidence = Path.Combine(new DirectoryInfo(AppContext.BaseDirectory).Parent!.FullName,
            "player-approach-native-probe", Guid.NewGuid().ToString("N"));
        string working = Path.Combine(evidence, "sandbox", "bin", "Win64_Shipping_Client");
        Directory.CreateDirectory(working);
        string shellRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0");
        var start = new ProcessStartInfo(Path.Combine(shellRoot, "powershell.exe")) {
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = working,
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true
        };
        start.Environment["PSModulePath"] = Path.Combine(shellRoot, "Modules");
        foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File",
            Path.Combine(TestOptions.FindWorkspace(), "ReignMcp/tests/Reign.Mcp.Tests/Features/WorldSimulation/PlayerApproachNativeCompatibilityProbe.ps1"),
            "-ClientAssembly", client, "-BannerlordPath", Environment.GetEnvironmentVariable("REIGN_BANNERLORD_PATH")!,
            "-EvidenceDirectory", evidence }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start approach native binding probe.");
        process.StandardInput.Close();
        Task<string> output = process.StandardOutput.ReadToEndAsync(), error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) {
            process.Kill(true); await process.WaitForExitAsync();
            throw new TimeoutException("Approach native probe exceeded 60 seconds. Evidence: " + evidence);
        }
        Assert.True(process.ExitCode == 0, (await output) + (await error));
        using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(evidence, "report.json")));
        Assert.True(report.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(4, report.RootElement.GetProperty("nativeHooks").GetArrayLength());
        Assert.Contains(report.RootElement.GetProperty("preparedMethods").EnumerateArray(),
            method => method.GetString() == "ReignBeta.Campaign.ReignRescueConversationBehavior.Listen");
        foreach (string guard in new[] { "CanPresentOnMap", "TryPresentOnMap" })
            Assert.Contains(report.RootElement.GetProperty("preparedMethods").EnumerateArray(),
                method => method.GetString() == "ReignBeta.UI.ReignPlayerApproachPopup." + guard);
    }

    [Fact]
    public void NativeProbeDocumentsItsBoundariesAndEvidence()
    {
        string root = TestOptions.FindWorkspace();
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reign.testing.json")));
        var probe = catalog.RootElement.GetProperty("playerApproaches").GetProperty("nativeCompatibility");
        Assert.Equal("reign-player-approach-native-probe-v1", probe.GetProperty("schema").GetString());
        Assert.True(File.Exists(Path.Combine(root, probe.GetProperty("probe").GetString()!)));
        foreach (string path in new[] { "docs/agent/PLAYER_ATTENTION_AND_APPROACHES.md", "ReignMcp/docs/tool-catalog.md", "ReignMcp/docs/security-model.md" })
            Assert.Contains("reign-player-approach-native-probe-v1", File.ReadAllText(Path.Combine(root, path)));
    }

    private sealed class ValidatedClientFactAttribute : FactAttribute
    {
        public ValidatedClientFactAttribute()
        {
            if (!OperatingSystem.IsWindows() || MapBarInputSafetyTests.CurrentClientArtifact() == null
                || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("REIGN_BANNERLORD_PATH")))
                Skip = "Requires same-run canonical client artifact and licensed Bannerlord assemblies.";
        }
    }
}
