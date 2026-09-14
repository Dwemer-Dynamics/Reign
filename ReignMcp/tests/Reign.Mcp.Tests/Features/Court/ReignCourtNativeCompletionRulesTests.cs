using Reign.Core.Contracts.Court;

namespace Reign.Mcp.Tests;

public sealed class ReignCourtNativeCompletionRulesTests
{
    [Fact]
    public void ExactCompletedConfirmedActionCanSettleOnce()
    {
        Assert.True(Check());
        Assert.False(Check(settled: true));
    }

    [Theory]
    [InlineData("Pending")]
    [InlineData("Executing")]
    [InlineData("Rejected")]
    [InlineData("Failed")]
    [InlineData("Invalid")]
    [InlineData("Obsolete")]
    [InlineData("")]
    public void GovernmentProgressOrOtherTerminalStatesAreNotCompletion(string status)
        => Assert.False(Check(status: status));

    [Fact]
    public void DialogueAssentWithoutPlayerConfirmationCannotSettle()
        => Assert.False(Check(confirmed: false));

    [Fact]
    public void UnrelatedActionOrChangedTermsCannotSettle()
    {
        Assert.False(Check(actionId: "other_native"));
        Assert.False(Check(source: "world_diplomacy_director"));
        Assert.False(Check(identity: false));
        Assert.False(Check(scope: false));
        Assert.False(Check(option: "refuse"));
        Assert.False(ReignCourtNativeCompletionRules.CanReconcile(true, false, "accept",
            "", "_native", "Completed", "court_decision", true, true));
    }

    private static bool Check(bool confirmed = true, bool settled = false, string option = "accept",
        string actionId = "matter_native", string status = "Completed", string source = "court_decision",
        bool identity = true, bool scope = true)
        => ReignCourtNativeCompletionRules.CanReconcile(confirmed, settled, option, "matter",
            actionId, status, source, identity, scope);
}
