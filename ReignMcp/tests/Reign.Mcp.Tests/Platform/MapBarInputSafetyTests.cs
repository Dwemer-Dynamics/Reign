using System.Diagnostics;
using System.Text.Json;

namespace Reign.Mcp.Tests;

public sealed class MapBarInputSafetyTests
{
    internal static string? CurrentClientArtifact()
    {
        var testOutput = new DirectoryInfo(AppContext.BaseDirectory);
        if (testOutput.Name != "out" || testOutput.Parent?.Name != "mcp-tests") return null;
        string path = Path.Combine(testOutput.Parent.Parent!.FullName, "client", "out", "ReignBeta.dll");
        return File.Exists(path) ? path : null;
    }

    [ValidatedClientFact]
    public async Task RealNativeMapBarsAndPersistedPendingRecordsPassTheIsolatedProbe()
    {
        string client = CurrentClientArtifact()!;
        string game = Environment.GetEnvironmentVariable("REIGN_BANNERLORD_PATH")!;
        string evidence = Path.Combine(new DirectoryInfo(AppContext.BaseDirectory).Parent!.FullName,
            "mapbar-input-probe", Guid.NewGuid().ToString("N"));
        string working = Path.Combine(evidence, "sandbox", "bin", "Win64_Shipping_Client");
        Directory.CreateDirectory(working);
        string shellRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0");
        var start = new ProcessStartInfo(Path.Combine(shellRoot, "powershell.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = working,
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true
        };
        // This framework-assembly probe needs only the Windows host's built-in
        // modules. Do not inherit a caller's module search path or input pipe.
        start.Environment["PSModulePath"] = Path.Combine(shellRoot, "Modules");
        foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File",
            Path.Combine(TestOptions.FindWorkspace(), "ReignMcp/tests/Reign.Mcp.Tests/Platform/MapBarInputSafetyProbe.ps1"),
            "-ClientAssembly", client, "-BannerlordPath", game, "-EvidenceDirectory", evidence })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start native assembly probe.");
        process.StandardInput.Close();
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            process.Kill(true);
            await process.WaitForExitAsync();
            string progressPath = Path.Combine(evidence, "progress.json");
            string progress = File.Exists(progressPath) ? File.ReadAllText(progressPath) : "no probe stage was recorded";
            throw new TimeoutException("Isolated map-bar assembly probe exceeded 60 seconds. Last stage: " + progress
                + ". Evidence: " + evidence + ". Output: " + (await output) + ". Error: " + (await error));
        }
        Assert.True(process.ExitCode == 0, (await output) + (await error));
        using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(evidence, "report.json")));
        Assert.True(report.RootElement.GetProperty("ok").GetBoolean());
        Assert.True(report.RootElement.GetProperty("nativeExceptionReproduced").GetBoolean());
        Assert.True(report.RootElement.GetProperty("baseMapBarPassed").GetBoolean());
        Assert.True(report.RootElement.GetProperty("pendingRoundTripPassed").GetBoolean());
        Assert.True(report.RootElement.GetProperty("nativeSaveFieldContractPassed").GetBoolean());
        if (File.Exists(Path.Combine(game, "Modules/NavalDLC/bin/Win64_Shipping_Client/NavalDLC.GauntletUI.dll")))
            Assert.True(report.RootElement.GetProperty("navalMapBarPassed").GetBoolean());
    }

    [Fact]
    public void ProductionWiresTheMapGuardPendingDeliveryAndProbeCatalog()
    {
        string root = TestOptions.FindWorkspace();
        string bootstrap = File.ReadAllText(Path.Combine(root, "ReignBeta/src/Modules/Platform/SubModule.cs"));
        Assert.Contains("ReignMapBarInputSafetyPatch.Apply();", bootstrap);
        Assert.Contains("ReignMapBarInputSafetyPatch.Unapply();", bootstrap);
        string events = File.ReadAllText(Path.Combine(root, "ReignBeta/src/Modules/WorldSimulation/Campaign/ReignSocialEventsCampaignBehavior.cs"));
        Assert.Contains("CampaignEvents.TickEvent.AddNonSerializedListener(this, OnTick)", events);
        Assert.Contains("ReignWildernessEventCompletion.RunAsync(", events);
        Assert.Contains("work => ReignMainThread.InvokeAsync(work)", events);
        Assert.Contains("ReferenceEquals(TaleWorlds.CampaignSystem.Campaign.Current, requestCampaign)", events);
        Assert.Contains("!CanStartGeneratedWildernessEvent(out _)", events);
        Assert.Contains("mainParty.CurrentSettlement != null || Settlement.CurrentSettlement != null", events);
        Assert.Contains("!(ScreenManager.FocusedLayer is SceneLayer)", events);
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reign.testing.json")));
        Assert.Equal("reign-mapbar-input-probe-v1", catalog.RootElement.GetProperty("mapBarInputSafety").GetProperty("schema").GetString());
    }

    // Server-only/focused test plans cannot claim native client coverage. Product
    // validation builds the client first and must execute this probe, not skip it.
    private sealed class ValidatedClientFactAttribute : FactAttribute
    {
        public ValidatedClientFactAttribute()
        {
            if (CurrentClientArtifact() == null || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("REIGN_BANNERLORD_PATH")))
                Skip = "Requires same-run canonical client artifact and licensed REIGN_BANNERLORD_PATH; run product validation.";
        }
    }
}
