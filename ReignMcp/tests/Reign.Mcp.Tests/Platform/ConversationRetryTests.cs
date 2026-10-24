using Reign.Core.Contracts.Dialogue;

namespace Reign.Mcp.Tests;

public sealed class ConversationRetryTests
{
    private static ConversationRetryTurn Turn(string scope = "campaign|load|screen|npc") =>
        new(scope, "The exact original words.", "original-request", "original-turn");

    [Fact]
    public void ExplicitRetryKeepsOriginalWordsAndBothRequestIdentities()
    {
        var state = new ConversationRetryState();
        var original = Turn(); state.Failed(original);
        Assert.True(state.HasPending);
        var retry = state.Begin(original.Scope);
        Assert.Same(original, retry);
        Assert.Equal("original-request", retry!.CorrelationId);
        Assert.Equal("original-turn", retry.SceneTurnId);
        Assert.Equal("The exact original words.", retry.Text);
        Assert.False(state.HasPending);
        Assert.Null(state.Begin(original.Scope));
    }

    [Fact]
    public void RepeatedFailureAllowsAnotherExplicitClickAndSuccessClearsIt()
    {
        var state = new ConversationRetryState(); var turn = Turn();
        state.Failed(turn); Assert.NotNull(state.Begin(turn.Scope));
        state.Failed(turn); Assert.NotNull(state.Begin(turn.Scope));
        state.Clear(); Assert.Null(state.Begin(turn.Scope));
    }

    [Theory]
    [InlineData("other-campaign|load|screen|npc")]
    [InlineData("campaign|other-load|screen|npc")]
    [InlineData("campaign|load|other-screen|npc")]
    [InlineData("campaign|load|screen|other-npc")]
    public void ScopeChangePermanentlyDiscardsRetry(string changed)
    {
        var state = new ConversationRetryState(); var turn = Turn(); state.Failed(turn);
        Assert.Null(state.Begin(changed)); Assert.Null(state.Begin(turn.Scope));
    }

    [Fact]
    public void ANewMessageCanDiscardTheOldRetryWithoutChangingItsOriginal()
    {
        var state = new ConversationRetryState(); var turn = Turn(); state.Failed(turn);
        state.Clear(); Assert.False(state.HasPending); Assert.Equal("The exact original words.", turn.Text);
    }

    [Fact]
    public void InFlightOriginalBlocksItsRetryButNotAnotherConversation()
    {
        var gate = new ConversationTurnDispatchGate();
        using var original = gate.Enter("turn-1", 0);
        Assert.NotNull(original);
        Assert.Null(gate.Enter("turn-1", 0));
        using var other = gate.Enter("turn-2", 0);
        Assert.NotNull(other);
        original!.Dispose();
        using var retry = gate.Enter("turn-1", 0);
        Assert.NotNull(retry);
    }

    [Fact]
    public void ObsoleteWaiterReleasesItsReservationAndNeverDispatches()
    {
        var gate = new ConversationTurnDispatchGate();
        using (gate.Enter("same-turn", 0))
            Assert.Throws<OperationCanceledException>(() => gate.Enter("same-turn", 10, () => throw new OperationCanceledException()));
        using var next = gate.Enter("same-turn", 0);
        Assert.NotNull(next);
    }
}
