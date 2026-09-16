using System.Diagnostics;
using System.Text.Json;

namespace Reign.Mcp.Tests;

public sealed class RelationshipThroughputContractTests
{
    [Fact]
    public void ProviderWaitCatalogRequiresSnapshotAndStaleResponseProof()
    {
        string root = TestOptions.FindWorkspace();
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reign.testing.json")));
        var scope = json.RootElement.GetProperty("campaignProviderWait");
        Assert.Equal("contracts", scope.GetProperty("suite").GetString());
        Assert.Equal("contracts.campaign_provider_wait", scope.GetProperty("check").GetString());
        Assert.False(scope.GetProperty("providerCalls").GetBoolean());
        var cases = scope.GetProperty("cases").EnumerateArray().Select(x => x.GetString()).ToList();
        Assert.Contains("snapshot_during_success", cases);
        Assert.Contains("restore_during_success", cases);
        Assert.Contains("restore_during_failure", cases);
        Assert.Contains("snapshot_during_failure", cases);
        Assert.True(File.Exists(Path.Combine(root, scope.GetProperty("procedure").GetString()!)));
        string verifier = File.ReadAllText(Path.Combine(root, "ReignBetaServer/src/Modules/Platform/VerificationLab.cs"));
        Assert.Contains("RunCampaignProviderWaitSelfTests()", verifier);
    }

    [Fact]
    public void ReplayCatalogPreservesMeasuredBudgetAndIsolatedRoutes()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(TestOptions.FindWorkspace(), "reign.testing.json")));
        var replay = json.RootElement.GetProperty("relationshipPerformance").GetProperty("replay");
        Assert.Equal("reign_relationship_replay_v3", replay.GetProperty("schema").GetString());
        Assert.Equal(new[] { "original", "candidate-equivalence", "candidate" },
            replay.GetProperty("routes").EnumerateArray().Select(x => x.GetString()).ToArray());
        Assert.Equal(100, replay.GetProperty("measuredDays").GetInt32());
        Assert.Equal(2000, replay.GetProperty("arrivalIntervalMs").GetInt32());
        Assert.Equal("reign_run_offline_verification", replay.GetProperty("tool").GetString());
        Assert.Equal("relationship_throughput", replay.GetProperty("acceptanceSuite").GetString());
        Assert.Contains("__relationship_replay_", replay.GetProperty("cleanup").GetString());
        var acceptance = json.RootElement.GetProperty("relationshipPerformance").GetProperty("launchAcceptance");
        Assert.Equal(1, acceptance.GetProperty("p95ServiceSecondsExclusive").GetInt32());
        Assert.True(acceptance.GetProperty("deploymentRequiredForNativeProof").GetBoolean());
    }

    [Fact]
    public void TransportProbeIsBoundedReadOnlyAndOutsideTimedRoutes()
    {
        string root = TestOptions.FindWorkspace();
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reign.testing.json")));
        var probe = json.RootElement.GetProperty("relationshipPerformance").GetProperty("transportProbe");
        Assert.Equal("reign_relationship_transport_probe_v2", probe.GetProperty("schema").GetString());
        Assert.True(probe.GetProperty("readOnly").GetBoolean());
        Assert.True(probe.GetProperty("loopbackOnly").GetBoolean());
        Assert.Equal(580, probe.GetProperty("maximumRequests").GetInt32());
        Assert.Equal(new[] { "synchronous", "asynchronous" },
            probe.GetProperty("executionModes").EnumerateArray().Select(x => x.GetString()).ToArray());
        Assert.Equal(30000, probe.GetProperty("requestBudgetMs").GetInt32());
        string source = File.ReadAllText(Path.Combine(root, "ReignBetaServer/src/Modules/Relationships/RelationshipThroughputReplay.cs"));
        Assert.Contains("options.IsValidation", source);
        Assert.Contains("IPAddress.IsLoopback(address)", source);
        Assert.Contains("SELECT length(@payload);", source);
        Assert.Contains("command.ExecuteScalarAsync().GetAwaiter().GetResult()", source);
        Assert.DoesNotContain("SslMode.Disable", source);
        Assert.Contains("inputs.Campaign != campaign", source);
        Assert.Contains("RelationshipReplayConceptions = null", source);
        Assert.True(source.IndexOf("Json.Serialize(ProbeRelationshipDatabaseTransport())", StringComparison.Ordinal)
            < source.IndexOf("RunRelationshipReplayDay(routeInputs[0])", StringComparison.Ordinal));
    }

    [Fact]
    public void DatabasePreparationDefaultsToPlanAndPreservesSource()
    {
        if (!OperatingSystem.IsWindows()) return;
        var result = RunPreparation();
        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Equal("Plan", json.RootElement.GetProperty("mode").GetString());
        Assert.False(json.RootElement.GetProperty("cutoverPerformed").GetBoolean());
        Assert.True(json.RootElement.GetProperty("originalDatabaseRetained").GetBoolean());
        Assert.Equal("Reign", json.RootElement.GetProperty("targetDistro").GetString());
        Assert.Equal(5433, json.RootElement.GetProperty("targetPort").GetInt32());
    }

    [Theory]
    [InlineData("Provision")]
    [InlineData("Backup")]
    [InlineData("Restore")]
    [InlineData("Verify")]
    public void DatabaseOperationsRefuseMissingModeConfirmation(string mode)
    {
        if (!OperatingSystem.IsWindows()) return;
        var result = RunPreparation("-Mode", mode);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Explicit mode confirmation", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void SourceAndTargetCannotAliasEvenInPlan()
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.NotEqual(0, RunPreparation("-SourceDistro", "Reign").ExitCode);
    }

    private static (int ExitCode, string Output, string Error) RunPreparation(params string[] arguments)
    {
        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-File",
            Path.Combine(TestOptions.FindWorkspace(), "ReignMcp", "scripts", "prepare-reign-database.ps1") }.Concat(arguments))
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30_000)) { process.Kill(); throw new TimeoutException("Preparation plan did not return promptly."); }
        return (process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
    }
}
