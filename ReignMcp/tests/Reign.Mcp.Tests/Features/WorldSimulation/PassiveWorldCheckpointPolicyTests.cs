using ReignLiveTest;

namespace Reign.Mcp.Tests;

public sealed class PassiveWorldCheckpointPolicyTests
{
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
