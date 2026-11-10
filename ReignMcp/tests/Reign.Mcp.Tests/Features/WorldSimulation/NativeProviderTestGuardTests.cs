using System.Text.Json;
using Reign.Mcp.Server;
using ReignBetaServer;

namespace Reign.Mcp.Tests;

public sealed class NativeProviderTestGuardTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "reign-provider-guard-" + Guid.NewGuid().ToString("N"));
    private readonly string _task = Guid.NewGuid().ToString();
    private string Marker => Path.Combine(_root, "tests", "war-economy", "provider-free.json");
    public NativeProviderTestGuardTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);

    private void Arm()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Marker)!);
        File.WriteAllText(Marker, JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["schema"] = NativeProviderTestGuard.Schema, ["runId"] = "run", ["campaignId"] = "campaign",
            ["timelineId"] = "timeline", ["taskId"] = _task, ["currentSaveName"] = "owned-current",
            ["baselineSaveName"] = "immutable-original"
        }));
    }

    [Fact]
    public void NormalRuntimeRemainsUnarmedButCannotLaterClaimZeroDispatches()
    {
        var guard = new NativeProviderTestGuard(_root);
        guard.BeforeDispatch("http_text_or_image");
        Assert.False((bool)guard.Snapshot()["armed"]!);
        Arm();
        Assert.Throws<NativeProviderTestBlockedException>(() => guard.BeforeDispatch("codex_text"));
        Assert.Equal(1L, guard.Snapshot()["allowedDispatchesSinceProcessStart"]);
        Assert.False((bool)guard.Snapshot()["zeroDispatchEvidence"]!);
    }

    [Fact]
    public void EveryGenerationBoundaryBlocksBeforeItsOutboundCallbackAndPersistsAttempts()
    {
        Arm();
        var guard = new NativeProviderTestGuard(_root);
        Assert.True((bool)guard.Snapshot()["cleanProviderFreeRun"]!);
        int dispatched = 0;
        foreach (string category in new[] { "http_text_or_image", "streaming_text", "codex_text", "codex_image",
            "atlas_reference_upload", "image_prediction_poll", "image_download" })
            Assert.Throws<NativeProviderTestBlockedException>(() => { guard.BeforeDispatch(category); dispatched++; });
        Assert.Equal(0, dispatched);
        var state = guard.Snapshot();
        Assert.True((bool)state["zeroDispatchEvidence"]!);
        Assert.False((bool)state["cleanProviderFreeRun"]!); // blocked attempts fail the campaign precondition.
        string evidence = Assert.Single(Directory.GetFiles(Path.GetDirectoryName(Marker)!, "provider-free-attempts-*.json"));
        using var report = JsonDocument.Parse(File.ReadAllText(evidence));
        Assert.Equal(7, report.RootElement.GetProperty("blockedAttemptsSinceProcessStart").GetInt64());
        Assert.Equal(0, report.RootElement.GetProperty("allowedDispatchesSinceProcessStart").GetInt64());
        Assert.DoesNotContain("prompt", File.ReadAllText(evidence), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("malformed")] [InlineData("oversized")] [InlineData("directory")] [InlineData("missing_identity")]
    public void UnverifiableMarkerFailsClosed(string kind)
    {
        Arm();
        File.Delete(Marker);
        if (kind == "directory") Directory.CreateDirectory(Marker);
        else File.WriteAllText(Marker, kind == "oversized" ? new string('x', 16385) : kind == "missing_identity" ? "{}" : "{broken");
        var guard = new NativeProviderTestGuard(_root);
        Assert.Throws<NativeProviderTestBlockedException>(() => guard.BeforeDispatch("http_text_or_image"));
        Assert.True((bool)guard.Snapshot()["armed"]!);
        Assert.False((bool)guard.Snapshot()["zeroDispatchEvidence"]!);
    }

    [Fact]
    public void MissingRuntimeRootCannotBeMistakenForAnUnarmedRuntime()
    {
        var guard = new NativeProviderTestGuard(Path.Combine(_root, "unavailable"));
        Assert.Throws<NativeProviderTestBlockedException>(() => guard.BeforeDispatch("codex_text"));
        Assert.False((bool)guard.Snapshot()["zeroDispatchEvidence"]!);
    }

    [Fact]
    public void MarkerRemovalCannotEnableAnObservedProcessAndRestartKeepsEarlierEvidence()
    {
        Arm();
        var first = new NativeProviderTestGuard(_root);
        Assert.True((bool)first.Snapshot()["armed"]!);
        File.Delete(Marker);
        Assert.Throws<NativeProviderTestBlockedException>(() => first.BeforeDispatch("codex_text"));
        Arm();
        var restarted = new NativeProviderTestGuard(_root);
        Assert.NotEqual(first.Snapshot()["serverInstance"], restarted.Snapshot()["serverInstance"]);
        Assert.Throws<NativeProviderTestBlockedException>(() => restarted.BeforeDispatch("codex_text"));
        Assert.Equal(2, Directory.GetFiles(Path.GetDirectoryName(Marker)!, "provider-free-attempts-*.json").Length);
    }

    [Fact]
    public void OnlyFullyStoppedManagedServerAndExactGuardOwnerPermitMutation()
    {
        const string stopped = "LoadState=loaded\nActiveState=inactive\nSubState=dead\nMainPID=0\n";
        TestingTools.RequireEconomyServerStopped(true, stopped);
        foreach (string output in new[] { "", stopped.Replace("inactive", "active"), stopped.Replace("dead", "failed"), stopped.Replace("MainPID=0", "MainPID=42") })
            Assert.Throws<InvalidOperationException>(() => TestingTools.RequireEconomyServerStopped(true, output));
        Assert.Throws<InvalidOperationException>(() => TestingTools.RequireEconomyServerStopped(false, stopped));
        Arm();
        using var marker = JsonDocument.Parse(File.ReadAllText(Marker));
        TestingTools.RequireEconomyGuardOwner(marker.RootElement, "run", "campaign", _task);
        Assert.Throws<InvalidOperationException>(() => TestingTools.RequireEconomyGuardOwner(marker.RootElement, "other-run", "campaign", _task));
        Assert.Throws<InvalidOperationException>(() => TestingTools.RequireEconomyGuardOwner(marker.RootElement, "run", "other-campaign", _task));
        Assert.Throws<InvalidOperationException>(() => TestingTools.RequireEconomyGuardOwner(marker.RootElement, "run", "campaign", Guid.NewGuid().ToString()));
    }

    [Theory]
    [InlineData("Bannerlord", true)] [InlineData("Bannerlord.Native", true)] [InlineData("Bannerlord.BLSE.LauncherEx", true)]
    [InlineData("TaleWorlds.MountAndBlade.Launcher", true)] [InlineData("ReignServer", false)]
    public void NativeProcessNamesPreventGuardChanges(string name, bool blocks) => Assert.Equal(blocks, TestingTools.IsEconomyNativeGameProcess(name));

    [Fact]
    public void DistroMustAlreadyBeRunningAndPathsStayUnderItsInstallation()
    {
        Assert.True(TestingTools.EconomyDistroIsRunning("R\0e\0i\0g\0n\0S\0e\0r\0v\0e\0r\0\r\0\n", "ReignServer"));
        Assert.False(TestingTools.EconomyDistroIsRunning("ReignServerOther\nUbuntu\n", "ReignServer"));
        var location = TestingTools.ValidateEconomyProviderGuardLocation("dwemerdistro-wsl", "ReignServer", @"\\wsl.localhost\ReignServer\var\www\html\ReignServer\data");
        Assert.EndsWith(Path.Combine("data", "tests", "war-economy"), location.Directory);
        using var installed = JsonDocument.Parse(JsonSerializer.Serialize(new {
            serverMode = "dwemerdistro-wsl", wslDistro = "ReignServer",
            dataRoot = @"\\wsl.localhost\ReignServer\var\www\html\ReignServer\data",
            serverRoot = @"\\wsl.localhost\ReignServer\var\www\html\ReignServer\runtime\current" }));
        Assert.Equal(location, TestingTools.ParseEconomyProviderGuardLocation(installed.RootElement));
        Assert.Throws<InvalidOperationException>(() => TestingTools.ValidateEconomyProviderGuardLocation("dwemerdistro-wsl", "ReignServer\n", @"\\wsl.localhost\ReignServer\var\www\html\ReignServer\data"));
        foreach (string root in new[] { @"C:\data", @"\\wsl.localhost\Other\data", @"\\wsl.localhost\ReignServer\data\..\other" })
            Assert.Throws<InvalidOperationException>(() => TestingTools.ValidateEconomyProviderGuardLocation("dwemerdistro-wsl", "ReignServer", root));
    }

    [Fact]
    public async Task MutationRejectsDisabledControlOrUnknownOperationsBeforeReadingInstallation()
    {
        var disabled = TestOptions.Create() with { AllowVerificationControl = false };
        await Assert.ThrowsAsync<InvalidOperationException>(() => TestingTools.SetWarEconomyProviderGuard(disabled, null!, null!, "campaign", "run", _task));
        var enabled = disabled with { AllowVerificationControl = true };
        await Assert.ThrowsAsync<ArgumentException>(() => TestingTools.SetWarEconomyProviderGuard(enabled, null!, null!, "campaign", "run", _task,
            operation: "start_server", confirmation: "manage Reign provider-free economy test guard"));
    }

    [Fact]
    public void EveryOutboundGenerationAdapterAndStatusKeepTheGuardBinding()
    {
        string root = Path.Combine(TestOptions.FindWorkspace(), "ReignServer", "src", "Modules");
        foreach (var entry in new[] { ("Platform/Program.cs", "http_text_or_image"), ("Platform/ProviderMiddleware.cs", "streaming_text"),
            ("Platform/CodexAppServerProvider.cs", "codex_text"), ("Portraits/CodexImageProvider.cs", "codex_image"),
            ("Portraits/PortraitGeneration.cs", "atlas_reference_upload"), ("Portraits/PortraitGeneration.cs", "image_prediction_poll"),
            ("Portraits/PortraitGeneration.cs", "image_download") })
            Assert.Contains("ProviderTestGuard.BeforeDispatch(\"" + entry.Item2 + "\")", File.ReadAllText(Path.Combine(root, entry.Item1)));
        Assert.Contains("[\"nativeProviderTestGuard\"] = ProviderTestGuard.Snapshot()", File.ReadAllText(Path.Combine(root, "Platform", "ProviderMiddleware.cs")));
    }

    [Fact]
    public void GuardOwnerAndDigestRemainPairedWhenTheMarkerIsReplaced()
    {
        Arm();
        byte[] original = File.ReadAllBytes(Marker);
        using var snapshot = TestingTools.ReadBoundedEconomyGuard(Marker, out string digest);
        File.WriteAllText(Marker, "{\"taskId\":\"another-owner\"}");
        Assert.Equal(_task, snapshot.RootElement.GetProperty("taskId").GetString());
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(original)).ToLowerInvariant(), digest);
        Assert.NotEqual(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Marker))).ToLowerInvariant(), digest);
    }

    [Theory]
    [InlineData(16384, true)] [InlineData(16385, false)]
    public void GuardSnapshotEnforcesTheExactByteLimit(int length, bool allowed)
    {
        Arm();
        string content = File.ReadAllText(Marker);
        File.WriteAllText(Marker, content + new string(' ', length - System.Text.Encoding.UTF8.GetByteCount(content)));
        if (!allowed)
            Assert.Throws<InvalidOperationException>(() => TestingTools.ReadBoundedEconomyGuard(Marker, out _));
        else
        {
            using var snapshot = TestingTools.ReadBoundedEconomyGuard(Marker, out string digest);
            Assert.Equal(_task, snapshot.RootElement.GetProperty("taskId").GetString());
            Assert.Equal(64, digest.Length);
        }
    }
}
