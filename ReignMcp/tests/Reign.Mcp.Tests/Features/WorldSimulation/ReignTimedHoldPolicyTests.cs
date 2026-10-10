using ReignBeta.World;
using Xunit;

namespace Reign.Mcp.Tests.Features.WorldSimulation;

public sealed class ReignTimedHoldPolicyTests
{
    [Theory]
    [InlineData("timed_hold", "explicit_agreement", "active", false, true, true, true)]
    [InlineData("timed_hold", "operator", "accepted", false, true, true, true)]
    [InlineData("move", "explicit_agreement", "active", false, true, true, false)]
    [InlineData("timed_hold", "direct_authority", "active", false, true, true, false)]
    [InlineData("timed_hold", "explicit_agreement", "completed", false, true, true, false)]
    [InlineData("timed_hold", "explicit_agreement", "cancelled", false, true, true, false)]
    [InlineData("timed_hold", "explicit_agreement", "failed", false, true, true, false)]
    [InlineData("timed_hold", "explicit_agreement", "refused", false, true, true, false)]
    [InlineData("timed_hold", "explicit_agreement", "awaiting_guidance", true, true, true, false)]
    [InlineData("timed_hold", "explicit_agreement", "active", true, true, true, false)]
    [InlineData("timed_hold", "explicit_agreement", "active", false, false, true, false)]
    [InlineData("timed_hold", "explicit_agreement", "active", false, true, false, false)]
    public void Only_an_active_accepted_hold_controls_independent_native_decisions(
        string objective, string authority, string status, bool guidance, bool enabled,
        bool eligibleParty, bool expected) =>
        Assert.Equal(expected, ReignTimedHoldPolicy.OwnsNativeDecisions(
            objective, authority, status, guidance, enabled, eligibleParty));

    [Fact]
    public void A_long_journey_does_not_consume_the_agreed_wait()
    {
        Assert.Equal(0, ReignTimedHoldPolicy.Deadline(100, 2, true, false, 0));
        Assert.Equal(0, ReignTimedHoldPolicy.Deadline(130, 2, true, false, 100 + 2d / 24));
        Assert.Equal(130 + 2d / 24, ReignTimedHoldPolicy.Deadline(130, 2, true, true, 0));
    }

    [Fact]
    public void Reloading_a_hold_preserves_its_deadline_including_an_elapsed_one()
    {
        double saved = ReignTimedHoldPolicy.Deadline(100, 2, true, true, 0);
        Assert.Equal(saved, ReignTimedHoldPolicy.Deadline(100.04, 2, true, true, saved));
        Assert.Equal(saved, ReignTimedHoldPolicy.Deadline(101, 2, true, true, saved));
    }

    [Fact]
    public void Leaving_the_gate_interrupts_the_wait_instead_of_claiming_completion()
    {
        double saved = ReignTimedHoldPolicy.Deadline(100, 2, true, true, 0);
        double interrupted = ReignTimedHoldPolicy.Deadline(101, 2, true, false, saved);
        Assert.Equal(0, interrupted);
        Assert.Equal(102 + 2d / 24, ReignTimedHoldPolicy.Deadline(102, 2, true, true, interrupted));
    }

    [Fact]
    public void A_current_position_hold_needs_no_settlement_and_keeps_fractional_hours()
    {
        double deadline = ReignTimedHoldPolicy.Deadline(100, .5, false, false, 0);
        Assert.Equal(100 + .5 / 24, deadline);
        Assert.Equal(deadline, ReignTimedHoldPolicy.Deadline(100.01, .5, false, false, deadline));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Invalid_durations_cannot_start_a_hold(double hours) =>
        Assert.Throws<System.ArgumentOutOfRangeException>(() =>
            ReignTimedHoldPolicy.Deadline(100, hours, false, true, 0));
}
