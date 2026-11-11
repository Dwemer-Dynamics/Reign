namespace Reign.Mcp.Tests;

public sealed class WarEconomyNativeHookTests
{
    [Fact]
    public void EconomyHooksWaitForTheNativeGameTextManager()
    {
        string source = File.ReadAllText(Path.Combine(TestOptions.FindWorkspace(), "ReignBeta/src/Modules/Platform/SubModule.cs"));
        string beforeGame = source[..source.IndexOf("protected override void InitializeGameStarter", StringComparison.Ordinal)];
        Assert.DoesNotContain("ReignEconomyPatches.Apply();", beforeGame);
        Assert.Contains("if (Game.Current?.GameTextManager == null)", source);
        Assert.Contains("ReignEconomyPatches.Apply();", source);
    }
    [ValidatedEconomyClientFact]
    public async Task CompiledNativeHooksPreserveNativeProductionBonusesAndPrivateUpgradeArguments()
    {
        string client = MapBarInputSafetyTests.CurrentClientArtifact()!;
        string game = Environment.GetEnvironmentVariable("REIGN_BANNERLORD_PATH")!;
        string evidence = Path.Combine(new DirectoryInfo(AppContext.BaseDirectory).Parent!.FullName,
            "war-economy-probe", Guid.NewGuid().ToString("N"));
        string shellRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0");
        var start = new System.Diagnostics.ProcessStartInfo(Path.Combine(shellRoot, "powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment["PSModulePath"] = Path.Combine(shellRoot, "Modules");
        foreach (string arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File",
            Path.Combine(TestOptions.FindWorkspace(), "ReignMcp/tests/Reign.Mcp.Tests/Features/WorldSimulation/WarEconomyHookProbe.ps1"),
            "-ClientAssembly", client, "-BannerlordPath", game, "-EvidenceDirectory", evidence })
            start.ArgumentList.Add(arg);
        using var process = System.Diagnostics.Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(true); throw new TimeoutException("War economy probe timed out: " + evidence); }
        Assert.True(process.ExitCode == 0, (await output) + (await error));
        using var report = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(evidence, "report.json")));
        Assert.True(report.RootElement.GetProperty("ok").GetBoolean());
        Assert.True(report.RootElement.GetProperty("cohortControlBinding").GetBoolean());
        Assert.True(report.RootElement.GetProperty("medicalDischargeBinding").GetBoolean());
        Assert.True(report.RootElement.GetProperty("medicalRosterConservation").GetBoolean());
        if (File.Exists(Path.Combine(game, "Modules", "NavalDLC", "bin", "Win64_Shipping_Client", "NavalDLC.dll")))
        {
            using var naval = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(evidence, "naval-funding.json")));
            Assert.True(naval.RootElement.GetProperty("binding").GetBoolean());
            Assert.Equal(15, naval.RootElement.GetProperty("hooks").GetInt32());
            Assert.True(naval.RootElement.GetProperty("nativeShipReferenceContainer").GetBoolean());
        }
        Assert.Equal(File.Exists(Path.Combine(game, "Modules", "NavalDLC", "bin", "Win64_Shipping_Client", "NavalDLC.dll")) ? 55 : 51,
            report.RootElement.GetProperty("boundHooks").GetInt32());
        Assert.Equal(64, report.RootElement.GetProperty("fundingBoundHooks").GetInt32());
        Assert.True(report.RootElement.GetProperty("fundingWalletCapacityBoundary").GetBoolean());
        Assert.Equal(32768, report.RootElement.GetProperty("fundingWalletLimit").GetInt32());
        Assert.Equal(8192, report.RootElement.GetProperty("fundingHistoryLimit").GetInt32());
        using var manifest = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(TestOptions.FindWorkspace(),
            "ReignServer/tests/ReignLiveTest/Features/WorldSimulation/war-economy-manifest.json")));
        Assert.Equal(report.RootElement.GetProperty("fundingWalletLimit").GetInt32(),
            manifest.RootElement.GetProperty("limits").GetProperty("fundingTraceWallets").GetInt32());
        Assert.Equal(6, manifest.RootElement.GetProperty("limits").GetProperty("fundingTraceVersion").GetInt32());
        Assert.True(report.RootElement.GetProperty("populationObserverBinding").GetBoolean());
        Assert.True(report.RootElement.GetProperty("fundingOriginBinding").GetBoolean());
        Assert.True(report.RootElement.GetProperty("leaderlessWageStoreBinding").GetBoolean());
        Assert.Equal(File.Exists(Path.Combine(game, "Modules", "NavalDLC", "bin", "Win64_Shipping_Client", "NavalDLC.dll")), report.RootElement.GetProperty("civilianFishingBinding").GetBoolean());
        Assert.True(report.RootElement.GetProperty("aiSpawnLocalityBinding").GetBoolean());
        Assert.True(report.RootElement.GetProperty("prisonerTransferBinding").GetBoolean());
        Assert.Equal(2, report.RootElement.GetProperty("nativeFinanceObservationSites").GetInt32());
        Assert.True(report.RootElement.GetProperty("transpilerPreservedNativeInstructions").GetBoolean());
        Assert.Equal(File.Exists(Path.Combine(game, "Modules", "NavalDLC", "bin", "Win64_Shipping_Client", "NavalDLC.dll"))
            ? "verified_binding" : "unavailable", report.RootElement.GetProperty("navalRoute").GetString());
        Assert.Equal(9, report.RootElement.GetProperty("savedAssistanceFields").GetInt32());
        Assert.True(report.RootElement.GetProperty("persistedAidObserved").GetBoolean());
        Assert.True(report.RootElement.GetProperty("wagePayerBinding").GetBoolean());
        Assert.True(report.RootElement.GetProperty("upgradePayerBinding").GetBoolean());
        Assert.True(report.RootElement.GetProperty("componentHashesObserved").GetBoolean());
        Assert.True(report.RootElement.GetProperty("tariffCollectionBinding").GetBoolean());
        Assert.True(report.RootElement.GetProperty("purchaseLoopNegativeControls").GetBoolean());
        Assert.True(report.RootElement.GetProperty("purchaseStockGate").GetBoolean());
        Assert.True(report.RootElement.GetProperty("nativeFoodCensusBounded").GetBoolean());
        Assert.True(report.RootElement.GetProperty("nativeSubsistenceForecast").GetBoolean());
        Assert.True(report.RootElement.GetProperty("reliefPolicyProbe").GetBoolean());
        Assert.True(report.RootElement.GetProperty("garrisonStackRounding").GetBoolean());
        Assert.True(report.RootElement.GetProperty("pressureParsingBounded").GetBoolean());
        Assert.True(report.RootElement.GetProperty("nativeRewriteNegativeControls").GetBoolean());
        Assert.True(report.RootElement.GetProperty("governmentHearthFlat").GetBoolean());
        Assert.Equal(48, report.RootElement.GetProperty("governmentHearthFlatCases").GetInt32());
        Assert.True(report.RootElement.GetProperty("unpatched").GetBoolean());
    }

    private sealed class ValidatedEconomyClientFactAttribute : FactAttribute
    {
        public ValidatedEconomyClientFactAttribute()
        {
            if (!OperatingSystem.IsWindows() || MapBarInputSafetyTests.CurrentClientArtifact() == null
                || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("REIGN_BANNERLORD_PATH")))
                Skip = "Requires the same-run client artifact and licensed Bannerlord assemblies.";
        }
    }

}
