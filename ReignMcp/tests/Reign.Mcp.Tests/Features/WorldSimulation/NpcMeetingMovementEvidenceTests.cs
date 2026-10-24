using ReignBeta.Campaign;

namespace Reign.Mcp.Tests;

public sealed class NpcMeetingMovementEvidenceTests
{
    private static void Add(NpcMeetingMovementEvidence buffer, string hero, double day = 10) =>
        buffer.Add(hero, day, "teleport_requested", "Active", "venue", "", "elsewhere", "immediate", true, true, new[] { "Native.Caller" });

    [Fact]
    public void RequestedHeroReceivesOnlyItsLastSixteenRecordsInChronologicalOrder()
    {
        var buffer = new NpcMeetingMovementEvidence();
        for (int i = 0; i < 25; i++) { Add(buffer, "issuer", i); Add(buffer, "target", i); }
        var issuer = buffer.ForHero("issuer");
        Assert.Equal(16, issuer.Count);
        Assert.All(issuer, row => Assert.Equal("issuer", row.HeroId));
        Assert.Equal(9, issuer[0].WorldDay);
        Assert.Equal(24, issuer[^1].WorldDay);
        Assert.Empty(buffer.ForHero("unrelated"));
        Assert.Equal(25, buffer.CountForHero("issuer"));
    }

    [Fact]
    public void OldRecordsEvictWithoutChangingAnAlreadyCapturedSnapshot()
    {
        var buffer = new NpcMeetingMovementEvidence(); Add(buffer, "issuer");
        var retained = buffer.ForHero("issuer");
        for (int i = 0; i < 100; i++) Add(buffer, "other", i);
        Assert.Equal(64, buffer.CountForHero("other"));
        Assert.Empty(buffer.ForHero("issuer"));
        Assert.Single(retained);
        Assert.Equal("issuer", retained[0].HeroId);
    }

    [Fact]
    public void CallerAndTextBoundsCannotExpandAResponseOrInjectControlCharacters()
    {
        var buffer = new NpcMeetingMovementEvidence();
        buffer.Add("issuer", 10, new string('a', 1024), "Active\n", "venue", "", "elsewhere", "detail", true, false,
            Enumerable.Repeat(new string('b', 1024) + "\r\n", 100));
        var row = Assert.Single(buffer.ForHero("issuer"));
        Assert.Equal(128, row.Operation.Length);
        Assert.Equal("Active ", row.HeroState);
        Assert.Equal(12, row.Callers.Count);
        Assert.All(row.Callers, caller => Assert.Equal(256, caller.Length));
        Assert.False(row.Allowed);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-1)]
    public void InvalidNativeDaysCannotEnterEvidence(double day)
    {
        var buffer = new NpcMeetingMovementEvidence(); Add(buffer, "issuer", day);
        Assert.Empty(buffer.ForHero("issuer"));
    }

    [Fact]
    public void ClearingAtLoadRemovesPreviousNativeInstanceObservations()
    {
        var buffer = new NpcMeetingMovementEvidence(); Add(buffer, "issuer"); buffer.Clear();
        Assert.Empty(buffer.ForHero("issuer"));
        Add(buffer, "issuer"); Assert.Equal(1, Assert.Single(buffer.ForHero("issuer")).Sequence);
    }
}
