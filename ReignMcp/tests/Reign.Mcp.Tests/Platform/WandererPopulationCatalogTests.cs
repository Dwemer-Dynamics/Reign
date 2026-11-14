using System.Text.Json;
using Reign.Mcp.Server;
using ReignBeta.Shared.Characters;

namespace Reign.Mcp.Tests;

public sealed class WandererPopulationCatalogTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(16)]
    [InlineData(256)]
    public void FasterCampaignPollingDoesNotMultiplyRealTimeMaintenance(int pollsPerObservation)
    {
        var cadence = new RealTimeMaintenanceCadence();
        int passes = 0;
        for (long milliseconds = 0; milliseconds <= 10000; milliseconds += 10)
            for (int poll = 0; poll < pollsPerObservation; poll++)
                if (cadence.TryTake(milliseconds, 1000)) passes++;
        Assert.Equal(10, passes);
    }

    [Fact]
    public void SimulationTickGateRemainsSeparateFromRealTimeCatchupAndBehaviorsKeepIndependentSlots()
    {
        var tavern = new RealTimeMaintenanceCadence();
        var wanderer = new RealTimeMaintenanceCadence();
        Assert.False(tavern.TryTake(0, 1000));
        Assert.False(wanderer.TryTake(0, 1000));
        Assert.False(tavern.TryTake(60000, 1000, false));
        Assert.False(wanderer.TryTake(60000, 1000, false));
        Assert.True(tavern.TryTake(60000, 1000));
        Assert.True(wanderer.TryTake(60000, 1000));
        Assert.False(tavern.TryTake(60000, 1000));
        Assert.False(wanderer.TryTake(60000, 1000));
    }

    [Fact]
    public void PausedPopulationCohortDrainsOnRealTimeThenStops()
    {
        var pending = new PausedPopulationCatchup(); var cadence = new RealTimeMaintenanceCadence();
        int work = 3, passes = 0;
        for (long tick = 0; tick <= 10000; tick += 10)
        {
            if (pending.CanPoll(true, true, false, false, true) && cadence.TryTake(tick, 1000))
            {
                passes++; work--; pending.Observe(work > 0, false);
            }
            // Bannerlord may also publish a paused simulation tick; it cannot consume another slot.
            Assert.False(cadence.TryTake(tick, 1000, false));
        }
        Assert.Equal(3, passes); Assert.Equal(0, work); Assert.False(pending.Pending);
        pending.Request(); Assert.True(pending.Pending);
        pending.Observe(false, true); Assert.True(pending.Pending); // Failure is not completion.
    }

    [Theory]
    [InlineData(true, true, false, false, true, true)]
    [InlineData(false, true, false, false, true, false)]
    [InlineData(true, false, false, false, true, false)]
    [InlineData(true, true, true, false, true, false)]
    [InlineData(true, true, false, true, true, false)]
    [InlineData(true, true, false, false, false, false)]
    public void PausedCatchupRequiresCurrentReadyIdleAlignedSaveSafeCampaign(
        bool current, bool ready, bool mission, bool saving, bool aligned, bool expected)
    {
        var pending = new PausedPopulationCatchup();
        Assert.Equal(expected, pending.CanPoll(current, ready, mission, saving, aligned));
        pending.Observe(false, false);
        Assert.False(pending.CanPoll(current, ready, mission, saving, aligned));
    }

    [Fact]
    public void FailedPausedCohortBlocksPollingWithoutClaimingCompletionAndCanRearm()
    {
        var pending = new PausedPopulationCatchup();
        for (int failure = 0; failure < 3; failure++) pending.Observe(true, true);
        Assert.True(pending.Pending); Assert.True(pending.Blocked);
        Assert.False(pending.CanPoll(true, true, false, false, true));
        pending.Observe(true, false); // A quiet wait is not successful progress.
        Assert.True(pending.Blocked);
        pending.Request();
        Assert.True(pending.CanPoll(true, true, false, false, true));
        pending.Observe(true, true); pending.Progress();
        pending.Observe(true, true); pending.Observe(true, true);
        Assert.False(pending.Blocked); // Actual progress restarted the failure cohort.
        pending.Observe(false, false); Assert.False(pending.Pending);
    }

    [Fact]
    public void ApplicationAndCampaignTickShareOneRealTimeSlot()
    {
        var cadence = new RealTimeMaintenanceCadence(); int application = 0, campaign = 0;
        for (long tick = 0; tick <= 10000; tick += 10)
        {
            if (cadence.TryTake(tick, 1000)) application++;
            if (cadence.TryTake(tick, 1000, true)) campaign++;
        }
        Assert.Equal(10, application + campaign);
    }

    [Fact]
    public void ResumingAfterLongDelayDoesOnePassAndKeepsFullNextInterval()
    {
        var cadence = new RealTimeMaintenanceCadence();
        Assert.False(cadence.TryTake(0, 1000));
        Assert.True(cadence.TryTake(60000, 1000));
        Assert.False(cadence.TryTake(60000, 1000));
        Assert.False(cadence.TryTake(60999, 1000));
        Assert.True(cadence.TryTake(61000, 1000));
    }

    [Fact]
    public void ClockResetStartsANewIntervalAndInvalidClockUnitsAreRejected()
    {
        var cadence = new RealTimeMaintenanceCadence();
        Assert.False(cadence.TryTake(10000, 1000));
        Assert.False(cadence.TryTake(0, 1000));
        Assert.False(cadence.TryTake(999, 1000));
        Assert.True(cadence.TryTake(1000, 1000));
        Assert.Throws<ArgumentOutOfRangeException>(() => cadence.TryTake(1000, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => cadence.TryTake(-1, 1000));
    }

    [Fact]
    public void CatalogDeclaresPopulationEvidenceAndSeparateNativeAcceptance()
    {
        string root = TestOptions.FindWorkspace();
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reign.testing.json")));
        var entry = document.RootElement.GetProperty("wandererPopulation");
        Assert.Equal("reign-wanderer-population-v1", entry.GetProperty("schema").GetString());
        Assert.Equal("_reign_wanderer_population_v1", entry.GetProperty("saveNamespace").GetString());
        Assert.Contains("wanderer_population", entry.GetProperty("verification").GetString());
        Assert.Contains("observational", entry.GetProperty("contactHistory").GetString());
        string paused = entry.GetProperty("pausedCatchup").GetString()!;
        foreach (string boundary in new[] { "Stop mode", "shared real-time cadence", "save finalization", "Save Sync alignment", "active missions", "stop application polling" })
            Assert.Contains(boundary, paused);
        Assert.Contains("current-town menu", entry.GetProperty("population").GetString());
        Assert.Contains("active missions", entry.GetProperty("population").GetString());
        Assert.Contains("disposable", entry.GetProperty("nativeAcceptance").GetString());
        Assert.Contains("Offline", entry.GetProperty("nativeAcceptance").GetString());
        foreach (string path in new[] { "docs/agent/TESTING_TOOL_GUIDE.md", "ReignMcp/docs/tool-catalog.md", "ReignMcp/docs/security-model.md" })
            Assert.Contains("wandererPopulation", TestingDocumentation.Read(Path.Combine(root, path)));
    }
}
