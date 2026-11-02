using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace Reign.Mcp.Tests.Features.WorldSimulation;

public sealed class NpcMeetingTeleportHookTests
{
    [ValidatedClientFact]
    public async Task Queued_teleport_guard_binds_before_native_removal_and_preserves_native_model_dispatch()
    {
        string client = MapBarInputSafetyTests.CurrentClientArtifact()!;
        string game = Environment.GetEnvironmentVariable("REIGN_BANNERLORD_PATH")!;
        string evidence = Path.Combine(new DirectoryInfo(AppContext.BaseDirectory).Parent!.FullName,
            "npc-meeting-teleport-probe", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(evidence);
        string shellRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0");
        var start = new ProcessStartInfo(Path.Combine(shellRoot, "powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment["PSModulePath"] = Path.Combine(shellRoot, "Modules");
        foreach (string arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File",
            Path.Combine(TestOptions.FindWorkspace(), "ReignMcp/tests/Reign.Mcp.Tests/Features/WorldSimulation/NpcMeetingTeleportHookProbe.ps1"),
            "-ClientAssembly", client, "-BannerlordPath", game, "-EvidenceDirectory", evidence })
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(true); throw new TimeoutException("Meeting teleport probe timed out: " + evidence); }
        Assert.True(process.ExitCode == 0, (await output) + (await error));
        using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(evidence, "report.json")));
        var root = report.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal(13, root.GetProperty("installedHookCount").GetInt32());
        Assert.True(root.GetProperty("rescuePrisonerHookBound").GetBoolean());
        Assert.True(root.GetProperty("rescuePrefixNoCampaignNoOp").GetBoolean());
        Assert.True(root.GetProperty("rescueHookRemoved").GetBoolean());
        Assert.True(root.GetProperty("replacementBeforeQueueRemoval").GetBoolean());
        Assert.True(root.GetProperty("preservedLabelsAndBlocks").GetBoolean());
        Assert.True(root.GetProperty("unsupportedShapesRejected").GetBoolean());
        Assert.Equal(2, root.GetProperty("customModelDispatchChecks").GetInt32());
        Assert.Equal(4, root.GetProperty("returnLeaseChecks").GetInt32());
        Assert.Equal(2048, root.GetProperty("announcementHoldGuardChecks").GetInt32());
        Assert.Equal(18, root.GetProperty("meetingFeatureGateChecks").GetInt32());
        Assert.Equal(4, root.GetProperty("shipTradeContextChecks").GetInt32());
        bool navalDlc = File.Exists(Path.Combine(game, "Modules/NavalDLC/bin/Win64_Shipping_Client/NavalDLC.dll"));
        Assert.Equal(navalDlc, root.GetProperty("shipTradeDlcAvailable").GetBoolean());
        if (navalDlc)
        {
            Assert.True(root.GetProperty("shipTradeHookBound").GetBoolean());
            Assert.Equal(2, root.GetProperty("shipTradeShapeRejections").GetInt32());
            Assert.True(root.GetProperty("shipTradeInstructionsPreserved").GetBoolean());
            Assert.True(root.GetProperty("shipTradeHookRemoved").GetBoolean());
        }
        Assert.True(root.GetProperty("rulerWaitsForReturnLease").GetBoolean());
        Assert.Equal(0, root.GetProperty("providerCalls").GetInt32());
        Assert.Equal(0, root.GetProperty("campaignMutations").GetInt32());
    }

    private sealed class ValidatedClientFactAttribute : FactAttribute
    {
        public ValidatedClientFactAttribute()
        {
            if (!OperatingSystem.IsWindows() || MapBarInputSafetyTests.CurrentClientArtifact() == null
                || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("REIGN_BANNERLORD_PATH")))
                Skip = "Requires the same-run canonical client artifact and licensed Bannerlord assemblies.";
        }
    }
}
