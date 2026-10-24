using ReignBeta.Campaign;
using System.Text.Json;

namespace Reign.Mcp.Tests;

public sealed class PassiveWorldAdvanceBoundaryTests
{
    private static readonly DateTime Now = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void DelayedControlRequestCannotKeepTheOwnedClockPastItsTarget()
    {
        var campaign = new object();
        var pendingNetworkRequest = new TaskCompletionSource<bool>();
        var boundary = new ReignPassiveWorldAdvanceBoundary(campaign, "owned", 91195.25, Now.AddMinutes(20));
        Assert.False(boundary.Observe(campaign, "owned", 91195.24, Now));
        Assert.False(pendingNetworkRequest.Task.IsCompleted);
        Assert.True(boundary.Observe(campaign, "owned", 91195.25, Now.AddSeconds(1)));
        Assert.True(boundary.TargetReached);
        Assert.False(boundary.TimedOut);
        // A delayed continuation or native wait restart cannot release the latch.
        Assert.True(boundary.Observe(campaign, "owned", 91195.24, Now.AddSeconds(2)));
        Assert.False(pendingNetworkRequest.Task.IsCompleted);
    }

    [Fact]
    public void DeadlinePausesEvenIfTheNetworkNeverAnswersAndTheTargetWasNotReached()
    {
        var campaign = new object();
        var boundary = new ReignPassiveWorldAdvanceBoundary(campaign, "owned", 100, Now.AddSeconds(30));
        Assert.False(boundary.Observe(campaign, "owned", 90, Now.AddSeconds(29)));
        Assert.True(boundary.Observe(campaign, "owned", 90, Now.AddSeconds(30)));
        Assert.True(boundary.TimedOut);
        Assert.False(boundary.TargetReached);
    }

    [Fact]
    public void AStaleBoundaryCannotPauseAnotherCampaignOrRun()
    {
        var campaign = new object();
        var boundary = new ReignPassiveWorldAdvanceBoundary(campaign, "owned", 100, Now);
        Assert.False(boundary.Observe(new object(), "owned", 200, Now));
        Assert.False(boundary.Observe(campaign, "other", 200, Now));
        Assert.False(boundary.TargetReached);
        Assert.False(boundary.TimedOut);
        Assert.True(boundary.Observe(campaign, "owned", 100, Now));
        Assert.False(boundary.Observe(new object(), "owned", 200, Now));
        Assert.False(boundary.Observe(campaign, "other", 200, Now));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void InvalidTargetsCannotArmABoundary(double day)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReignPassiveWorldAdvanceBoundary(new object(), "owned", day, Now));
    }

    [Fact]
    public void ProductionTickAndAsyncContinuationUseTheSameOwnedBoundary()
    {
        var root = TestOptions.FindWorkspace();
        var host = File.ReadAllText(Path.Combine(root, "ReignBeta/src/Modules/WorldSimulation/Campaign/ReignLiveInteractionTestHost.cs"));
        var advance = File.ReadAllText(Path.Combine(root, "ReignBeta/src/Modules/WorldSimulation/Campaign/ReignLiveInteractionPassiveWorldHost.cs"));
        Assert.Contains("EnforcePassiveWorldAdvanceBoundary(campaign);", host);
        Assert.Contains("boundary.Owns(campaign, _activeRunId)", advance);
        Assert.Contains("if (startResult.Status == \"completed\")", advance);
        Assert.Contains("if (!boundaryStopped && campaign != null", advance);
        Assert.Contains("ReferenceEquals(_passiveWorldAdvanceBoundary, boundary)", advance);
        Assert.Contains("finally", advance);
        Assert.Contains("[\"localBoundaryReached\"] = boundary.TargetReached", advance);
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reign.testing.json")));
        var contract = catalog.RootElement.GetProperty("campaignTest").GetProperty("passiveWorldLocalBoundary");
        Assert.True(contract.GetProperty("independentOfNetworkWaits").GetBoolean());
        Assert.False(contract.GetProperty("createsSaves").GetBoolean());
    }
}
