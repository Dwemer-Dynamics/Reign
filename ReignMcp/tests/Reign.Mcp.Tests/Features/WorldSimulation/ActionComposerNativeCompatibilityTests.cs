using System.Diagnostics;
using System.Text.Json;

namespace Reign.Mcp.Tests.Features.WorldSimulation;

public sealed class ActionComposerNativeCompatibilityTests
{
    [ValidatedClientFact]
    public async Task CompiledEditorAndCalibrationBindToTheInstalledNativeVectorAssembly()
    {
        string client = MapBarInputSafetyTests.CurrentClientArtifact()!;
        string game = Environment.GetEnvironmentVariable("REIGN_BANNERLORD_PATH")!;
        string evidence = Path.Combine(new DirectoryInfo(AppContext.BaseDirectory).Parent!.FullName,
            "action-composer-native-probe", Guid.NewGuid().ToString("N"));
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
            Path.Combine(TestOptions.FindWorkspace(), "ReignMcp/tests/Reign.Mcp.Tests/Features/WorldSimulation/ActionComposerNativeCompatibilityProbe.ps1"),
            "-ClientAssembly", client, "-BannerlordPath", game, "-EvidenceDirectory", evidence })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start native editor assembly probe.");
        process.StandardInput.Close();
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            process.Kill(true);
            await process.WaitForExitAsync();
            throw new TimeoutException("Native editor assembly probe exceeded 60 seconds. Evidence: " + evidence
                + ". Output: " + (await output) + ". Error: " + (await error));
        }
        Assert.True(process.ExitCode == 0, (await output) + (await error));
        using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(evidence, "report.json")));
        Assert.True(report.RootElement.GetProperty("ok").GetBoolean());
        Assert.True(report.RootElement.GetProperty("visibleTextFieldsCompatible").GetBoolean());
        Assert.Equal(6, report.RootElement.GetProperty("mailboxEditorChecks").GetInt32());
        Assert.Equal(10, report.RootElement.GetProperty("mailboxPortraitChecks").GetInt32());
        Assert.Equal(4, report.RootElement.GetProperty("heroKnowledgeChecks").GetInt32());
        Assert.Equal(29, report.RootElement.GetProperty("managedSelectionChecks").GetInt32());
        Assert.Equal(2, report.RootElement.GetProperty("custodySnapshotChecks").GetInt32());
        Assert.Equal(4, report.RootElement.GetProperty("nativeObservationChecks").GetInt32());
        Assert.Equal(2, report.RootElement.GetProperty("bribeObservationChecks").GetInt32());
        Assert.Equal(4, report.RootElement.GetProperty("nativeFaultChecks").GetInt32());
        Assert.Equal(6, report.RootElement.GetProperty("pendingOfferChecks").GetInt32());
        Assert.Equal(11, report.RootElement.GetProperty("nativeSafetyChecks").GetInt32());
        Assert.Equal(16, report.RootElement.GetProperty("allianceDurationChecks").GetInt32());
        Assert.Equal(8, report.RootElement.GetProperty("goldObservationChecks").GetInt32());
        Assert.Equal(16, report.RootElement.GetProperty("tributeObservationChecks").GetInt32());
        Assert.Equal(10, report.RootElement.GetProperty("romanceObservationChecks").GetInt32());
        Assert.Equal(10, report.RootElement.GetProperty("rebellionPreparationLifecycleChecks").GetInt32());
        Assert.Equal(2, report.RootElement.GetProperty("rebellionSavedLoadChecks").GetInt32());
        Assert.Equal("System.Numerics.Vectors", report.RootElement.GetProperty("nativeVectorAssemblyName").GetString());
        Assert.Contains(report.RootElement.GetProperty("preparedMethods").EnumerateArray(),
            method => method.GetString() == "ReignBeta.UI.Widgets.ReignActionEditableTextWidget.OnLateUpdate");
        Assert.Contains(report.RootElement.GetProperty("preparedMethods").EnumerateArray(),
            method => method.GetString() == "ReignBeta.Actions.ReignNativeActionConsole.ObserveFactionPair");
        Assert.Contains(report.RootElement.GetProperty("preparedMethods").EnumerateArray(),
            method => method.GetString() == "ReignBeta.Campaign.ReignLiveInteractionTestHost.ApplyStructuredPlayerSettlementEncounterFixture");
    }

    [Fact]
    public void NativeCompatibilityProbeIsCatalogedWithItsIsolationAndEvidenceLimits()
    {
        string root = TestOptions.FindWorkspace();
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reign.testing.json")));
        var probe = catalog.RootElement.GetProperty("structuredActionAgreements").GetProperty("nativeUiCompatibility");
        Assert.Equal("reign-action-composer-native-probe-v1", probe.GetProperty("schema").GetString());
        Assert.True(File.Exists(Path.Combine(root, probe.GetProperty("probe").GetString()!)));
        foreach (string path in new[] { probe.GetProperty("procedure").GetString()!,
            "ReignMcp/docs/tool-catalog.md", "ReignMcp/docs/security-model.md" })
            Assert.Contains("reign-action-composer-native-probe-v1", File.ReadAllText(Path.Combine(root, path)));
    }

    [Fact]
    public void DiplomaticGoldObservationIsCatalogedWithoutNewRuntimeAuthority()
    {
        string root = TestOptions.FindWorkspace();
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reign.testing.json")));
        var observation = catalog.RootElement.GetProperty("structuredActionAgreements").GetProperty("diplomaticGoldObservations");
        const string schema = "reign-diplomacy-gold-observation-v1";
        Assert.Equal(schema, observation.GetProperty("schema").GetString());
        foreach (string path in new[] { "docs/agent/testing/FEATURE_HARNESSES.md", "ReignMcp/docs/tool-catalog.md", "ReignMcp/docs/security-model.md" })
            Assert.Contains(schema, File.ReadAllText(Path.Combine(root, path)));
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
