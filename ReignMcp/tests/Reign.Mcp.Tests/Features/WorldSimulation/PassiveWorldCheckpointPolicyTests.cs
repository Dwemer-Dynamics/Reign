using ReignLiveTest;

namespace Reign.Mcp.Tests;

public sealed class PassiveWorldCheckpointPolicyTests
{
    [Theory]
    [InlineData(false, 0, 0, 0, false, true, "")]
    [InlineData(true, -1, 0, 0, false, true, "")]
    [InlineData(true, 1, 0, 0, false, true, "")]
    [InlineData(true, 0, -1, 0, false, true, "")]
    [InlineData(true, 0, 1, 0, false, true, "")]
    [InlineData(true, 0, 0, -1, false, true, "")]
    [InlineData(true, 0, 0, 1, false, true, "")]
    [InlineData(true, 0, 0, 0, true, true, "")]
    [InlineData(true, 0, 0, 0, null, true, "")]
    [InlineData(true, 0, 0, 0, false, false, "")]
    [InlineData(true, 0, 0, 0, false, true, "transport failure")]
    [InlineData(true, 0, 0, 0, false, true, null)]
    public void PendingOrUnobservableGossipBlocksCheckpoint(bool valid, long pending, long ready, long due,
        bool? inFlight, bool current, string? error) => Assert.False(PassiveWorldCheckpointPolicy.PersonalGossipQuiescent(
            valid, true, true, pending, ready, due, inFlight, current, error!));

    [Fact]
    public void FutureCorrespondenceAndAnUninitializedEmptySchemaPermitCheckpoint()
    {
        Assert.True(PassiveWorldCheckpointPolicy.PersonalGossipQuiescent(true, true, true, 0, 0, 0, false, true, ""));
        Assert.True(PassiveWorldCheckpointPolicy.PersonalGossipQuiescent(true, true, false, 0, 0, 0, false, false, ""));
        Assert.True(PassiveWorldCheckpointPolicy.PersonalGossipQuiescent(true, false, true, 1, 1, 1, false, false, ""));
    }
    [Theory]
    [InlineData("", true, true, false, 0, 0, "")]
    [InlineData("reign-government-runtime-v1", false, true, false, 0, 0, "")]
    [InlineData("reign-government-runtime-v1", true, null, false, 0, 0, "")]
    [InlineData("reign-government-runtime-v1", true, false, false, 0, 0, "")]
    [InlineData("reign-government-runtime-v1", true, true, null, 0, 0, "")]
    [InlineData("reign-government-runtime-v1", true, true, true, 0, 0, "")]
    [InlineData("reign-government-runtime-v1", true, true, false, -1, 0, "")]
    [InlineData("reign-government-runtime-v1", true, true, false, 1, 0, "")]
    [InlineData("reign-government-runtime-v1", true, true, false, 0, -1, "")]
    [InlineData("reign-government-runtime-v1", true, true, false, 0, 1, "")]
    [InlineData("reign-government-runtime-v1", true, true, false, 0, 0, null)]
    [InlineData("reign-government-runtime-v1", true, true, false, 0, 0, "sync failed")]
    public void MissingBusyOrFailedGovernmentRefusesCheckpoint(string schema, bool available,
        bool? calendar, bool? inFlight, long missing, long pending, string? error)
    {
        Assert.False(PassiveWorldCheckpointPolicy.GovernmentQuiescent(schema, available, calendar, inFlight, missing, pending, error!));
    }

    [Fact]
    public void AppliedGovernmentStateAllowsOtherCheckpointGates()
    {
        Assert.True(PassiveWorldCheckpointPolicy.GovernmentQuiescent("reign-government-runtime-v1", true, true, false, 0, 0, ""));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    [InlineData(20)]
    public void MissingOrFailedActionHealthRefusesCheckpoint(long failures)
    {
        Assert.NotEmpty(PassiveWorldCheckpointPolicy.ActionFailureError(failures));
    }

    [Fact]
    public void NoActionFailuresAllowsOtherQuiescenceChecks()
    {
        Assert.Empty(PassiveWorldCheckpointPolicy.ActionFailureError(0));
    }
}
