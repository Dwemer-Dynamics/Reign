using Reign.Core.Contracts.Dialogue;
using System.Text.Json;

namespace Reign.Mcp.Tests;

public sealed class ConversationContinuationTests
{
    private static ConversationContinuationToken Token(string reason = "scene_transition") => new()
    {
        CampaignId = "campaign", TimelineId = "timeline", PlayerId = "player", NpcId = "npc",
        SessionId = "storage", LogicalConversationId = "logical", Generation = "generation", Reason = reason
    };

    [Fact]
    public void ExplicitHandoffIsSingleUseAndStaleCompletionCannotReviveIt()
    {
        var slot = new ConversationContinuationSlot();
        var token = Token();
        Assert.False(slot.Offer(token));
        slot.Begin(token.Generation);
        Assert.True(slot.Offer(token));
        Assert.Null(slot.Consume("campaign", "timeline", "player", "another"));
        Assert.Same(token, slot.Consume("campaign", "timeline", "player", "npc"));
        Assert.Null(slot.Consume("campaign", "timeline", "player", "npc"));
        Assert.False(slot.Offer(token));
        slot.Begin("new-screen");
        Assert.False(slot.Offer(token));
    }

    [Theory]
    [InlineData("other", "timeline", "player", "npc")]
    [InlineData("campaign", "rollback", "player", "npc")]
    [InlineData("campaign", "timeline", "heir", "npc")]
    [InlineData("campaign", "timeline", "player", "another")]
    public void HandoffsAreBoundToSaveScopeAndParticipants(string c, string t, string p, string n)
        => Assert.False(Token().Matches(c, t, p, n));

    [Theory]
    [InlineData("closed_by_player")]
    [InlineData("goodbye")]
    [InlineData("conversation_ended")]
    [InlineData("")]
    public void TrueEndingsCannotBecomeHandoffs(string reason) => Assert.False(Token(reason).IsValid);

    [Fact]
    public void SaveRoundtripRequiresExplicitConsumptionAndPreservesIdentity()
    {
        var restored = JsonSerializer.Deserialize<ConversationContinuationToken>(JsonSerializer.Serialize(Token("campaign_reload")))!;
        var slot = new ConversationContinuationSlot();
        slot.Restore(restored);
        Assert.Equal("storage", slot.Snapshot.SessionId);
        Assert.Equal("logical", slot.Snapshot.LogicalConversationId);
        Assert.NotNull(slot.Consume("campaign", "timeline", "player", "npc"));
        slot.Restore(restored);
        slot.Discard();
        Assert.False(slot.Offer(restored)); // player close wins over an in-flight transition
        restored.Version = 2;
        slot.Restore(restored);
        Assert.Null(slot.Snapshot);
    }

    [Fact]
    public async Task ConcurrentStartSharesOneCallAndExplicitRetryKeepsTheSameId()
    {
        var gate = new ConversationSessionStartGate("stable");
        var completion = new TaskCompletionSource<ConversationSessionStart>(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        Task<ConversationSessionStart> Start() { calls++; return completion.Task; }
        var first = gate.EnsureAsync(Start, false);
        Assert.Same(first, gate.EnsureAsync(Start, true));
        completion.SetResult(new ConversationSessionStart { Error = "lost response" });
        Assert.False((await first).Ok);
        Assert.Equal(1, calls);
        var second = await gate.EnsureAsync(() => { calls++; return Task.FromResult(new ConversationSessionStart
            { Ok = true, SessionId = gate.SessionId, LogicalConversationId = "logical", Status = "open" }); }, true);
        Assert.True(second.Ok);
        Assert.Equal("stable", second.SessionId);
        Assert.Same(second, await gate.EnsureAsync(Start, true));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task RepeatedFailureIsBoundedAndClosedSessionsAreNeverReopened()
    {
        int calls = 0;
        var gate = new ConversationSessionStartGate("stable");
        Task<ConversationSessionStart> Start() { calls++; throw new IOException("offline"); }
        for (int i = 0; i < 8; i++) Assert.False((await gate.EnsureAsync(Start, true)).Ok);
        Assert.Equal(2, calls);
        foreach (string status in new[] { "closed", "interrupted" })
        {
            var closed = new ConversationSessionStartGate("stable");
            Assert.False((await closed.EnsureAsync(() => Task.FromResult(new ConversationSessionStart
                { Ok = true, SessionId = "stable", LogicalConversationId = "logical", Status = status }), false)).Ok);
        }
    }
}
