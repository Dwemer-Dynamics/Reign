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
    public void PausedTicksDoNoMaintenanceAndBehaviorsKeepIndependentSlots()
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
        Assert.Contains("current-town menu", entry.GetProperty("population").GetString());
        Assert.Contains("active missions", entry.GetProperty("population").GetString());
        Assert.Contains("disposable", entry.GetProperty("nativeAcceptance").GetString());
        Assert.Contains("Offline", entry.GetProperty("nativeAcceptance").GetString());
        foreach (string path in new[] { "docs/agent/TESTING_TOOL_GUIDE.md", "ReignMcp/docs/tool-catalog.md", "ReignMcp/docs/security-model.md" })
            Assert.Contains("wandererPopulation", TestingDocumentation.Read(Path.Combine(root, path)));
    }
}
