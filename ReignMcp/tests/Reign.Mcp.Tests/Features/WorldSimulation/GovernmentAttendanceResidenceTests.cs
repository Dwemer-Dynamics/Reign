using System.Text.Json;
using ReignBeta.Government;

namespace Reign.Mcp.Tests;

public sealed class GovernmentAttendanceResidenceTests
{
    private static GovernmentResidenceLease Visit() => new() { HeroId = "member", KingdomId = "realm",
        HostSettlementId = "capital", ConstituencySettlementId = "village" };

    [Fact]
    public void OfficialAttendancePreservesHomeWithoutAwardingAHostCitySeat()
    {
        var lease = Visit();
        Assert.True(Resident("village", false, lease));
        Assert.False(Resident("capital", true, lease));
        Assert.False(Resident("another-village", true, lease));
    }

    [Fact]
    public void OverlappingHearingsAndSerializedAttendanceKeepOneConstituency()
    {
        var first = Visit(); var second = Visit();
        var restored = JsonSerializer.Deserialize<GovernmentResidenceLease>(JsonSerializer.Serialize(second,
            new JsonSerializerOptions { IncludeFields = true }), new JsonSerializerOptions { IncludeFields = true })!;
        string before = JsonSerializer.Serialize(restored, new JsonSerializerOptions { IncludeFields = true });
        Assert.True(Resident("village", false, first, restored));
        first.Released = true;
        Assert.True(Resident("village", false, first, restored));
        Assert.False(Resident("capital", true, first, restored));
        Assert.Equal(before, JsonSerializer.Serialize(restored, new JsonSerializerOptions { IncludeFields = true }));
    }

    [Theory]
    [InlineData("released")]
    [InlineData("different_hero")]
    [InlineData("different_host")]
    public void AnUnownedVisitCannotOverrideNativeResidence(string fault)
    {
        var lease = Visit();
        if (fault == "released") lease.Released = true;
        if (fault == "different_hero") lease.HeroId = "other";
        if (fault == "different_host") lease.HostSettlementId = "elsewhere";
        Assert.False(Resident("village", false, lease));
        Assert.True(Resident("capital", true, lease));
    }

    [Theory]
    [InlineData("foreign_realm")]
    [InlineData("unknown_home")]
    [InlineData("conflicting_home")]
    public void AmbiguousOrForeignAttendanceCannotCreateResidence(string fault)
    {
        var lease = Visit(); var other = Visit();
        if (fault == "foreign_realm") lease.KingdomId = "foreign";
        if (fault == "unknown_home") lease.ConstituencySettlementId = "";
        if (fault == "conflicting_home") other.ConstituencySettlementId = "another-village";
        Assert.False(Resident("village", true, lease, other));
        Assert.False(Resident("capital", true, lease, other));
    }

    [Fact]
    public void CompletedReturnAndOrdinaryResidenceRemainNative()
    {
        var lease = Visit();
        Assert.True(GovernmentAttendanceResidencePolicy.Qualifies("member", "realm", "village", "village", true, new[] { lease }));
        Assert.False(GovernmentAttendanceResidencePolicy.Qualifies("member", "realm", "capital", "village", false, new[] { lease }));
        Assert.True(Resident("capital", true));
        Assert.False(Resident("village", false));
        Assert.False(GovernmentAttendanceResidencePolicy.Qualifies("", "realm", "village", "capital", true, new[] { lease }));
    }

    private static bool Resident(string constituency, bool native, params GovernmentResidenceLease[] leases) =>
        GovernmentAttendanceResidencePolicy.Qualifies("member", "realm", constituency, "capital", native, leases);
}

public sealed class GovernmentAttendanceReturnTests
{
    [Fact]
    public void AConflictingDutyDefersReturnWithoutSurrenderingTheHomeConstituency()
    {
        Assert.Equal(GovernmentAttendanceReturnAction.Defer,
            GovernmentAttendanceReturnPolicy.Decide(true, true, true, true));
        var retained = new GovernmentResidenceLease { HeroId = "member", KingdomId = "realm",
            HostSettlementId = "capital", ConstituencySettlementId = "home" };
        Assert.True(GovernmentAttendanceResidencePolicy.Qualifies("member", "realm", "home", "capital", false, new[] { retained }));
        Assert.False(GovernmentAttendanceResidencePolicy.Qualifies("member", "realm", "capital", "capital", true, new[] { retained }));
    }

    [Fact]
    public void TheOwnedReturnCanResumeAfterTheDutyEnds()
    {
        Assert.Equal(GovernmentAttendanceReturnAction.Defer,
            GovernmentAttendanceReturnPolicy.Decide(true, true, true, true));
        Assert.Equal(GovernmentAttendanceReturnAction.ReturnHome,
            GovernmentAttendanceReturnPolicy.Decide(true, true, true, false));
    }

    [Fact]
    public void AnotherHearingCannotRecallTheHeroAfterAnEarlierLeaseReturnedThem()
    {
        Assert.Equal(GovernmentAttendanceReturnAction.ReturnHome,
            GovernmentAttendanceReturnPolicy.Decide(true, true, true, false));
        Assert.Equal(GovernmentAttendanceReturnAction.Release,
            GovernmentAttendanceReturnPolicy.Decide(true, true, false, false));
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    public void AnUnownedOrUnavailableHeroIsReleasedEvenIfAnotherDutyExists(bool moved, bool available, bool atHost)
    {
        Assert.Equal(GovernmentAttendanceReturnAction.Release,
            GovernmentAttendanceReturnPolicy.Decide(moved, available, atHost, true));
    }
}
