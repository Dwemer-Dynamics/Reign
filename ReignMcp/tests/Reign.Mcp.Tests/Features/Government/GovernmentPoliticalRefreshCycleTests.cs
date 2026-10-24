using ReignBeta.Government;

namespace Reign.Mcp.Tests;

public sealed class GovernmentPoliticalRefreshCycleTests
{
    private static readonly DateTime Start = new(2026, 10, 19, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void SlowFiveThousandCharacterSweepFinishesWithoutRefreshingItsFirstBatchesAgain()
    {
        var cycle = new GovernmentPoliticalRefreshCycle();
        var acknowledgements = new DateTime?[5000];
        var now = Start;
        cycle.Observe(now, 10);
        for (int offset = 0; offset < acknowledgements.Length; offset += 64)
        {
            cycle.Observe(now, 10);
            Assert.All(acknowledgements.Take(offset), last => Assert.False(cycle.NeedsRefresh(last)));
            int end = Math.Min(offset + 64, acknowledgements.Length);
            for (int i = offset; i < end; i++)
            {
                Assert.True(cycle.NeedsRefresh(acknowledgements[i]));
                acknowledgements[i] = now;
            }
            now = now.AddSeconds(8);
        }
        Assert.True(now - Start > TimeSpan.FromMinutes(5));
        Assert.True(cycle.CompleteIfAcknowledged(acknowledgements, now));
        cycle.Observe(now.AddMinutes(4), 10);
        Assert.All(acknowledgements, last => Assert.False(cycle.NeedsRefresh(last)));
    }

    [Fact]
    public void QuietIntervalStartsAfterTheFinalAcknowledgement()
    {
        var cycle = new GovernmentPoliticalRefreshCycle();
        cycle.Observe(Start, 10);
        var finished = Start.AddMinutes(12);
        Assert.True(cycle.CompleteIfAcknowledged(new DateTime?[] { Start, finished }, finished));
        cycle.Observe(finished.AddMinutes(5).AddTicks(-1), 10);
        Assert.False(cycle.NeedsRefresh(Start));
        cycle.Observe(finished.AddMinutes(5), 10);
        Assert.True(cycle.NeedsRefresh(Start));
        Assert.True(cycle.NeedsRefresh(finished));
    }

    [Fact]
    public void MissingOrInvalidatedAcknowledgementRemainsUrgentDuringTheQuietInterval()
    {
        var cycle = new GovernmentPoliticalRefreshCycle();
        cycle.Observe(Start, 10);
        Assert.True(cycle.CompleteIfAcknowledged(new DateTime?[] { Start }, Start));
        cycle.Observe(Start.AddSeconds(1), 10);
        Assert.False(cycle.NeedsRefresh(Start));
        Assert.True(cycle.NeedsRefresh(null));
    }

    [Fact]
    public void UnseenPoliticalEvidenceRemainsUrgentDespiteAFreshCacheAcknowledgement()
    {
        var cycle = new GovernmentPoliticalRefreshCycle();
        cycle.Observe(Start, 10);
        Assert.True(cycle.CompleteIfAcknowledged(new DateTime?[] { Start }, Start));
        Assert.True(cycle.NeedsRefresh(Start, required: true));
        Assert.False(cycle.NeedsRefresh(Start, required: false));
    }

    [Fact]
    public void NewPoliticalIntervalImmediatelyStartsAFreshSweep()
    {
        var cycle = new GovernmentPoliticalRefreshCycle();
        cycle.Observe(Start, 10);
        Assert.True(cycle.CompleteIfAcknowledged(new DateTime?[] { Start }, Start));
        cycle.Observe(Start.AddSeconds(1), 11);
        Assert.True(cycle.NeedsRefresh(Start));
        Assert.False(cycle.NeedsRefresh(Start.AddSeconds(1)));
    }

    [Fact]
    public void UnacknowledgedLivingCharactersPreventCompletion()
    {
        var cycle = new GovernmentPoliticalRefreshCycle();
        cycle.Observe(Start, 10);
        Assert.False(cycle.CompleteIfAcknowledged(new DateTime?[] { Start, null }, Start.AddMinutes(8)));
        Assert.False(cycle.CompleteIfAcknowledged(new DateTime?[] { Start, Start.AddTicks(-1) }, Start.AddMinutes(8)));
        Assert.True(cycle.CompleteIfAcknowledged(new DateTime?[] { Start, Start.AddMinutes(8) }, Start.AddMinutes(8)));
    }

    [Fact]
    public void RemovedCharactersDoNotRetainARefreshOrSaveBlock()
    {
        var cycle = new GovernmentPoliticalRefreshCycle();
        cycle.Observe(Start, 10);
        Assert.False(cycle.CompleteIfAcknowledged(new DateTime?[] { Start, null }, Start));
        Assert.True(cycle.CompleteIfAcknowledged(new DateTime?[] { Start }, Start.AddSeconds(1)));
    }

    [Fact]
    public void EmptyPopulationIsQuietAndRepeatedCompletionCannotExtendTheTimer()
    {
        var cycle = new GovernmentPoliticalRefreshCycle();
        cycle.Observe(Start, 10);
        Assert.True(cycle.CompleteIfAcknowledged(Array.Empty<DateTime?>(), Start));
        Assert.False(cycle.CompleteIfAcknowledged(Array.Empty<DateTime?>(), Start.AddMinutes(4)));
        cycle.Observe(Start.AddMinutes(5), 10);
        Assert.True(cycle.NeedsRefresh(Start));
    }
}
