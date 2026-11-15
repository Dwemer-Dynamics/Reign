using System.Diagnostics;
using System.Text.Json;

namespace Reign.Mcp.Tests;

public sealed class RbmNativeEconomicsTests
{
    [NativeEconomicTheory]
    [InlineData("Campaign")]
    [InlineData("CampaignStoryMode")]
    public async Task NativeItemsAndCraftedDesignsKeepTheirEconomicValuesWithDifferentCombatData(string gameType)
    {
        string evidence = Path.Combine(new DirectoryInfo(AppContext.BaseDirectory).Parent!.FullName,
            "rbm-native-economics", gameType + "-" + Guid.NewGuid().ToString("N"));
        string temporary = Path.Combine(evidence, "compiler-temp");
        Directory.CreateDirectory(temporary);
        string shell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0");
        foreach (string mode in new[] { "Reference", "Patched" }) {
        var start = new ProcessStartInfo(Path.Combine(shell, "powershell.exe")) {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true
        };
        start.Environment["PSModulePath"] = Path.Combine(shell, "Modules");
        start.Environment["TEMP"] = temporary; start.Environment["TMP"] = temporary;
        foreach (string arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File",
            Path.Combine(TestOptions.FindWorkspace(), "ReignMcp/tests/Reign.Mcp.Tests/Platform/RbmNativeEconomicsProbe.ps1"),
            "-ClientAssembly", MapBarInputSafetyTests.CurrentClientArtifact()!,
            "-BannerlordPath", Environment.GetEnvironmentVariable("REIGN_BANNERLORD_PATH")!,
            "-EvidenceDirectory", evidence, "-GameType", gameType, "-Mode", mode }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        process.StandardInput.Close();
        Task<string> output = process.StandardOutput.ReadToEndAsync(), error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) {
            process.Kill(true); await process.WaitForExitAsync();
            throw new TimeoutException("Native economics " + mode + " probe timed out: " + evidence);
        }
        Assert.True(process.ExitCode == 0, (await output) + (await error) + " Evidence: " + evidence);
        }
        using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(evidence, "report.json")));
        Assert.True(report.RootElement.GetProperty("ok").GetBoolean());
        Assert.True(report.RootElement.GetProperty("nativeItemCount").GetInt32() >= 1000);
        Assert.True(report.RootElement.GetProperty("nativeCraftChecks").GetInt32() >= 12);
        Assert.True(report.RootElement.GetProperty("physicalChecks").GetInt32() >= 2);
        Assert.True(report.RootElement.GetProperty("registryPreserved").GetBoolean());
    }

    private sealed class NativeEconomicTheoryAttribute : TheoryAttribute
    {
        public NativeEconomicTheoryAttribute()
        {
            if (!OperatingSystem.IsWindows() || MapBarInputSafetyTests.CurrentClientArtifact() == null
                || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("REIGN_BANNERLORD_PATH")))
                Skip = "Requires same-run canonical client artifact and licensed native assemblies/XML on Windows.";
        }
    }
}
