using System.Diagnostics;
using System.Text.Json;
using Reign.Mcp.Server;

namespace Reign.Mcp.Tests;

public sealed class ConversationClockNativeTests
{
    [Fact]
    public async Task ProbeRequiresVerificationGateBeforeAnyDependency()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => ConversationClockTools.ProbeConversationClock(
            null!, TestOptions.Create(), null!, "campaign", "run", "probe", "pulse", ConversationClockTools.Confirmation));
    }

    [Fact]
    public async Task ProbeRequiresExactConfirmationBeforeAnyDependency()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => ConversationClockTools.ProbeConversationClock(
            null!, TestOptions.Create() with { AllowVerificationControl = true }, null!, "campaign", "run", "probe", "pulse", "yes"));
    }

    [Theory]
    [InlineData("", "run", "probe", "observe")]
    [InlineData("campaign", "", "probe", "observe")]
    [InlineData("campaign", "run", "", "observe")]
    [InlineData("campaign", "run", "probe", "pulse")]
    [InlineData("campaign", "run", "probe", "advance")]
    [InlineData("campaign", "run", "probe", "500")]
    public async Task InvalidProbeInputsCannotReachCampaignOrRuntime(string campaign, string run, string probe, string profile)
    {
        await Assert.ThrowsAnyAsync<ArgumentException>(() => ConversationClockTools.ProbeConversationClock(
            null!, TestOptions.Create() with { AllowVerificationControl = true }, null!, campaign, run, probe, profile, ConversationClockTools.Confirmation));
    }

    [Fact]
    public void ProbeCannotBeActivatedByNormalConversationOrConfiguration()
    {
        string root = TestOptions.FindWorkspace();
        string clientRoot = Path.Combine(root, "ReignBeta", "src");
        var users = Directory.EnumerateFiles(clientRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path).Contains("ReignConversationClockProbe", StringComparison.Ordinal))
            .Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "ReignConversationClockProbe.cs", "ReignLiveInteractionConversationClockHost.cs" }, users);
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reign.testing.json")));
        JsonElement probe = catalog.RootElement.GetProperty("conversationClockFeasibility");
        Assert.False(probe.GetProperty("productionEnabled").GetBoolean());
        Assert.False(probe.GetProperty("nativeAcceptance").GetBoolean());
        Assert.Equal(0, probe.GetProperty("maximumNativeMinutesPerLoad").GetInt32());
        Assert.Equal("reign_probe_conversation_clock", probe.GetProperty("nativeEntry").GetString());
        string host = TestSourceLocator.PartialTypeSourceText(Path.Combine(clientRoot, "Modules/WorldSimulation/Campaign/ReignLiveInteractionTestHost.cs"));
        Assert.Contains("if (await ReignMainThread.InvokeAsync(() => ConversationClockRequiresRestore)", host);
        Assert.Contains("if (ConversationClockRequiresRestore", host);
    }

    [ValidatedClockClientFact]
    public async Task LicensedNativeClockArithmeticAndTimerMismatchAreMeasuredInIsolation()
    {
        string evidence = Path.Combine(new DirectoryInfo(AppContext.BaseDirectory).Parent!.FullName,
            "conversation-clock-probe", Guid.NewGuid().ToString("N"));
        string working = Path.Combine(evidence, "sandbox", "bin", "Win64_Shipping_Client");
        Directory.CreateDirectory(working);
        string shellRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0");
        var start = new ProcessStartInfo(Path.Combine(shellRoot, "powershell.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = working,
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true
        };
        start.Environment["PSModulePath"] = Path.Combine(shellRoot, "Modules");
        foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File",
            Path.Combine(TestOptions.FindWorkspace(), "ReignMcp/tests/Reign.Mcp.Tests/Platform/ConversationClockNativeProbe.ps1"),
            "-ClientAssembly", MapBarInputSafetyTests.CurrentClientArtifact()!,
            "-BannerlordPath", Environment.GetEnvironmentVariable("REIGN_BANNERLORD_PATH")!, "-EvidenceDirectory", evidence })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start isolated clock probe.");
        process.StandardInput.Close();
        Task<string> output = process.StandardOutput.ReadToEndAsync(), error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            process.Kill(true); await process.WaitForExitAsync();
            throw new TimeoutException("Clock probe exceeded 60 seconds. Evidence: " + evidence);
        }
        Assert.True(process.ExitCode == 0, (await output) + (await error) + " Evidence: " + evidence);
        using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(evidence, "report.json")));
        JsonElement result = report.RootElement;
        Assert.True(result.GetProperty("ok").GetBoolean());
        Assert.True(result.GetProperty("checks").GetInt32() >= 220);
        Assert.Equal(500, result.GetProperty("hundredPulseMinutes").GetInt32());
        Assert.True(result.GetProperty("tickerDebtLossReproduced").GetBoolean());
        Assert.Equal(8, result.GetProperty("absoluteCatchUpCallbacks").GetInt32());
        Assert.False(result.GetProperty("nativeAcceptance").GetBoolean());
        Assert.False(result.GetProperty("timerAccountingQualified").GetBoolean());
    }

    private sealed class ValidatedClockClientFactAttribute : FactAttribute
    {
        public ValidatedClockClientFactAttribute()
        {
            if (!OperatingSystem.IsWindows() || MapBarInputSafetyTests.CurrentClientArtifact() == null
                || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("REIGN_BANNERLORD_PATH")))
                Skip = "Requires same-run canonical client artifact and licensed REIGN_BANNERLORD_PATH; no in-scene acceptance is inferred.";
        }
    }
}
