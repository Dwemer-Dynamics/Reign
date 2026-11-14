using ReignBeta.Integration;
using ReignLiveTest;

namespace Reign.Mcp.Tests.Features.WorldSimulation;

public sealed class CommunicationPollHoldTests
{
    private static readonly DateTime Start = new(2026, 11, 5, 0, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(61)]
    public void InvalidDeadlineCannotCreateALease(int seconds)
    {
        var hold = new ReignCommunicationPollHold();
        Assert.Throws<ArgumentException>(() => hold.Acquire(new object(), "scope", "token", seconds, Start));
        Assert.False(hold.HasLease);
    }

    [Fact]
    public void LeaseExpiresAndDoesNotSuppressExplicitOrReceiptWork()
    {
        var campaign = new object(); var hold = new ReignCommunicationPollHold();
        hold.Acquire(campaign, "campaign|timeline|generation", "owner", 60, Start);
        Assert.True(hold.Suppresses(campaign, "campaign|timeline|generation", Start.AddSeconds(59), false, false));
        Assert.False(hold.Suppresses(campaign, "campaign|timeline|generation", Start.AddSeconds(59), true, false));
        Assert.False(hold.Suppresses(campaign, "campaign|timeline|generation", Start.AddSeconds(59), false, true));
        Assert.True(hold.Suppresses(campaign, "campaign|timeline|generation", Start.AddSeconds(59), false, false));
        Assert.False(hold.Suppresses(campaign, "campaign|timeline|generation", Start.AddSeconds(60), false, false));
        Assert.False(hold.HasLease);
    }

    [Fact]
    public void OwnerCanRenewButAnotherOwnerCannotStealTheHold()
    {
        var campaign = new object(); var hold = new ReignCommunicationPollHold();
        hold.Acquire(campaign, "scope", "owner", 60, Start);
        Assert.Throws<InvalidOperationException>(() => hold.Acquire(campaign, "scope", "other", 60, Start.AddSeconds(20)));
        hold.Acquire(campaign, "scope", "owner", 60, Start.AddSeconds(20));
        Assert.True(hold.Active(campaign, "scope", Start.AddSeconds(79)));
        Assert.False(hold.Active(campaign, "scope", Start.AddSeconds(80)));
    }

    [Fact]
    public void StaleReleaseCannotRemoveALeaseForANewGeneration()
    {
        var campaign = new object(); var otherCampaign = new object(); var hold = new ReignCommunicationPollHold();
        hold.Acquire(campaign, "old", "owner", 60, Start);
        hold.Acquire(otherCampaign, "new", "successor", 60, Start.AddSeconds(1));
        Assert.False(hold.Release(campaign, "old", "owner", Start.AddSeconds(2)));
        Assert.False(hold.Release(otherCampaign, "new", "wrong", Start.AddSeconds(2)));
        Assert.True(hold.Active(otherCampaign, "new", Start.AddSeconds(2)));
        Assert.True(hold.Release(otherCampaign, "new", "successor", Start.AddSeconds(2)));
        Assert.False(hold.HasLease);
    }

    [Fact]
    public void TimelineChangeInvalidatesTheLeaseWithoutPersistingOrChangingTheClock()
    {
        var campaign = new object(); var hold = new ReignCommunicationPollHold();
        hold.Acquire(campaign, "timelineA", "owner", 60, Start);
        Assert.False(hold.Active(campaign, "timelineB", Start.AddSeconds(1)));
        Assert.False(hold.HasLease);
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid token")]
    [InlineData("x/../x")]
    [InlineData("token\n")]
    public void UnsafeOwnershipTokenIsRejected(string token)
    {
        var hold = new ReignCommunicationPollHold();
        Assert.Throws<ArgumentException>(() => hold.Acquire(new object(), "scope", token, 10, Start));
    }

    [Fact]
    public void RealQuiescenceWindowCanCompleteWhilePeriodicStartsAreHeld()
    {
        var campaign = new object(); var hold = new ReignCommunicationPollHold();
        hold.Acquire(campaign, "scope", "owner", 60, Start);
        int heldStreak = 0, ordinaryStreak = 0, longestOrdinaryStreak = 0;
        for (int seconds = 0; seconds < 30; seconds += 2)
        {
            bool periodicDue = seconds % 10 == 0;
            bool inFlight = periodicDue && !hold.Suppresses(campaign, "scope", Start.AddSeconds(seconds), false, false);
            heldStreak = PassiveWorldCheckpointPolicy.PersonalGossipQuiescent(true, true, true, 0, 0, 0, inFlight, true, "") ? heldStreak + 1 : 0;
            ordinaryStreak = periodicDue ? 0 : ordinaryStreak + 1;
            longestOrdinaryStreak = Math.Max(longestOrdinaryStreak, ordinaryStreak);
        }
        Assert.True(heldStreak >= 8);
        Assert.True(longestOrdinaryStreak < 8);
        // Real pending composition and native work still refuse the same unchanged gate.
        Assert.False(PassiveWorldCheckpointPolicy.PersonalGossipQuiescent(true, true, true, 1, 0, 0, false, true, ""));
        Assert.False(PassiveWorldCheckpointPolicy.PersonalGossipQuiescent(true, true, true, 0, 0, 0, true, true, ""));
    }
}
