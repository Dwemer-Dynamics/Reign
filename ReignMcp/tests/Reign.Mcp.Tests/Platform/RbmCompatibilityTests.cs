using System.Diagnostics;
using System.Text.Json;

namespace Reign.Mcp.Tests;

public sealed class RbmCompatibilityTests
{
    [RbmClientFact]
    public async Task ProductionPolicyPreservesPreferencesAndCompetingHarmonyOwners()
    {
        using var report = await RunProbe("Policy");
        Assert.True(report.RootElement.GetProperty("ok").GetBoolean());
        Assert.True(report.RootElement.GetProperty("checks").GetInt32() >= 40);
        Assert.Equal(24, report.RootElement.GetProperty("disabledSettings").GetInt32());
        Assert.Equal(11, report.RootElement.GetProperty("suppressionContracts").GetInt32());
    }

    [SupportedRbmBindingsFact]
    public async Task RealInstalledRbmBindingsAndDefaultGenerationPreservePreferences()
    {
        using var report = await RunProbe("Bindings");
        Assert.True(report.RootElement.GetProperty("ok").GetBoolean());
        Assert.True(report.RootElement.GetProperty("autoResolveGateVerified").GetBoolean());
        Assert.True(report.RootElement.GetProperty("startupCallerVerified").GetBoolean());
        Assert.True(report.RootElement.GetProperty("legacyCallerFailureReproduced").GetBoolean());
        Assert.True(report.RootElement.GetProperty("checks").GetInt32() >= 30);
    }

    private static async Task<JsonDocument> RunProbe(string mode)
    {
        string client = MapBarInputSafetyTests.CurrentClientArtifact()!;
        string game = Environment.GetEnvironmentVariable("REIGN_BANNERLORD_PATH")!;
        string evidence = Path.Combine(new DirectoryInfo(AppContext.BaseDirectory).Parent!.FullName,
            "rbm-compatibility", Guid.NewGuid().ToString("N"));
        string temporary = Path.Combine(evidence, "compiler-temp");
        Directory.CreateDirectory(temporary);
        string shell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0");
        var start = new ProcessStartInfo(Path.Combine(shell, "powershell.exe")) {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true
        };
        start.Environment["PSModulePath"] = Path.Combine(shell, "Modules");
        start.Environment["TEMP"] = temporary;
        start.Environment["TMP"] = temporary;
        foreach (string arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File",
            Path.Combine(TestOptions.FindWorkspace(), "ReignMcp/tests/Reign.Mcp.Tests/Platform/RbmCompatibilityProbe.ps1"),
            "-ClientAssembly", client, "-BannerlordPath", game, "-EvidenceDirectory", evidence, "-Mode", mode }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        process.StandardInput.Close();
        Task<string> output = process.StandardOutput.ReadToEndAsync(), error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(75));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) {
            process.Kill(true); await process.WaitForExitAsync();
            throw new TimeoutException("RBM policy probe timed out: " + evidence);
        }
        Assert.True(process.ExitCode == 0, (await output) + (await error) + " Evidence: " + evidence);
        return JsonDocument.Parse(File.ReadAllText(Path.Combine(evidence, "report.json")));
    }

    private sealed class SupportedRbmBindingsFactAttribute : FactAttribute
    {
        public SupportedRbmBindingsFactAttribute()
        {
            string? game = Environment.GetEnvironmentVariable("REIGN_BANNERLORD_PATH");
            string file = Path.Combine(game ?? "", "Modules/RBM/bin/Win64_Shipping_Client/RBM.dll");
            if (!OperatingSystem.IsWindows() || MapBarInputSafetyTests.CurrentClientArtifact() == null || !File.Exists(file)
                || Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file))) != "456BA61917F3AD848DBD9FC3254A070D8ADD38FC0BF341FFCAE3855846B34E60")
                Skip = "Requires the audited installed RBM binary and same-run native client artifact.";
        }
    }

    private sealed class RbmClientFactAttribute : FactAttribute
    {
        public RbmClientFactAttribute()
        {
            if (!OperatingSystem.IsWindows() || MapBarInputSafetyTests.CurrentClientArtifact() == null
                || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("REIGN_BANNERLORD_PATH")))
                Skip = "Requires same-run canonical client artifact and licensed Bannerlord dependencies on Windows.";
        }
    }
}
